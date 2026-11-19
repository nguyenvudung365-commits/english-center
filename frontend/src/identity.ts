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
export type ManagedAccount = Pick<Profile, "id" | "fullName" | "email" | "phone" | "studentCode" | "revision"> & { isLocked: boolean };
export type ManagedAccountDirectory = "students" | "staff";
export type StaffProvisionRequest = Omit<CreateAccountRequest, "role">;
export type StudentContactRequest = Pick<UpdateProfileRequest, "fullName" | "email" | "phone" | "expectedRevision">;
type ManagedAccountPage = { items: ManagedAccount[]; total: number; page: number; pageSize: number };
const directoryPath = (directory: ManagedAccountDirectory) => `/identity/users/${directory}`;

export function listManagedAccounts(directory: ManagedAccountDirectory, search: { q: string; page: number }, signal?: AbortSignal) {
    const query = new URLSearchParams({ q: search.q, page: String(search.page) });
    return api<ManagedAccountPage>(`${directoryPath(directory)}?${query}`, { signal });
}
const studentPath = (id: string) => `${directoryPath("students")}/${encodeURIComponent(id)}`;
export const getManagedStudent = (id: string, signal?: AbortSignal) => api<ManagedAccount>(studentPath(id), { signal });
export const updateManagedStudent = (id: string, request: StudentContactRequest, signal?: AbortSignal) =>
    api<ManagedAccount>(studentPath(id), { method: "PUT", body: JSON.stringify(request), signal });
export const provisionStaff = (request: StaffProvisionRequest, signal?: AbortSignal) =>
    api<CreatedAccount>(directoryPath("staff"), { method: "POST", body: JSON.stringify(request), signal });
export const setManagedAccountLock = (directory: ManagedAccountDirectory, id: string, isLocked: boolean, expectedRevision: string, signal?: AbortSignal) =>
    api<ManagedAccount>(`${directoryPath(directory)}/${encodeURIComponent(id)}/lock`, {
        method: "PUT", body: JSON.stringify({ isLocked, expectedRevision }), signal,
    });

export const vietnamMobileHint = "Ví dụ: 090 123 4567 hoặc +84 90 123 4567.";

export function normalizeVietnamMobile(input: string | null | undefined, previous?: string | null): string | null {
    const value = input?.trim();
    if (!value) return null;
    if (value === previous) return previous;
    const number = value.length <= 32 && /^(?:0|\+84)(?:[ .-]*[0-9]){9}$/.test(value)
        ? value.replace(/[ .-]/g, "").replace(/^(?:0|\+84)/, "") : "";
    if (!/^(?:3[2-9]|5[25689]|7[06789]|8[1-9]|9[0-9])[0-9]{7}$/.test(number)) {
        const message = `Nhập số di động Việt Nam hợp lệ. ${vietnamMobileHint}`;
        throw new ApiError(400, message, { Phone: [message] });
    }
    return `+84${number}`;
}

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
