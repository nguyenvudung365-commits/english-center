import { beforeEach, describe, expect, it, vi } from "vitest";
import { submitAssignment, validateSubmissionFiles } from "./submissions";

const mocks = vi.hoisted(() => ({ api: vi.fn(), generation: 1 }));
vi.mock("./api", async (original) => ({
    ...(await original<typeof import("./api")>()),
    api: mocks.api,
    getSessionGeneration: () => mocks.generation,
}));
const assignment = "10000000-0000-0000-0000-000000000001";
const ids = [1, 2, 3].map((n) => `20000000-0000-0000-0000-00000000000${n}`);
const file = (name = "work.pdf", size = 10) => new File([new Uint8Array(size)], name, { type: "application/pdf" });
const result = { id: ids[0], assignmentId: assignment, studentId: ids[0], revision: ids[1], submittedAt: new Date().toISOString(), files: [] };

beforeEach(() => {
    vi.restoreAllMocks();
    mocks.api.mockReset(); mocks.generation = 1;
    let issued = 0;
    mocks.api.mockImplementation(async (path: string) => {
        if (path.endsWith("/presign")) return { fileId: ids[issued++], url: "https://storage.example/upload", method: "PUT", requiredHeaders: { "Content-Type": "application/pdf" } };
        if (path.endsWith("/confirm")) return { fileId: path.split("/").at(-2) };
        return result;
    });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 200 })));
});

describe("multi-file submission", () => {
    it("confirms every file before one academic save with server-issued IDs and revision", async () => {
        const progress = vi.fn();
        await expect(submitAssignment(assignment, [file(), file("two.pdf"), file("three.pdf")], ids[2], progress)).resolves.toEqual(result);
        const paths = mocks.api.mock.calls.map(([path]) => path);
        expect(paths.filter((path) => path.endsWith("/confirm"))).toHaveLength(3);
        expect(paths.at(-1)).toBe(`/academic/assignments/${assignment}/submissions`);
        const save = mocks.api.mock.calls.at(-1)!;
        expect(save[1].method).toBe("PUT");
        expect(JSON.parse(save[1].body)).toEqual({ fileIds: ids, expectedRevision: ids[2] });
        expect(progress).toHaveBeenCalledWith(2, "confirmed");
        const init = vi.mocked(fetch).mock.calls[0][1]!;
        expect(init.credentials).toBe("omit");
        expect(new Headers(init.headers).has("Authorization")).toBe(false);
        expect(init.method).toBe("PUT");
    });
    it.each(["put", "confirm"])("does not save a partial submission after %s failure", async (phase) => {
        if (phase === "put") vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 503 }));
        else {
            const implementation = mocks.api.getMockImplementation()!;
            mocks.api.mockImplementation(async (...args) => {
                if (args[0].endsWith("/confirm")) throw new Error("confirmation unavailable");
                return implementation(...args);
            });
        }
        await expect(submitAssignment(assignment, [file(), file("two.pdf")], null)).rejects.toThrow();
        expect(mocks.api.mock.calls.some(([path]) => path.includes("/assignments/"))).toBe(false);
    });
    it("does not cross a logout/login boundary during an upload", async () => {
        vi.mocked(fetch).mockImplementationOnce(async () => { mocks.generation++; return new Response(null, { status: 200 }); });
        await expect(submitAssignment(assignment, [file()], null)).rejects.toThrow();
        expect(mocks.api.mock.calls).toHaveLength(1);
    });
    it("honors cancellation before any request", async () => {
        const controller = new AbortController(); controller.abort();
        await expect(submitAssignment(assignment, [file()], null, undefined, controller.signal)).rejects.toThrow();
        expect(mocks.api).not.toHaveBeenCalled();
    });
    it("honors cancellation between upload and confirmation", async () => {
        const controller = new AbortController();
        vi.mocked(fetch).mockImplementationOnce(async () => { controller.abort(); return new Response(null, { status: 200 }); });
        await expect(submitAssignment(assignment, [file()], null, undefined, controller.signal)).rejects.toThrow();
        expect(mocks.api.mock.calls).toHaveLength(1);
    });
    it("rejects a mismatched confirmation instead of linking a different file", async () => {
        const implementation = mocks.api.getMockImplementation()!;
        mocks.api.mockImplementation(async (...args) => args[0].endsWith("/confirm") ? { fileId: ids[2] } : implementation(...args));
        await expect(submitAssignment(assignment, [file()], null)).rejects.toThrow();
        expect(mocks.api.mock.calls.some(([path]) => path.includes("/assignments/"))).toBe(false);
    });
    it("rejects unsafe storage URLs before sending file bytes", async () => {
        mocks.api.mockResolvedValueOnce({ fileId: ids[0], url: "javascript:alert(1)", method: "PUT", requiredHeaders: { "Content-Type": "application/pdf" } });
        await expect(submitAssignment(assignment, [file()], null)).rejects.toThrow();
        expect(fetch).not.toHaveBeenCalled();
    });
    it("does not report success when the academic save fails", async () => {
        const implementation = mocks.api.getMockImplementation()!;
        mocks.api.mockImplementation(async (...args) => {
            if (args[0].includes("/assignments/")) throw new Error("revision conflict");
            return implementation(...args);
        });
        await expect(submitAssignment(assignment, [file()], null)).rejects.toThrow("revision conflict");
    });
    it.each([
        ["voice.m4a", "audio/x-m4a", "audio/mp4"],
        ["voice.mp3", "audio/mp3", "audio/mpeg"],
        ["photo.jpg", "image/pjpeg", "image/jpeg"],
    ])("normalizes the browser MIME alias for %s before requesting an upload", async (name, alias, canonical) => {
        const input = new File([new Uint8Array(10)], name, { type: alias });
        expect(validateSubmissionFiles([input])).toBeNull();
        const implementation = mocks.api.getMockImplementation()!;
        mocks.api.mockImplementation(async (...args) => args[0].endsWith("/presign")
            ? { fileId: ids[0], url: "https://storage.example/upload", method: "PUT", requiredHeaders: { "Content-Type": canonical } }
            : implementation(...args));
        await submitAssignment(assignment, [input], null);
        expect(JSON.parse(mocks.api.mock.calls[0][1].body).contentType).toBe(canonical);
        expect(new Headers(vi.mocked(fetch).mock.calls[0][1]?.headers).get("Content-Type")).toBe(canonical);
        expect(validateSubmissionFiles([new File([new Uint8Array(10)], "wrong.pdf", { type: alias })])).not.toBeNull();
    });
    it("validates count, each size, total size, filename and supported formats before upload", async () => {
        expect(validateSubmissionFiles([file()])).toBeNull();
        for (const files of [[], Array.from({ length: 6 }, () => file()), [file("big.pdf", 20 * 1024 * 1024 + 1)],
            [file("a.pdf", 18 * 1024 * 1024), file("b.pdf", 18 * 1024 * 1024), file("c.pdf", 18 * 1024 * 1024)],
            [file("zero.pdf", 0)], [file("../work.pdf")], [file("bad.exe")]]) {
            expect(validateSubmissionFiles(files)).not.toBeNull();
        }
        await expect(submitAssignment(assignment, [], null)).rejects.toThrow();
        expect(mocks.api).not.toHaveBeenCalled();
    });
});
