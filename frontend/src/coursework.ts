import { api, type Role } from "./api";
import type { Page } from "./attendance";
import { vnLocalToIso } from "./format";
import { uploadClassFile, validateClassFile, type Assignment, type Submission } from "./submissions";

// Hằng số khớp giới hạn máy chủ (Assignment/Submission/PracticeExercise).
export const ASSIGNMENT_TITLE_MAX = 200;
export const ASSIGNMENT_DESCRIPTION_MAX = 5000;
export const ASSIGNMENT_UNIT_MAX = 100;
export const ASSIGNMENT_LESSON_MAX = 200;
export const FEEDBACK_MAX = 2000;
export const PRACTICE_TITLE_MAX = 200;
export const PRACTICE_CONTENT_MAX = 20000;

export const assignmentStatuses = ["Draft", "Upcoming", "Open", "Closed"] as const;
export const assignmentStatusLabels: Record<string, string> = {
    Draft: "Nháp (học viên chưa thấy)", Upcoming: "Sắp mở", Open: "Đang mở", Closed: "Đã đóng",
};
export const assignmentKinds = ["InClass", "Homework", "Exam"] as const;
export const assignmentKindLabels: Record<string, string> = { InClass: "Trên lớp", Homework: "Về nhà", Exam: "Bài thi" };
export const MATERIAL_TITLE_MAX = 200;
export const MATERIAL_DESCRIPTION_MAX = 2000;
export const submissionStatuses = ["NotSubmitted", "Submitted", "Graded", "Missed", "Exempted"] as const;
export const submissionStatusLabels: Record<string, string> = {
    NotSubmitted: "Chưa nộp", Submitted: "Đã nộp", Graded: "Đã chấm", Missed: "Bỏ bài", Exempted: "Được miễn",
};
export const assignmentStatusLabel = (status: string) => assignmentStatusLabels[status] ?? status;
export const assignmentKindLabel = (kind: string) => assignmentKindLabels[kind] ?? kind;
export const submissionStatusLabel = (status: string) => submissionStatusLabels[status] ?? status;

// Máy chủ chỉ cho Teacher đúng lớp chấm; Staff/Admin chỉ xem. Ẩn nút theo vai trò, máy chủ vẫn quyết định.
export const canGrade = (role: Role) => role === "Teacher";
export const canManageCoursework = (role: Role) => role !== "Student";
// Bài đã công bố/legacy chỉ sửa được tiêu đề, mô tả, Unit/Lesson.
export const isAssignmentLocked = (item: Pick<Assignment, "status" | "isLegacy">) => item.isLegacy || item.status !== "Draft";

// Điểm 0–10, bước 0,1. Chấp nhận dấu phẩy hoặc dấu chấm; null nếu không hợp lệ.
export function parseScore(input: string): number | null {
    const text = input.trim();
    if (!/^(10([.,]0)?|\d([.,]\d)?)$/.test(text)) return null;
    return Number(text.replace(",", "."));
}
// Chưa chấm là null, không phải 0.
export const formatScore = (score: number | null | undefined) =>
    typeof score === "number" && Number.isFinite(score) ? score.toFixed(1).replace(".", ",") : "Chưa chấm";
export const scoreInput = (score: number | null | undefined) =>
    typeof score === "number" && Number.isFinite(score) ? score.toFixed(1).replace(".", ",") : "";
export const isGraded = (submission: Pick<Submission, "gradeVersion">) => submission.gradeVersion > 0;
// Học viên chỉ có điểm khi trạng thái là Graded; các trạng thái khác không hiển thị điểm giả.
export const studentScoreText = (status: string, score: number | null | undefined) =>
    status === "Graded" ? formatScore(score) : status === "Submitted" ? "Chưa chấm" : null;

export type GradeDraft = { score: string; feedback: string };
export function validateGradeDraft(draft: GradeDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (parseScore(draft.score) === null) errors.score = ["Điểm từ 0 đến 10, bước 0,1 (ví dụ 8,5)."];
    if (draft.feedback.trim().length > FEEDBACK_MAX) errors.feedback = [`Nhận xét tối đa ${FEEDBACK_MAX} ký tự.`];
    return errors;
}

export type AssignmentDraft = {
    title: string; description: string; kind: string; unit: string; lesson: string;
    opensLocal: string; closesLocal: string; sessionId: string; saveAsDraft: boolean;
    // Bài thi: file đề PDF chọn mới (null = giữ đề hiện có khi sửa).
    promptFile: File | null;
};
export const emptyAssignmentDraft = (): AssignmentDraft => ({
    title: "", description: "", kind: "Homework", unit: "", lesson: "", opensLocal: "", closesLocal: "", sessionId: "", saveAsDraft: false,
    promptFile: null,
});
// Bài thi cần đề PDF; đề đã công bố không đổi được nên bài khóa không kiểm tra.
export const isExamKind = (kind: string) => kind === "Exam";
// Học viên chỉ lấy đề khi bài đang mở; giáo viên/nhân viên bất cứ lúc nào (máy chủ vẫn quyết định).
export const canDownloadPrompt = (item: Pick<Assignment, "kind" | "status" | "promptFileName">, role: Role) =>
    !!item.promptFileName && (role !== "Student" || item.status === "Open");
// `locked`: bài đã công bố, giữ nguyên hạn/loại nên không kiểm tra các trường đó.
// `publishing`: tạo và công bố ngay — máy chủ bắt buộc gắn buổi học của lớp trước khi công bố (AC-05).
// `hasPrompt`: bài thi đã có đề (khi sửa).
export function validateAssignmentDraft(draft: AssignmentDraft, locked = false, publishing = false, hasPrompt = false): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (!draft.title.trim() || draft.title.trim().length > ASSIGNMENT_TITLE_MAX) errors.title = [`Tiêu đề bắt buộc, tối đa ${ASSIGNMENT_TITLE_MAX} ký tự.`];
    if (draft.description.length > ASSIGNMENT_DESCRIPTION_MAX) errors.description = [`Hướng dẫn tối đa ${ASSIGNMENT_DESCRIPTION_MAX} ký tự.`];
    if (draft.unit.trim().length > ASSIGNMENT_UNIT_MAX) errors.unit = [`Unit tối đa ${ASSIGNMENT_UNIT_MAX} ký tự.`];
    if (draft.lesson.trim().length > ASSIGNMENT_LESSON_MAX) errors.lesson = [`Lesson tối đa ${ASSIGNMENT_LESSON_MAX} ký tự.`];
    if (!locked) {
        if (!(assignmentKinds as readonly string[]).includes(draft.kind)) errors.kind = ["Chọn Trên lớp, Về nhà hoặc Bài thi."];
        if (isExamKind(draft.kind) && !draft.promptFile && !hasPrompt) errors.promptFile = ["Chọn file đề thi PDF."];
        if (draft.promptFile) {
            const invalid = validateClassFile(draft.promptFile, "exam");
            if (invalid) errors.promptFile = [invalid];
        }
        const opens = vnLocalToIso(draft.opensLocal);
        const closes = vnLocalToIso(draft.closesLocal);
        if (!opens) errors.opensLocal = ["Nhập giờ mở hợp lệ (giờ Việt Nam)."];
        if (!closes) errors.closesLocal = ["Nhập giờ đóng hợp lệ (giờ Việt Nam)."];
        else if (opens && Date.parse(closes) <= Date.parse(opens)) errors.closesLocal = ["Giờ đóng phải sau giờ mở."];
    }
    if (publishing && !draft.sessionId) errors.sessionId = ["Chọn buổi học của lớp trước khi công bố, hoặc chọn “Lưu nháp”."];
    return errors;
}

// Tên học viên ở màn hình giáo viên (kèm mã học viên nếu có); null khi máy chủ không trả tên để giao diện hiện ID.
export function studentLabel(submission: Pick<Submission, "studentName" | "studentCode">): string | null {
    const name = submission.studentName?.trim();
    if (!name) return null;
    return submission.studentCode ? `${name} (${submission.studentCode})` : name;
}

export type PracticeDraft = { title: string; content: string };
export function validatePracticeDraft(draft: PracticeDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (!draft.title.trim() || draft.title.trim().length > PRACTICE_TITLE_MAX) errors.title = [`Tên bài bắt buộc, tối đa ${PRACTICE_TITLE_MAX} ký tự.`];
    if (!draft.content.trim() || draft.content.trim().length > PRACTICE_CONTENT_MAX) errors.content = [`Nội dung bắt buộc, tối đa ${PRACTICE_CONTENT_MAX} ký tự.`];
    return errors;
}

export type StudentAssignment = {
    assignment: Assignment; submissionStatus: string; isObligated: boolean; submissionId: string | null;
    submittedAt: string | null; score: number | null; feedback: string | null; gradedAt: string | null;
    isTransferredHistory?: boolean;
};
export type GradeHistoryEntry = {
    version: number; score: number; previousScore: number | null; feedback: string | null; gradedBy: string;
    gradedByName: string | null; gradedAt: string;
};
export type PracticeSummary = {
    id: string; classId: string; title: string; preview: string; createdBy: string; createdByName: string;
    createdAt: string; updatedAt: string | null; version: number;
};
export type Practice = Omit<PracticeSummary, "preview"> & { content: string; updatedBy: string | null };

function query(values: Record<string, string | number | undefined>) {
    const parameters = new URLSearchParams();
    for (const [key, value] of Object.entries(values)) if (value !== undefined && String(value).trim() !== "") parameters.set(key, String(value).trim());
    return parameters.toString();
}
export type AssignmentFilter = { search?: string; status?: string; kind?: string };
export const listClassAssignments = (classId: string, filter: AssignmentFilter = {}) =>
    api<Assignment[]>(`/academic/assignments?${query({ classId, search: filter.search, status: filter.status, kind: filter.kind })}`);
export type MineFilter = AssignmentFilter & { classId?: string; submissionStatus?: string; page?: number; pageSize?: number };
export const listMyAssignments = (filter: MineFilter = {}) =>
    api<Page<StudentAssignment>>(`/academic/assignments/mine?${query({
        classId: filter.classId, search: filter.search, status: filter.status, submissionStatus: filter.submissionStatus,
        kind: filter.kind, page: filter.page ?? 1, pageSize: filter.pageSize ?? 20,
    })}`);

const text = (value: string) => value.trim() || null;
// Bài thi: tải đề PDF lên và xác nhận trước, rồi mới tạo bài kèm promptFileId. Tải lỗi thì chưa tạo gì.
export async function createAssignment(classId: string, draft: AssignmentDraft, signal?: AbortSignal) {
    const promptFileId = draft.promptFile ? await uploadClassFile(draft.promptFile, isExamKind(draft.kind) ? "exam" : "material", signal) : undefined;
    return api<Assignment>("/academic/assignments", {
        method: "POST", signal,
        body: JSON.stringify({
            classId, title: draft.title.trim(), description: draft.description, kind: draft.kind,
            unit: text(draft.unit), lesson: text(draft.lesson), sessionId: draft.sessionId || null,
            opensAtUtc: vnLocalToIso(draft.opensLocal), closesAtUtc: vnLocalToIso(draft.closesLocal),
            publish: !draft.saveAsDraft, promptFileId,
        }),
    });
}
// Bài đã công bố: gửi lại đúng hạn/loại/buổi hiện có để máy chủ không từ chối (409); đề thi đã công bố không đổi.
export async function updateAssignment(item: Assignment, draft: AssignmentDraft, signal?: AbortSignal) {
    const locked = isAssignmentLocked(item);
    const promptFileId = !locked && draft.promptFile ? await uploadClassFile(draft.promptFile, isExamKind(draft.kind) ? "exam" : "material", signal) : undefined;
    return api<Assignment>(`/academic/assignments/${encodeURIComponent(item.id)}`, {
        method: "PUT", signal,
        body: JSON.stringify({
            title: draft.title.trim(), description: draft.description, unit: text(draft.unit), lesson: text(draft.lesson),
            kind: locked ? item.kind : draft.kind, sessionId: locked ? item.sessionId : draft.sessionId || item.sessionId,
            opensAtUtc: locked ? item.opensAtUtc : vnLocalToIso(draft.opensLocal),
            closesAtUtc: locked ? item.closesAtUtc : vnLocalToIso(draft.closesLocal),
            expectedVersion: item.version, promptFileId,
        }),
    });
}

export type ClassMaterial = {
    id: string; classId: string; title: string; description: string; fileName: string; contentType: string; sizeBytes: number;
    uploadedBy: string; createdAt: string;
};
export type MaterialDraft = { title: string; description: string; file: File | null };
export const emptyMaterialDraft = (): MaterialDraft => ({ title: "", description: "", file: null });
export function validateMaterialDraft(draft: MaterialDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (!draft.title.trim() || draft.title.trim().length > MATERIAL_TITLE_MAX) errors.title = [`Tiêu đề bắt buộc, tối đa ${MATERIAL_TITLE_MAX} ký tự.`];
    if (draft.description.trim().length > MATERIAL_DESCRIPTION_MAX) errors.description = [`Mô tả tối đa ${MATERIAL_DESCRIPTION_MAX} ký tự.`];
    if (!draft.file) errors.file = ["Chọn file tài liệu."];
    else {
        const invalid = validateClassFile(draft.file, "material");
        if (invalid) errors.file = [invalid];
    }
    return errors;
}
export const listMaterials = (classId: string) => api<ClassMaterial[]>(`/academic/materials?${query({ classId })}`);
// Tải file lên trước, chỉ đăng tài liệu khi file đã được xác nhận.
export async function createMaterial(classId: string, draft: MaterialDraft, signal?: AbortSignal) {
    const fileId = await uploadClassFile(draft.file!, "material", signal);
    return api<ClassMaterial>("/academic/materials", {
        method: "POST", signal,
        body: JSON.stringify({ classId, title: draft.title.trim(), description: draft.description.trim(), fileId }),
    });
}
export type FileLink = { url: string; expiresAt: string };
export const materialDownload = (id: string, signal?: AbortSignal) =>
    api<FileLink>(`/academic/materials/${encodeURIComponent(id)}/download`, { signal });
export const promptDownload = (assignmentId: string, signal?: AbortSignal) =>
    api<FileLink>(`/academic/assignments/${encodeURIComponent(assignmentId)}/prompt/download`, { signal });
// Link tải do máy chủ cấp: chỉ nhận http(s), không chứa thông tin đăng nhập.
export function safeDownloadUrl(link: FileLink): string {
    const url = new URL(link.url);
    if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) throw new Error("Đường dẫn tải không hợp lệ.");
    return url.href;
}
export const publishAssignment = (item: Assignment) =>
    api<Assignment>(`/academic/assignments/${encodeURIComponent(item.id)}/publish`, {
        method: "POST", body: JSON.stringify({ expectedVersion: item.version }),
    });

export function gradeSubmission(submission: Submission, draft: GradeDraft) {
    return api<Submission>(`/academic/submissions/${encodeURIComponent(submission.id)}/grade`, {
        method: "PUT",
        body: JSON.stringify({
            score: parseScore(draft.score), feedback: draft.feedback.trim() || null,
            expectedGradeVersion: submission.gradeVersion, expectedRevision: submission.revision,
        }),
    });
}
export const listGradeHistory = (submissionId: string) =>
    api<GradeHistoryEntry[]>(`/academic/submissions/${encodeURIComponent(submissionId)}/grades`);

export const listPractice = (classId: string, search = "", page = 1, pageSize = 10) =>
    api<Page<PracticeSummary>>(`/academic/practice?${query({ classId, search, page, pageSize })}`);
export const getPractice = (id: string) => api<Practice>(`/academic/practice/${encodeURIComponent(id)}`);
export const createPractice = (classId: string, draft: PracticeDraft) => api<Practice>("/academic/practice", {
    method: "POST", body: JSON.stringify({ classId, title: draft.title.trim(), content: draft.content.trim() }),
});
export const updatePractice = (item: Practice, draft: PracticeDraft) =>
    api<Practice>(`/academic/practice/${encodeURIComponent(item.id)}`, {
        method: "PUT", body: JSON.stringify({ title: draft.title.trim(), content: draft.content.trim(), expectedVersion: item.version }),
    });
export const deletePractice = (item: Practice) =>
    api<void>(`/academic/practice/${encodeURIComponent(item.id)}?expectedVersion=${item.version}`, { method: "DELETE" });
