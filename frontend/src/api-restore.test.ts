import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Cookie-mode browser session contract: the refresh token lives only in an HttpOnly cookie, so the SPA opts in with
// X-Session-Mode: cookie, sends an empty body, and restores the session after a reload from the cookie alone.
type Api = typeof import("./api");

const token = (role = "Staff") =>
    `header.${btoa(JSON.stringify({ sub: "test-user", role }))}.signature`;
const session = (expired = false) => ({
    accessToken: token(),
    accessTokenExpiresAt: new Date(Date.now() + (expired ? -60000 : 600000)).toISOString(),
});
const json = (value: unknown, status = 200) =>
    new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const header = (init: RequestInit | undefined, name: string) => new Headers(init?.headers).get(name);

describe("cookie-mode session restore and opt-in", () => {
    const fetcher = vi.fn<typeof fetch>();
    let mod: Api;
    const storageWrites = vi.fn();

    beforeEach(async () => {
        vi.resetModules(); // authState/session are module-global; every test starts from a fresh page load
        fetcher.mockReset();
        storageWrites.mockReset();
        vi.stubGlobal("fetch", fetcher);
        const storage = { getItem: () => null, setItem: storageWrites, removeItem: vi.fn(), clear: vi.fn() };
        vi.stubGlobal("localStorage", storage);
        vi.stubGlobal("sessionStorage", storage);
        mod = await import("./api");
    });
    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
    });

    // ---- restoreSession ----

    it("starts in 'restoring' with no session on a fresh load", () => {
        expect(mod.getAuthState()).toBe("restoring");
        expect(mod.getSession()).toBeNull();
        expect(fetcher).not.toHaveBeenCalled();
    });

    it("restores from the cookie alone: POST refresh, opt-in header, empty body, no bearer", async () => {
        fetcher.mockResolvedValue(json(session()));
        await mod.restoreSession();
        expect(fetcher).toHaveBeenCalledTimes(1);
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/identity/auth/refresh");
        expect(init?.method).toBe("POST");
        expect(header(init, "X-Session-Mode")).toBe("cookie");
        expect(header(init, "Authorization")).toBeNull();
        expect(JSON.parse(String(init?.body))).toEqual({});
        expect(mod.getAuthState()).toBe("ready");
        expect(mod.identity(mod.getSession())).toEqual({ id: "test-user", role: "Staff" });
    });

    it("treats 401 as 'not signed in': ready, no session, no retry", async () => {
        fetcher.mockResolvedValue(json({}, 401));
        await mod.restoreSession();
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(mod.getAuthState()).toBe("ready");
        expect(mod.getSession()).toBeNull();
    });

    it("treats a refresh response with an invalid identity as signed out", async () => {
        fetcher.mockResolvedValue(json({ ...session(), accessToken: token("Unknown") }));
        await mod.restoreSession();
        expect(mod.getAuthState()).toBe("ready");
        expect(mod.getSession()).toBeNull();
    });

    it("retries a network failure, then reports 'unavailable' without signing out", async () => {
        vi.useFakeTimers();
        fetcher.mockRejectedValue(new TypeError("network"));
        const restoring = mod.restoreSession();
        await vi.advanceTimersByTimeAsync(10000);
        await restoring;
        expect(fetcher.mock.calls.length).toBeGreaterThan(1);
        expect(fetcher.mock.calls.length).toBeLessThanOrEqual(3);
        expect(mod.getAuthState()).toBe("unavailable");
        expect(mod.getSession()).toBeNull();
    });

    it("recovers when a retry succeeds after a transient network failure", async () => {
        vi.useFakeTimers();
        fetcher.mockRejectedValueOnce(new TypeError("network")).mockResolvedValue(json(session()));
        const restoring = mod.restoreSession();
        await vi.advanceTimersByTimeAsync(10000);
        await restoring;
        expect(fetcher).toHaveBeenCalledTimes(2);
        expect(mod.getAuthState()).toBe("ready");
        expect(mod.getSession()).not.toBeNull();
    });

    it("reports 'unavailable' on 429 without hammering the endpoint", async () => {
        fetcher.mockResolvedValue(json({}, 429));
        await mod.restoreSession();
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(mod.getAuthState()).toBe("unavailable");
    });

    it("shares one request between concurrent restore calls", async () => {
        fetcher.mockResolvedValue(json(session()));
        await Promise.all([mod.restoreSession(), mod.restoreSession()]);
        expect(fetcher).toHaveBeenCalledTimes(1);
    });

    it("does not overwrite a login that happened while restoring", async () => {
        let finish!: (response: Response) => void;
        fetcher.mockReturnValueOnce(new Promise<Response>((resolve) => { finish = resolve; }));
        const restoring = mod.restoreSession();
        const login = { ...session(), accessToken: token("Admin") };
        mod.setSession(login);
        finish(json(session()));
        await restoring;
        expect(mod.getSession()).toBe(login);
        expect(mod.getAuthState()).toBe("ready");
    });

    it("notifies subscribers when the auth state changes", async () => {
        fetcher.mockResolvedValue(json({}, 401));
        const listener = vi.fn();
        mod.subscribe(listener);
        await mod.restoreSession();
        expect(listener).toHaveBeenCalled();
    });

    // ---- opt-in header on every auth call ----

    it("login opts into cookie mode and sends credentials only", async () => {
        fetcher.mockResolvedValue(json(session()));
        await mod.login("staff@example.test", "password");
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/identity/auth/login");
        expect(header(init, "X-Session-Mode")).toBe("cookie");
        expect(JSON.parse(String(init?.body))).toEqual({ email: "staff@example.test", password: "password" });
    });

    it("refresh during an API call opts into cookie mode with an empty body", async () => {
        mod.setSession(session(true));
        fetcher.mockImplementation(async (path) =>
            String(path).endsWith("/refresh") ? json(session()) : json({ ok: true }));
        await mod.api("/academic/courses");
        const refresh = fetcher.mock.calls.find(([url]) => String(url).endsWith("/refresh"))!;
        expect(header(refresh[1], "X-Session-Mode")).toBe("cookie");
        expect(header(refresh[1], "Authorization")).toBeNull();
        expect(JSON.parse(String(refresh[1]?.body))).toEqual({});
    });

    it("business requests do not carry the cookie-mode header", async () => {
        mod.setSession(session());
        fetcher.mockResolvedValue(json({ ok: true }));
        await mod.api("/academic/courses");
        expect(header(fetcher.mock.calls[0][1], "X-Session-Mode")).toBeNull();
        expect(header(fetcher.mock.calls[0][1], "Authorization")).toMatch(/^Bearer /);
    });

    it("logout opts into cookie mode with an empty body", async () => {
        mod.setSession(session());
        fetcher.mockResolvedValue(new Response(null, { status: 204 }));
        await mod.logout();
        const [url, init] = fetcher.mock.calls[0];
        expect(url).toBe("/api/identity/auth/logout");
        expect(header(init, "X-Session-Mode")).toBe("cookie");
        expect(JSON.parse(String(init?.body))).toEqual({});
        expect(mod.getSession()).toBeNull();
    });

    it("logout works right after a reload, before any access token exists", async () => {
        fetcher.mockResolvedValue(new Response(null, { status: 204 }));
        await mod.logout();
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(header(fetcher.mock.calls[0][1], "X-Session-Mode")).toBe("cookie");
        expect(header(fetcher.mock.calls[0][1], "Authorization")).toBeNull();
    });

    it("logout treats 401 as already signed out and still clears local state", async () => {
        mod.setSession(session());
        fetcher.mockResolvedValue(json({}, 401));
        await expect(mod.logout()).resolves.toBeUndefined();
        expect(mod.getSession()).toBeNull();
    });

    it("logout surfaces a network failure but still clears local state", async () => {
        mod.setSession(session());
        fetcher.mockRejectedValue(new TypeError("network"));
        await expect(mod.logout()).rejects.toMatchObject({ status: 0 });
        expect(mod.getSession()).toBeNull();
    });

    // ---- transient refresh failures must not look like a logout ----

    it("keeps the session when a refresh fails with a network error", async () => {
        const current = session(true);
        mod.setSession(current);
        fetcher.mockRejectedValue(new TypeError("network"));
        await expect(mod.api("/academic/courses")).rejects.toMatchObject({ status: 0 });
        expect(mod.getSession()).toBe(current);
    });

    it("clears the session when the cookie is rejected on refresh", async () => {
        mod.setSession(session(true));
        fetcher.mockResolvedValue(json({}, 401));
        await expect(mod.api("/academic/courses")).rejects.toMatchObject({ status: 401 });
        expect(mod.getSession()).toBeNull();
    });

    it("never submits a pending write with a different browser session, even for the same user", async () => {
        mod.setSession({ ...session(true), sessionId: "older" });
        fetcher.mockResolvedValue(json({ ...session(), sessionId: "newer" }));
        await expect(mod.api("/academic/example", { method: "POST" })).rejects.toMatchObject({ status: 401 });
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(header(fetcher.mock.calls[0][1], "X-Expected-Session")).toBe("older");
        expect(mod.getSession()).toBeNull();
    });

    it("retries unconfirmed logout after reload without refreshing or persisting a token", async () => {
        const storage = new Map<string, string>();
        vi.stubGlobal("localStorage", {
            getItem: (key: string) => storage.get(key) ?? null,
            setItem: (key: string, value: string) => storage.set(key, value),
            removeItem: (key: string) => storage.delete(key),
        });
        mod.setSession({ ...session(), sessionId: "original-session" });
        fetcher.mockResolvedValueOnce(json({}, 429));
        await expect(mod.logout()).rejects.toMatchObject({ status: 429 });
        expect(mod.getAuthState()).toBe("logout-pending");
        expect([...storage.values()]).toEqual(["original-session"]);
        vi.resetModules();
        mod = await import("./api");
        fetcher.mockResolvedValueOnce(new Response(null, { status: 204 }));
        await mod.restoreSession();
        expect(fetcher.mock.calls.map(([url]) => url)).toEqual(Array(2).fill("/api/identity/auth/logout"));
        expect(header(fetcher.mock.calls[1][1], "X-Expected-Session")).toBe("original-session");
        expect(header(fetcher.mock.calls[1][1], "Authorization")).toBeNull();
        expect(mod.getSession()).toBeNull();
        expect(storage.size).toBe(0);
    });

    // ---- no refresh token persisted or forwarded ----

    it("never writes to browser storage and never forwards a refresh token", async () => {
        const secret = "legacy-refresh-secret";
        // A fresh Response per call: a body can be read only once.
        fetcher.mockImplementation(async (url) => {
            const path = String(url);
            if (path.endsWith("/login")) return json({ ...session(), refreshToken: secret });
            if (path.endsWith("/refresh")) return json(session());
            if (path.endsWith("/logout")) return new Response(null, { status: 204 });
            return json({ ok: true });
        });
        await mod.login("staff@example.test", "password");
        await mod.restoreSession();
        mod.setSession({ ...session(true), refreshToken: secret });
        await mod.api("/academic/courses");
        await mod.logout();

        expect(storageWrites).not.toHaveBeenCalled();
        for (const [url, init] of fetcher.mock.calls)
            if (String(url).includes("/identity/auth/"))
                expect(String(init?.body ?? "")).not.toContain(secret);
    });

    it("keeps the session free of a refresh token when the server omits it (cookie mode)", async () => {
        fetcher.mockResolvedValue(json(session()));
        await mod.login("staff@example.test", "password");
        expect(mod.getSession()).not.toHaveProperty("refreshToken");
    });
});
