export type Role = "Admin" | "Staff" | "Teacher" | "Student";
export type Session = {
    accessToken: string;
    accessTokenExpiresAt: string;
    sessionId?: string;
    // Legacy JSON contract only; the browser never receives the refresh token (HttpOnly cookie).
    refreshToken?: string;
    refreshTokenExpiresAt?: string;
};
export class ApiError extends Error {
    constructor(
        public status: number,
        message: string,
        public errors: Record<string, string[]> = {},
    ) {
        super(message);
    }
}

// The access token stays in memory. The refresh token is an HttpOnly browser-session cookie that scripts cannot
// read; "X-Session-Mode: cookie" opts the auth calls into it. A reload restores the session via restoreSession().
const browserMode = { "X-Session-Mode": "cookie" };
export type AuthState = "restoring" | "ready" | "unavailable" | "logout-pending";
let authState: AuthState = "restoring";
let restoring: Promise<void> | null = null;
let session: Session | null = null;
let generation = 0;
let sessionBoundary = new AbortController();
let refreshing: Promise<void> | null = null;
const logoutKey = "english-center:pending-logout";
let pendingLogout: string | null = null;
const channel = typeof window !== "undefined" && typeof BroadcastChannel !== "undefined"
    ? new BroadcastChannel("english-center-session") : null;
const listeners = new Set<() => void>();
export const subscribe = (listener: () => void) => {
    listeners.add(listener);
    return () => {
        listeners.delete(listener);
    };
};
export const getSession = () => session;
// Login/logout boundary, deliberately unchanged by a refresh of the same session.
export const getSessionGeneration = () => generation;
export function setSession(value: Session | null) {
    sessionBoundary.abort(new ApiError(401, "Phiên đã thay đổi."));
    sessionBoundary = new AbortController();
    generation++;
    session = value;
    for (const listener of listeners) listener();
}
channel?.addEventListener("message", ({ data }) => {
    if (!session?.sessionId || !data || typeof data.id !== "string") return;
    if ((data.action === "login" && data.id !== session.sessionId) ||
        (data.action === "logout" && data.id === session.sessionId)) {
        setSession(null);
        setAuthState("ready");
    }
});

function readPendingLogout() {
    try { return globalThis.localStorage ? globalThis.localStorage.getItem(logoutKey) : pendingLogout; }
    catch { return pendingLogout; }
}

function markPendingLogout(id: string | null) {
    pendingLogout = id;
    try {
        if (id) globalThis.localStorage?.setItem(logoutKey, id);
        else globalThis.localStorage?.removeItem(logoutKey);
    } catch { /* The in-memory marker still applies when storage is blocked. */ }
}
export function identity(
    value: Session | null,
): { id: string; role: Role } | null {
    if (!value) return null;
    try {
        const payload = value.accessToken
            .split(".")[1]
            .replace(/-/g, "+")
            .replace(/_/g, "/");
        const claims = JSON.parse(atob(payload));
        if (
            typeof claims.sub !== "string" ||
            !["Admin", "Staff", "Teacher", "Student"].includes(claims.role)
        )
            return null;
        return { id: claims.sub, role: claims.role };
    } catch {
        return null;
    }
}
async function response<T>(result: Response): Promise<T> {
    if (!result.ok) {
        const problem = await result.json().catch(() => ({}));
        const messages: Record<number, string> = {
            401: "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.",
            403: "Bạn không có quyền thực hiện thao tác này.",
            429: "Bạn thao tác quá nhanh. Vui lòng thử lại sau.",
            500: "Máy chủ đang gặp sự cố. Vui lòng thử lại.",
        };
        throw new ApiError(
            result.status,
            messages[result.status] ??
                problem.title ??
                "Không thể hoàn tất yêu cầu.",
            problem.errors ?? {},
        );
    }
    return result.status === 204 ? (undefined as T) : result.json();
}
async function request(path: string, init: RequestInit, token?: string) {
    try {
        return await fetch(`/api${path}`, {
            ...init,
            headers: {
                "Content-Type": "application/json",
                ...init.headers,
                ...(token ? { Authorization: `Bearer ${token}` } : {}),
            },
            signal: init.signal
                ? AbortSignal.any([init.signal, AbortSignal.timeout(15000)])
                : AbortSignal.timeout(15000),
        });
    } catch (error) {
        if (init.signal?.aborted) throw error;
        throw new ApiError(
            0,
            "Không kết nối được máy chủ. Kiểm tra mạng và thử lại.",
        );
    }
}
// Serialize cookie changes across tabs. The expected session and DB concurrency checks also protect the fallback.
function exclusive<T>(job: () => Promise<T>): Promise<T> {
    const locks = globalThis.navigator?.locks;
    return locks ? (locks.request("english-center-auth", job) as Promise<T>) : job();
}
// Network, throttling and server failures say nothing about the session; only a rejection does.
const transient = (error: unknown) =>
    error instanceof ApiError &&
    (error.status === 0 || error.status === 409 || error.status === 429 || error.status >= 500);
const authHeaders = (id?: string | null) => id
    ? { ...browserMode, "X-Expected-Session": id } : browserMode;
function checkGeneration(version: number) {
    if (version !== generation) throw new ApiError(401, "Phiên đã thay đổi.");
}
async function exchange(id?: string) {
    return response<Session>(await request("/identity/auth/refresh", {
        method: "POST", headers: authHeaders(id), body: "{}",
    }));
}
async function refresh() {
    if (!session) throw new ApiError(401, "Vui lòng đăng nhập lại.");
    if (refreshing) return refreshing;
    const version = generation;
    const current = session;
    const task = (async () => {
        try {
            const next = await exclusive(() => {
                checkGeneration(version);
                if (current.sessionId && readPendingLogout() === current.sessionId)
                    throw new ApiError(401, "Phiên đang đăng xuất.");
                return exchange(current.sessionId);
            });
            checkGeneration(version);
            const previousUser = identity(current);
            const nextUser = identity(next);
            if (!previousUser || !nextUser || previousUser.id !== nextUser.id ||
                previousUser.role !== nextUser.role || current.sessionId !== next.sessionId)
                throw new ApiError(401, "Phiên đã thay đổi.");
            session = next;
            for (const listener of listeners) listener();
        } catch (error) {
            if (version === generation && !transient(error)) setSession(null);
            throw error;
        }
    })();
    refreshing = task;
    try {
        await task;
    } finally {
        if (refreshing === task) refreshing = null;
    }
}
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
    if (!session) throw new ApiError(401, "Vui lòng đăng nhập.");
    const version = generation;
    init = { ...init, signal: init.signal ? AbortSignal.any([init.signal, sessionBoundary.signal]) : sessionBoundary.signal };
    if (Date.parse(session.accessTokenExpiresAt) <= Date.now() + 10000)
        await refresh();
    checkGeneration(version);
    const token = session?.accessToken;
    let result = await request(path, init, token);
    checkGeneration(version);
    if (result.status === 401 && session) {
        if (session.accessToken === token) await refresh();
        checkGeneration(version);
        result = await request(path, init, session?.accessToken);
    }
    checkGeneration(version);
    if (result.status === 401) setSession(null);
    const value = await response<T>(result);
    checkGeneration(version);
    return value;
}
export async function login(email: string, password: string) {
    const version = generation;
    await exclusive(async () => {
        checkGeneration(version);
        const next = await response<Session>(await request("/identity/auth/login", {
            method: "POST",
            headers: browserMode,
            body: JSON.stringify({ email, password }),
        }));
        if (!identity(next)) throw new ApiError(0, "Phản hồi đăng nhập không hợp lệ.");
        checkGeneration(version);
        markPendingLogout(null);
        setSession(next);
        setAuthState("ready");
        if (next.sessionId) channel?.postMessage({ action: "login", id: next.sessionId });
    });
}
export async function logout() {
    const version = generation;
    const current = session;
    try {
        await exclusive(async () => {
            checkGeneration(version);
            const id = current?.sessionId ?? readPendingLogout();
            if (id) {
                markPendingLogout(id);
                channel?.postMessage({ action: "logout", id });
            }
            await confirmLogout(id, current?.accessToken);
        });
    } finally {
        if (generation === version) {
            setSession(null);
            setAuthState(readPendingLogout() ? "logout-pending" : "ready");
        }
    }
}

async function confirmLogout(id: string | null, token?: string) {
    const result = await request("/identity/auth/logout", {
        method: "POST", headers: authHeaders(id), body: "{}",
    }, token);
    // A 401 means this cookie is no longer the intended session; leave any newer cookie alone.
    if (result.status !== 401) await response<void>(result);
    if (id && readPendingLogout() === id) markPendingLogout(null);
}
export const getAuthState = () => authState;
function setAuthState(value: AuthState) {
    authState = value;
    for (const listener of listeners) listener();
}
// Page load: the cookie, not the user, proves the session. Unauthorized means log in; anything transient is retried
// and then reported as unavailable so a flaky network never looks like a logout. The URL is never touched.
export function restoreSession(): Promise<void> {
    if (restoring) return restoring;
    const version = generation;
    setAuthState("restoring");
    const task = (async () => {
        for (let attempt = 0; ; attempt++) {
            if (version !== generation) return setAuthState("ready");
            try {
                const next = await exclusive(async () => {
                    checkGeneration(version);
                    const pending = readPendingLogout();
                    if (!pending) return exchange();
                    await confirmLogout(pending);
                    return null;
                });
                if (version === generation) {
                    if (next && !identity(next)) throw new ApiError(401, "Phiên không hợp lệ.");
                    setSession(next);
                }
                return setAuthState("ready");
            } catch (error) {
                if (version !== generation) return setAuthState("ready");
                if (readPendingLogout()) return setAuthState("logout-pending");
                const retryable = error instanceof ApiError && (error.status === 0 || error.status >= 500);
                if (retryable && attempt < 2) {
                    await new Promise((done) => setTimeout(done, 600 * 2 ** attempt));
                    continue;
                }
                return setAuthState(transient(error) ? "unavailable" : "ready");
            }
        }
    })();
    restoring = task;
    return task.finally(() => {
        if (restoring === task) restoring = null;
    });
}
