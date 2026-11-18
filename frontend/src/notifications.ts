import { api } from "./api";
import { isAcademicId } from "./academic";

export type NotificationItem = {
    id: string; kind: string; title: string; targetType: string; targetId: string;
    createdAtUtc: string; readAtUtc: string | null; isRead: boolean;
};
export type NotificationList = { items: NotificationItem[]; total: number; page: number; pageSize: number; unreadCount: number };

export const listNotifications = (query: { unreadOnly?: boolean; page?: number; pageSize?: number } = {}) =>
    api<NotificationList>(`/media-notification/notifications?${new URLSearchParams({
        unreadOnly: String(!!query.unreadOnly), page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 20),
    })}`);
export const markNotificationRead = (id: string) =>
    api<void>(`/media-notification/notifications/${encodeURIComponent(id)}/read`, { method: "POST" });
export const markAllNotificationsRead = () =>
    api<{ marked: number }>("/media-notification/notifications/read-all", { method: "POST" });
export const getUnreadCount = (signal?: AbortSignal) =>
    api<{ unreadCount: number }>("/media-notification/notifications/unread-count", { signal });

// Chỉ ánh xạ tới route đang có; loại đích lạ hoặc ID không hợp lệ thì không tạo liên kết.
export function notificationLink(targetType: string, targetId: string): string | null {
    if (targetType === "Invoice") return "/invoices";
    if (targetType === "Warning") return "/warnings";
    if (!isAcademicId(targetId)) return null;
    if (targetType === "Class") return `/classes/${targetId}`;
    if (targetType === "Assignment") return `/assignments/${targetId}`;
    return null;
}
export const notificationLinkLabel = (targetType: string) =>
    ({ Class: "Mở lớp học", Assignment: "Mở bài tập", Invoice: "Xem hóa đơn", Warning: "Xem cảnh báo" } as Record<string, string>)[targetType] ?? "";

// Huy hiệu chuông: "99+" khi lớn; aria-label luôn nói số thật.
export const unreadBadge = (count: number | null) => (count && count > 0 ? (count > 99 ? "99+" : String(count)) : null);
export const bellLabel = (count: number | null) => (count && count > 0 ? `Thông báo, ${count} chưa đọc` : "Thông báo");

// Kho nhỏ giữ số chưa đọc để chuông (header) và trang thông báo dùng chung.
let unread: number | null = null;
const listeners = new Set<() => void>();
export const subscribeUnread = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
export const getUnread = () => unread;
export function setUnread(value: number | null) {
    if (value === unread) return;
    unread = value;
    for (const listener of listeners) listener();
}
export const adjustUnread = (delta: number) => { if (unread !== null) setUnread(Math.max(0, unread + delta)); };
