import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    assignmentKindLabel, assignmentStatusLabel, canGrade, canManageCoursework, createAssignment, createPractice, deletePractice,
    emptyAssignmentDraft, formatScore, gradeSubmission, isAssignmentLocked, isGraded, listClassAssignments, listGradeHistory,
    listMyAssignments, listPractice, parseScore, publishAssignment, scoreInput, studentLabel, studentScoreText, submissionStatusLabel,
    updateAssignment, updatePractice, validateAssignmentDraft, validateGradeDraft, validatePracticeDraft,
    type AssignmentDraft, type Practice,
} from "./coursework";
import type { Assignment, Submission } from "./submissions";

const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });
const assignment = (patch: Partial<Assignment> = {}): Assignment => ({
    id: "11111111-1111-1111-1111-111111111111", classId: "22222222-2222-2222-2222-222222222222", title: "T", description: "D",
    opensAtUtc: "2026-11-16T01:00:00Z", closesAtUtc: "2026-11-18T16:59:00Z", kind: "Homework", unit: null, lesson: null, sessionId: null,
    isLegacy: false, status: "Draft", publishedAt: null, createdAt: null, version: 3, ...patch,
});
const submission: Submission = {
    id: "33333333-3333-3333-3333-333333333333", assignmentId: "a", studentId: "s", revision: "44444444-4444-4444-4444-444444444444",
    submittedAt: "2026-11-17T00:00:00Z", files: [], score: null, feedback: null, gradeVersion: 0, gradedAt: null, gradedBy: null,
};
const draft = (patch: Partial<AssignmentDraft> = {}): AssignmentDraft => ({
    ...emptyAssignmentDraft(), title: "Bài 1", opensLocal: "2026-11-17T08:00", closesLocal: "2026-11-18T20:00", ...patch,
});
function mockFetch() {
    const fetcher = vi.fn().mockImplementation(async () => new Response("{}"));
    vi.stubGlobal("fetch", fetcher);
    setSession(session);
    return fetcher;
}

describe("điểm 0–10 bước 0,1", () => {
    it("nhận điểm hợp lệ, cả dấu chấm và dấu phẩy", () => {
        expect(parseScore("0")).toBe(0);
        expect(parseScore("8,5")).toBe(8.5);
        expect(parseScore(" 8.5 ")).toBe(8.5);
        expect(parseScore("10")).toBe(10);
        expect(parseScore("10,0")).toBe(10);
        expect(parseScore("0.1")).toBe(0.1);
    });
    it("từ chối điểm ngoài khoảng, sai bước hoặc sai định dạng", () => {
        for (const bad of ["", " ", "-1", "10,1", "11", "8,55", "8.", ".5", "abc", "1e1", "08", "8,5,1", "NaN"])
            expect(parseScore(bad), bad).toBeNull();
    });
    it("kiểm tra điểm và nhận xét", () => {
        expect(validateGradeDraft({ score: "7,5", feedback: "Tốt" })).toEqual({});
        expect(validateGradeDraft({ score: "7,5", feedback: "x".repeat(2000) })).toEqual({});
        expect(validateGradeDraft({ score: "", feedback: "" })).toHaveProperty("score");
        expect(validateGradeDraft({ score: "7,5", feedback: "x".repeat(2001) })).toHaveProperty("feedback");
    });
    it("chưa chấm là 'Chưa chấm', không phải 0", () => {
        expect(formatScore(null)).toBe("Chưa chấm");
        expect(formatScore(undefined)).toBe("Chưa chấm");
        expect(formatScore(0)).toBe("0,0");
        expect(formatScore(8.5)).toBe("8,5");
        expect(formatScore(10)).toBe("10,0");
        expect(scoreInput(null)).toBe("");
        expect(scoreInput(9)).toBe("9,0");
    });
    it("học viên chỉ thấy điểm khi đã chấm", () => {
        expect(studentScoreText("Graded", 7.5)).toBe("7,5");
        expect(studentScoreText("Submitted", null)).toBe("Chưa chấm");
        expect(studentScoreText("Submitted", 7.5)).toBe("Chưa chấm");
        expect(studentScoreText("NotSubmitted", null)).toBeNull();
        expect(studentScoreText("Missed", null)).toBeNull();
        expect(isGraded({ gradeVersion: 0 })).toBe(false);
        expect(isGraded({ gradeVersion: 1 })).toBe(true);
    });
});

describe("vai trò và trạng thái", () => {
    it("chỉ Teacher chấm; Staff/Admin quản lý nhưng không chấm; Student không quản lý", () => {
        expect(canGrade("Teacher")).toBe(true);
        expect(canGrade("Staff")).toBe(false);
        expect(canGrade("Admin")).toBe(false);
        expect(canGrade("Student")).toBe(false);
        expect(canManageCoursework("Staff")).toBe(true);
        expect(canManageCoursework("Teacher")).toBe(true);
        expect(canManageCoursework("Student")).toBe(false);
    });
    it("nhãn trạng thái, loại bài, trạng thái nộp", () => {
        expect(assignmentStatusLabel("Open")).toBe("Đang mở");
        expect(assignmentStatusLabel("Closed")).toBe("Đã đóng");
        expect(assignmentStatusLabel("Draft")).toContain("Nháp");
        expect(assignmentStatusLabel("Lạ")).toBe("Lạ");
        expect(assignmentKindLabel("InClass")).toBe("Trên lớp");
        expect(assignmentKindLabel("Homework")).toBe("Về nhà");
        expect(submissionStatusLabel("Graded")).toBe("Đã chấm");
        expect(submissionStatusLabel("Exempted")).toBe("Được miễn");
    });
    it("bài đã công bố hoặc legacy bị khóa các trường hạn/loại/buổi", () => {
        expect(isAssignmentLocked(assignment({ status: "Draft" }))).toBe(false);
        expect(isAssignmentLocked(assignment({ status: "Open" }))).toBe(true);
        expect(isAssignmentLocked(assignment({ status: "Closed" }))).toBe(true);
        expect(isAssignmentLocked(assignment({ status: "Open", isLegacy: true }))).toBe(true);
    });
});

describe("form bài tập và luyện tập", () => {
    it("form bài tập hợp lệ", () => {
        expect(validateAssignmentDraft(draft())).toEqual({});
        expect(validateAssignmentDraft(draft({ kind: "InClass", unit: "u".repeat(100), lesson: "l".repeat(100) }))).toEqual({});
    });
    it("bắt lỗi tiêu đề, hướng dẫn, nhãn, loại và thời gian", () => {
        expect(validateAssignmentDraft(draft({ title: "  " }))).toHaveProperty("title");
        expect(validateAssignmentDraft(draft({ title: "a".repeat(201) }))).toHaveProperty("title");
        expect(validateAssignmentDraft(draft({ description: "a".repeat(5001) }))).toHaveProperty("description");
        expect(validateAssignmentDraft(draft({ unit: "u".repeat(101) }))).toHaveProperty("unit");
        expect(validateAssignmentDraft(draft({ lesson: "l".repeat(101) }))).toHaveProperty("lesson");
        expect(validateAssignmentDraft(draft({ kind: "Khác" }))).toHaveProperty("kind");
        expect(validateAssignmentDraft(draft({ opensLocal: "" }))).toHaveProperty("opensLocal");
        expect(validateAssignmentDraft(draft({ closesLocal: "2026-02-30T10:00" }))).toHaveProperty("closesLocal");
        expect(validateAssignmentDraft(draft({ closesLocal: "2026-11-17T08:00" }))).toHaveProperty("closesLocal");
        expect(validateAssignmentDraft(draft({ closesLocal: "2026-11-17T07:00" }))).toHaveProperty("closesLocal");
    });
    it("công bố ngay bắt buộc chọn buổi học; lưu nháp hoặc sửa bài thì không", () => {
        expect(validateAssignmentDraft(draft(), false, true)).toHaveProperty("sessionId");
        expect(validateAssignmentDraft(draft({ sessionId: "55555555-5555-5555-5555-555555555555" }), false, true)).toEqual({});
        expect(validateAssignmentDraft(draft(), false, false)).toEqual({});
    });
    it("tên học viên kèm mã; thiếu tên thì để giao diện hiện ID", () => {
        expect(studentLabel({ studentName: "Nguyễn Văn A", studentCode: "HV0001" })).toBe("Nguyễn Văn A (HV0001)");
        expect(studentLabel({ studentName: "Nguyễn Văn A", studentCode: null })).toBe("Nguyễn Văn A");
        expect(studentLabel({ studentName: "  ", studentCode: "HV0001" })).toBeNull();
        expect(studentLabel({})).toBeNull();
    });
    it("bài đã công bố không kiểm tra loại/thời gian nhưng vẫn kiểm tra tiêu đề", () => {
        expect(validateAssignmentDraft(draft({ opensLocal: "", closesLocal: "", kind: "" }), true)).toEqual({});
        expect(validateAssignmentDraft(draft({ title: "" }), true)).toHaveProperty("title");
    });
    it("form luyện tập", () => {
        expect(validatePracticeDraft({ title: "Bài", content: "Nội dung\nnhiều dòng" })).toEqual({});
        expect(validatePracticeDraft({ title: "a".repeat(200), content: "x".repeat(20000) })).toEqual({});
        expect(validatePracticeDraft({ title: " ", content: " " })).toHaveProperty("title");
        expect(validatePracticeDraft({ title: " ", content: " " })).toHaveProperty("content");
        expect(validatePracticeDraft({ title: "a".repeat(201), content: "x" })).toHaveProperty("title");
        expect(validatePracticeDraft({ title: "a", content: "x".repeat(20001) })).toHaveProperty("content");
    });
});

describe("hợp đồng API", () => {
    it("chấm điểm gửi expectedGradeVersion và expectedRevision", async () => {
        const fetcher = mockFetch();
        await gradeSubmission({ ...submission, gradeVersion: 2 }, { score: "8,5", feedback: " Tốt " });
        expect(fetcher.mock.calls[0][0]).toBe(`/api/academic/submissions/${submission.id}/grade`);
        expect(fetcher.mock.calls[0][1].method).toBe("PUT");
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ score: 8.5, feedback: "Tốt", expectedGradeVersion: 2, expectedRevision: submission.revision });
        await gradeSubmission(submission, { score: "0", feedback: "  " });
        expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ score: 0, feedback: null, expectedGradeVersion: 0, expectedRevision: submission.revision });
        await listGradeHistory(submission.id);
        expect(fetcher.mock.calls[2][0]).toBe(`/api/academic/submissions/${submission.id}/grades`);
    });
    it("tạo bài: giờ VN, publish theo ô lưu nháp", async () => {
        const fetcher = mockFetch();
        await createAssignment("c", draft({ unit: " Unit 1 ", saveAsDraft: true }));
        const body = JSON.parse(fetcher.mock.calls[0][1].body);
        expect(fetcher.mock.calls[0][0]).toBe("/api/academic/assignments");
        expect(body).toMatchObject({ classId: "c", title: "Bài 1", kind: "Homework", unit: "Unit 1", lesson: null, sessionId: null, publish: false });
        expect(body.opensAtUtc).toBe("2026-11-17T08:00:00+07:00");
        await createAssignment("c", draft());
        expect(JSON.parse(fetcher.mock.calls[1][1].body).publish).toBe(true);
    });
    it("sửa bài đã công bố giữ nguyên hạn/loại/buổi và gửi expectedVersion", async () => {
        const fetcher = mockFetch();
        const item = assignment({ status: "Open", kind: "InClass", sessionId: "s1" });
        await updateAssignment(item, draft({ title: "Mới", kind: "Homework", sessionId: "khác", opensLocal: "2030-01-01T00:00" }));
        const body = JSON.parse(fetcher.mock.calls[0][1].body);
        expect(fetcher.mock.calls[0][0]).toBe(`/api/academic/assignments/${item.id}`);
        expect(body).toMatchObject({ title: "Mới", kind: "InClass", sessionId: "s1", opensAtUtc: item.opensAtUtc, closesAtUtc: item.closesAtUtc, expectedVersion: 3 });
    });
    it("công bố gửi expectedVersion; danh sách lọc đúng tham số", async () => {
        const fetcher = mockFetch();
        const item = assignment();
        await publishAssignment(item);
        expect(fetcher.mock.calls[0][0]).toBe(`/api/academic/assignments/${item.id}/publish`);
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ expectedVersion: 3 });
        await listClassAssignments("c", { search: " abc ", status: "Open", kind: "" });
        expect(fetcher.mock.calls[1][0]).toBe("/api/academic/assignments?classId=c&search=abc&status=Open");
        await listMyAssignments({ submissionStatus: "Graded", page: 2 });
        expect(fetcher.mock.calls[2][0]).toBe("/api/academic/assignments/mine?submissionStatus=Graded&page=2&pageSize=20");
    });
    it("luyện tập: danh sách, tạo, sửa và xóa gửi version", async () => {
        const fetcher = mockFetch();
        const practice = { id: "p1", classId: "c", title: "t", content: "x", createdBy: "u", createdByName: "n", createdAt: "", updatedAt: null, updatedBy: null, version: 4 } satisfies Practice;
        await listPractice("c", " từ khóa ", 2);
        expect(decodeURIComponent(fetcher.mock.calls[0][0])).toBe("/api/academic/practice?classId=c&search=từ+khóa&page=2&pageSize=10");
        await createPractice("c", { title: " T ", content: " N " });
        expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ classId: "c", title: "T", content: "N" });
        await updatePractice(practice, { title: "T2", content: "N2" });
        expect(fetcher.mock.calls[2][1].method).toBe("PUT");
        expect(JSON.parse(fetcher.mock.calls[2][1].body)).toEqual({ title: "T2", content: "N2", expectedVersion: 4 });
        await deletePractice(practice);
        expect(fetcher.mock.calls[3][0]).toBe("/api/academic/practice/p1?expectedVersion=4");
        expect(fetcher.mock.calls[3][1].method).toBe("DELETE");
    });
});
