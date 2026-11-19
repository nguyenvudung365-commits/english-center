import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getSession, login, logout, setSession, type Session } from "./api";

// Chỉ khoá đường dẫn/method của API auth (tương thích tĩnh), không giả định chế độ cookie hay tên hàm mới.
const token = `header.${btoa(JSON.stringify({ sub: "test-user", role: "Staff" }))}.signature`;
const session = (): Session => ({
    accessToken: token,
    accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "refresh-one",
    refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
});
const json = (value: unknown, status = 200) =>
    new Response(JSON.stringify(value), {
        status,
        headers: { "Content-Type": "application/json" },
    });

describe("auth API path compatibility", () => {
    const fetcher = vi.fn<typeof fetch>();
    beforeEach(() => {
        setSession(null);
        fetcher.mockReset();
        vi.stubGlobal("fetch", fetcher);
    });
    afterEach(() => {
        setSession(null);
        vi.unstubAllGlobals();
    });

    it("login POSTs credentials to /api/identity/auth/login without a bearer token", async () => {
        fetcher.mockResolvedValue(json(session()));
        await login("staff@example.test", "password");
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/identity/auth/login");
        expect(init?.method).toBe("POST");
        expect(JSON.parse(String(init?.body))).toMatchObject({
            email: "staff@example.test",
            password: "password",
        });
        expect(new Headers(init?.headers).get("Authorization")).toBeNull();
    });

    it("logout POSTs to /api/identity/auth/logout with the current bearer token and clears local state", async () => {
        const current = session();
        setSession(current);
        fetcher.mockResolvedValue(new Response(null, { status: 204 }));
        await logout();
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/identity/auth/logout");
        expect(init?.method).toBe("POST");
        expect(new Headers(init?.headers).get("Authorization")).toBe(
            `Bearer ${current.accessToken}`,
        );
        expect(getSession()).toBeNull();
    });
});
