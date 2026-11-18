import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import {
    canManagePlacement, canReadMonitoring, createPlacementTest, emptyPlacementDraft, listPlacementTests, listWarnings,
    placementBody, placementServerErrors, placementToDraft, updatePlacementTest, validatePlacementDraft, warningLabel, type PlacementDraft,
    type PlacementTest,
} from "./monitoring";

const id = "11111111-1111-1111-1111-111111111111";
const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });
const now = Date.parse("2026-11-17T00:00:00Z");
const draft: PlacementDraft = {
    ...emptyPlacementDraft, studentId: id, location: " Phòng 101 ", testName: " IELTS ", scheduledAt: "2026-11-20T09:30",
};

describe("Cảnh báo và hẹn test đầu vào", () => {
    it("nhãn tiếng Việt cho loại cảnh báo", () => {
        expect(warningLabel("Absence")).toBe("Vắng mặt");
        expect(warningLabel("Late")).toBe("Đi trễ");
        expect(warningLabel("SkippedAssignment")).toBe("Bỏ bài tập");
        expect(warningLabel("Khác")).toBe("Khác");
    });
    it("Giáo viên bị ẩn mục; chỉ Staff/Admin quản lý hẹn test", () => {
        expect(canReadMonitoring("Teacher")).toBe(false);
        expect(canReadMonitoring("Student")).toBe(true);
        expect(canManagePlacement("Admin")).toBe(true);
        expect(canManagePlacement("Student")).toBe(false);
    });
    it("kiểm tra form hẹn test hợp lệ", () => {
        expect(validatePlacementDraft(draft, true, now)).toEqual({});
    });
    it("bắt các lỗi từng trường", () => {
        const errors = validatePlacementDraft({ ...emptyPlacementDraft, studentId: "x", mode: "Hybrid", teacherId: "x" }, true, now);
        expect(Object.keys(errors).sort()).toEqual(["location", "mode", "scheduledAt", "studentId", "teacherId", "testName"]);
    });
    it("Pending phải ở tương lai nhưng Done/Cancelled thì không bắt buộc", () => {
        const past = { ...draft, scheduledAt: "2026-11-10T09:30" };
        expect(validatePlacementDraft(past, true, now).scheduledAt).toBeDefined();
        expect(validatePlacementDraft({ ...past, status: "Done" }, false, now)).toEqual({});
        expect(validatePlacementDraft({ ...past, status: "Cancelled" }, false, now)).toEqual({});
        expect(validatePlacementDraft({ ...draft, status: "Lạ" }, false, now).status).toBeDefined();
    });
    it("ánh xạ lỗi 400 của máy chủ về đúng trường", () => {
        expect(placementServerErrors("Valid location ... Pending tests must be in the future.")).toHaveProperty("scheduledAt");
        expect(placementServerErrors("Student does not exist or is not synchronized yet.")).toHaveProperty("studentId");
        expect(placementServerErrors("Teacher does not exist or is not synchronized yet.")).toHaveProperty("teacherId");
        expect(placementServerErrors("Lỗi khác")).toEqual({});
    });
    it("tạo thân yêu cầu có offset +07:00, bỏ teacherId rỗng", () => {
        expect(placementBody(draft, true)).toEqual({
            studentId: id, location: "Phòng 101", mode: "Offline", scheduledAt: "2026-11-20T09:30:00+07:00", testName: "IELTS", teacherId: null,
        });
        expect(placementBody({ ...draft, status: "Done", teacherId: ` ${id} ` }, false)).toEqual({
            location: "Phòng 101", mode: "Offline", scheduledAt: "2026-11-20T09:30:00+07:00", testName: "IELTS", teacherId: id, status: "Done",
        });
    });
    it("đưa bản ghi máy chủ về form theo giờ VN", () => {
        const test: PlacementTest = {
            id, studentId: id, location: "A", mode: "Online", scheduledAtUtc: "2026-11-20T02:30:00Z", testName: "B", status: "Pending",
            teacherId: null, createdBy: id, createdAtUtc: "2026-11-17T00:00:00Z",
        };
        expect(placementToDraft(test)).toMatchObject({ scheduledAt: "2026-11-20T09:30", teacherId: "", mode: "Online" });
    });
    it("gọi đúng API cảnh báo, hẹn test", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 10 })));
        vi.stubGlobal("fetch", fetcher);
        await listWarnings({ studentId: ` ${id} `, page: 3 });
        await listPlacementTests();
        await createPlacementTest(draft);
        await updatePlacementTest(id, { ...draft, status: "Done" });
        expect(fetcher.mock.calls[0][0]).toBe(`/api/enrollment/warnings?page=3&pageSize=10&studentId=${id}`);
        expect(fetcher.mock.calls[1][0]).toBe("/api/enrollment/placement-tests?page=1&pageSize=10");
        expect(fetcher.mock.calls[2][1].method).toBe("POST");
        expect(fetcher.mock.calls[3][0]).toBe(`/api/enrollment/placement-tests/${id}`);
        expect(fetcher.mock.calls[3][1].method).toBe("PUT");
    });
});
