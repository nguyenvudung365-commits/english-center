import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getSession, setSession } from "./api";
import {
    attendanceStatuses,
    getAttendanceSummary,
    listAttendance,
    listAttendanceHistory,
    listAttendanceReplays,
    listSessionAttendance,
    recordAttendance,
    replayAttendance,
    type AttendanceQuery,
    type AttendanceRow,
    type AttendanceStatus,
    type PageQuery,
    type RosterPage,
    type Summary,
    type SummaryQuery,
} from "./attendance";

const classId = "11111111-1111-1111-1111-111111111111";
const sessionId = "22222222-2222-2222-2222-222222222222";
const studentId = "33333333-3333-3333-3333-333333333333";
const attendanceId = "44444444-4444-4444-4444-444444444444";
const actorId = "55555555-5555-5555-5555-555555555555";
const statuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"] as const;
const row: AttendanceRow = {
    id: attendanceId, sessionId, classId, studentId, status: "LateOver20Min", revision: 2,
    recordedBy: actorId, recordedAtUtc: "2026-11-13T01:00:00.000001Z",
    updatedBy: actorId, updatedAtUtc: "2026-11-13T01:00:00.000002Z",
};
const counts = { present: 1, absentUnexcused: 1, absentExcused: 1, late: 0, lateOver20Min: 0, leftEarly: 0 };
const summary: Summary = {
    recordedCount: 3, counts,
    percentages: { present: 33, absentUnexcused: 33, absentExcused: 33, late: 0, lateOver20Min: 0, leftEarly: 0 },
    absentCount: 2, lateCount: 0, leftEarlyCount: 0,
};
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { "Content-Type": "application/json" },
});
const invalidIds: unknown[] = [
    undefined, null, "", " ", "00000000-0000-0000-0000-000000000000", "not-a-uuid",
    "../attendance", `${attendanceId}/history`, `${attendanceId}?page=2`, `${attendanceId}\n`,
    `${attendanceId}\r\n`, `${attendanceId}\u200b`, 0, {}, [],
];
const invalidStatuses: unknown[] = [undefined, null, "", "present", "Unknown", "1", 0, 1, {}, []];
const invalidPages: PageQuery[] = ([
    ...[0, -1, 1.5, NaN, Infinity, "2", null, [], {}].map(page => ({ page })),
    ...[0, -1, 101, 1.5, NaN, Infinity, "20", null, [], {}].map(pageSize => ({ pageSize })),
] as unknown) as PageQuery[];

describe("Attendance frozen helper contract through the real session client", () => {
    const fetcher = vi.fn<typeof fetch>();
    beforeEach(() => {
        fetcher.mockReset();
        vi.stubGlobal("fetch", fetcher);
        setSession({ accessToken: "attendance-access", refreshToken: "attendance-refresh",
            accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
            refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString() });
    });
    afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

    function expectRead(path: string, query: Record<string, string> = {}) {
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        const parsed = new URL(String(url), "https://example.test");
        expect(parsed.pathname).toBe(`/api/academic${path}`);
        expect([...parsed.searchParams.entries()].sort()).toEqual(Object.entries(query).sort());
        expect(init?.method ?? "GET").toBe("GET");
        expect(init?.body).toBeUndefined();
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer attendance-access");
    }

    async function expectInvalid(operation: Promise<unknown>) {
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({ status: 400 });
        expect(fetcher).not.toHaveBeenCalled();
    }

    it("exports exactly the six canonical named statuses in frozen order", () => {
        expect(attendanceStatuses).toEqual(statuses);
    });

    it("GETs the roster envelope, preserving unrecorded null, names, inactive history and server write capability", async () => {
        const roster: RosterPage = {
            sessionId, classId, asOfUtc: row.updatedAtUtc, canWrite: false,
            writeClosesAtUtc: "2026-11-14T01:00:00Z", page: 1, pageSize: 20, total: 2,
            items: [
                { studentId, fullName: "Nguyễn O'An 🗓", studentCode: "HV-01", isActive: true, attendance: null },
                { studentId: actorId, fullName: null, studentCode: null, isActive: false, attendance: { ...row, studentId: actorId } },
            ],
        };
        fetcher.mockResolvedValue(json(roster));
        expect(await listSessionAttendance(sessionId)).toEqual(roster);
        expectRead(`/sessions/${sessionId}/attendance`, { page: "1", pageSize: "20" });
    });

    it.each(statuses)("PUTs only status=%s and explicit null expectedRevision on creation", async (status) => {
        const body = { status, expectedRevision: null, studentId: actorId, revision: 99, canWrite: true };
        const original = structuredClone(body);
        const response = { ...row, status, revision: 1 };
        fetcher.mockResolvedValue(json(response));
        expect(await recordAttendance(sessionId, studentId, body)).toEqual(response);
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe(`/api/academic/sessions/${sessionId}/attendance/${studentId}`);
        expect(init?.method).toBe("PUT");
        expect(JSON.parse(String(init?.body))).toEqual({ status, expectedRevision: null });
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer attendance-access");
        expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
        expect(body).toEqual(original);
    });

    it("PUTs an explicit positive expectedRevision without inventing actor/time metadata", async () => {
        fetcher.mockResolvedValue(json(row));
        expect(await recordAttendance(sessionId, studentId, { status: "LateOver20Min", expectedRevision: 1 })).toEqual(row);
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ status: "LateOver20Min", expectedRevision: 1 });
    });

    it.each(invalidStatuses)("rejects invalid/missing status %j before PUT", async (status) => {
        await expectInvalid(recordAttendance(sessionId, studentId, { status: status as AttendanceStatus, expectedRevision: null }));
    });

    it.each([undefined, 0, -1, 1.5, NaN, Infinity, "1", false, {}, []])(
        "rejects invalid expectedRevision %j instead of converting it to create", async (expectedRevision) => {
            await expectInvalid(recordAttendance(sessionId, studentId, {
                status: "Present", expectedRevision: expectedRevision as number,
            }));
        },
    );

    it("rejects an omitted expectedRevision property before PUT", async () => {
        await expectInvalid(recordAttendance(sessionId, studentId,
            { status: "Present" } as Parameters<typeof recordAttendance>[2]));
    });

    it.each([null, undefined])("rejects a missing PUT body %j as ApiError400", async (body) => {
        await expectInvalid(recordAttendance(sessionId, studentId, body as unknown as Parameters<typeof recordAttendance>[2]));
    });

    it.each(statuses)("encodes all attendance filters including %s and explicit page bounds as individual query entries", async (status) => {
        const query = { classId, sessionId, studentId, status, page: 2, pageSize: 100 };
        const original = structuredClone(query);
        const page = { items: [{ ...row, status }], page: 2, pageSize: 100, total: 101 };
        fetcher.mockResolvedValue(json(page));
        expect(await listAttendance(query)).toEqual(page);
        expectRead("/attendance", { classId, sessionId, studentId, status, page: "2", pageSize: "100" });
        expect(query).toEqual(original);
    });

    it("omits absent filters, applies default page bounds and preserves a genuine empty page", async () => {
        const page = { items: [], page: 1, pageSize: 20, total: 0 };
        fetcher.mockResolvedValue(json(page));
        expect(await listAttendance()).toEqual(page);
        expectRead("/attendance", { page: "1", pageSize: "20" });
    });

    it("requests the whole-filter summary without page parameters and preserves rounded components and actual zeros", async () => {
        const query = { classId, sessionId, studentId, status: "AbsentExcused" as const };
        fetcher.mockResolvedValue(json(summary));
        expect(await getAttendanceSummary(query)).toEqual(summary);
        expectRead("/attendance/summary", query);
    });

    it("requests unfiltered summary without an invented studentId or paging", async () => {
        fetcher.mockResolvedValue(json(summary));
        expect(await getAttendanceSummary()).toEqual(summary);
        expectRead("/attendance/summary");
    });

    it("GETs immutable history including a null initial previousStatus and microsecond timestamps", async () => {
        const page = { items: [
            { attendanceId, revision: 2, previousStatus: "Present", status: row.status, changedBy: actorId, changedAtUtc: row.updatedAtUtc },
            { attendanceId, revision: 1, previousStatus: null, status: "Present", changedBy: actorId, changedAtUtc: row.recordedAtUtc },
        ], page: 1, pageSize: 20, total: 2 };
        fetcher.mockResolvedValue(json(page));
        expect(await listAttendanceHistory(attendanceId)).toEqual(page);
        expectRead(`/attendance/${attendanceId}/history`, { page: "1", pageSize: "20" });
    });

    it("POSTs only revision and trimmed Unicode reason; preserves the explicit dispatch response", async () => {
        const body = { revision: 1, reason: "  Sửa lỗi 🗓 O'An &status=Failed?+  ", dispatchStatus: "Dispatched", requestedBy: studentId };
        const original = structuredClone(body);
        const response = { attemptId: actorId, attendanceId, revision: 1, dispatchStatus: "Dispatched" };
        fetcher.mockResolvedValue(json(response));
        expect(await replayAttendance(attendanceId, body)).toEqual(response);
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe(`/api/academic/attendance/${attendanceId}/replay`);
        expect(init?.method).toBe("POST");
        expect(JSON.parse(String(init?.body))).toEqual({ revision: 1, reason: "Sửa lỗi 🗓 O'An &status=Failed?+" });
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer attendance-access");
        expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
        expect(body).toEqual(original);
    });

    it.each(["x", "x".repeat(500)])("accepts replay reason at allowed trimmed length: %j", async (reason) => {
        fetcher.mockResolvedValue(json({ attemptId: actorId, attendanceId, revision: 1, dispatchStatus: "Dispatched" }));
        await replayAttendance(attendanceId, { revision: 1, reason: ` ${reason} ` });
        expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ revision: 1, reason });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it.each([undefined, null, 0, -1, 1.5, NaN, Infinity, "1", {}, []])("rejects replay revision %j before POST", async (revision) => {
        await expectInvalid(replayAttendance(attendanceId, { revision: revision as number, reason: "Recovery" }));
    });

    it.each([undefined, null, "", " \t\n", "x".repeat(501), 0, {}, []])("rejects replay reason %j before POST", async (reason) => {
        await expectInvalid(replayAttendance(attendanceId, { revision: 1, reason: reason as string }));
    });

    it.each([null, undefined])("rejects missing replay body %j as ApiError400", async (body) => {
        await expectInvalid(replayAttendance(attendanceId, body as unknown as Parameters<typeof replayAttendance>[1]));
    });

    it("GETs replay audit without changing Pending/Failed states or null completion", async () => {
        const page = { items: [
            { id: actorId, attendanceId, revision: 1, requestedBy: actorId, requestedAtUtc: row.updatedAtUtc,
                reason: "Recovery", dispatchStatus: "Pending", completedAtUtc: null },
            { id: studentId, attendanceId, revision: 2, requestedBy: actorId, requestedAtUtc: row.recordedAtUtc,
                reason: "Retry", dispatchStatus: "Failed", completedAtUtc: row.updatedAtUtc },
        ], page: 1, pageSize: 20, total: 2 };
        fetcher.mockResolvedValue(json(page));
        expect(await listAttendanceReplays(attendanceId)).toEqual(page);
        expectRead(`/attendance/${attendanceId}/replays`, { page: "1", pageSize: "20" });
    });

    const pagedReads = [
        { name: "roster", path: `/sessions/${sessionId}/attendance`, load: (query: PageQuery) => listSessionAttendance(sessionId, query) },
        { name: "attendance", path: "/attendance", load: (query: PageQuery) => listAttendance(query) },
        { name: "history", path: `/attendance/${attendanceId}/history`, load: (query: PageQuery) => listAttendanceHistory(attendanceId, query) },
        { name: "replays", path: `/attendance/${attendanceId}/replays`, load: (query: PageQuery) => listAttendanceReplays(attendanceId, query) },
    ];
    for (const { name, path, load } of pagedReads) {
        it.each([{ page: 2, pageSize: 1 }, { page: 999, pageSize: 100 }])(`${name} preserves requested pages, including past-end total: %j`, async (query) => {
            const page = { items: [], ...query, total: 101 };
            fetcher.mockResolvedValue(json(page));
            expect(await load(query)).toEqual(page);
            expectRead(path, { page: String(query.page), pageSize: String(query.pageSize) });
        });
        it.each(invalidPages)(`${name} rejects invalid paging %j rather than omitting/clamping zero or null`, async (query) => {
            await expectInvalid(load(query));
        });
    }

    for (const badId of invalidIds) {
        it(`rejects every attendance path ID location before fetch: ${JSON.stringify(badId)}`, async () => {
            const id = badId as string;
            await expectInvalid(listSessionAttendance(id));
            await expectInvalid(recordAttendance(id, studentId, { status: "Present", expectedRevision: null }));
            await expectInvalid(recordAttendance(sessionId, id, { status: "Present", expectedRevision: null }));
            await expectInvalid(listAttendanceHistory(id));
            await expectInvalid(listAttendanceReplays(id));
            await expectInvalid(replayAttendance(id, { revision: 1, reason: "Recovery" }));
        });
    }

    for (const field of ["classId", "sessionId", "studentId"] as const) {
        it.each(invalidIds.filter(value => value !== undefined))(`rejects present invalid ${field} filter %j for both reads`, async (value) => {
            const query = { [field]: value };
            await expectInvalid(listAttendance(query as AttendanceQuery));
            await expectInvalid(getAttendanceSummary(query as SummaryQuery));
        });
    }

    it.each(invalidStatuses.filter(value => value !== undefined))("rejects present invalid status filter %j for list and summary", async (value) => {
        const query = { status: value as AttendanceStatus };
        await expectInvalid(listAttendance(query));
        await expectInvalid(getAttendanceSummary(query));
    });

    it("preserves stale revision409 errors and input, with no blind retry or overwrite", async () => {
        const body = { status: "Present" as const, expectedRevision: 1 };
        const original = structuredClone(body);
        const errors = { ExpectedRevision: ["Attendance changed."] };
        fetcher.mockResolvedValue(json({ title: "Revision conflict", errors }, 409));
        await expect(recordAttendance(sessionId, studentId, body)).rejects.toMatchObject({ status: 409, message: "Revision conflict", errors });
        expect(body).toEqual(original);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    const operations = [
        () => listSessionAttendance(sessionId), () => listAttendance(), () => getAttendanceSummary(),
        () => listAttendanceHistory(attendanceId), () => listAttendanceReplays(attendanceId),
        () => recordAttendance(sessionId, studentId, { status: "Present", expectedRevision: null }),
        () => replayAttendance(attendanceId, { revision: 1, reason: "Recovery" }),
    ];
    it.each(operations)("preserves forbidden403 without refreshing, returning empty data, or retrying (%#)", async (load) => {
        fetcher.mockResolvedValue(json({ title: "Out of scope" }, 403));
        await expect(load()).rejects.toMatchObject({ status: 403, message: "Bạn không có quyền thực hiện thao tác này." });
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(getSession()).not.toBeNull();
    });

    it("preserves explicit replay503 and durable failure message, without retrying POST", async () => {
        const errors = { Dispatch: ["Source remains available for recovery."] };
        fetcher.mockResolvedValue(json({ title: "Replay dispatch failed", errors }, 503));
        await expect(replayAttendance(attendanceId, { revision: 1, reason: "Recovery" })).rejects.toMatchObject({
            status: 503, message: "Replay dispatch failed", errors,
        });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it.each(operations)("does not hide a network failure as an empty/successful attendance result (%#)", async (load) => {
        fetcher.mockRejectedValue(new TypeError("Failed to fetch"));
        await expect(load()).rejects.toMatchObject({ status: 0 });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });
});
