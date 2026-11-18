import { api, type Role } from "./api";
import type { Page } from "./attendance";

export type FeedbackStatus = "New" | "InProgress" | "Resolved";
export type Feedback = {
    id: string; studentId: string; title: string; content: string; rating: number; status: FeedbackStatus;
    createdAtUtc: string; handledBy: string | null; handledAtUtc: string | null; revision: string;
};
export type FeedbackDraft = { title: string; content: string; rating: string };
export type Faq = { id: string; question: string; answer: string; isPublished: boolean; createdAtUtc: string; updatedAtUtc: string };
export type FaqDraft = { question: string; answer: string; isPublished: boolean };

export const feedbackStatuses: FeedbackStatus[] = ["New", "InProgress", "Resolved"];
export const feedbackStatusLabels: Record<FeedbackStatus, string> = { New: "Mới", InProgress: "Đang xử lý", Resolved: "Đã xử lý" };
export const feedbackStatusLabel = (status: string) => feedbackStatusLabels[status as FeedbackStatus] ?? status;
// Backend chỉ cho New -> InProgress -> Resolved, không mở lại.
export const nextFeedbackStatus = (status: string): FeedbackStatus | null =>
    status === "New" ? "InProgress" : status === "InProgress" ? "Resolved" : null;
export const canSeeFeedback = (role: Role) => role !== "Teacher";
export const canManageFeedback = (role: Role) => role === "Staff" || role === "Admin";

const hasControl = (text: string) => [...text].some((c) => { const code = c.charCodeAt(0); return (code < 32 && !"\n\r\t".includes(c)) || code === 127; });
// Văn bản thuần: cắt khoảng trắng hai đầu, 1..max ký tự, không ký tự điều khiển (khớp luật máy chủ).
export function validatePlainText(value: string, max: number, label: string, multiline = false): string | null {
    const text = value.trim();
    if (!text) return `${label} là bắt buộc.`;
    if (text.length > max) return `${label} tối đa ${max} ký tự.`;
    if (multiline ? hasControl(text) : [...text].some((c) => c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127)) return `${label} chứa ký tự không hợp lệ.`;
    return null;
}
export function validateFeedbackDraft(draft: FeedbackDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    const title = validatePlainText(draft.title, 150, "Tiêu đề");
    const content = validatePlainText(draft.content, 2000, "Nội dung", true);
    if (title) errors.title = [title];
    if (content) errors.content = [content];
    if (!/^[1-5]$/.test(draft.rating)) errors.rating = ["Chọn mức đánh giá từ 1 đến 5."];
    return errors;
}
export function validateFaqDraft(draft: FaqDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    const question = validatePlainText(draft.question, 300, "Câu hỏi");
    const answer = validatePlainText(draft.answer, 4000, "Câu trả lời", true);
    if (question) errors.question = [question];
    if (answer) errors.answer = [answer];
    return errors;
}

export type FeedbackQuery = { status?: FeedbackStatus; search?: string; page?: number; pageSize?: number };
export function listFeedback(query: FeedbackQuery = {}) {
    const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 10) });
    if (query.status) parameters.set("status", query.status);
    if (query.search?.trim()) parameters.set("search", query.search.trim());
    return api<Page<Feedback>>(`/media-notification/feedback?${parameters}`);
}
export const createFeedback = (draft: FeedbackDraft) => api<Feedback>("/media-notification/feedback", {
    method: "POST", body: JSON.stringify({ title: draft.title.trim(), content: draft.content.trim(), rating: Number(draft.rating) }),
});
export const updateFeedbackStatus = (item: Feedback, status: FeedbackStatus) =>
    api<Feedback>(`/media-notification/feedback/${encodeURIComponent(item.id)}/status`, {
        method: "PATCH", body: JSON.stringify({ status, expectedRevision: item.revision }),
    });

export function listFaqs(query: { search?: string; published?: boolean; page?: number; pageSize?: number } = {}, admin = false) {
    const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 10) });
    if (query.search?.trim()) parameters.set("search", query.search.trim());
    if (admin && query.published !== undefined) parameters.set("published", String(query.published));
    return api<Page<Faq>>(`/media-notification/${admin ? "admin/" : ""}faqs?${parameters}`);
}
const faqBody = (draft: FaqDraft) => JSON.stringify({ question: draft.question.trim(), answer: draft.answer.trim(), isPublished: draft.isPublished });
export const createFaq = (draft: FaqDraft) => api<Faq>("/media-notification/admin/faqs", { method: "POST", body: faqBody(draft) });
export const updateFaq = (id: string, draft: FaqDraft) =>
    api<Faq>(`/media-notification/admin/faqs/${encodeURIComponent(id)}`, { method: "PUT", body: faqBody(draft) });
export const deleteFaq = (id: string) => api<void>(`/media-notification/admin/faqs/${encodeURIComponent(id)}`, { method: "DELETE" });
