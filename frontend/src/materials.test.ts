import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    canDownloadPrompt, createAssignment, createMaterial, emptyAssignmentDraft, emptyMaterialDraft, listMaterials, materialDownload,
    promptDownload, safeDownloadUrl, updateAssignment, validateAssignmentDraft, validateMaterialDraft, type AssignmentDraft,
} from "./coursework";
import { uploadClassFile, validateClassFile, type Assignment } from "./submissions";

const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

const fileId = "20000000-0000-0000-0000-000000000001";
const file = (name: string, size = 10, type = "application/pdf") => new File([new Uint8Array(size)], name, { type });
const exam = (patch: Partial<Assignment> = {}): Assignment => ({
    id: "11111111-1111-1111-1111-111111111111", classId: "c", title: "Thi", description: "", opensAtUtc: "2026-11-16T01:00:00Z",
    closesAtUtc: "2026-11-18T16:59:00Z", kind: "Exam", unit: null, lesson: null, sessionId: "s", isLegacy: false, status: "Open",
    publishedAt: "2026-11-15T00:00:00Z", createdAt: null, version: 2, promptFileName: "de.pdf", ...patch,
});
const draft = (patch: Partial<AssignmentDraft> = {}): AssignmentDraft => ({
    ...emptyAssignmentDraft(), title: "Thi giữa kỳ", kind: "Exam", opensLocal: "2026-11-17T08:00", closesLocal: "2026-11-18T20:00",
    sessionId: "s", promptFile: file("de.pdf"), ...patch,
});

// presign → PUT lên kho → confirm → thao tác Academic. Trả về danh sách lời gọi API (không tính PUT).
function mockServer(onAcademic: (path: string, init: RequestInit) => unknown = () => ({ id: "x" })) {
    const calls: { path: string; body: any; method?: string }[] = [];
    const puts: string[] = [];
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async (input: any, init: any = {}) => {
        const path = String(input);
        if (!path.startsWith("/api/")) { puts.push(path); return new Response(null, { status: 200 }); }
        calls.push({ path, method: init.method, body: init.body ? JSON.parse(init.body) : undefined });
        if (path.endsWith("/presign")) return Response.json({ fileId, url: "https://storage.example/up", method: "PUT", requiredHeaders: { "Content-Type": "application/pdf" } });
        if (path.endsWith("/confirm")) return Response.json({ fileId });
        return Response.json(onAcademic(path, init));
    }));
    setSession(session);
    return { calls, puts };
}

describe("đề thi PDF", () => {
    it("chỉ nhận PDF cho đề thi, tài liệu nhận thêm DOCX/MP3", () => {
        expect(validateClassFile(file("de.pdf"), "exam")).toBeNull();
        expect(validateClassFile(file("de.docx", 10, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), "exam")).not.toBeNull();
        expect(validateClassFile(file("de.docx", 10, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), "material")).toBeNull();
        expect(validateClassFile(file("big.pdf", 20 * 1024 * 1024 + 1), "exam")).not.toBeNull();
        expect(validateClassFile(file("../x.pdf"), "material")).not.toBeNull();
    });
    it("bài thi bắt buộc có đề (hoặc đã có đề khi sửa)", () => {
        expect(validateAssignmentDraft(draft())).toEqual({});
        expect(validateAssignmentDraft(draft({ promptFile: null }))).toHaveProperty("promptFile");
        expect(validateAssignmentDraft(draft({ promptFile: null }), false, false, true)).toEqual({});
        expect(validateAssignmentDraft(draft({ promptFile: file("de.png", 10, "image/png") }))).toHaveProperty("promptFile");
        expect(validateAssignmentDraft(draft({ kind: "Homework", promptFile: null }))).toEqual({});
        // Bài đã khóa (đã công bố) không kiểm tra đề.
        expect(validateAssignmentDraft(draft({ promptFile: null }), true)).toEqual({});
    });
    it("tạo bài thi: tải đề lên và xác nhận trước, rồi mới gửi promptFileId", async () => {
        const { calls, puts } = mockServer();
        await createAssignment("c", draft());
        expect(calls.map(x => x.path)).toEqual(["/api/media-notification/files/presign", `/api/media-notification/files/${fileId}/confirm`, "/api/academic/assignments"]);
        expect(calls[0].body).toEqual({ fileName: "de.pdf", contentType: "application/pdf", sizeBytes: 10, purpose: "exam" });
        expect(puts).toEqual(["https://storage.example/up"]);
        expect(calls[2].body).toMatchObject({ classId: "c", kind: "Exam", promptFileId: fileId, publish: true });
    });
    it("tải đề lỗi thì không tạo bài", async () => {
        const { calls } = mockServer();
        vi.mocked(fetch).mockImplementation(async (input: any) => String(input).startsWith("/api/")
            ? Response.json({ fileId, url: "https://storage.example/up", method: "PUT", requiredHeaders: { "Content-Type": "application/pdf" } })
            : new Response(null, { status: 503 }));
        await expect(createAssignment("c", draft())).rejects.toThrow();
        expect(calls.some(x => x.path === "/api/academic/assignments")).toBe(false);
    });
    it("bài thường tải đề PDF tùy chọn với loại tài liệu", async () => {
        const { calls } = mockServer();
        await createAssignment("c", draft({ kind: "Homework" }));
        expect(calls[0].body).toMatchObject({ purpose: "material" });
        expect(calls.at(-1)!.body).toMatchObject({ kind: "Homework", promptFileId: fileId });
    });
    it("sửa: bài đã công bố không đổi đề; bản nháp có đề mới thì tải lên", async () => {
        let { calls } = mockServer();
        await updateAssignment(exam(), draft({ promptFile: file("moi.pdf") }));
        expect(calls.map(x => x.path)).toEqual([`/api/academic/assignments/${exam().id}`]);
        expect(calls[0].body.promptFileId).toBeUndefined();
        ({ calls } = mockServer());
        await updateAssignment(exam({ status: "Draft", publishedAt: null }), draft({ promptFile: file("moi.pdf") }));
        expect(calls.at(-1)!.body).toMatchObject({ kind: "Exam", promptFileId: fileId, expectedVersion: 2 });
        ({ calls } = mockServer());
        await updateAssignment(exam({ status: "Draft", publishedAt: null }), draft({ promptFile: null }));
        expect(calls.map(x => x.path)).toHaveLength(1);
        expect(calls[0].body.promptFileId).toBeUndefined();
    });
    it("học viên chỉ thấy nút tải đề khi bài đang mở; giáo viên thì luôn", () => {
        for (const status of ["Upcoming", "Closed", "Draft"]) {
            expect(canDownloadPrompt(exam({ status }), "Student")).toBe(false);
            expect(canDownloadPrompt(exam({ status }), "Teacher")).toBe(true);
        }
        expect(canDownloadPrompt(exam({ status: "Open" }), "Student")).toBe(true);
        expect(canDownloadPrompt(exam({ promptFileName: null }), "Teacher")).toBe(false);
        expect(canDownloadPrompt(exam({ kind: "Homework" }), "Teacher")).toBe(true);
    });
    it("link tải đề đi qua máy chủ và chỉ nhận http(s)", async () => {
        const { calls } = mockServer(() => ({ url: "https://storage.example/x", expiresAt: "2026-11-19T00:00:00Z" }));
        await promptDownload("a/b");
        expect(calls[0].path).toBe("/api/academic/assignments/a%2Fb/prompt/download");
        expect(safeDownloadUrl({ url: "https://storage.example/x", expiresAt: "" })).toBe("https://storage.example/x");
        for (const bad of ["javascript:alert(1)", "https://user:pw@storage.example/x", "not a url"])
            expect(() => safeDownloadUrl({ url: bad, expiresAt: "" })).toThrow();
    });
});

describe("tài liệu lớp", () => {
    it("kiểm tra tiêu đề, mô tả và file", () => {
        const ok = { ...emptyMaterialDraft(), title: "Unit 4", file: file("a.pdf") };
        expect(validateMaterialDraft(ok)).toEqual({});
        expect(validateMaterialDraft({ ...ok, title: "  " })).toHaveProperty("title");
        expect(validateMaterialDraft({ ...ok, title: "t".repeat(201) })).toHaveProperty("title");
        expect(validateMaterialDraft({ ...ok, description: "d".repeat(2001) })).toHaveProperty("description");
        expect(validateMaterialDraft({ ...ok, file: null })).toHaveProperty("file");
        expect(validateMaterialDraft({ ...ok, file: file("a.exe", 10, "application/octet-stream") })).toHaveProperty("file");
    });
    it("đăng: tải file lên và xác nhận trước, rồi mới gửi fileId; danh sách và tải qua máy chủ", async () => {
        const { calls } = mockServer(path => path.includes("/download") ? { url: "https://storage.example/m", expiresAt: "" } : []);
        await createMaterial("c", { title: " Unit 4 ", description: " Đọc trước ", file: file("a.pdf") });
        expect(calls.map(x => x.path)).toEqual(["/api/media-notification/files/presign", `/api/media-notification/files/${fileId}/confirm`, "/api/academic/materials"]);
        expect(calls[0].body.purpose).toBe("material");
        expect(calls[2].body).toEqual({ classId: "c", title: "Unit 4", description: "Đọc trước", fileId });
        await listMaterials("c d");
        expect(calls[3].path).toBe("/api/academic/materials?classId=c+d");
        await materialDownload("m1");
        expect(calls[4].path).toBe("/api/academic/materials/m1/download");
    });
    it("file không hợp lệ bị chặn trước mọi lời gọi mạng", async () => {
        const { calls } = mockServer();
        await expect(uploadClassFile(file("de.docx", 10, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), "exam")).rejects.toThrow();
        await expect(createMaterial("c", { title: "x", description: "", file: file("a.exe", 10, "application/octet-stream") })).rejects.toThrow();
        expect(calls).toHaveLength(0);
    });
});
