import { api, type Role } from "./api";
import type { Page } from "./attendance";

export type Invoice = {
    id: string; invoiceNo: string; enrollmentId: string; studentId: string; classId: string; amount: number;
    status: "Unpaid" | "Paid"; issuedAtUtc: string; dueDate: string; paidAtUtc: string | null; confirmedBy: string | null;
};
export const invoiceStatusLabel = (status: string) => ({ Unpaid: "Chưa thanh toán", Paid: "Đã thanh toán" } as Record<string, string>)[status] ?? status;
// Chỉ Staff/Admin xác nhận thanh toán; hóa đơn đã trả không có thao tác (máy chủ trả 409 nếu gọi lại).
export const canPayInvoice = (role: Role, invoice: Pick<Invoice, "status">) => (role === "Staff" || role === "Admin") && invoice.status === "Unpaid";
export const canReadInvoices = (role: Role) => role !== "Teacher";

export function listInvoices(query: { studentId?: string; page?: number; pageSize?: number } = {}) {
    const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 10) });
    if (query.studentId?.trim()) parameters.set("studentId", query.studentId.trim());
    return api<Page<Invoice>>(`/finance/invoices?${parameters}`);
}
export const payInvoice = (id: string) => api<Invoice>(`/finance/invoices/${encodeURIComponent(id)}/pay`, { method: "POST" });
