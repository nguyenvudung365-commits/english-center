import { afterEach, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import { enrollmentStatusLabel, listMyEnrollments } from "./enrollment";

afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

it("loads the current student's enrollment history with pagination and bearer authentication", async () => {
    const session: Session = {
        accessToken: "student-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
        refreshToken: "refresh", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    };
    setSession(session);
    const page = { items: [], total: 0, page: 2, pageSize: 20 };
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify(page)));
    vi.stubGlobal("fetch", fetcher);
    expect(await listMyEnrollments(2)).toEqual(page);
    expect(fetcher.mock.calls[0][0]).toBe("/api/enrollment/enrollments/mine?page=2&pageSize=20");
    expect(new Headers(fetcher.mock.calls[0][1].headers).get("Authorization")).toBe("Bearer student-token");
});

it("labels enrollment status independently from coursework access", () => {
    expect(enrollmentStatusLabel("Active")).toBe("Đang ghi danh");
    expect(enrollmentStatusLabel("Cancelled")).toBe("Đã hủy");
});
