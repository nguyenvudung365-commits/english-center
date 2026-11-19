import { describe, expect, it } from "vitest";
import { formatDateOnly, formatVnDateTime, formatVnd, pageCount, utcToVnLocal, vnLocalToIso } from "./format";

const plain = (text: string) => text.replace(/\s/g, " ");

describe("Định dạng tiền và thời gian Việt Nam", () => {
    it("định dạng VND là số nguyên theo vi-VN", () => {
        expect(plain(formatVnd(1500000))).toBe("1.500.000 ₫");
        expect(plain(formatVnd(0))).toBe("0 ₫");
    });
    it("hiển thị thời điểm UTC theo UTC+7, qua nửa đêm sang ngày kế tiếp", () => {
        const text = formatVnDateTime("2026-11-17T17:30:00Z");
        expect(text).toContain("00:30");
        expect(text).toContain("18");
        expect(formatVnDateTime(null)).toBe("—");
        expect(formatVnDateTime("không hợp lệ")).toBe("—");
    });
    it("định dạng ngày DateOnly không lệch múi giờ", () => {
        expect(formatDateOnly("2026-12-01")).toBe("01/12/2026");
        expect(formatDateOnly("2026-12-01T00:00:00Z")).toBe("—");
        expect(formatDateOnly(undefined)).toBe("—");
    });
    it("chuyển datetime-local giờ VN sang ISO có offset và ngược lại", () => {
        expect(vnLocalToIso("2026-11-20T09:30")).toBe("2026-11-20T09:30:00+07:00");
        expect(Date.parse(vnLocalToIso("2026-11-20T09:30")!)).toBe(Date.parse("2026-11-20T02:30:00Z"));
        expect(utcToVnLocal("2026-11-20T02:30:00Z")).toBe("2026-11-20T09:30");
        expect(utcToVnLocal("2026-11-20T20:00:00Z")).toBe("2026-11-21T03:00");
        expect(utcToVnLocal(vnLocalToIso("2026-02-28T23:59")!)).toBe("2026-02-28T23:59");
    });
    it("từ chối giá trị datetime-local sai hoặc ngày không tồn tại", () => {
        for (const value of ["", "2026-11-20", "2026-11-20T09:30:00", "2026-02-30T09:30", "2026-13-01T09:30", "2026-11-20T25:00"])
            expect(vnLocalToIso(value), value).toBeNull();
    });
    it("tính số trang tối thiểu là 1", () => {
        expect(pageCount(0, 10)).toBe(1);
        expect(pageCount(21, 10)).toBe(3);
        expect(pageCount(5, 0)).toBe(5);
    });
});
