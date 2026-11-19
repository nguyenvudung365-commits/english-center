import { afterEach, expect, it, vi } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { setSession } from "./api";
import { Enrollments } from "./assignment-pages";

const readState = vi.hoisted(() => ({
    data: {
        items: [{
            id: "11111111-1111-1111-1111-111111111111",
            classId: "22222222-2222-2222-2222-222222222222",
            classCode: "IELTS-01",
            startDate: "2026-11-01",
            endDate: "2026-12-31",
            status: "Active",
            enrolledAtUtc: "2026-11-01T02:30:00Z",
            tuitionFee: 4500000,
            isActive: false,
        }, {
            id: "00000000-0000-0000-0000-000000000001",
            classId: "00000000-0000-0000-0000-000000000002",
            classCode: "IELTS-02",
            startDate: "2026-11-01",
            endDate: "2026-12-31",
            status: "Active",
            enrolledAtUtc: "2026-11-01T02:30:00Z",
            tuitionFee: 4500000,
            isActive: true,
        }],
        total: 2,
        page: 1,
        pageSize: 20,
    },
    loading: false,
    error: null,
    retry: vi.fn(),
}));

vi.mock("./academic-pages", async (importOriginal) => ({
    ...await importOriginal<typeof import("./academic-pages")>(),
    useRead: () => readState,
}));

afterEach(() => setSession(null));

it("opens the student's enrollment route with history and coursework access status", () => {
    setSession({
        accessToken: `header.${btoa(JSON.stringify({ sub: "student-id", role: "Student" }))}.signature`,
        accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(),
        refreshToken: "refresh", refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    });
    const html = renderToStaticMarkup(<MemoryRouter initialEntries={["/enrollment"]}><Enrollments /></MemoryRouter>);
    expect(html).toContain("Ghi danh của tôi");
    expect(html).toContain("IELTS-01");
    expect(html).toContain("Đang ghi danh");
    expect(html).toContain("Đã ngừng");
    const [inactive, active] = readState.data.items;
    expect(html).not.toContain(`href="/classes/${inactive.classId}"`);
    expect(html).toContain(`href="/classes/${active.classId}"`);
    expect(html).toContain(`>${active.classCode}</a>`);
    expect(html).toContain('href="/invoices"');
    expect(html).toContain("Phân trang ghi danh");
    expect(html).not.toContain("Cấp quyền học tập");
});
