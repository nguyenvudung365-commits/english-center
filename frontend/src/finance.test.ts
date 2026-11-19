import { afterEach, describe, expect, it, vi } from "vitest";
import { setSession, type Session } from "./api";
import { canPayInvoice, canReadInvoices, invoiceStatusLabel, listInvoices, payInvoice } from "./finance";

const id = "11111111-1111-1111-1111-111111111111";
const session: Session = {
    accessToken: "test-token", accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
    refreshToken: "r", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
};
afterEach(() => { setSession(null); vi.unstubAllGlobals(); });

describe("Hóa đơn", () => {
    it("nhãn trạng thái và quyền thanh toán", () => {
        expect(invoiceStatusLabel("Unpaid")).toBe("Chưa thanh toán");
        expect(invoiceStatusLabel("Paid")).toBe("Đã thanh toán");
        expect(canPayInvoice("Staff", { status: "Unpaid" })).toBe(true);
        expect(canPayInvoice("Staff", { status: "Paid" })).toBe(false);
        expect(canPayInvoice("Student", { status: "Unpaid" })).toBe(false);
        expect(canReadInvoices("Teacher")).toBe(false);
    });
    it("gọi đúng API hóa đơn", async () => {
        setSession(session);
        const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 10 })));
        vi.stubGlobal("fetch", fetcher);
        await listInvoices({ studentId: id });
        await payInvoice(id);
        expect(fetcher.mock.calls[0][0]).toBe(`/api/finance/invoices?page=1&pageSize=10&studentId=${id}`);
        expect(fetcher.mock.calls[1][0]).toBe(`/api/finance/invoices/${id}/pay`);
        expect(fetcher.mock.calls[1][1].method).toBe("POST");
    });
});
