import { afterEach, expect, it, vi } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { setSession, type Role } from "./api";
import { StaffAdministration, StudentAdministration } from "./student-administration";

vi.mock("./academic-pages", async (importOriginal) => ({
    ...await importOriginal<typeof import("./academic-pages")>(),
    useRead: () => ({ data: { items: [{ id: "sample-id", fullName: "Hà Nguyễn", email: "learner@example.test", studentCode: "HV000001-26" }], total: 40, page: 1, pageSize: 20 }, retry: vi.fn() }),
}));

function render(role: Role, staffPage = false) {
    setSession({
        accessToken: `header.${btoa(JSON.stringify({ sub: "operator", role }))}.signature`,
        accessTokenExpiresAt: new Date(Date.now() + 600000).toISOString(), refreshToken: "refresh",
        refreshTokenExpiresAt: new Date(Date.now() + 86400000).toISOString(),
    });
    return renderToStaticMarkup(<MemoryRouter>{staffPage ? <StaffAdministration /> : <StudentAdministration />}</MemoryRouter>);
}

afterEach(() => setSession(null));

it("shows staff the searchable, paginated learner directory without an account restriction control", () => {
    const html = render("Staff");
    expect(html).toContain("Hà Nguyễn");
    expect(html).toContain("HV000001-26");
    expect(html).toContain("Phân trang học viên");
    expect(html).toContain('type="search"');
    expect(html).not.toContain("Khóa tài khoản");
});

it("limits the staff directory and fixed-role provisioning entry point to admins", () => {
    expect(render("Staff", true)).not.toContain("Tạo tài khoản giáo vụ");
    expect(render("Admin", true)).toContain("Tạo tài khoản giáo vụ");
    expect(render("Teacher")).not.toContain("HV000001-26");
    expect(render("Student")).not.toContain("HV000001-26");
});
