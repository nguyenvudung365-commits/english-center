import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
    api,
    getSession,
    identity,
    login,
    logout,
    setSession,
    type Session,
} from "./api";

const token = (role = "Staff") =>
    `header.${btoa(JSON.stringify({ sub: "test-user", role }))}.signature`;
const tokens = (expired = false): Session => ({
    accessToken: token(),
    accessTokenExpiresAt: new Date(
        Date.now() + (expired ? -60000 : 600000),
    ).toISOString(),
    refreshToken: "refresh-one",
    refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
});
const json = (value: unknown, status = 200) =>
    new Response(JSON.stringify(value), {
        status,
        headers: { "Content-Type": "application/json" },
    });

describe("API session contract", () => {
    it("retains a deadline when a caller provides cancellation", async () => {
        setSession(tokens());
        const caller = new AbortController();
        const deadline = new AbortController();
        const timeout = vi.spyOn(AbortSignal, "timeout").mockReturnValue(deadline.signal);
        fetcher.mockImplementation((_path, init) => new Promise((_resolve, reject) => {
            init!.signal!.addEventListener("abort", () => reject(init!.signal!.reason), { once: true });
        }));
        try {
            const operation = api("/identity/me", { signal: caller.signal });
            expect(timeout).toHaveBeenCalledWith(15000);
            const signal = fetcher.mock.calls[0][1]!.signal!;
            expect(signal).not.toBe(caller.signal);
            deadline.abort(new DOMException("Deadline", "TimeoutError"));
            await expect(operation).rejects.toMatchObject({ status: 0 });
            expect(caller.signal.aborted).toBe(false);
        } finally { caller.abort(); timeout.mockRestore(); }
    });
    it("does not clear a newer login when an old logout completes", async () => {
        setSession(tokens());
        let finish!: (response: Response) => void;
        fetcher.mockReturnValue(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const operation = logout();
        setSession({ ...tokens(), refreshToken: "new-login" });
        finish(new Response(null, { status: 204 }));
        await operation;
        expect(getSession()?.refreshToken).toBe("new-login");
    });
    it("does not clear a new session when an old request returns 401", async () => {
        setSession(tokens());
        let finish!: (response: Response) => void;
        fetcher.mockReturnValue(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const operation = api("/academic/courses");
        const replacement = { ...tokens(), refreshToken: "new-login" };
        setSession(replacement);
        finish(json({}, 401));
        await expect(operation).rejects.toMatchObject({ status: 401 });
        expect(getSession()?.refreshToken).toBe("new-login");
        expect(fetcher).toHaveBeenCalledTimes(1);
    });
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

    it("accepts real token response and derives display role", async () => {
        fetcher.mockResolvedValue(json(tokens()));
        await login("staff@example.test", "password");
        expect(identity(getSession())).toEqual({
            id: "test-user",
            role: "Staff",
        });
        expect(fetcher.mock.calls[0][0]).toBe("/api/identity/auth/login");
    });
    it("rejects invalid role claims", async () => {
        fetcher.mockResolvedValue(
            json({ ...tokens(), accessToken: token("Unknown") }),
        );
        await expect(login("a@b.test", "password")).rejects.toThrow(
            "không hợp lệ",
        );
        expect(getSession()).toBeNull();
    });
    it("shares a single refresh between concurrent reads", async () => {
        setSession(tokens(true));
        fetcher.mockImplementation(async (path) =>
            String(path).endsWith("/refresh")
                ? json({ ...tokens(), refreshToken: "rotated" })
                : json({ ok: true }),
        );
        await Promise.all([
            api("/academic/courses"),
            api("/academic/teachers"),
        ]);
        expect(
            fetcher.mock.calls.filter((call) =>
                String(call[0]).endsWith("/refresh"),
            ),
        ).toHaveLength(1);
        expect(getSession()?.refreshToken).toBe("rotated");
    });
    it("clears session when refresh is rejected", async () => {
        setSession(tokens(true));
        fetcher.mockResolvedValue(json({}, 401));
        await expect(api("/academic/courses")).rejects.toMatchObject({
            status: 401,
        });
        expect(getSession()).toBeNull();
        expect(fetcher).toHaveBeenCalledTimes(1);
    });
    it("does not refresh a forbidden request", async () => {
        setSession(tokens());
        fetcher.mockResolvedValue(json({}, 403));
        await expect(api("/academic/courses")).rejects.toMatchObject({
            status: 403,
        });
        expect(fetcher).toHaveBeenCalledTimes(1);
        expect(getSession()).not.toBeNull();
    });
    it("retries an unauthorized response at most once", async () => {
        setSession(tokens());
        fetcher
            .mockResolvedValueOnce(json({}, 401))
            .mockResolvedValueOnce(json(tokens()))
            .mockResolvedValueOnce(json({}, 401));
        await expect(api("/academic/courses")).rejects.toMatchObject({
            status: 401,
        });
        expect(fetcher).toHaveBeenCalledTimes(3);
        expect(getSession()).toBeNull();
    });
    it("logout sends the rotated refresh token after access expiry", async () => {
        setSession(tokens(true));
        fetcher
            .mockResolvedValueOnce(
                json({ ...tokens(), refreshToken: "rotated" }),
            )
            .mockResolvedValueOnce(new Response(null, { status: 204 }));
        await logout();
        expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual({
            refreshToken: "rotated",
        });
        expect(getSession()).toBeNull();
    });
    it("clears local credentials even when logout network fails", async () => {
        setSession(tokens());
        fetcher.mockRejectedValue(new TypeError("network"));
        await expect(logout()).rejects.toMatchObject({ status: 0 });
        expect(getSession()).toBeNull();
    });
    it("cannot resurrect a cleared session from a late refresh", async () => {
        setSession(tokens(true));
        let finish!: (response: Response) => void;
        fetcher.mockReturnValue(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const operation = api("/academic/courses");
        setSession(null);
        finish(json(tokens()));
        await expect(operation).rejects.toMatchObject({ status: 401 });
        expect(getSession()).toBeNull();
    });
});
