import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    canManageFeedback, canSeeFeedback, createFeedback, feedbackStatusLabel, listFaqs, listFeedback, nextFeedbackStatus,
    updateFeedbackStatus, validateFaqDraft, validateFeedbackDraft, type Feedback,
} from "./feedback";

const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });
const item: Feedback = {
    id: "22222222-2222-2222-2222-222222222222", studentId: "s", title: "t", content: "c", rating: 4, status: "New",
    createdAtUtc: "2026-11-17T00:00:00Z", handledBy: null, handledAtUtc: null, revision: "33333333-3333-3333-3333-333333333333",
};

describe("Phản hồi", () => {
    it("chỉ cho chuyển New -> InProgress -> Resolved", () => {
        expect(nextFeedbackStatus("New")).toBe("InProgress");
        expect(nextFeedbackStatus("InProgress")).toBe("Resolved");
        expect(nextFeedbackStatus("Resolved")).toBeNull();
        expect(nextFeedbackStatus("khác")).toBeNull();
    });
    it("nhãn trạng thái tiếng Việt", () => {
        expect(feedbackStatusLabel("InProgress")).toBe("Đang xử lý");
        expect(feedbackStatusLabel("Lạ")).toBe("Lạ");
    });
    it("Giáo viên không dùng phản hồi; chỉ Staff/Admin xử lý", () => {
        expect(canSeeFeedback("Teacher")).toBe(false);
        expect(canSeeFeedback("Student")).toBe(true);
        expect(canManageFeedback("Staff")).toBe(true);
        expect(canManageFeedback("Student")).toBe(false);
    });
    it("kiểm tra tiêu đề, nội dung, đánh giá", () => {
        expect(validateFeedbackDraft({ title: "Tiêu đề", content: "Nội dung\nnhiều dòng", rating: "5" })).toEqual({});
        expect(Object.keys(validateFeedbackDraft({ title: "  ", content: "", rating: "" })).sort()).toEqual(["content", "rating", "title"]);
        expect(validateFeedbackDraft({ title: "a".repeat(151), content: "x", rating: "3" }).title).toBeDefined();
        expect(validateFeedbackDraft({ title: "a".repeat(150), content: "x".repeat(2000), rating: "3" })).toEqual({});
        expect(validateFeedbackDraft({ title: "a", content: "x".repeat(2001), rating: "3" }).content).toBeDefined();
        expect(validateFeedbackDraft({ title: "a", content: "x", rating: "0" }).rating).toBeDefined();
        expect(validateFeedbackDraft({ title: "a", content: "x", rating: "6" }).rating).toBeDefined();
        expect(validateFeedbackDraft({ title: "a\nb", content: "x", rating: "3" }).title).toBeDefined();
        expect(validateFeedbackDraft({ title: "a", content: "x\u0000y", rating: "3" }).content).toBeDefined();
    });
    it("kiểm tra FAQ", () => {
        expect(validateFaqDraft({ question: "Hỏi?", answer: "Đáp", isPublished: false })).toEqual({});
        expect(validateFaqDraft({ question: "q".repeat(301), answer: "", isPublished: true })).toHaveProperty("question");
        expect(validateFaqDraft({ question: "q", answer: "a".repeat(4001), isPublished: true })).toHaveProperty("answer");
    });
    it("gửi và đổi trạng thái đúng hợp đồng API", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 10 })));
        vi.stubGlobal("fetch", fetcher);
        await createFeedback({ title: " Tựa ", content: " Nội dung ", rating: "4" });
        expect(fetcher.mock.calls[0][0]).toBe("/api/media-notification/feedback");
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ title: "Tựa", content: "Nội dung", rating: 4 });
        await updateFeedbackStatus(item, "InProgress");
        expect(fetcher.mock.calls[1][0]).toBe(`/api/media-notification/feedback/${item.id}/status`);
        expect(fetcher.mock.calls[1][1].method).toBe("PATCH");
        expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ status: "InProgress", expectedRevision: item.revision });
        await listFeedback({ status: "New", search: " abc ", page: 2 });
        expect(fetcher.mock.calls[2][0]).toBe("/api/media-notification/feedback?page=2&pageSize=10&status=New&search=abc");
        await listFaqs({ search: "học phí" }, false);
        await listFaqs({}, true);
        expect(fetcher.mock.calls[3][0]).toContain("/api/media-notification/faqs?page=1&pageSize=10&search=");
        expect(fetcher.mock.calls[4][0]).toBe("/api/media-notification/admin/faqs?page=1&pageSize=10");
    });
});
