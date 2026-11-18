import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    authorLabel, canModerateFeed, COMMENT_MAX, createComment, createPost, feedAccessMessage, hidePost, isFeedAuthor, POST_MAX, roleLabel, setLike,
    validateFeedContent,
} from "./feed";

const id = "11111111-1111-1111-1111-111111111111";
const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

describe("Bảng tin lớp", () => {
    it("kiểm tra độ dài nội dung bài và bình luận", () => {
        expect(validateFeedContent("Xin chào\nlớp", POST_MAX)).toBeNull();
        expect(validateFeedContent("   ", POST_MAX)).not.toBeNull();
        expect(validateFeedContent("a".repeat(POST_MAX), POST_MAX)).toBeNull();
        expect(validateFeedContent("a".repeat(POST_MAX + 1), POST_MAX)).not.toBeNull();
        expect(validateFeedContent("a".repeat(COMMENT_MAX + 1), COMMENT_MAX)).not.toBeNull();
        expect(validateFeedContent("a\u0007b", POST_MAX)).not.toBeNull();
    });
    it("nút kiểm duyệt chỉ cho Teacher/Staff/Admin; tác giả so khớp không phân biệt hoa thường", () => {
        expect(canModerateFeed("Student")).toBe(false);
        expect(canModerateFeed("Teacher")).toBe(true);
        expect(isFeedAuthor(id.toUpperCase(), id)).toBe(true);
        expect(isFeedAuthor("khác", id)).toBe(false);
    });
    it("nhãn vai trò và thông báo 403/404 thân thiện", () => {
        expect(roleLabel("Teacher")).toBe("Giáo viên");
        expect(feedAccessMessage(403)).toContain("không có quyền");
        expect(feedAccessMessage(404)).toContain("không còn tồn tại");
        expect(feedAccessMessage(500)).toBeNull();
    });
    it("hiển thị tên tác giả kèm vai trò, không có tên thì chỉ hiện vai trò", () => {
        expect(authorLabel("  Nguyễn An ", "Student")).toBe("Nguyễn An (Học viên)");
        expect(authorLabel(null, "Teacher")).toBe("Giáo viên");
        expect(authorLabel("   ", "Teacher")).toBe("Giáo viên");
        expect(authorLabel(undefined, "Student")).toBe("Học viên");
    });
    it("gọi đúng API bảng tin", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ likeCount: 1, likedByMe: true })));
        vi.stubGlobal("fetch", fetcher);
        await createPost(id, "  Nội dung  ");
        await createComment(id, "Bình luận");
        await setLike(id, true);
        await setLike(id, false);
        await hidePost(id, true);
        expect(fetcher.mock.calls[0][0]).toBe(`/api/media-notification/classes/${id}/posts`);
        expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ content: "Nội dung" });
        expect(fetcher.mock.calls[1][0]).toBe(`/api/media-notification/posts/${id}/comments`);
        expect(fetcher.mock.calls[2][1].method).toBe("PUT");
        expect(fetcher.mock.calls[3][1].method).toBe("DELETE");
        expect(fetcher.mock.calls[4][0]).toBe(`/api/media-notification/posts/${id}/hidden`);
        expect(JSON.parse(fetcher.mock.calls[4][1].body)).toEqual({ hidden: true });
    });
});
