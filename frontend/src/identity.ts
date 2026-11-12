import { api, ApiError, getSessionGeneration, type Role } from "./api";

export type Profile = {
    id: string;
    role: Role;
    fullName: string;
    email: string;
    phone: string | null;
    studentCode: string | null;
    avatarFileId: string | null;
    avatarUrl: string | null;
    avatarUrlExpiresAt: string | null;
    revision: string;
};
export type CreateAccountRequest = {
    fullName: string;
    email: string;
    password: string;
    role: "Student" | "Teacher";
    phone?: string | null;
};
export type CreatedAccount = Pick<Profile, "id" | "email" | "fullName" | "role"> & {
    studentCode?: string | null;
    phone?: string | null;
};
export type UpdateProfileRequest = Pick<Profile, "fullName" | "email" | "phone" | "avatarFileId"> & {
    expectedRevision: string;
};

export const canCreateAccounts = (role: Role) => role === "Admin" || role === "Staff";
export const createAccount = (request: CreateAccountRequest, signal?: AbortSignal) =>
    api<CreatedAccount>("/identity/users", { method: "POST", body: JSON.stringify(request), signal });
export const getProfile = (signal?: AbortSignal) => api<Profile>("/identity/me", { signal });
export const updateProfile = (request: UpdateProfileRequest, signal?: AbortSignal) =>
    api<Profile>("/identity/me", { method: "PUT", body: JSON.stringify(request), signal });

export function validateAvatar(file: File): string | null {
    const type = avatarContentType(file);
    if (!type || (file.type && file.type.toLowerCase() !== type))
        return "Chọn ảnh JPG hoặc PNG đúng định dạng.";
    if (file.size <= 0 || file.size > 2 * 1024 * 1024)
        return "Ảnh phải lớn hơn 0 và không quá 2 MB (2 MiB).";
    if (file.name.length > 255 || file.name !== file.name.trim() || /[\x00-\x1f\x7f/\\:<>\"|?*]/.test(file.name))
        return "Tên ảnh không hợp lệ (tối đa 255 ký tự, không chứa đường dẫn).";
    return null;
}

function avatarContentType(file: File) {
    const extension = file.name.split(".").at(-1)?.toLowerCase();
    return extension === "jpg" ? "image/jpeg" : extension === "png" ? "image/png" : null;
}

export async function uploadAvatar(
    file: File,
    onProgress: (message: string) => void,
    signal: AbortSignal,
): Promise<string> {
    const generation = getSessionGeneration();
    const check = () => {
        signal.throwIfAborted();
        if (generation !== getSessionGeneration()) throw new ApiError(401, "Phiên đã thay đổi. Vui lòng chọn lại ảnh.");
    };
    check();
    const invalid = validateAvatar(file);
    if (invalid) throw new ApiError(400, invalid, { AvatarFileId: [invalid] });
    const contentType = avatarContentType(file)!;
    onProgress("Đang chuẩn bị tải ảnh…");
    const ticket = await api<{ fileId: string; url: string; method: string; requiredHeaders: Record<string, string> }>(
        "/media-notification/files/presign",
        { method: "POST", body: JSON.stringify({ fileName: file.name, contentType, sizeBytes: file.size, purpose: "avatar" }), signal },
    );
    check();
    const url = new URL(ticket.url);
    const headers = new Headers(ticket.requiredHeaders);
    if (!["http:", "https:"].includes(url.protocol) || url.username || url.password ||
        ticket.method !== "PUT" || headers.get("Content-Type") !== contentType ||
        [...headers.keys()].some((key) => key !== "content-type") ||
        !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(ticket.fileId))
        throw new ApiError(0, "Vé tải ảnh không hợp lệ.");
    onProgress("Đang tải ảnh…");
    const uploaded = await fetch(url, {
        method: "PUT", headers, body: file,
        signal: AbortSignal.any([signal, AbortSignal.timeout(60000)]),
        credentials: "omit", redirect: "error", cache: "no-store",
    });
    check();
    if (!uploaded.ok) throw new ApiError(uploaded.status, "Không tải được ảnh. Hồ sơ chưa được lưu.");
    onProgress("Đang xác nhận ảnh…");
    const confirmed = await api<{ fileId: string }>(`/media-notification/files/${ticket.fileId}/confirm`, { method: "POST", signal });
    check();
    if (confirmed.fileId !== ticket.fileId) throw new ApiError(0, "Xác nhận ảnh không khớp. Hồ sơ chưa được lưu.");
    return confirmed.fileId;
}
