import { api } from "./api";
import type { Page } from "./attendance";

export type MyEnrollment = {
    id: string;
    classId: string;
    classCode: string;
    startDate: string;
    endDate: string;
    status: "Active" | "Cancelled";
    enrolledAtUtc: string;
    tuitionFee: number;
    isActive: boolean;
};

export const enrollmentStatusLabel = (status: string) =>
    ({ Active: "Đang ghi danh", Cancelled: "Đã hủy" } as Record<string, string>)[status] ?? status;

export const listMyEnrollments = (page = 1, pageSize = 20) =>
    api<Page<MyEnrollment>>(`/enrollment/enrollments/mine?${new URLSearchParams({ page: String(page), pageSize: String(pageSize) })}`);
