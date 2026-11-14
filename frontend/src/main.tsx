import React, {
    useEffect,
    useRef,
    useState,
    useSyncExternalStore,
} from "react";
import { createRoot } from "react-dom/client";
import {
    BrowserRouter,
    Navigate,
    NavLink,
    Route,
    Routes,
    useLocation,
} from "react-router-dom";
import {
    ArrowRight,
    BookOpen,
    Bell,
    CalendarDays,
    ChartNoAxesCombined,
    Check,
    ChevronRight,
    GraduationCap,
    LayoutDashboard,
    LockKeyhole,
    LogOut,
    Menu,
    MessageSquare,
    ShieldCheck,
    UserRound,
    X,
} from "lucide-react";
import {
    api,
    getSession,
    getSessionGeneration,
    identity,
    login,
    logout,
    setSession,
    subscribe,
} from "./api";
import { canManageAcademic } from "./academic";
import {
    Catalog,
    ClassCreate,
    ClassDetails,
    Classes,
    ManagerOnly,
} from "./academic-pages";
import { ErrorMessage, PageHeading, Unavailable } from "./ui";
import { Assignments, AssignmentDetails, Enrollments } from "./assignment-pages";
import { AccountCreate, ProfilePanel } from "./identity-pages";
import "./styles.css";

const labels = {
    Admin: "Quản trị viên",
    Staff: "Giáo vụ",
    Teacher: "Giáo viên",
    Student: "Học viên",
};
function Login({ notice }: { notice: string }) {
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <main className="login">
            <section className="login-story">
                <div className="brand">
                    <GraduationCap size={30} /> English Center
                </div>
                <div>
                    <span className="eyebrow">KHÔNG GIAN HỌC TẬP CỦA BẠN</span>
                    <h1>
                        Mỗi ngày,
                        <br />
                        một bước tiến.
                    </h1>
                    <p>
                        Kết nối lớp học, giáo viên và hành trình học tiếng Anh
                        trong một không gian chung.
                    </p>
                    <div className="story-line">
                        <span>01</span>
                        <div>
                            <strong>Học tập có định hướng</strong>
                            <p>Sẵn sàng cho chặng đường tiếp theo.</p>
                        </div>
                    </div>
                </div>
                <small>English Center · Hệ thống quản lý trung tâm</small>
            </section>
            <section className="login-form">
                <div className="form-wrap">
                    <span className="eyebrow">CHÀO MỪNG TRỞ LẠI</span>
                    <h2 id="login-heading" tabIndex={-1}>
                        Đăng nhập
                    </h2>
                    <p className="muted">
                        Sử dụng tài khoản do trung tâm cung cấp.
                    </p>
                    {notice && (
                        <p className="notice" role="status">
                            {notice}
                        </p>
                    )}
                    <form
                        onSubmit={async (e) => {
                            e.preventDefault();
                            if (busy) return;
                            setBusy(true);
                            setError(null);
                            try {
                                await login(email.trim(), password);
                            } catch (err) {
                                setError(err);
                            } finally {
                                setBusy(false);
                            }
                        }}
                    >
                        <label>
                            Email
                            <input
                                type="email"
                                autoComplete="username"
                                value={email}
                                onChange={(e) => setEmail(e.target.value)}
                                required
                                maxLength={254}
                                placeholder="ban@example.com"
                            />
                        </label>
                        <label>
                            Mật khẩu
                            <input
                                type="password"
                                autoComplete="current-password"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                                required
                                maxLength={128}
                            />
                        </label>
                        <ErrorMessage error={error} />
                        <button className="primary" disabled={busy}>
                            {busy
                                ? "Đang đăng nhập…"
                                : "Vào không gian học tập"}
                            <ArrowRight size={18} />
                        </button>
                    </form>
                    <p className="help">
                        <LockKeyhole size={16} />
                        Quên mật khẩu? Liên hệ giáo vụ để được hỗ trợ.
                    </p>
                    <p className="muted small">
                        Phiên chỉ được giữ trong bộ nhớ. Tải lại trang sẽ yêu
                        cầu đăng nhập lại.
                    </p>
                </div>
            </section>
        </main>
    );
}
function Overview() {
    const user = identity(getSession())!;
    return (
        <>
            <div className="welcome">
                <div>
                    <span className="eyebrow">SẴN SÀNG CHO HÔM NAY</span>
                    <h1>Chào mừng bạn trở lại.</h1>
                    <p>
                        Một nơi để kết nối với trung tâm và quản lý tài khoản
                        của bạn.
                    </p>
                    <NavLink className="primary inline" to="/account">
                        Xem tài khoản <ArrowRight size={18} />
                    </NavLink>
                </div>
                <div className="welcome-art" aria-hidden="true">
                    <BookOpen size={88} strokeWidth={1} />
                    <span>LEARN. CONNECT. GROW.</span>
                </div>
            </div>
            <div className="section-heading">
                <h2>Không gian của bạn</h2>
                <span className="pill">{labels[user.role]}</span>
            </div>
            <div className="tiles">
                <NavLink className="tile" to="/account">
                    <UserRound />
                    <h3>Tài khoản & bảo mật</h3>
                    <p>Thông tin phiên đăng nhập và thay đổi mật khẩu.</p>
                    <span>
                        Mở tài khoản <ChevronRight size={16} />
                    </span>
                </NavLink>
                <NavLink className="tile" to="/classes">
                    <BookOpen />
                    <h3>Lớp học</h3>
                    <p>
                        {["Admin", "Staff"].includes(user.role)
                            ? "Tra cứu chi tiết lớp bằng mã định danh từ hệ thống."
                            : "Không gian lớp học sẽ có khi API học tập được tích hợp."}
                    </p>
                    <span>
                        Xem lớp học <ChevronRight size={16} />
                    </span>
                </NavLink>
                <NavLink className="tile" to="/notifications">
                    <Bell />
                    <h3>Thông báo</h3>
                    <p>Theo dõi trạng thái tích hợp thông báo từ trung tâm.</p>
                    <span>
                        Mở thông báo <ChevronRight size={16} />
                    </span>
                </NavLink>
            </div>
            <div className="availability">
                <ShieldCheck />
                <div>
                    <h3>Hiển thị đúng dữ liệu, đúng thời điểm</h3>
                    <p>
                        Đăng nhập và bảo mật đã kết nối API. Lịch, tiến độ,
                        thống kê và thông báo sẽ mở khi API nghiệp vụ sẵn sàng;
                        trang này không sử dụng số liệu minh họa.
                    </p>
                </div>
            </div>
        </>
    );
}
function Account({ onNotice }: { onNotice: (message: string) => void }) {
    const operation = useRef<AbortController | null>(null);
    useEffect(() => () => operation.current?.abort(), []);
    const [current, setCurrent] = useState("");
    const [next, setNext] = useState("");
    const [confirm, setConfirm] = useState("");
    const [error, setError] = useState<unknown>();
    const [busy, setBusy] = useState(false);
    return (
        <>
            <PageHeading
                title="Tài khoản & bảo mật"
                subtitle="Bảo vệ không gian học tập của bạn."
            />
            <div className="account-grid">
                <ProfilePanel />
                <section className="panel">
                    <h2>Đổi mật khẩu</h2>
                    <p className="muted">
                        Sau khi đổi thành công, bạn cần đăng nhập lại.
                    </p>
                    <form
                        onSubmit={async (e) => {
                            e.preventDefault();
                            if (busy) return;
                            setError(null);
                            if (next !== confirm) {
                                setError(
                                    new Error("Mật khẩu xác nhận chưa khớp."),
                                );
                                return;
                            }
                            setBusy(true);
                            const controller = new AbortController();
                            operation.current = controller;
                            const generation = getSessionGeneration();
                            try {
                                await api("/identity/auth/change-password", {
                                    method: "POST",
                                    signal: controller.signal,
                                    body: JSON.stringify({
                                        currentPassword: current,
                                        newPassword: next,
                                    }),
                                });
                                if (controller.signal.aborted || generation !== getSessionGeneration()) return;
                                onNotice(
                                    "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.",
                                );
                                setSession(null);
                            } catch (err) {
                                if (!controller.signal.aborted && generation === getSessionGeneration()) setError(err);
                            } finally {
                                if (!controller.signal.aborted && generation === getSessionGeneration()) setBusy(false);
                            }
                        }}
                    >
                        <label>
                            Mật khẩu hiện tại
                            <input
                                type="password"
                                autoComplete="current-password"
                                required
                                maxLength={128}
                                value={current}
                                onChange={(e) => setCurrent(e.target.value)}
                            />
                        </label>
                        <label>
                            Mật khẩu mới
                            <input
                                type="password"
                                autoComplete="new-password"
                                required
                                minLength={8}
                                maxLength={128}
                                value={next}
                                onChange={(e) => setNext(e.target.value)}
                            />
                            <small>Từ 8 đến 128 ký tự.</small>
                        </label>
                        <label>
                            Xác nhận mật khẩu mới
                            <input
                                type="password"
                                autoComplete="new-password"
                                required
                                minLength={8}
                                maxLength={128}
                                value={confirm}
                                onChange={(e) => setConfirm(e.target.value)}
                            />
                        </label>
                        <ErrorMessage error={error} />
                        <button className="primary" disabled={busy}>
                            {busy ? "Đang cập nhật…" : "Cập nhật mật khẩu"}
                            <Check size={18} />
                        </button>
                    </form>
                </section>
            </div>
        </>
    );
}
const commonLinks = [
    { to: "/", label: "Tổng quan", icon: LayoutDashboard },
    { to: "/classes", label: "Lớp học", icon: BookOpen },
    { to: "/schedule", label: "Lịch học", icon: CalendarDays },
    { to: "/progress", label: "Tiến độ học tập", icon: ChartNoAxesCombined },
    { to: "/notifications", label: "Thông báo", icon: Bell },
    { to: "/feedback", label: "Phản hồi", icon: MessageSquare },
    { to: "/statistics", label: "Thống kê", icon: ChartNoAxesCombined },
    { to: "/account", label: "Tài khoản", icon: UserRound },
];
function navigation(role: import("./api").Role) {
    const manager = canManageAcademic(role);
    const operational = [
        ...(manager
            ? [
                  { to: "/classes/new", label: "Tạo lớp học", icon: BookOpen },
                  { to: "/courses", label: "Khóa học", icon: BookOpen },
                  { to: "/teachers", label: "Giáo viên", icon: UserRound },
                  { to: "/users/new", label: "Tạo tài khoản", icon: ShieldCheck },
              ]
            : []),
        ...(manager || role === "Student"
            ? [
                  {
                      to: "/enrollment",
                      label:
                          role === "Student"
                              ? "Ghi danh của tôi"
                              : "Ghi danh / chuyển lớp",
                      icon: BookOpen,
                  },
                  {
                      to: "/invoices",
                      label:
                          role === "Student"
                              ? "Học phí của tôi"
                              : "Hóa đơn & học phí",
                      icon: BookOpen,
                  },
              ]
            : []),
        ...(manager || role === "Teacher"
            ? [{ to: "/attendance", label: "Điểm danh", icon: Check }]
            : []),
        { to: "/assignments", label: "Bài tập", icon: BookOpen },
    ];
    return [
        ...commonLinks.slice(0, 2),
        ...operational,
        ...commonLinks.slice(2),
    ].map((link) => ({
        ...link,
        unavailable:
            ![
                "/",
                "/account",
                "/classes",
                "/classes/new",
                "/courses",
                "/teachers",
                "/assignments",
                "/users/new",
                ...(manager ? ["/enrollment"] : []),
            ].includes(link.to) ||
            (link.to === "/classes" && !manager),
    }));
}
function App() {
    const session = useSyncExternalStore(subscribe, getSession);
    const generation = useSyncExternalStore(subscribe, getSessionGeneration);
    const user = identity(session);
    const [notice, setNotice] = useState("");
    const [open, setOpen] = useState(false);
    const [mobile, setMobile] = useState(
        () => window.matchMedia("(max-width: 760px)").matches,
    );
    const [focusRequest, setFocusRequest] = useState<"main" | "trigger" | null>(
        null,
    );
    const sidebarRef = useRef<HTMLElement>(null);
    const triggerRef = useRef<HTMLButtonElement>(null);
    const mainRef = useRef<HTMLElement>(null);
    const [leaving, setLeaving] = useState(false);
    const location = useLocation();
    const authenticated = user !== null;
    const drawerOpen = mobile && open && authenticated;
    const dismissDrawer = () => {
        setOpen(false);
        setFocusRequest("trigger");
    };
    useEffect(() => {
        const breakpoint = window.matchMedia("(max-width: 760px)");
        const update = () => {
            setMobile(breakpoint.matches);
            if (!breakpoint.matches) {
                setOpen(false);
                if (sidebarRef.current?.contains(document.activeElement))
                    setFocusRequest("main");
            }
        };
        breakpoint.addEventListener("change", update);
        return () => breakpoint.removeEventListener("change", update);
    }, []);
    useEffect(() => {
        // Login/logout must move focus even when the requested URL is unchanged.
        setOpen(false);
        setFocusRequest("main");
    }, [location.pathname, generation, authenticated]);
    useEffect(() => {
        if (!drawerOpen) return;
        const sidebar = sidebarRef.current;
        if (!sidebar) return;
        const previousOverflow =
            document.body.style.getPropertyValue("overflow");
        const previousPriority =
            document.body.style.getPropertyPriority("overflow");
        document.body.style.setProperty("overflow", "hidden");
        const controls = () =>
            Array.from(
                sidebar.querySelectorAll<HTMLElement>(
                    "a[href], button, input, select, textarea, [tabindex]",
                ),
            ).filter(
                (element) =>
                    element.tabIndex >= 0 &&
                    !element.matches(":disabled") &&
                    element.getAttribute("aria-disabled") !== "true" &&
                    element.getClientRects().length > 0 &&
                    getComputedStyle(element).visibility !== "hidden" &&
                    !element.closest("[inert]"),
            );
        const focusFirst = () => (controls()[0] ?? sidebar).focus();
        focusFirst();
        const handleKey = (event: KeyboardEvent) => {
            if (event.key === "Escape") {
                event.preventDefault();
                setOpen(false);
                setFocusRequest("trigger");
            }
            if (event.key === "Tab") {
                const elements = controls();
                const first = elements[0];
                const last = elements[elements.length - 1];
                if (!first || !sidebar.contains(document.activeElement)) {
                    event.preventDefault();
                    (event.shiftKey
                        ? (last ?? sidebar)
                        : (first ?? sidebar)
                    ).focus();
                } else if (
                    event.shiftKey &&
                    (document.activeElement === first ||
                        document.activeElement === sidebar)
                ) {
                    event.preventDefault();
                    last?.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault();
                    first?.focus();
                }
            }
        };
        const containFocus = (event: FocusEvent) => {
            if (event.target instanceof Node && !sidebar.contains(event.target))
                focusFirst();
        };
        document.addEventListener("keydown", handleKey);
        document.addEventListener("focusin", containFocus);
        return () => {
            document.removeEventListener("keydown", handleKey);
            document.removeEventListener("focusin", containFocus);
            if (previousOverflow)
                document.body.style.setProperty(
                    "overflow",
                    previousOverflow,
                    previousPriority,
                );
            else document.body.style.removeProperty("overflow");
        };
    }, [drawerOpen]);
    useEffect(() => {
        // Wait until the modal closes and React removes background inertness.
        if (drawerOpen || !focusRequest) return;
        const target = !authenticated
            ? document.getElementById("login-heading")
            : focusRequest === "trigger" && mobile
              ? triggerRef.current
              : mainRef.current;
        target?.focus();
        setFocusRequest(null);
    }, [focusRequest, drawerOpen, authenticated, mobile]);
    if (!user) return <Login notice={notice} />;
    const links = navigation(user.role);
    const title =
        location.pathname === "/classes/new"
            ? "Tạo lớp học"
            : location.pathname.startsWith("/classes/")
              ? "Chi tiết lớp học"
              : (links.find((link) => link.to === location.pathname)?.label ??
                "Trang");
    return (
        <div className="app">
            <a className="skip" href="#main-content" inert={drawerOpen}>
                Đến nội dung chính
            </a>
            <aside
                ref={sidebarRef}
                id="workspace-navigation"
                className={`sidebar ${drawerOpen ? "is-open" : ""}`}
                role={drawerOpen ? "dialog" : undefined}
                aria-modal={drawerOpen ? true : undefined}
                aria-label={drawerOpen ? "Menu điều hướng" : undefined}
                tabIndex={-1}
                inert={mobile && !drawerOpen}
            >
                <div className="brand">
                    <GraduationCap size={29} />
                    <span>
                        English Center<small>LEARNING WORKSPACE</small>
                    </span>
                </div>
                <button
                    className="mobile close"
                    aria-label="Đóng menu"
                    onClick={dismissDrawer}
                >
                    <X />
                </button>
                <p className="nav-caption">KHÔNG GIAN LÀM VIỆC</p>
                <nav
                    aria-label="Điều hướng chính"
                    onClick={(event) => {
                        if (
                            event.target instanceof Element &&
                            event.target.closest("a[href]") &&
                            event.button === 0 &&
                            !event.metaKey &&
                            !event.ctrlKey &&
                            !event.shiftKey &&
                            !event.altKey
                        ) {
                            setOpen(false);
                            setFocusRequest("main");
                        }
                    }}
                >
                    {links.map((link) => (
                        <NavLink
                            end={link.to === "/"}
                            className={
                                link.to === "/classes/new"
                                    ? ({ isActive }) =>
                                          isActive
                                              ? "active nav-child"
                                              : "nav-child"
                                    : undefined
                            }
                            key={link.to}
                            to={link.to}
                        >
                            <link.icon size={19} />
                            {link.label}
                            {link.unavailable && (
                                <small className="nav-status">
                                    Chưa có API
                                </small>
                            )}
                        </NavLink>
                    ))}
                </nav>
                <div className="sidebar-bottom">
                    <span className="avatar">
                        <UserRound size={20} />
                    </span>
                    <div>
                        <strong>{labels[user.role]}</strong>
                        <small>Phiên đang hoạt động</small>
                    </div>
                    <button
                        disabled={leaving}
                        title="Đăng xuất"
                        aria-label="Đăng xuất"
                        onClick={async () => {
                            setLeaving(true);
                            setNotice("");
                            try {
                                await logout();
                            } catch {
                                setNotice(
                                    "Đã thoát trên trình duyệt; chưa xác nhận thu hồi phiên trên máy chủ.",
                                );
                            } finally {
                                setLeaving(false);
                            }
                        }}
                    >
                        <LogOut size={19} />
                    </button>
                </div>
            </aside>
            {drawerOpen && (
                <button
                    className="scrim"
                    aria-label="Đóng menu"
                    tabIndex={-1}
                    onClick={dismissDrawer}
                />
            )}
            <div className="workspace" inert={drawerOpen}>
                <header>
                    <div className="header-left">
                        <button
                            ref={triggerRef}
                            className="mobile"
                            aria-label="Mở menu"
                            aria-controls="workspace-navigation"
                            aria-expanded={drawerOpen}
                            onClick={() => setOpen(true)}
                        >
                            <Menu />
                        </button>
                        <span>
                            Không gian học tập <ChevronRight size={14} />{" "}
                            <strong>{title}</strong>
                        </span>
                    </div>
                    <NavLink
                        className="icon-link"
                        to="/notifications"
                        aria-label="Thông báo"
                    >
                        <Bell size={20} />
                    </NavLink>
                </header>
                <main ref={mainRef} id="main-content" tabIndex={-1}>
                    <Routes key={`${generation}:${location.pathname}`}>
                        <Route path="/" element={<Overview />} />
                        <Route
                            path="/account"
                            element={<Account onNotice={setNotice} />}
                        />
                        <Route path="/users/new" element={<AccountCreate />} />
                        <Route path="/classes" element={<Classes />} />
                        <Route
                            path="/classes/new"
                            element={
                                <ManagerOnly>
                                    <ClassCreate />
                                </ManagerOnly>
                            }
                        />
                        <Route
                            path="/classes/:id"
                            element={
                                <ManagerOnly>
                                    <ClassDetails />
                                </ManagerOnly>
                            }
                        />
                        <Route
                            path="/courses"
                            element={
                                <ManagerOnly>
                                    <Catalog kind="courses" />
                                </ManagerOnly>
                            }
                        />
                        <Route
                            path="/teachers"
                            element={
                                <ManagerOnly>
                                    <Catalog kind="teachers" />
                                </ManagerOnly>
                            }
                        />
                        <Route path="/assignments" element={<Assignments />} />
                        <Route path="/assignments/:id" element={<AssignmentDetails />} />
                        <Route path="/enrollment" element={<Enrollments />} />
                        {[
                            {
                                path: "/invoices",
                                title: "Hóa đơn & học phí",
                                text: "Finance chưa có API truy vấn hóa đơn, học phí hoặc thanh toán. Trang này không tạo hóa đơn, thu tiền hay xác nhận đã thanh toán; chưa có số dư hoặc tổng tiền để hiển thị.",
                            },
                            {
                                path: "/attendance",
                                title: "Điểm danh",
                                text: "Academic chưa có API danh sách buổi học và ghi nhận điểm danh. Trang này chưa tải hoặc lưu trạng thái có mặt, vắng mặt; không có bản ghi minh họa.",
                            },
                            {
                                path: "/schedule",
                                title: "Lịch học",
                                text: "Lịch học và lịch dạy sẽ hiển thị khi API lịch của Academic được hoàn thiện.",
                            },
                            {
                                path: "/progress",
                                title: "Tiến độ học tập",
                                text: "Điểm danh, bài tập và điểm cần dữ liệu thực tế từ Academic. Chưa có số liệu để tổng hợp.",
                            },
                            {
                                path: "/notifications",
                                title: "Thông báo",
                                text: "API danh sách và đánh dấu thông báo đã đọc chưa được triển khai. Đây không phải hộp thư trống.",
                            },
                            {
                                path: "/feedback",
                                title: "Phản hồi",
                                text: "API gửi và xử lý phản hồi chưa sẵn sàng. Vui lòng liên hệ trực tiếp giáo vụ; trang này chưa gửi hay lưu phản hồi.",
                            },
                            {
                                path: "/statistics",
                                title: "Thống kê",
                                text: "Chưa có API thống kê nghiệp vụ. Biểu đồ và số liệu sẽ hiển thị khi có dữ liệu được xác minh.",
                            },
                        ].map((page) => (
                            <Route
                                key={page.path}
                                path={page.path}
                                element={
                                    <Unavailable
                                        title={page.title}
                                        text={page.text}
                                    />
                                }
                            />
                        ))}
                        <Route
                            path="/login"
                            element={<Navigate to="/" replace />}
                        />
                        <Route
                            path="*"
                            element={
                                <Unavailable
                                    title="Không tìm thấy trang"
                                    text="Đường dẫn không tồn tại. Hãy quay lại trang tổng quan."
                                />
                            }
                        />
                    </Routes>
                </main>
                <footer>
                    English Center{" "}
                    <span>Học tập rõ ràng. Kết nối dễ dàng.</span>
                </footer>
            </div>
        </div>
    );
}
createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
        <BrowserRouter>
            <App />
        </BrowserRouter>
    </React.StrictMode>,
);
