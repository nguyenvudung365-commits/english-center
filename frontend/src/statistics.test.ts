import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getSession, setSession } from "./api";
import {
    getClassProgress,
    getPersonalStatistics,
    validateStatisticsMonth,
    type ClassProgress,
    type PersonalStatistics,
} from "./statistics";

const classId = "11111111-1111-1111-1111-111111111111";
const counts = { present: 0, absentUnexcused: 0, absentExcused: 0, late: 0, lateOver20Min: 0, leftEarly: 0 };
const emptySummary = { recordedCount: 0, counts, percentages: counts, absentCount: 0, lateCount: 0, leftEarlyCount: 0 };
const statistics: PersonalStatistics = {
    month: "2026-11", timezone: "UTC+07:00", asOfUtc: "2026-11-13T01:00:00.000001Z", availability: "partial",
    academic: { currentClassCount: 0, currentCompletedSessions: 0, previousCompletedSessions: 0,
        completedSessionsChangePercent: null, attendance: emptySummary },
    contributors: [
        { key: "assessment", availability: "available", reason: null, metrics: [
            { key: "gradedSubmissionCount", current: 0, previous: 0, changePercent: null },
            { key: "averageGrade", current: null, previous: null, changePercent: null },
        ] },
        { key: "warnings", availability: "unavailable", reason: "not_registered", metrics: null },
        { key: "feedback", availability: "unavailable", reason: "provider_failed", metrics: null },
    ],
    chart: Array.from({ length: 12 }, (_, index) => ({
        month: `2026-${String(index + 1).padStart(2, "0")}`, completedSessions: 0, recordedAttendance: 0, counts,
    })),
};
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { "Content-Type": "application/json" },
});
const validMonths = ["0002-01", "0002-12", "0099-02", "2028-02", "9998-01", "9998-12"];
const invalidMonths: unknown[] = [
    null, "", " ", "0000-01", "0001-12", "9999-01", "10000-01", "2026-00", "2026-13",
    "2026-1", "026-01", "2026/11", "2026-11-01", "2026-11T00:00:00Z", "2026-11+07:00",
    " 2026-11", "2026-11 ", "2026-11\n", "2026-11\r\n", "２０２６-１１", "2026-11\u200b",
    "2026-11&studentId=other", 0, 202611, {}, [],
];

describe("Strict statistics month validation", () => {
    it.each(validMonths)("accepts supported strict YYYY-MM month %s", (month) => {
        expect(validateStatisticsMonth(month)).toBe(true);
    });
    it.each([...invalidMonths, undefined])("rejects malformed/out-of-range month %j without coercion", (month) => {
        expect(validateStatisticsMonth(month as string)).toBe(false);
    });
});

describe("Statistics and progress transport through the real session client", () => {
    const fetcher = vi.fn<typeof fetch>();
    beforeEach(() => {
        fetcher.mockReset();
        vi.stubGlobal("fetch", fetcher);
        setSession({ accessToken: "statistics-access", refreshToken: "statistics-refresh",
            accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
            refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString() });
    });
    afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

    function expectRead(path: string, query: Record<string, string> = {}) {
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        const parsed = new URL(String(url), "https://example.test");
        expect(parsed.pathname).toBe(`/api/academic${path}`);
        expect([...parsed.searchParams.entries()]).toEqual(Object.entries(query));
        expect(init?.method ?? "GET").toBe("GET");
        expect(init?.body).toBeUndefined();
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer statistics-access");
    }

    it.each([undefined, "2026-11"])("GETs personal statistics unchanged with month %j and no impersonation/paging parameters", async (month) => {
        fetcher.mockResolvedValue(json(statistics));
        expect(await getPersonalStatistics(month)).toEqual(statistics);
        expectRead("/statistics/mine", month === undefined ? {} : { month });
    });

    it("omits month when called without arguments so the server owns the current Vietnam month", async () => {
        fetcher.mockResolvedValue(json(statistics));
        expect(await getPersonalStatistics()).toEqual(statistics);
        expectRead("/statistics/mine");
    });

    it.each(validMonths)("sends supported month %s without losing leading year zeros or altering the query", async (month) => {
        const response = { ...statistics, month };
        fetcher.mockResolvedValue(json(response));
        expect(await getPersonalStatistics(month)).toEqual(response);
        expectRead("/statistics/mine", { month });
    });

    it.each(invalidMonths)("rejects month %j as ApiError400 before fetch, not as a default-month request", async (month) => {
        const operation = getPersonalStatistics(month as string);
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({ status: 400 });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("keeps real zero distinct from missing contribution/grade/baseline, and retains all 12 chart rows", async () => {
        fetcher.mockResolvedValue(json(statistics));
        const result = await getPersonalStatistics("2026-11");
        expect(result).toEqual(statistics);
        expect(result.academic.completedSessionsChangePercent).toBeNull();
        expect(result.contributors[0].metrics?.[0].current).toBe(0);
        expect(result.contributors[0].metrics?.[1].current).toBeNull();
        expect(result.contributors[1]).toEqual({ key: "warnings", availability: "unavailable", reason: "not_registered", metrics: null });
        expect(result.contributors[2]).toEqual({ key: "feedback", availability: "unavailable", reason: "provider_failed", metrics: null });
        expect(result.chart).toHaveLength(12);
        expect(result.chart.map(point => point.month)).toEqual(Array.from({ length: 12 }, (_, i) => `2026-${String(i + 1).padStart(2, "0")}`));
        expectRead("/statistics/mine", { month: "2026-11" });
    });

    it("preserves complete provider metrics, negative changes, whole rounded percentages and authoritative chart values", async () => {
        const response: PersonalStatistics = {
            ...statistics, availability: "complete",
            academic: { ...statistics.academic, currentClassCount: 2, currentCompletedSessions: 1,
                previousCompletedSessions: 3, completedSessionsChangePercent: -67,
                attendance: { recordedCount: 3, counts: { ...counts, present: 1, absentExcused: 1, lateOver20Min: 1 },
                    percentages: { ...counts, present: 33, absentExcused: 33, lateOver20Min: 33 }, absentCount: 1, lateCount: 1, leftEarlyCount: 0 } },
            contributors: [
                { key: "assessment", availability: "available", reason: null, metrics: [
                    { key: "gradedSubmissionCount", current: 2, previous: 3, changePercent: -33 },
                    { key: "averageGrade", current: 8.25, previous: 7.5, changePercent: 10 },
                ] },
                { key: "warnings", availability: "available", reason: null, metrics: [
                    { key: "issuedCount", current: 0, previous: 1, changePercent: -100 },
                    { key: "activeCount", current: 0, previous: 0, changePercent: null },
                ] },
                { key: "feedback", availability: "available", reason: null, metrics: [
                    { key: "submittedCount", current: 1, previous: 0, changePercent: null },
                ] },
            ],
            chart: statistics.chart.map((point, index) => index === 10 ? {
                ...point, completedSessions: 1, recordedAttendance: 3, counts: { ...counts, present: 1, absentExcused: 1, lateOver20Min: 1 },
            } : point),
        };
        fetcher.mockResolvedValue(json(response));
        expect(await getPersonalStatistics("2026-11")).toEqual(response);
        expectRead("/statistics/mine", { month: "2026-11" });
    });

    it.each<ClassProgress>([
        { classId, asOfUtc: statistics.asOfUtc, totalSessions: 0, completedSessions: 0, remainingSessions: 0, completionPercent: 0 },
        { classId, asOfUtc: statistics.asOfUtc, totalSessions: 3, completedSessions: 1, remainingSessions: 2, completionPercent: 33 },
    ])("GETs class progress without query/body or browser recomputation: %j", async (response) => {
        fetcher.mockResolvedValue(json(response));
        expect(await getClassProgress(classId)).toEqual(response);
        expectRead(`/classes/${classId}/progress`);
    });

    it.each<unknown>([
        undefined, null, "", " ", "00000000-0000-0000-0000-000000000000", "not-a-uuid", "../statistics/mine",
        `${classId}/progress`, `${classId}?month=2026-11`, `${classId}\n`, `${classId}\r\n`, `${classId}\u200b`, 0, {}, [],
    ])("rejects invalid class progress ID %j before fetch", async (id) => {
        const operation = getClassProgress(id as string);
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({ status: 400 });
        expect(fetcher).not.toHaveBeenCalled();
    });

    for (const [name, load] of [
        ["statistics", () => getPersonalStatistics("2026-11")],
        ["progress", () => getClassProgress(classId)],
    ] as const) {
        it.each([400, 403, 404, 409, 503])(`${name} preserves server failure %s instead of fabricating empty/partial zeros`, async (status) => {
            const errors = { Query: ["Request failed."] };
            fetcher.mockResolvedValue(json({ title: "Academic query failed", errors }, status));
            const operation = load();
            await expect(operation).rejects.toBeInstanceOf(ApiError);
            await expect(operation).rejects.toMatchObject({ status, errors,
                message: status === 403 ? "Bạn không có quyền thực hiện thao tác này." : "Academic query failed" });
            expect(fetcher).toHaveBeenCalledTimes(1);
            expect(getSession()).not.toBeNull();
        });
        it(`${name} exposes network failure without inventing zero data`, async () => {
            fetcher.mockRejectedValue(new TypeError("Failed to fetch"));
            await expect(load()).rejects.toMatchObject({ status: 0 });
            expect(fetcher).toHaveBeenCalledTimes(1);
        });
        it(`${name} requires the shared session before requesting business data`, async () => {
            setSession(null);
            await expect(load()).rejects.toMatchObject({ status: 401 });
            expect(fetcher).not.toHaveBeenCalled();
        });
    }
});
