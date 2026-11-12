export type Role = "Admin" | "Staff" | "Teacher" | "Student";
export type Session = {
    accessToken: string;
    accessTokenExpiresAt: string;
    refreshToken: string;
    refreshTokenExpiresAt: string;
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

// Tokens stay in memory: reload intentionally requires login; no browser persistence of credentials.
let session: Session | null = null;
let generation = 0;
let refreshing: Promise<void> | null = null;
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
    generation++;
    session = value;
    for (const listener of listeners) listener();
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
async function refresh() {
    if (!session) throw new ApiError(401, "Vui lòng đăng nhập lại.");
    if (refreshing) return refreshing;
    const original = session;
    const version = generation;
    const task = (async () => {
        try {
            const next = await response<Session>(
                await request("/identity/auth/refresh", {
                    method: "POST",
                    body: JSON.stringify({
                        refreshToken: original.refreshToken,
                    }),
                }),
            );
            if (version !== generation)
                throw new ApiError(401, "Phiên đã thay đổi.");
            session = next;
            for (const listener of listeners) listener();
        } catch (error) {
            if (version === generation) setSession(null);
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
    if (Date.parse(session.accessTokenExpiresAt) <= Date.now() + 10000)
        await refresh();
    const version = generation;
    const token = session?.accessToken;
    let result = await request(path, init, token);
    if (version !== generation) throw new ApiError(401, "Phiên đã thay đổi.");
    if (result.status === 401 && session) {
        if (session.accessToken === token) await refresh();
        result = await request(path, init, session?.accessToken);
    }
    if (version !== generation) throw new ApiError(401, "Phiên đã thay đổi.");
    if (result.status === 401) setSession(null);
    return response<T>(result);
}
export async function login(email: string, password: string) {
    const version = generation;
    const next = await response<Session>(
        await request("/identity/auth/login", {
            method: "POST",
            body: JSON.stringify({ email, password }),
        }),
    );
    if (!identity(next))
        throw new ApiError(0, "Phản hồi đăng nhập không hợp lệ.");
    if (generation === version) setSession(next);
}
export async function logout() {
    const version = generation;
    try {
        if (session) {
            if (Date.parse(session.accessTokenExpiresAt) <= Date.now() + 10000)
                await refresh();
            if (session)
                await response<void>(
                    await request(
                        "/identity/auth/logout",
                        {
                            method: "POST",
                            body: JSON.stringify({
                                refreshToken: session.refreshToken,
                            }),
                        },
                        session.accessToken,
                    ),
                );
        }
    } finally {
        if (generation === version) setSession(null);
    }
}
