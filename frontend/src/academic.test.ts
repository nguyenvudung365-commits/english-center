import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getSession, setSession, type Role, type Session } from "./api";
import {
    canManageAcademic,
    createClass,
    getClass,
    listCourses,
    listTeachers,
    validateClassDraft,
    type ClassDetail,
    type ClassDraft,
    type Course,
    type Teacher,
} from "./academic";

const courseId = "11111111-1111-1111-1111-111111111111";
const teacherId = "22222222-2222-2222-2222-222222222222";
const classId = "33333333-3333-3333-3333-333333333333";
const zeroId = "00000000-0000-0000-0000-000000000000";
const draft = (changes: Partial<ClassDraft> = {}): ClassDraft => ({
    code: "IELTS-01",
    name: "IELTS Foundation",
    courseId,
    teacherId,
    classType: "Main",
    startDate: "2028-02-29",
    endDate: "2028-03-01",
    maxStudents: "20",
    tuitionFee: "1000000",
    ...changes,
});
const detail: ClassDetail = {
    id: classId,
    code: "IELTS-01",
    name: "IELTS Foundation",
    courseId,
    courseCode: "IELTS",
    courseName: "Foundation",
    teacherId,
    teacherName: "Nguyễn An",
    classType: "Main",
    startDate: "2028-02-29",
    endDate: "2028-03-01",
    maxStudents: 20,
    tuitionFee: 1000000,
    status: "Ongoing",
    createdAt: "2026-11-07T08:00:00+00:00",
};
const courses: Course[] = [
    { id: courseId, code: "IELTS", name: "Foundation", description: null },
    { id: classId, code: "ENG", name: "English", description: "General English" },
];
const teachers: Teacher[] = [
    { id: teacherId, fullName: "Nguyễn An", email: "teacher@example.test" },
];
const session = (expired = false): Session => ({
    accessToken: "academic-access-token",
    accessTokenExpiresAt: new Date(
        Date.now() + (expired ? -60000 : 600000),
    ).toISOString(),
    refreshToken: "academic-refresh-token",
    refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
});
const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), {
        status,
        headers: { "Content-Type": "application/json" },
    });

const invalidCases: [keyof ClassDraft, string][] = [
    ...["", "   ", "A".repeat(51), "-ABC", ".ABC", "_ABC", "A B", "A/B", "LỚP"].map(
        (value): [keyof ClassDraft, string] => ["code", value],
    ),
    ...["", "   ", "N".repeat(201)].map(
        (value): [keyof ClassDraft, string] => ["name", value],
    ),
    ...["courseId", "teacherId"].flatMap((field) =>
        ["", "not-a-uuid", zeroId, "11111111111111111111111111111111"].map(
            (value): [keyof ClassDraft, string] => [field as keyof ClassDraft, value],
        ),
    ),
    ...(["courseId", "teacherId"] as const).flatMap((field) =>
        [draft()[field], zeroId].flatMap((value) =>
            ["\n", "\r\n"].map(
                (terminator): [keyof ClassDraft, string] => [field, value + terminator],
            ),
        ),
    ),
    ...["", "main", "supplementary", "1", "Other"].map(
        (value): [keyof ClassDraft, string] => ["classType", value],
    ),
    ...["startDate", "endDate"].flatMap((field) =>
        ["", "2027-02-29", "2028-02-30", "2028-04-31", "2028-13-01", "2028-00-01", "2028-01-00", "0000-01-01", "2028-2-01", "2028-02-29T00:00:00Z", "invalid"].map(
            (value): [keyof ClassDraft, string] => [field as keyof ClassDraft, value],
        ),
    ),
    ...(["startDate", "endDate"] as const).flatMap((field) =>
        ["\n", "\r\n"].map(
            (terminator): [keyof ClassDraft, string] => [field, draft()[field] + terminator],
        ),
    ),
    ["endDate", "2028-02-29"],
    ["endDate", "2028-02-28"],
    ...["", "   ", "0", "-1", "1.5", "2147483648", "NaN", "Infinity", "abc"].map(
        (value): [keyof ClassDraft, string] => ["maxStudents", value],
    ),
    ...["", "   ", "-1", "0.5", "1000000000000", "NaN", "Infinity", "abc"].map(
        (value): [keyof ClassDraft, string] => ["tuitionFee", value],
    ),
];

describe("Academic draft validation", () => {
    it.each<Partial<ClassDraft>>([
        {},
        { classType: "Supplementary" },
        { code: "  ielts.a_1-2  ", name: "  Lớp tiếng Anh  " },
        { code: "A", name: "N", maxStudents: "1", tuitionFee: "0" },
        { code: "A".repeat(50), name: "N".repeat(200), maxStudents: "2147483647", tuitionFee: "999999999999" },
        { startDate: "2000-02-29", endDate: "2000-03-01" },
        { startDate: "2028-12-31", endDate: "2029-01-01" },
    ])("accepts valid normalized, enum and boundary values: %j", (changes) => {
        expect(validateClassDraft(draft(changes))).toEqual({});
    });

    it.each(invalidCases)("rejects %s=%j with camelCase field errors", (field, value) => {
        const errors = validateClassDraft(draft({ [field]: value }));
        expect(errors[field]).toEqual(expect.arrayContaining([expect.any(String)]));
        expect(errors[field].every((message: string) => message.trim().length > 0)).toBe(true);
    });

    it("reports multiple invalid fields together without mutating entered values", () => {
        const input = draft({ code: "  ", name: "", maxStudents: "", tuitionFee: "" });
        const original = { ...input };
        expect(validateClassDraft(input)).toMatchObject({
            code: expect.any(Array),
            name: expect.any(Array),
            maxStudents: expect.any(Array),
            tuitionFee: expect.any(Array),
        });
        expect(input).toEqual(original);
    });
});

describe("Academic manager role policy", () => {
    it.each<[Role, boolean]>([
        ["Admin", true],
        ["Staff", true],
        ["Teacher", false],
        ["Student", false],
    ])("allows manager operations for %s: %s", (role, expected) => {
        expect(canManageAcademic(role)).toBe(expected);
    });
});

describe("Academic API contract through the real session client", () => {
    const fetcher = vi.fn<typeof fetch>();
    beforeEach(() => {
        fetcher.mockReset();
        vi.stubGlobal("fetch", fetcher);
        setSession(session());
    });
    afterEach(() => {
        setSession(null);
        vi.unstubAllGlobals();
    });

    function expectRead(path: string) {
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe(path);
        expect(init?.method ?? "GET").toBe("GET");
        expect(init?.body).toBeUndefined();
        expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer academic-access-token");
    }

    it("GETs the course catalog unchanged, including nullable descriptions", async () => {
        fetcher.mockResolvedValue(json(courses));
        expect(await listCourses()).toEqual(courses);
        expectRead("/api/academic/courses");
    });

    it("GETs the teacher catalog unchanged", async () => {
        fetcher.mockResolvedValue(json(teachers));
        expect(await listTeachers()).toEqual(teachers);
        expectRead("/api/academic/teachers");
    });

    it("GETs only the requested class detail and preserves authoritative fields/status", async () => {
        fetcher.mockResolvedValue(json(detail));
        expect(await getClass(classId)).toEqual(detail);
        expectRead(`/api/academic/classes/${classId}`);
    });

    it.each([listCourses, listTeachers])("keeps a truly empty catalog empty", async (load) => {
        fetcher.mockResolvedValue(json([]));
        expect(await load()).toEqual([]);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it.each(["Main", "Supplementary"])("POSTs the exact normalized %s payload with bearer credentials", async (classType) => {
        const input = draft({ code: "  ielts.a_1-2  ", name: "  Lớp tiếng Anh  ", classType });
        const original = { ...input };
        fetcher.mockResolvedValue(json({ ...detail, classType }, 201));
        expect(await createClass(input)).toEqual({ ...detail, classType });
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/academic/classes");
        expect(init?.method).toBe("POST");
        const headers = new Headers(init?.headers);
        expect(headers.get("Authorization")).toBe("Bearer academic-access-token");
        expect(headers.get("Content-Type")).toBe("application/json");
        expect(JSON.parse(String(init?.body))).toEqual({
            code: "IELTS.A_1-2",
            name: "Lớp tiếng Anh",
            courseId,
            teacherId,
            classType,
            startDate: "2028-02-29",
            endDate: "2028-03-01",
            maxStudents: 20,
            tuitionFee: 1000000,
        });
        expect(input).toEqual(original);
    });

    it("POSTs zero fee and numeric ceilings as JSON numbers", async () => {
        fetcher.mockImplementation(async () => json(detail, 201));
        await createClass(draft({ maxStudents: "2147483647", tuitionFee: "999999999999" }));
        expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toMatchObject({
            maxStudents: 2147483647, tuitionFee: 999999999999,
        });
        await createClass(draft({ maxStudents: "1", tuitionFee: "0" }));
        expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toMatchObject({
            maxStudents: 1, tuitionFee: 0,
        });
    });

    it.each(invalidCases)("rejects invalid %s=%j before submitting", async (field, value) => {
        const operation = createClass(draft({ [field]: value }));
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({
            status: 400,
            errors: { [field]: expect.arrayContaining([expect.any(String)]) },
        });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it.each([
        "", zeroId, "not-a-uuid", "../courses", `${classId}/extra`, "33333333333333333333333333333333",
        ...[classId, zeroId].flatMap((id) => ["\n", "\r\n"].map((terminator) => id + terminator)),
    ])("rejects invalid detail ID %j without a request", async (id) => {
        const operation = getClass(id);
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({ status: 400 });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("preserves server 400 title and PascalCase field errors without resubmitting", async () => {
        const errors = { Code: ["Invalid code."], TeacherId: ["Teacher not found."] };
        fetcher.mockResolvedValue(json({ title: "Academic validation failed.", errors }, 400));
        const operation = createClass(draft());
        await expect(operation).rejects.toBeInstanceOf(ApiError);
        await expect(operation).rejects.toMatchObject({ status: 400, message: "Academic validation failed.", errors });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("preserves duplicate-code 409 message/field errors and entered values", async () => {
        const input = draft({ code: "  duplicate  " });
        const original = { ...input };
        const errors = { Code: ["Class code already exists."] };
        fetcher.mockResolvedValue(json({ title: "Academic data already exists.", errors }, 409));
        await expect(createClass(input)).rejects.toMatchObject({
            status: 409, message: "Academic data already exists.", errors,
        });
        expect(input).toEqual(original);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("preserves shared 403 handling and does not refresh", async () => {
        fetcher.mockResolvedValue(json({}, 403));
        await expect(listTeachers()).rejects.toMatchObject({
            status: 403, message: "Bạn không có quyền thực hiện thao tác này.",
        });
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(getSession()).not.toBeNull();
    });

    it("preserves a class-detail 404 without inventing a record", async () => {
        fetcher.mockResolvedValue(json({ title: "Class not found." }, 404));
        await expect(getClass(classId)).rejects.toMatchObject({ status: 404, message: "Class not found." });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("exposes shared network errors without retrying a mutation", async () => {
        fetcher.mockRejectedValue(new TypeError("Failed to fetch"));
        await expect(createClass(draft())).rejects.toMatchObject({
            status: 0, message: "Không kết nối được máy chủ. Kiểm tra mạng và thử lại.",
        });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("does not issue a business request when no session exists", async () => {
        setSession(null);
        await expect(listCourses()).rejects.toMatchObject({ status: 401 });
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("uses the shared refresh before reading with an expired token", async () => {
        setSession(session(true));
        const rotated = { ...session(), accessToken: "rotated-access", refreshToken: "rotated-refresh" };
        fetcher.mockResolvedValueOnce(json(rotated)).mockResolvedValueOnce(json(courses));
        expect(await listCourses()).toEqual(courses);
        expect(fetcher.mock.calls.map(([url]) => url)).toEqual([
            "/api/identity/auth/refresh", "/api/academic/courses",
        ]);
        expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ refreshToken: "academic-refresh-token" });
        expect(new Headers(fetcher.mock.calls[1][1]?.headers).get("Authorization")).toBe("Bearer rotated-access");
    });

    it("keeps shared 401 retry bounded and clears a rejected session", async () => {
        fetcher.mockResolvedValueOnce(json({}, 401))
            .mockResolvedValueOnce(json(session()))
            .mockResolvedValueOnce(json({}, 401));
        await expect(getClass(classId)).rejects.toMatchObject({ status: 401 });
        expect(fetcher.mock.calls.map(([url]) => url)).toEqual([
            `/api/academic/classes/${classId}`, "/api/identity/auth/refresh", `/api/academic/classes/${classId}`,
        ]);
        expect(getSession()).toBeNull();
    });
});
