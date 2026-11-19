// Giờ Việt Nam là UTC+7 quanh năm (không có giờ mùa hè), nên cộng/trừ 7 giờ là chính xác.
const VN_OFFSET_MS = 7 * 60 * 60 * 1000;

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 });
const dateTime = new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh", hourCycle: "h23",
});

export const formatVnd = (amount: number) => money.format(amount);
export function formatVnDateTime(instant: string | null | undefined): string {
    if (!instant) return "—";
    const time = Date.parse(instant);
    return Number.isNaN(time) ? "—" : dateTime.format(time);
}
// DateOnly của máy chủ (YYYY-MM-DD) không có múi giờ: không đi qua Date để tránh lệch ngày.
export function formatDateOnly(value: string | null | undefined): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? "");
    return match ? `${match[3]}/${match[2]}/${match[1]}` : "—";
}
// Giá trị <input type="datetime-local"> (giờ VN) -> ISO có offset +07:00; null nếu không hợp lệ.
export function vnLocalToIso(local: string): string | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(local);
    if (!match) return null;
    const iso = `${local}:00+07:00`;
    const time = Date.parse(iso);
    if (Number.isNaN(time) || new Date(time + VN_OFFSET_MS).toISOString().slice(0, 16) !== local) return null;
    return iso;
}
// Thời điểm UTC từ máy chủ -> giá trị datetime-local theo giờ VN.
export function utcToVnLocal(instant: string): string {
    const time = Date.parse(instant);
    return Number.isNaN(time) ? "" : new Date(time + VN_OFFSET_MS).toISOString().slice(0, 16);
}
export const pageCount = (total: number, pageSize: number) => Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
