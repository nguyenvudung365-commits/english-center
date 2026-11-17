import { api, ApiError } from "./api";
import { isAcademicId } from "./academic";
import type { StatusCounts, Summary } from "./attendance";

export type ClassProgress = { classId: string; asOfUtc: string; totalSessions: number; completedSessions: number; remainingSessions: number; completionPercent: number };
export type AcademicStatistics = {
    currentClassCount: number; currentCompletedSessions: number; previousCompletedSessions: number;
    completedSessionsChangePercent: number | null; attendance: Summary;
};
export type ContributorMetric = { key: string; current: number | null; previous: number | null; changePercent: number | null };
export type ContributorSection = {
    key: "assessment" | "warnings" | "feedback"; availability: "available" | "unavailable";
    reason: "not_registered" | "provider_failed" | null; metrics: ContributorMetric[] | null;
};
export type MonthPoint = { month: string; completedSessions: number; recordedAttendance: number; counts: StatusCounts };
export type PersonalStatistics = {
    month: string; timezone: "UTC+07:00"; asOfUtc: string; availability: "complete" | "partial";
    academic: AcademicStatistics; contributors: ContributorSection[]; chart: MonthPoint[];
};
export function validateStatisticsMonth(month: string): boolean {
    return typeof month === "string" && month.length === 7 && /^\d{4}-(0[1-9]|1[0-2])$/.test(month) && month >= "0002-01" && month <= "9998-12";
}
export async function getClassProgress(classId: string): Promise<ClassProgress> {
    if (typeof classId !== "string" || classId.length !== 36 || !isAcademicId(classId))
        throw new ApiError(400, "Mã lớp phải là UUID hợp lệ, khác rỗng.");
    return api<ClassProgress>(`/academic/classes/${classId}/progress`);
}
export async function getPersonalStatistics(month?: string): Promise<PersonalStatistics> {
    if (month !== undefined && !validateStatisticsMonth(month)) throw new ApiError(400, "Chọn tháng từ 0002-01 đến 9998-12, theo YYYY-MM.");
    const query = month === undefined ? "" : `?${new URLSearchParams({ month })}`;
    return api<PersonalStatistics>(`/academic/statistics/mine${query}`);
}
