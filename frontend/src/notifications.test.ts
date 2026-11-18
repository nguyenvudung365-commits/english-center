import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    adjustUnread, bellLabel, getUnread, listNotifications, markAllNotificationsRead, markNotificationRead,
    notificationLink, setUnread, unreadBadge,
} from "./notifications";

const id = "11111111-1111-1111-1111-111111111111";
const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); setUnread(null); vi.unstubAllGlobals(); });

describe("Thông báo", () => {
    it("ánh xạ đích thông báo tới route hiện có", () => {
        expect(notificationLink("Class", id)).toBe(`/classes/${id}`);
        expect(notificationLink("Assignment", id)).toBe(`/assignments/${id}`);
        expect(notificationLink("Invoice", id)).toBe("/invoices");
        expect(notificationLink("Warning", id)).toBe("/warnings");
    });
    it("không tạo liên kết cho loại lạ hoặc ID không hợp lệ", () => {
        expect(notificationLink("Unknown", id)).toBeNull();
        expect(notificationLink("Class", "../admin")).toBeNull();
        expect(notificationLink("Assignment", "00000000-0000-0000-0000-000000000000")).toBeNull();
    });
    it("huy hiệu chuông và nhãn trợ năng", () => {
        expect(unreadBadge(null)).toBeNull();
        expect(unreadBadge(0)).toBeNull();
        expect(unreadBadge(7)).toBe("7");
        expect(unreadBadge(100)).toBe("99+");
        expect(bellLabel(0)).toBe("Thông báo");
        expect(bellLabel(120)).toBe("Thông báo, 120 chưa đọc");
    });
    it("kho số chưa đọc không âm và bỏ qua khi chưa biết", () => {
        adjustUnread(-1);
        expect(getUnread()).toBeNull();
        setUnread(1);
        adjustUnread(-5);
        expect(getUnread()).toBe(0);
    });
    it("gọi đúng API danh sách, đánh dấu đọc và đọc tất cả", async () => {
        setSession(session);
        const fetcher = vi.fn()
            .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], total: 0, page: 2, pageSize: 5, unreadCount: 0 })))
            .mockResolvedValueOnce(new Response(null, { status: 204 }))
            .mockResolvedValueOnce(new Response(JSON.stringify({ marked: 3 })));
        vi.stubGlobal("fetch", fetcher);
        await listNotifications({ unreadOnly: true, page: 2, pageSize: 5 });
        await markNotificationRead(id);
        expect(await markAllNotificationsRead()).toEqual({ marked: 3 });
        expect(fetcher.mock.calls[0][0]).toBe("/api/media-notification/notifications?unreadOnly=true&page=2&pageSize=5");
        expect(fetcher.mock.calls[1][0]).toBe(`/api/media-notification/notifications/${id}/read`);
        expect(fetcher.mock.calls[1][1].method).toBe("POST");
        expect(fetcher.mock.calls[2][0]).toBe("/api/media-notification/notifications/read-all");
    });
});
