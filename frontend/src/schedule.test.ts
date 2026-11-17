import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, setSession } from "./api";
import {
    calendarRange,
    centerDate,
    createSession,
    getScheduledSession,
    listSessions,
    navigateCalendar,
    sessionsForDay,
    validateSessionDraft,
    vietnamLocalToUtc,
    type ScheduledSession,
    type SessionDraft,
} from "./schedule";

const classId = "11111111-1111-4111-8111-111111111111";
const id = "22222222-2222-4222-8222-222222222222";
const item: ScheduledSession = {
    id, classId, classCode: "A-01", className: "Lớp Việt 🗓",
    teacherId: "33333333-3333-4333-8333-333333333333", teacherName: "Teacher",
    unit: "Unit 1", lesson: "Bài học", skills: ["Listening", "Speaking"],
    sessionType: "Regular", examSlot: null,
    startsAtUtc: "2027-01-01T16:30:00Z", endsAtUtc: "2027-01-01T17:30:00Z",
    createdAtUtc: "2026-11-13T00:00:00Z",
};
const draft = (changes: Partial<SessionDraft> = {}): SessionDraft => ({
    classId, unit: " Unit 1 ", lesson: " Bài học ", skills: ["Speaking", "Listening"],
    sessionType: "Regular", examSlot: " ",
    startsAtLocal: "2027-01-01T23:30", endsAtLocal: "2027-01-02T00:30", ...changes,
});
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { "Content-Type": "application/json" },
});

describe("Fixed UTC+7 schedule calendar", () => {
    it.each([
        ["2026-12-31T16:59:59Z", "2026-12-31"],
        ["2026-12-31T17:00:00Z", "2027-01-01"],
        ["2027-01-01T00:00:00+07:00", "2027-01-01"],
    ])("maps %s to center date %s independently of host timezone", (instant, expected) => {
        expect(centerDate(instant)).toBe(expected);
    });

    it("returns a Monday week crossing a year, including every day exactly once", () => {
        expect(calendarRange("2027-01-03", "week")).toEqual({
            fromUtc: "2026-12-27T17:00:00.000Z", toUtc: "2027-01-03T17:00:00.000Z",
            days: ["2026-12-28", "2026-12-29", "2026-12-30", "2026-12-31", "2027-01-01", "2027-01-02", "2027-01-03"],
        });
    });

    it("returns a leap-month Monday grid with preceding/following spillover weeks", () => {
        const range = calendarRange("2028-02-29", "month");
        expect(range.fromUtc).toBe("2028-01-30T17:00:00.000Z");
        expect(range.toUtc).toBe("2028-03-05T17:00:00.000Z");
        expect(range.days).toHaveLength(35);
        expect(range.days[0]).toBe("2028-01-31");
        expect(range.days.at(-1)).toBe("2028-03-05");
        expect(range.days).toContain("2028-02-29");
        expect(new Set(range.days).size).toBe(35);
        expect(Date.parse(range.toUtc) - Date.parse(range.fromUtc)).toBe(35 * 86400000);
    });

    it("does not add unnecessary spillover when a month starts Monday and ends Sunday", () => {
        const range = calendarRange("2027-02-15", "month");
        expect(range.days).toHaveLength(28);
        expect(range.days[0]).toBe("2027-02-01");
        expect(range.days.at(-1)).toBe("2027-02-28");
    });

    it.each([
        ["2027-12-29", "week", 1, "2028-01-05"],
        ["2027-01-03", "week", -1, "2026-12-27"],
        ["2028-01-31", "month", 1, "2028-02-29"],
        ["2027-03-31", "month", -1, "2027-02-28"],
        ["2027-12-31", "month", 1, "2028-01-31"],
    ] as const)("navigates %s/%s/%s to %s without skipping months", (anchor, mode, direction, expected) => {
        expect(navigateCalendar(anchor, mode, direction)).toBe(expected);
    });

    it("converts datetime-local using explicit +07, not browser-local Date parsing", () => {
        expect(vietnamLocalToUtc("2028-02-29T00:00")).toBe("2028-02-28T17:00:00.000Z");
        expect(vietnamLocalToUtc("2027-01-01T23:30")).toBe("2027-01-01T16:30:00.000Z");
    });

    it.each(["", "2027-02-29T10:00", "2028-02-30T10:00", "2028-02-29T24:00", "2028-02-29T10:00Z"])(
        "rejects invalid or already-offset datetime-local %j instead of rolling it over", (value) => {
            expect(() => vietnamLocalToUtc(value)).toThrow();
        },
    );

    it("buckets a cross-midnight session in both intersecting days, excludes touching bounds, and sorts without mutation", () => {
        const earlier = { ...item, id: "44444444-4444-4444-8444-444444444444", startsAtUtc: "2027-01-01T15:30:00Z", endsAtUtc: item.startsAtUtc };
        const next = { ...item, id: "55555555-5555-4555-8555-555555555555", startsAtUtc: "2027-01-02T17:00:00Z", endsAtUtc: "2027-01-02T18:00:00Z" };
        const previous = { ...item, id: "66666666-6666-4666-8666-666666666666", startsAtUtc: "2026-12-31T16:00:00Z", endsAtUtc: "2026-12-31T17:00:00Z" };
        const values = [next, item, earlier, previous];
        const original = structuredClone(values);
        expect(sessionsForDay(values, "2027-01-01").map(x => x.id)).toEqual([earlier.id, item.id]);
        expect(sessionsForDay(values, "2027-01-02").map(x => x.id)).toEqual([item.id]);
        expect(sessionsForDay([], "2027-01-02")).toEqual([]);
        expect(values).toEqual(original);
    });
});

describe("Session draft validation and HTTP contract", () => {
    const fetcher = vi.fn<typeof fetch>();
    beforeEach(() => {
        fetcher.mockReset();
        vi.stubGlobal("fetch", fetcher);
        setSession({ accessToken: "schedule-access", refreshToken: "schedule-refresh",
            accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
            refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString() });
    });
    afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

    it("GETs detail unchanged with shared bearer credentials", async () => {
        fetcher.mockResolvedValue(json(item));
        expect(await getScheduledSession(id)).toEqual(item);
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe(`/api/academic/sessions/${id}`);
        expect(init?.method ?? "GET").toBe("GET");
        expect(init?.body).toBeUndefined();
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer schedule-access");
    });

    it("encodes explicit offset range bounds and class filter without changing their meaning", async () => {
        const query = { fromUtc: "2027-01-01T00:00:00+07:00", toUtc: "2027-01-03T00:00:00+07:00", classId };
        fetcher.mockResolvedValue(json([item]));
        expect(await listSessions(query)).toEqual([item]);
        const [url, init] = fetcher.mock.calls[0];
        const parsed = new URL(String(url), "https://example.test");
        expect(parsed.pathname).toBe("/api/academic/sessions");
        expect(Object.fromEntries(parsed.searchParams)).toEqual(query);
        expect(init?.method ?? "GET").toBe("GET");
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("allows exactly 93 elapsed days, omits absent classId, and preserves empty range data", async () => {
        fetcher.mockResolvedValue(json([]));
        expect(await listSessions({ fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-04-04T00:00:00Z" })).toEqual([]);
        const url = new URL(String(fetcher.mock.calls[0][0]), "https://example.test");
        expect([...url.searchParams.keys()].sort()).toEqual(["fromUtc", "toUtc"]);
    });

    it("POSTs only frozen API fields, trims text, normalizes timestamps and skill order without mutating draft", async () => {
        const input = draft(); const original = structuredClone(input);
        fetcher.mockResolvedValue(json(item, 201));
        expect(validateSessionDraft(input)).toEqual({});
        expect(await createSession(input)).toEqual(item);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/academic/sessions");
        expect(init?.method).toBe("POST");
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer schedule-access");
        expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
        expect(JSON.parse(String(init?.body))).toEqual({ classId, unit: "Unit 1", lesson: "Bài học",
            skills: ["Listening", "Speaking"], sessionType: "Regular", examSlot: null,
            startsAtUtc: "2027-01-01T16:30:00.000Z", endsAtUtc: "2027-01-01T17:30:00.000Z" });
        expect(input).toEqual(original);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("accepts a test slot and text ceilings", async () => {
        fetcher.mockResolvedValue(json({ ...item, sessionType: "FinalTest", examSlot: "Ca sáng" }, 201));
        const input = draft({ unit: "u".repeat(100), lesson: "l".repeat(200), sessionType: "FinalTest", examSlot: " Ca sáng " });
        expect(validateSessionDraft(input)).toEqual({});
        await createSession(input);
        expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toMatchObject({ sessionType: "FinalTest", examSlot: "Ca sáng" });
    });

    it.each<[keyof SessionDraft, unknown]>([
        ["classId", "00000000-0000-0000-0000-000000000000"],
        ["unit", " "], ["unit", "u".repeat(101)], ["lesson", "l".repeat(201)],
        ["skills", []], ["skills", ["Listening", "Listening"]], ["skills", ["Unknown"]],
        ["sessionType", "0"], ["examSlot", "Regular cannot have slot"],
        ["startsAtLocal", "2027-02-29T10:00"], ["endsAtLocal", "2027-01-01T23:30"],
    ])("rejects invalid %s before POST and attributes the error to that field", async (field, value) => {
        const input = { ...draft(), [field]: value } as SessionDraft;
        expect(validateSessionDraft(input)[field]).toEqual(expect.arrayContaining([expect.any(String)]));
        await expect(createSession(input)).rejects.toMatchObject({ status: 400, errors: { [field]: expect.any(Array) } });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("rejects invalid detail IDs before fetching", async () => {
        await expect(getScheduledSession("../classes")).rejects.toBeInstanceOf(ApiError);
        expect(fetcher).not.toHaveBeenCalled();
    });

    it.each([
        { fromUtc: "2027-01-01T00:00:00", toUtc: "2027-01-02T00:00:00Z" },
        { fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-01-01T00:00:00Z" },
        { fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-04-04T00:00:00.001Z" },
        { fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-01-02T00:00:00Z", classId: "invalid" },
    ])("rejects invalid range without a request: %j", async (query) => {
        await expect(listSessions(query)).rejects.toMatchObject({ status: 400 });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("preserves overlap409 and entered values without blindly retrying POST", async () => {
        const input = draft(); const original = structuredClone(input);
        const errors = { StartsAtUtc: ["Teacher already booked."] };
        fetcher.mockResolvedValue(json({ title: "Teacher overlap", errors }, 409));
        await expect(createSession(input)).rejects.toMatchObject({ status: 409, message: "Teacher overlap", errors });
        expect(input).toEqual(original);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("preserves network errors instead of returning an empty range", async () => {
        fetcher.mockRejectedValue(new TypeError("Failed to fetch"));
        await expect(listSessions({ fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-01-02T00:00:00Z" })).rejects.toMatchObject({ status: 0 });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });
});

describe("Sub-millisecond schedule precision", () => {
    it.each([
        ["2027-01-01T17:00:00Z", false],
        ["2027-01-01T17:00:00.000001Z", true],
        ["2027-01-01T17:00:00.0000001Z", true],
    ] as const)("buckets an end at %s using the exclusive Vietnam midnight boundary", (endsAtUtc, included) => {
        const session = { ...item, endsAtUtc };
        expect(sessionsForDay([session], "2027-01-02")).toEqual(included ? [session] : []);
    });

    it.each([
        ["2027-01-01T16:30:00.000001Z", "2027-01-01T16:30:00.000002Z"],
        ["2027-01-01T16:30:00.0000001Z", "2027-01-01T16:30:00.0000002Z"],
    ])("sorts %s before %s even when IDs sort in reverse", (earlierStart, laterStart) => {
        const earlier = { ...item, id: "00000000-0000-0000-0000-000000000002", startsAtUtc: earlierStart };
        const later = { ...item, id: "00000000-0000-0000-0000-000000000001", startsAtUtc: laterStart };
        const sessions = [{ ...later, id: `${id.slice(0, -1)}1` }, { ...earlier, id: `${id.slice(0, -1)}2` }];
        const original = structuredClone(sessions);
        expect(sessionsForDay(sessions, "2027-01-01")).toEqual([original[1], original[0]]);
        expect(sessions).toEqual(original);
    });

    describe("range validation", () => {
        const fetcher = vi.fn<typeof fetch>();
        beforeEach(() => {
            fetcher.mockReset();
            vi.stubGlobal("fetch", fetcher);
            setSession({ accessToken: "schedule-access", refreshToken: "schedule-refresh",
                accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
                refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString() });
        });
        afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

        it.each(["2027-04-04T00:00:00.000001Z", "2027-04-04T00:00:00.0000001Z"])(
            "rejects a range beyond 93 days ending at %s before fetching", async (toUtc) => {
                fetcher.mockResolvedValue(json([]));
                await expect(listSessions({ fromUtc: "2027-01-01T00:00:00Z", toUtc })).rejects.toMatchObject({ status: 400 });
                expect(fetcher).not.toHaveBeenCalled();
            },
        );

        it.each([
            { fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-01-01T00:00:00.000001Z" },
            { fromUtc: "2027-01-01T00:00:00.0000010Z", toUtc: "2027-01-01T00:00:00.0000020Z" },
        ])(
            "accepts a positive one-microsecond range with aligned bounds unchanged: %j", async (query) => {
                fetcher.mockResolvedValue(json([]));
                expect(await listSessions(query)).toEqual([]);
                expect(fetcher).toHaveBeenCalledTimes(1);
                const url = new URL(String(fetcher.mock.calls[0][0]), "https://example.test");
                expect(Object.fromEntries(url.searchParams)).toEqual(query);
            },
        );

        it.each([
            { fromUtc: "2027-01-01T00:00:00.0000001Z", toUtc: "2027-01-02T00:00:00Z" },
            { fromUtc: "2027-01-01T00:00:00Z", toUtc: "2027-01-01T00:00:00.0000001Z" },
        ])("rejects a nonzero seventh fractional digit in either request bound: %j", async (query) => {
            fetcher.mockResolvedValue(json([]));
            const request = listSessions(query);
            await expect(request).rejects.toBeInstanceOf(ApiError);
            await expect(request).rejects.toMatchObject({ status: 400 });
            expect(fetcher).not.toHaveBeenCalled();
        });
    });
});
