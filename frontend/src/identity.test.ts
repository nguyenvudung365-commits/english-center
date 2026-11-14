import { afterEach, describe, expect, it, vi } from "vitest";
import { getSession, setSession, type Session } from "./api";
import { canCreateAccounts, createAccount, getProfile, updateProfile, uploadAvatar, validateAvatar } from "./identity";

const session: Session = {
    accessToken: "test-token",
    accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "operator-refresh",
    refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

describe("Identity account and profile contracts", () => {
    it("permits only Admin and Staff to create accounts", () => {
        expect(canCreateAccounts("Admin")).toBe(true);
        expect(canCreateAccounts("Staff")).toBe(true);
        expect(canCreateAccounts("Teacher")).toBe(false);
        expect(canCreateAccounts("Student")).toBe(false);
    });
    it("creates an account without replacing the operator session", async () => {
        setSession(session);
        const request = { fullName: "Student", email: "s@example.test", password: "password123", role: "Student" as const };
        const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "new-id", ...request, password: undefined }), { status: 201 }));
        vi.stubGlobal("fetch", fetcher);
        await createAccount(request);
        expect(fetcher.mock.calls[0][0]).toBe("/api/identity/users");
        expect(fetcher.mock.calls[0][1].method).toBe("POST");
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual(request);
        expect(getSession()).toEqual(session);
    });
    it("reads and updates only the caller profile", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ fullName: "Name" }), { status: 200 }));
        vi.stubGlobal("fetch", fetcher);
        await getProfile();
        const request = { fullName: "Name", email: "n@example.test", phone: null, avatarFileId: null, expectedRevision: "revision" };
        await updateProfile(request);
        expect(fetcher.mock.calls[0][0]).toBe("/api/identity/me");
        expect(fetcher.mock.calls[1][0]).toBe("/api/identity/me");
        expect(fetcher.mock.calls[1][1].method).toBe("PUT");
        expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual(request);
    });
    it("surfaces duplicate email errors without losing the operator session", async () => {
        setSession(session);
        vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Email is already registered." }), { status: 409 })));
        await expect(createAccount({ fullName: "Name", email: "n@example.test", password: "password123", role: "Teacher" })).rejects.toMatchObject({ status: 409 });
        expect(getSession()).toEqual(session);
    });

    it.each([
        ["avatar.jpg", "image/jpeg", 1, true],
        ["avatar.PNG", "image/png", 2 * 1024 * 1024, true],
        ["avatar.jpg", "image/png", 1, false],
        ["avatar.svg", "image/svg+xml", 1, false],
        ["avatar.jpeg", "image/jpeg", 1, false],
        ["avatar.png", "image/png", 0, false],
        ["avatar.png", "image/png", 2 * 1024 * 1024 + 1, false],
        ["../avatar.png", "image/png", 1, false],
    ])("validates avatar %s %s %s bytes", (name, type, size, valid) => {
        const file = new File([new Uint8Array(size)], name, { type });
        expect(validateAvatar(file) === null).toBe(valid);
    });

    const fileId = "11111111-2222-4333-8444-555555555555";
    const ticket = {
        fileId, url: "https://storage.example.test/avatars/test?signature=test", method: "PUT",
        requiredHeaders: { "Content-Type": "image/png" },
    };
    const file = () => new File([new Uint8Array([137, 80, 78, 71])], "avatar.png", { type: "image/png" });
    const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });

    it("uploads and confirms only an avatar without sending app credentials to storage", async () => {
        setSession(session);
        const fetcher = vi.fn().mockResolvedValueOnce(json(ticket))
            .mockResolvedValueOnce(new Response(null, { status: 200 }))
            .mockResolvedValueOnce(json({ fileId }));
        vi.stubGlobal("fetch", fetcher);
        const progress = vi.fn();
        expect(await uploadAvatar(file(), progress, new AbortController().signal)).toBe(fileId);
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toMatchObject({ purpose: "avatar", contentType: "image/png" });
        const upload = fetcher.mock.calls[1][1];
        expect(upload.credentials).toBe("omit");
        expect(upload.redirect).toBe("error");
        expect(upload.headers.get("Authorization")).toBeNull();
        expect(fetcher.mock.calls[2][0]).toBe(`/api/media-notification/files/${fileId}/confirm`);
        expect(progress).toHaveBeenCalledTimes(3);
    });

    it.each([
        { ...ticket, method: "POST" },
        { ...ticket, url: "javascript:alert(1)" },
        { ...ticket, url: "https://user:pass@storage.example.test/image" },
        { ...ticket, requiredHeaders: { Authorization: "Bearer secret", "Content-Type": "image/png" } },
        { ...ticket, requiredHeaders: { "Content-Type": "image/jpeg" } },
        { ...ticket, fileId: "not-a-guid" },
    ])("rejects invalid upload tickets before accessing storage %#", async (invalidTicket) => {
        setSession(session);
        const fetcher = vi.fn().mockResolvedValue(json(invalidTicket));
        vi.stubGlobal("fetch", fetcher);
        await expect(uploadAvatar(file(), vi.fn(), new AbortController().signal)).rejects.toThrow();
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("does not confirm a failed upload", async () => {
        setSession(session);
        const fetcher = vi.fn().mockResolvedValueOnce(json(ticket)).mockResolvedValueOnce(new Response(null, { status: 503 }));
        vi.stubGlobal("fetch", fetcher);
        await expect(uploadAvatar(file(), vi.fn(), new AbortController().signal)).rejects.toMatchObject({ status: 503 });
        expect(fetcher).toHaveBeenCalledTimes(2);
    });

    it("rejects mismatched confirmation IDs", async () => {
        setSession(session);
        const fetcher = vi.fn().mockResolvedValueOnce(json(ticket)).mockResolvedValueOnce(new Response(null, { status: 200 }))
            .mockResolvedValueOnce(json({ fileId: "different" }));
        vi.stubGlobal("fetch", fetcher);
        await expect(uploadAvatar(file(), vi.fn(), new AbortController().signal)).rejects.toThrow("không khớp");
    });

    it("stops uploads after the session changes during ticket creation", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => {
            setSession({ ...session, refreshToken: "another-user" });
            return json(ticket);
        });
        vi.stubGlobal("fetch", fetcher);
        await expect(uploadAvatar(file(), vi.fn(), new AbortController().signal)).rejects.toMatchObject({ status: 401 });
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("does not request a ticket for invalid images or aborted operations", async () => {
        setSession(session);
        const fetcher = vi.fn();
        vi.stubGlobal("fetch", fetcher);
        await expect(uploadAvatar(new File([], "empty.png", { type: "image/png" }), vi.fn(), new AbortController().signal)).rejects.toMatchObject({ status: 400 });
        const controller = new AbortController();
        controller.abort();
        await expect(uploadAvatar(file(), vi.fn(), controller.signal)).rejects.toThrow();
        expect(fetcher).not.toHaveBeenCalled();
    });
});
