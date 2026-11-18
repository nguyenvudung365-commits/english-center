import { api, type Role } from "./api";
import type { Page } from "./attendance";
import { validatePlainText } from "./feedback";

export type Post = {
    id: string; classId: string; authorId: string; authorRole: string; authorName: string | null; content: string; createdAtUtc: string;
    updatedAtUtc: string | null; isHidden: boolean; likeCount: number; likedByMe: boolean; commentCount: number;
};
export type PostComment = {
    id: string; postId: string; authorId: string; authorRole: string; authorName: string | null; content: string; createdAtUtc: string;
    updatedAtUtc: string | null; isHidden: boolean;
};
export const POST_MAX = 4000;
export const COMMENT_MAX = 1000;

export const validateFeedContent = (value: string, max: number) => validatePlainText(value, max, "Nội dung", true);
export const roleLabel = (role: string) =>
    ({ Admin: "Quản trị viên", Staff: "Giáo vụ", Teacher: "Giáo viên", Student: "Học viên" } as Record<string, string>)[role] ?? role;
export const authorLabel = (name: string | null | undefined, role: string) =>
    name?.trim() ? `${name.trim()} (${roleLabel(role)})` : roleLabel(role);
// Máy chủ vẫn là nơi quyết định quyền; đây chỉ quyết định nút nào được hiện.
export const isFeedAuthor = (userId: string, authorId: string) => userId.toLowerCase() === authorId.toLowerCase();
export const canModerateFeed = (role: Role) => role === "Teacher" || role === "Staff" || role === "Admin";
export function feedAccessMessage(status: number | undefined): string | null {
    if (status === 403) return "Bạn không có quyền với bảng tin lớp này. Cần ghi danh đang hoạt động, là giáo viên phụ trách lớp, hoặc là giáo vụ/quản trị viên.";
    if (status === 404) return "Nội dung không còn tồn tại hoặc bạn không thể xem. Hãy tải lại.";
    return null;
}

const base = "/media-notification";
const body = (content: string) => JSON.stringify({ content: content.trim() });
const paging = (page: number, pageSize: number) => new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
export const listPosts = (classId: string, page: number) =>
    api<Page<Post>>(`${base}/classes/${encodeURIComponent(classId)}/posts?${paging(page, 10)}`);
export const createPost = (classId: string, content: string) =>
    api<Post>(`${base}/classes/${encodeURIComponent(classId)}/posts`, { method: "POST", body: body(content) });
export const editPost = (id: string, content: string) => api<void>(`${base}/posts/${encodeURIComponent(id)}`, { method: "PUT", body: body(content) });
export const removePost = (id: string) => api<void>(`${base}/posts/${encodeURIComponent(id)}`, { method: "DELETE" });
export const hidePost = (id: string, hidden: boolean) =>
    api<void>(`${base}/posts/${encodeURIComponent(id)}/hidden`, { method: "PUT", body: JSON.stringify({ hidden }) });
export const setLike = (id: string, liked: boolean) =>
    api<{ likeCount: number; likedByMe: boolean }>(`${base}/posts/${encodeURIComponent(id)}/like`, { method: liked ? "PUT" : "DELETE" });
export const listComments = (postId: string, page: number) =>
    api<Page<PostComment>>(`${base}/posts/${encodeURIComponent(postId)}/comments?${paging(page, 20)}`);
export const createComment = (postId: string, content: string) =>
    api<PostComment>(`${base}/posts/${encodeURIComponent(postId)}/comments`, { method: "POST", body: body(content) });
export const editComment = (id: string, content: string) =>
    api<PostComment>(`${base}/comments/${encodeURIComponent(id)}`, { method: "PUT", body: body(content) });
export const removeComment = (id: string) => api<void>(`${base}/comments/${encodeURIComponent(id)}`, { method: "DELETE" });
export const hideComment = (id: string, hidden: boolean) =>
    api<void>(`${base}/comments/${encodeURIComponent(id)}/hidden`, { method: "PUT", body: JSON.stringify({ hidden }) });
