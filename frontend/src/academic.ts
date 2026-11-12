import { api, ApiError, type Role } from "./api";

export type Course = {
    id: string;
    code: string;
    name: string;
    description: string | null;
};
export type Teacher = { id: string; fullName: string; email: string };
export type ClassDraft = {
    code: string;
    name: string;
    courseId: string;
    teacherId: string;
    classType: string;
    startDate: string;
    endDate: string;
    maxStudents: string;
    tuitionFee: string;
};
export type ClassDetail = Omit<
    ClassDraft,
    "maxStudents" | "tuitionFee" | "classType"
> & {
    id: string;
    courseCode: string;
    courseName: string;
    teacherName: string;
    classType: "Main" | "Supplementary";
    maxStudents: number;
    tuitionFee: number;
    status: string;
    createdAt: string;
};

export const canManageAcademic = (role: Role) =>
    role === "Admin" || role === "Staff";
export function isAcademicId(value: string): boolean {
    return (
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
            value,
        ) && value !== "00000000-0000-0000-0000-000000000000"
    );
}
function isDate(value: string): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const [year, month, day] = value.split("-").map(Number);
    if (year < 1 || month < 1 || month > 12 || day < 1) return false;
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    return (
        day <=
        [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1]
    );
}
export function validateClassDraft(
    draft: ClassDraft,
): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    const code = draft.code.trim().toUpperCase();
    if (!code || code.length > 50 || !/^[A-Z0-9][A-Z0-9._-]*$/.test(code))
        errors.code = [
            "Mã lớp gồm 1–50 ký tự A–Z, 0–9, dấu chấm, gạch dưới hoặc gạch ngang; bắt đầu bằng chữ hoặc số.",
        ];
    if (!draft.name.trim() || draft.name.trim().length > 200)
        errors.name = ["Tên lớp bắt buộc, tối đa 200 ký tự."];
    if (!isAcademicId(draft.courseId))
        errors.courseId = ["Chọn khóa học có UUID hợp lệ, khác rỗng."];
    if (!isAcademicId(draft.teacherId))
        errors.teacherId = ["Chọn giáo viên có UUID hợp lệ, khác rỗng."];
    if (!["Main", "Supplementary"].includes(draft.classType))
        errors.classType = ["Chọn lớp chính hoặc lớp bổ trợ."];
    if (!isDate(draft.startDate))
        errors.startDate = ["Ngày bắt đầu phải hợp lệ, theo YYYY-MM-DD."];
    if (!isDate(draft.endDate))
        errors.endDate = ["Ngày kết thúc phải hợp lệ, theo YYYY-MM-DD."];
    else if (isDate(draft.startDate) && draft.endDate <= draft.startDate)
        errors.endDate = ["Ngày kết thúc phải sau ngày bắt đầu."];
    const whole = (value: string, min: number, max: number) =>
        /^\d+$/.test(value.trim()) &&
        Number.isSafeInteger(Number(value)) &&
        Number(value) >= min &&
        Number(value) <= max;
    if (!whole(draft.maxStudents, 1, 2147483647))
        errors.maxStudents = [
            "Sĩ số phải là số nguyên từ 1 đến 2.147.483.647.",
        ];
    if (!whole(draft.tuitionFee, 0, 999999999999))
        errors.tuitionFee = [
            "Học phí bắt buộc, là số nguyên VND từ 0 đến 999.999.999.999.",
        ];
    return errors;
}
export const listCourses = () => api<Course[]>("/academic/courses");
export const listTeachers = () => api<Teacher[]>("/academic/teachers");
export async function getClass(id: string): Promise<ClassDetail> {
    if (!isAcademicId(id))
        throw new ApiError(
            400,
            "Mã định danh lớp phải là UUID hợp lệ, khác rỗng.",
        );
    return api<ClassDetail>(`/academic/classes/${encodeURIComponent(id)}`);
}
export async function createClass(draft: ClassDraft): Promise<ClassDetail> {
    const errors = validateClassDraft(draft);
    if (Object.keys(errors).length)
        throw new ApiError(400, "Vui lòng kiểm tra thông tin lớp.", errors);
    return api<ClassDetail>("/academic/classes", {
        method: "POST",
        body: JSON.stringify({
            ...draft,
            code: draft.code.trim().toUpperCase(),
            name: draft.name.trim(),
            maxStudents: Number(draft.maxStudents),
            tuitionFee: Number(draft.tuitionFee),
        }),
    });
}
