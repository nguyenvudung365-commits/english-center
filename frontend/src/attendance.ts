import { api, ApiError } from "./api";
import { isAcademicId } from "./academic";

export const attendanceStatuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"] as const;
export type AttendanceStatus = typeof attendanceStatuses[number];
export type PageQuery = { page?: number; pageSize?: number };
export type Page<T> = { items: T[]; page: number; pageSize: number; total: number };
export type AttendanceRow = {
    id: string; sessionId: string; classId: string; studentId: string; status: AttendanceStatus; revision: number;
    recordedBy: string; recordedAtUtc: string; updatedBy: string; updatedAtUtc: string;
};
export type AttendanceRevision = {
    attendanceId: string; revision: number; previousStatus: string | null; status: AttendanceStatus;
    changedBy: string; changedAtUtc: string;
};
export type StatusCounts = { present: number; absentUnexcused: number; absentExcused: number; late: number; lateOver20Min: number; leftEarly: number };
export type StatusPercents = StatusCounts;
export type Summary = { recordedCount: number; counts: StatusCounts; percentages: StatusPercents; absentCount: number; lateCount: number; leftEarlyCount: number };
export type SummaryQuery = { classId?: string; sessionId?: string; studentId?: string; status?: AttendanceStatus };
export type AttendanceQuery = SummaryQuery & PageQuery;
export type RosterItem = { studentId: string; fullName: string | null; studentCode: string | null; isActive: boolean; attendance: AttendanceRow | null };
export type RosterPage = Page<RosterItem> & { sessionId: string; classId: string; asOfUtc: string; canWrite: boolean; writeClosesAtUtc: string };
export type ReplayAttempt = {
    id: string; attendanceId: string; revision: number; requestedBy: string; requestedAtUtc: string; reason: string;
    dispatchStatus: "Pending" | "Dispatched" | "Failed"; completedAtUtc: string | null;
};
export type ReplayDispatchResponse = { attemptId: string; attendanceId: string; revision: number; dispatchStatus: "Dispatched" };

function requireId(id: string) {
    if (typeof id !== "string" || id.length !== 36 || !isAcademicId(id))
        throw new ApiError(400, "Mã định danh phải là UUID hợp lệ, khác rỗng.");
}
const positive = (value: number) => Number.isSafeInteger(value) && value >= 1;
function pagination(query: PageQuery) {
    if (!query || typeof query !== "object" || Array.isArray(query)) throw new ApiError(400, "Bộ lọc phân trang không hợp lệ.");
    const page = query.page === undefined ? 1 : query.page;
    const pageSize = query.pageSize === undefined ? 20 : query.pageSize;
    if (!positive(page) || !positive(pageSize) || pageSize > 100)
        throw new ApiError(400, "Trang phải là số nguyên dương; số mục mỗi trang từ 1 đến 100.");
    return new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
}
function filters(query: SummaryQuery, parameters = new URLSearchParams()) {
    if (!query || typeof query !== "object" || Array.isArray(query)) throw new ApiError(400, "Bộ lọc điểm danh không hợp lệ.");
    for (const key of ["classId", "sessionId", "studentId"] as const) {
        const value = query[key];
        if (value !== undefined) { requireId(value); parameters.set(key, value); }
    }
    if (query.status !== undefined) {
        if (!attendanceStatuses.includes(query.status)) throw new ApiError(400, "Chọn trạng thái điểm danh hợp lệ.");
        parameters.set("status", query.status);
    }
    return parameters;
}
export async function listSessionAttendance(sessionId: string, query: PageQuery = {}): Promise<RosterPage> {
    requireId(sessionId);
    return api<RosterPage>(`/academic/sessions/${sessionId}/attendance?${pagination(query)}`);
}
export async function recordAttendance(sessionId: string, studentId: string, body: { status: AttendanceStatus; expectedRevision: number | null }): Promise<AttendanceRow> {
    requireId(sessionId); requireId(studentId);
    if (!body || !attendanceStatuses.includes(body.status) || (body.expectedRevision !== null && !positive(body.expectedRevision)))
        throw new ApiError(400, "Trạng thái và expectedRevision (null hoặc số nguyên dương) là bắt buộc.");
    return api<AttendanceRow>(`/academic/sessions/${sessionId}/attendance/${studentId}`, {
        method: "PUT", body: JSON.stringify({ status: body.status, expectedRevision: body.expectedRevision }),
    });
}
export async function listAttendance(query: AttendanceQuery = {}): Promise<Page<AttendanceRow>> {
    return api<Page<AttendanceRow>>(`/academic/attendance?${filters(query, pagination(query))}`);
}
export async function getAttendanceSummary(query: SummaryQuery = {}): Promise<Summary> {
    const parameters = filters(query).toString();
    return api<Summary>(`/academic/attendance/summary${parameters ? `?${parameters}` : ""}`);
}
export async function listAttendanceHistory(attendanceId: string, query: PageQuery = {}): Promise<Page<AttendanceRevision>> {
    requireId(attendanceId);
    return api<Page<AttendanceRevision>>(`/academic/attendance/${attendanceId}/history?${pagination(query)}`);
}
export async function replayAttendance(attendanceId: string, body: { revision: number; reason: string }): Promise<ReplayDispatchResponse> {
    requireId(attendanceId);
    if (!body || !positive(body.revision) || typeof body.reason !== "string" || !body.reason.trim() || body.reason.trim().length > 500)
        throw new ApiError(400, "Phiên bản phải là số nguyên dương; lý do bắt buộc, tối đa 500 ký tự.");
    return api<ReplayDispatchResponse>(`/academic/attendance/${attendanceId}/replay`, {
        method: "POST", body: JSON.stringify({ revision: body.revision, reason: body.reason.trim() }),
    });
}
export async function listAttendanceReplays(attendanceId: string, query: PageQuery = {}): Promise<Page<ReplayAttempt>> {
    requireId(attendanceId);
    return api<Page<ReplayAttempt>>(`/academic/attendance/${attendanceId}/replays?${pagination(query)}`);
}
