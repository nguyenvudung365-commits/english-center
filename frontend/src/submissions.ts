import { api, ApiError, getSessionGeneration } from "./api";

export type Assignment = {
    id: string; classId: string; title: string; description: string;
    opensAtUtc: string; closesAtUtc: string;
    kind: string; unit: string | null; lesson: string | null; sessionId: string | null; isLegacy: boolean;
    status: string; publishedAt: string | null; createdAt: string | null; version: number;
    // Tên file đề PDF; tải qua /academic/assignments/{id}/prompt/download.
    promptFileName?: string | null;
};
export type Submission = {
    id: string; assignmentId: string; studentId: string; revision: string; submittedAt: string;
    files: { fileId: string; fileName: string; contentType: string; sizeBytes: number }[];
    answerText?: string | null;
    score: number | null; feedback: string | null; gradeVersion: number; gradedAt: string | null; gradedBy: string | null;
    // Chỉ có ở màn hình giáo viên/nhân viên; null khi Identity không trả tên (ví dụ tài khoản bị khóa).
    studentName?: string | null; studentCode?: string | null;
};
export type UploadStatus = "uploading" | "confirming" | "confirmed";
const types: Record<string, string> = {
    pdf: "application/pdf", docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    mp3: "audio/mpeg", m4a: "audio/mp4", jpg: "image/jpeg", png: "image/png",
};
export const submissionAccept = ".pdf,.docx,.mp3,.m4a,.jpg,.png";
export const ANSWER_MAX = 10000;
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

// Tệp lớp giáo viên đăng: "material" (tài liệu, cùng định dạng với bài nộp) hoặc "exam" (đề thi, chỉ PDF).
export type ClassFilePurpose = "material" | "exam";
export const examAccept = ".pdf";
export function validateClassFile(file: File, purpose: ClassFilePurpose): string | null {
    if (purpose === "exam" && contentType(file) !== "application/pdf") return "Đề thi phải là file PDF.";
    return validateSubmissionFiles([file]);
}

type UploadTicket = { fileId: string; url: string; method: string; requiredHeaders: Record<string, string> };
// Phiên đăng nhập đổi hoặc hủy giữa chừng thì dừng, không gửi tiếp file của phiên cũ.
function uploadGuard(signal?: AbortSignal) {
    const version = getSessionGeneration();
    return () => {
        signal?.throwIfAborted();
        if (version !== getSessionGeneration()) throw new ApiError(401, "Phiên đã thay đổi. Vui lòng chọn lại file.");
    };
}
// Presign → PUT thẳng lên kho → xác nhận; trả fileId đã xác nhận. `taken`: ID đã nhận trong cùng đợt (chống vé trùng).
async function uploadConfirmed(
    file: File, purpose: "submission" | ClassFilePurpose, signal: AbortSignal | undefined, check: () => void, taken: string[],
    onProgress?: (status: UploadStatus) => void, failure = "Bài nộp chưa được lưu.",
): Promise<string> {
    check(); onProgress?.("uploading"); check();
    const type = contentType(file);
    const ticket = await api<UploadTicket>("/media-notification/files/presign", {
        method: "POST", body: JSON.stringify({ fileName: file.name, contentType: type, sizeBytes: file.size, purpose }), signal,
    });
    check();
    const url = new URL(ticket.url);
    const headers = new Headers(ticket.requiredHeaders);
    if (!["https:", "http:"].includes(url.protocol) || url.username || url.password ||
        ticket.method !== "PUT" || headers.get("Content-Type") !== type ||
        [...headers.keys()].some((key) => key.toLowerCase() !== "content-type") ||
        !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(ticket.fileId) || taken.includes(ticket.fileId))
        throw new ApiError(0, "Vé tải file không hợp lệ.");
    const uploaded = await fetch(url, { method: "PUT", headers, body: file, signal, credentials: "omit", redirect: "error", cache: "no-store" });
    check();
    if (!uploaded.ok) throw new ApiError(uploaded.status, `Không tải được file ${file.name}. ${failure}`);
    onProgress?.("confirming"); check();
    const confirmed = await api<{ fileId: string }>(`/media-notification/files/${ticket.fileId}/confirm`, { method: "POST", signal });
    check();
    if (confirmed.fileId !== ticket.fileId) throw new ApiError(0, `Xác nhận file không khớp. ${failure}`);
    onProgress?.("confirmed");
    return confirmed.fileId;
}

// Tải một tài liệu/đề thi lên Media; chỉ giáo viên/nhân viên được cấp vé (máy chủ quyết định, không tin nút bấm).
export async function uploadClassFile(file: File, purpose: ClassFilePurpose, signal?: AbortSignal): Promise<string> {
    const check = uploadGuard(signal);
    check();
    const invalid = validateClassFile(file, purpose);
    if (invalid) throw new ApiError(400, invalid);
    return uploadConfirmed(file, purpose, signal, check, [], undefined, "Chưa lưu gì.");
}

export async function submitAssignment(
    assignmentId: string, files: File[], expectedRevision: string | null,
    onProgress?: (index: number, status: UploadStatus) => void, signal?: AbortSignal, answerText?: string,
): Promise<Submission> {
    const check = uploadGuard(signal);
    check();
    if ((answerText?.length ?? 0) > ANSWER_MAX) throw new ApiError(400, `Câu trả lời tối đa ${ANSWER_MAX} ký tự.`);
    const answer = answerText?.trim() || undefined;
    const invalid = files.length || !answer ? validateSubmissionFiles(files) : null;
    if (invalid) throw new ApiError(400, invalid);
    const fileIds: string[] = [];
    // Sequential bounded uploads avoid buffering several 20 MiB bodies at once.
    for (const [index, file] of files.entries())
        fileIds.push(await uploadConfirmed(file, "submission", signal, check, fileIds, status => onProgress?.(index, status)));
    check();
    const submission = await api<Submission>(`/academic/assignments/${encodeURIComponent(assignmentId)}/submissions`, {
        method: "PUT", body: JSON.stringify({ fileIds, expectedRevision, answerText: answer }), signal,
    });
    check();
    return submission;
}
