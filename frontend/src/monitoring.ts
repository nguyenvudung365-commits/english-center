import { api, type Role } from "./api";
import { isAcademicId } from "./academic";
import { utcToVnLocal, vnLocalToIso } from "./format";
import type { Page } from "./attendance";
import { validatePlainText } from "./feedback";

export type Warning = {
    id: string; enrollmentId: string; studentId: string; classId: string;
    warningType: "Absence" | "Late" | "SkippedAssignment"; message: string; issuedAtUtc: string;
};
export type PlacementMode = "Offline" | "Online";
export type PlacementStatus = "Pending" | "Done" | "Cancelled";
export type PlacementTest = {
    id: string; studentId: string; location: string; mode: PlacementMode; scheduledAtUtc: string; testName: string;
    status: PlacementStatus; teacherId: string | null; createdBy: string; createdAtUtc: string;
};
// scheduledAt là giá trị datetime-local theo giờ Việt Nam.
export type PlacementDraft = {
    studentId: string; location: string; mode: string; scheduledAt: string; testName: string; status: string; teacherId: string;
};

export const warningLabel = (type: string) =>
    ({ Absence: "Vắng mặt", Late: "Đi trễ", SkippedAssignment: "Bỏ bài tập" } as Record<string, string>)[type] ?? type;
export const placementModeLabel = (mode: string) => ({ Offline: "Trực tiếp", Online: "Trực tuyến" } as Record<string, string>)[mode] ?? mode;
export const placementStatusLabel = (status: string) =>
    ({ Pending: "Chờ kiểm tra", Done: "Đã kiểm tra", Cancelled: "Đã hủy" } as Record<string, string>)[status] ?? status;
export const canReadMonitoring = (role: Role) => role !== "Teacher";
export const canManagePlacement = (role: Role) => role === "Staff" || role === "Admin";

export const emptyPlacementDraft: PlacementDraft = {
    studentId: "", location: "", mode: "Offline", scheduledAt: "", testName: "", status: "Pending", teacherId: "",
};
export const placementToDraft = (test: PlacementTest): PlacementDraft => ({
    studentId: test.studentId, location: test.location, mode: test.mode, scheduledAt: utcToVnLocal(test.scheduledAtUtc),
    testName: test.testName, status: test.status, teacherId: test.teacherId ?? "",
});
export function validatePlacementDraft(draft: PlacementDraft, creating: boolean, now = Date.now()): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (creating && !isAcademicId(draft.studentId.trim())) errors.studentId = ["Nhập ID học viên là UUID hợp lệ, khác rỗng."];
    const location = validatePlainText(draft.location, 200, "Địa điểm");
    const testName = validatePlainText(draft.testName, 200, "Tên bài kiểm tra");
    if (location) errors.location = [location];
    if (testName) errors.testName = [testName];
    if (!["Offline", "Online"].includes(draft.mode)) errors.mode = ["Chọn hình thức trực tiếp hoặc trực tuyến."];
    if (!["Pending", "Done", "Cancelled"].includes(draft.status)) errors.status = ["Chọn trạng thái hợp lệ."];
    const iso = vnLocalToIso(draft.scheduledAt);
    if (!iso) errors.scheduledAt = ["Chọn ngày giờ hẹn hợp lệ (giờ Việt Nam)."];
    else if (draft.status === "Pending" && Date.parse(iso) <= now) errors.scheduledAt = ["Lịch hẹn ở trạng thái chờ kiểm tra phải nằm trong tương lai."];
    if (draft.teacherId.trim() && !isAcademicId(draft.teacherId.trim())) errors.teacherId = ["ID giáo viên phải là UUID hợp lệ hoặc để trống."];
    return errors;
}
// Máy chủ trả 400 chỉ có tiêu đề tiếng Anh; ánh xạ về trường tương ứng để hiện cạnh ô nhập.
export function placementServerErrors(message: string): Record<string, string[]> {
    if (/pending tests must be in the future/i.test(message)) return { scheduledAt: ["Lịch hẹn ở trạng thái chờ kiểm tra phải nằm trong tương lai."] };
    if (/student does not exist/i.test(message)) return { studentId: ["Học viên không tồn tại hoặc chưa được đồng bộ."] };
    if (/teacher does not exist/i.test(message)) return { teacherId: ["Giáo viên không tồn tại hoặc chưa được đồng bộ."] };
    return {};
}
// Thân yêu cầu gửi lên máy chủ; chỉ gọi sau khi validatePlacementDraft không còn lỗi.
export function placementBody(draft: PlacementDraft, creating: boolean) {
    const common = {
        location: draft.location.trim(), mode: draft.mode, scheduledAt: vnLocalToIso(draft.scheduledAt),
        testName: draft.testName.trim(), teacherId: draft.teacherId.trim() || null,
    };
    return creating ? { studentId: draft.studentId.trim(), ...common } : { ...common, status: draft.status };
}

type Query = { studentId?: string; page?: number; pageSize?: number };
const params = (query: Query) => {
    const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 10) });
    if (query.studentId?.trim()) parameters.set("studentId", query.studentId.trim());
    return parameters;
};
export const listWarnings = (query: Query = {}) => api<Page<Warning>>(`/enrollment/warnings?${params(query)}`);
export const listPlacementTests = (query: Query = {}) => api<Page<PlacementTest>>(`/enrollment/placement-tests?${params(query)}`);
export const createPlacementTest = (draft: PlacementDraft) =>
    api<PlacementTest>("/enrollment/placement-tests", { method: "POST", body: JSON.stringify(placementBody(draft, true)) });
export const updatePlacementTest = (id: string, draft: PlacementDraft) =>
    api<PlacementTest>(`/enrollment/placement-tests/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(placementBody(draft, false)) });
