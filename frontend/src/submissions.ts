import { api, ApiError, getSessionGeneration } from "./api";

export type Assignment = {
    id: string; classId: string; title: string; description: string;
    opensAtUtc: string; closesAtUtc: string;
};
export type Submission = {
    id: string; assignmentId: string; studentId: string; revision: string; submittedAt: string;
    files: { fileId: string; fileName: string; contentType: string; sizeBytes: number }[];
};
export type UploadStatus = "uploading" | "confirming" | "confirmed";
const types: Record<string, string> = {
    pdf: "application/pdf", docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    mp3: "audio/mpeg", m4a: "audio/mp4", jpg: "image/jpeg", png: "image/png",
};
export const submissionAccept = ".pdf,.docx,.mp3,.m4a,.jpg,.png";
const mimeAliases: Record<string, string> = {
    "audio/x-m4a": "audio/mp4", "audio/mp3": "audio/mpeg", "image/pjpeg": "image/jpeg",
};
function contentType(file: File) { return types[file.name.split(".").at(-1)?.toLowerCase() ?? ""]; }
export function validateSubmissionFiles(files: File[]): string | null {
    if (files.length < 1 || files.length > 5) return "Chọn từ 1 đến 5 file.";
    for (const file of files) {
        if (!file.name || file.name.length > 255 || file.name !== file.name.trim() || /[\x00-\x1f\x7f/\\:<>"|?*]/.test(file.name))
            return "Tên file không hợp lệ (tối đa 255 ký tự, không có ký tự đường dẫn).";
        const type = contentType(file);
        const reportedType = file.type.toLowerCase();
        if (!type || (reportedType && (mimeAliases[reportedType] ?? reportedType) !== type))
            return "Chỉ nhận PDF, DOCX, MP3, M4A, JPG và PNG đúng định dạng.";
        if (file.size <= 0 || file.size > 20 * 1024 * 1024) return "Mỗi file phải lớn hơn 0 và không quá 20 MiB.";
    }
    if (files.reduce((sum, file) => sum + file.size, 0) > 50 * 1024 * 1024) return "Tổng dung lượng không quá 50 MiB.";
    return null;
}

type UploadTicket = { fileId: string; url: string; method: string; requiredHeaders: Record<string, string> };
export async function submitAssignment(
    assignmentId: string, files: File[], expectedRevision: string | null,
    onProgress?: (index: number, status: UploadStatus) => void, signal?: AbortSignal,
): Promise<Submission> {
    const version = getSessionGeneration();
    const check = () => {
        signal?.throwIfAborted();
        if (version !== getSessionGeneration()) throw new ApiError(401, "Phiên đã thay đổi. Vui lòng chọn lại file.");
    };
    check();
    const invalid = validateSubmissionFiles(files);
    if (invalid) throw new ApiError(400, invalid);
    const fileIds: string[] = [];
    // Sequential bounded uploads avoid buffering several 20 MiB bodies at once.
    for (const [index, file] of files.entries()) {
        check(); onProgress?.(index, "uploading"); check();
        const type = contentType(file);
        const ticket = await api<UploadTicket>("/media-notification/files/presign", {
            method: "POST", body: JSON.stringify({ fileName: file.name, contentType: type, sizeBytes: file.size, purpose: "submission" }), signal,
        });
        check();
        const url = new URL(ticket.url);
        const headers = new Headers(ticket.requiredHeaders);
        if (!["https:", "http:"].includes(url.protocol) || url.username || url.password ||
            ticket.method !== "PUT" || headers.get("Content-Type") !== type ||
            [...headers.keys()].some((key) => key.toLowerCase() !== "content-type") ||
            !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(ticket.fileId) || fileIds.includes(ticket.fileId))
            throw new ApiError(0, "Vé tải file không hợp lệ.");
        const uploaded = await fetch(url, { method: "PUT", headers, body: file, signal, credentials: "omit", redirect: "error", cache: "no-store" });
        check();
        if (!uploaded.ok) throw new ApiError(uploaded.status, `Không tải được file ${index + 1}. Bài nộp chưa được lưu.`);
        onProgress?.(index, "confirming"); check();
        const confirmed = await api<{ fileId: string }>(`/media-notification/files/${ticket.fileId}/confirm`, { method: "POST", signal });
        check();
        if (confirmed.fileId !== ticket.fileId) throw new ApiError(0, "Xác nhận file không khớp. Bài nộp chưa được lưu.");
        fileIds.push(confirmed.fileId); onProgress?.(index, "confirmed");
    }
    check();
    const submission = await api<Submission>(`/academic/assignments/${encodeURIComponent(assignmentId)}/submissions`, {
        method: "PUT", body: JSON.stringify({ fileIds, expectedRevision }), signal,
    });
    check();
    return submission;
}
