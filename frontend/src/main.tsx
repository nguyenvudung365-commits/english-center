import React, {
    useEffect,
    useRef,
    useState,
    useSyncExternalStore,
} from "react";
import { createRoot } from "react-dom/client";
import { zaloSupportUrl } from "./support-contact";
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
    CircleHelp,
    ClipboardList,
    GraduationCap,
    LayoutDashboard,
    LockKeyhole,
    LogOut,
    Menu,
    MessageSquare,
    NotebookPen,
    Receipt,
    ShieldCheck,
    TriangleAlert,
    UserRound,
    X,
} from "lucide-react";
import {
    api,
    getAuthState,
    getSession,
    getSessionGeneration,
    identity,
    login,
    logout,
    restoreSession,
    subscribe,
} from "./api";
import { canManageAcademic } from "./academic";
import {
    CourseManagement,
    TeacherCatalog,
    ClassCreate,
    ClassDetails,
    Classes,
    ManagerOnly,
} from "./academic-pages";
import { ErrorMessage, PageHeading, Unavailable } from "./ui";
import { Assignments, AssignmentDetails, Enrollments } from "./assignment-pages";
import { PracticeDetails, PracticePage } from "./practice-pages";
import { AccountCreate, ProfilePanel, TeacherAdministration } from "./identity-pages";
import { Schedule, SessionDetails } from "./schedule-pages";
import { AttendancePage } from "./attendance-pages";
import { ProgressPage, StatisticsPage } from "./statistics-pages";
import { Notifications } from "./notification-pages";
import { FaqManagePage, FaqPage, FeedbackPage } from "./feedback-pages";
import { Invoices } from "./finance-pages";
import { PlacementTests, Warnings } from "./monitoring-pages";
import { ClassFeed } from "./feed-pages";
import { bellLabel, getUnread, getUnreadCount, setUnread, subscribeUnread, unreadBadge } from "./notifications";
import "./styles.css";

const labels = {
    Admin: "Quản trị viên",
    Staff: "Giáo vụ",
    Teacher: "Giáo viên",
    Student: "Học viên",
};
const supportUrl = zaloSupportUrl(import.meta.env.VITE_SUPPORT_ZALO_URL);
const supportOpensZaloApp = supportUrl?.startsWith("https://zaloapp.com/");

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
                    <div className="story-line">
                        <span>02</span>
                        <div>
                            <strong>Lớp học, lịch và điểm danh</strong>
                            <p>Theo dõi lớp, lịch học và điểm danh của bạn.</p>
                        </div>
                    </div>
                    <div className="story-line">
                        <span>03</span>
                        <div>
                            <strong>Bài tập, điểm và thông báo</strong>
                            <p>Nhận bài tập, điểm và thông báo ở một nơi.</p>
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
                        <LockKeyhole size={16} aria-hidden="true" />
                        <span>Quên mật khẩu? {supportUrl ? (
                            <>
                                <a href={supportUrl} target="_blank" rel="noopener noreferrer">
                                    {supportOpensZaloApp ? "Liên hệ giáo vụ (mở ứng dụng Zalo, tab mới)" : "Liên hệ giáo vụ qua Zalo (mở tab mới)"}
                                </a>
                                {supportOpensZaloApp && " Nên mở trên điện thoại có Zalo; trên máy tính có thể chỉ hiện trang chủ Zalo."}
                            </>
                        ) : "Liên hệ giáo vụ để được hỗ trợ."}</span>
                    </p>
                    <p className="muted small">
                        Phiên được giữ khi tải lại trang và kết thúc khi bạn
                        đăng xuất hoặc đóng trình duyệt.
                    </p>
                </div>
            </section>
        </main>
    );
}
function Restoring({ state }: { state: "restoring" | "unavailable" | "logout-pending" }) {
    const pendingLogout = state === "logout-pending";
    return (
        <main className="login">
            <section className="login-form">
                <div className="form-wrap">
                    <h2 role="status">{pendingLogout ? "Chưa xác nhận đăng xuất" : state === "unavailable" ? "Chưa thể khôi phục phiên" : "Đang khôi phục phiên đăng nhập…"}</h2>
                    {state !== "restoring" && (
                        <>
                            <p className="muted">
                                {pendingLogout ? "Phiên sẽ không được mở lại. Thử lại để hoàn tất đăng xuất trên máy chủ." : "Không kết nối được máy chủ. Phiên của bạn chưa bị đăng xuất."}
                            </p>
                            <button className="primary" onClick={() => void restoreSession()}>
                                Thử lại
                            </button>
                        </>
                    )}
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
                        Tra cứu lớp, thông tin giáo viên và lịch học trong phạm vi tài khoản.
                    </p>
                    <span>
                        Xem lớp học <ChevronRight size={16} />
                    </span>
                </NavLink>
                <NavLink className="tile" to="/notifications">
                    <Bell />
                    <h3>Thông báo</h3>
                    <p>Xem thông báo về ghi danh, bài tập, điểm, hóa đơn và cảnh báo.</p>
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
                        Lớp học, lịch học, điểm danh và tiến độ sử dụng dữ liệu
                        nghiệp vụ. Nguồn thống kê hoặc thông báo chưa khả dụng
                        được ghi rõ, không thay bằng số liệu minh họa.
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
                                // The server revoked every session; logout also clears the refresh cookie.
                                await logout().catch(() => undefined);
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
    { to: "/warnings", label: "Cảnh báo", icon: TriangleAlert },
    { to: "/placement-tests", label: "Hẹn test đầu vào", icon: ClipboardList },
    { to: "/faq", label: "Câu hỏi thường gặp", icon: CircleHelp },
    { to: "/statistics", label: "Thống kê", icon: ChartNoAxesCombined },
    { to: "/account", label: "Tài khoản", icon: UserRound },
];
// Backend trả 403 cho Teacher ở các mục này nên ẩn khỏi menu.
const hiddenFromTeacher = ["/feedback", "/warnings", "/placement-tests"];
function navigation(role: import("./api").Role) {
    const manager = canManageAcademic(role);
    const operational = [
        ...(manager
            ? [
                  { to: "/courses", label: "Khóa học", icon: BookOpen },
                  { to: "/teachers", label: "Giáo viên", icon: UserRound },
                  { to: "/users/new", label: "Tạo tài khoản", icon: ShieldCheck },
              ]
            : []),
        ...(role === "Admin"
            ? [{ to: "/users/teachers", label: "Quản trị giáo viên", icon: ShieldCheck },
               { to: "/faq/manage", label: "Quản trị FAQ", icon: CircleHelp }]
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
                      icon: Receipt,
                  },
              ]
            : []),
        { to: "/attendance", label: role === "Student" ? "Điểm danh của tôi" : "Điểm danh", icon: Check },
        { to: "/assignments", label: "Bài tập", icon: BookOpen },
        { to: "/practice", label: "Luyện tập", icon: NotebookPen },
    ];
    return [
        ...commonLinks.slice(0, 2),
        ...operational,
        ...commonLinks.slice(2),
    ]
        .filter((link) => link.to !== "/statistics" || role === "Student")
        .filter((link) => role !== "Teacher" || !hiddenFromTeacher.includes(link.to));
}
function App() {
    const session = useSyncExternalStore(subscribe, getSession);
    const generation = useSyncExternalStore(subscribe, getSessionGeneration);
    const authState = useSyncExternalStore(subscribe, getAuthState);
    useEffect(() => {
        void restoreSession();
    }, []);
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
    const unread = useSyncExternalStore(subscribeUnread, getUnread);
    useEffect(() => setUnread(null), [generation]);
    useEffect(() => {
        // Chỉ đọc nhẹ khi vào app/đổi route; trang Thông báo tự cập nhật số này.
        if (!authenticated) {
            setUnread(null);
            return;
        }
        if (location.pathname === "/notifications") return;
        const controller = new AbortController();
        const version = getSessionGeneration();
        void getUnreadCount(controller.signal).then(
            (value) => {
                if (!controller.signal.aborted && version === getSessionGeneration()) setUnread(value.unreadCount);
            },
            () => undefined,
        );
        return () => controller.abort();
    }, [location.pathname, generation, authenticated]);
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
    if (!user && authState !== "ready") return <Restoring state={authState} />;
    if (!user) return <Login notice={notice} />;
    const links = navigation(user.role);
    const title =
        location.pathname === "/classes/new"
            ? "Tạo lớp học"
            : location.pathname.startsWith("/classes/")
              ? "Chi tiết lớp học"
              : location.pathname.startsWith("/schedule/")
                ? "Chi tiết buổi học"
                : location.pathname.startsWith("/assignments/")
                  ? "Chi tiết bài tập"
                  : location.pathname.startsWith("/practice/")
                    ? "Chi tiết luyện tập"
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
                            end={link.to === "/" || link.to === "/faq"}
                            key={link.to}
                            to={link.to}
                        >
                            <link.icon size={19} />
                            {link.label}
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
                        aria-label={bellLabel(unread)}
                    >
                        <Bell size={20} />
                        {unreadBadge(unread) && (
                            <span className="badge" aria-hidden="true">
                                {unreadBadge(unread)}
                            </span>
                        )}
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
                        <Route path="/users/teachers" element={user.role === "Admin" ? <TeacherAdministration /> : <Navigate to="/" replace />} />
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
                                <ClassDetails
                                    feed={(classId) => (
                                        <ClassFeed classId={classId} />
                                    )}
                                />
                            }
                        />
                        <Route path="/schedule" element={<Schedule />} />
                        <Route path="/schedule/:id" element={<SessionDetails />} />
                        <Route path="/attendance" element={<AttendancePage />} />
                        <Route path="/attendance/:sessionId" element={<AttendancePage />} />
                        <Route path="/progress" element={<ProgressPage />} />
                        <Route path="/progress/:classId" element={<ProgressPage />} />
                        <Route path="/statistics" element={<StatisticsPage />} />
                        <Route
                            path="/courses"
                            element={
                                <ManagerOnly>
                                    <CourseManagement />
                                </ManagerOnly>
                            }
                        />
                        <Route
                            path="/teachers"
                            element={
                                <ManagerOnly>
                                    <TeacherCatalog />
                                </ManagerOnly>
                            }
                        />
                        <Route path="/assignments" element={<Assignments />} />
                        <Route path="/assignments/:id" element={<AssignmentDetails />} />
                        <Route path="/practice" element={<PracticePage />} />
                        <Route path="/practice/:id" element={<PracticeDetails />} />
                        <Route path="/enrollment" element={<Enrollments />} />
                        <Route path="/invoices" element={<Invoices />} />
                        <Route path="/notifications" element={<Notifications />} />
                        <Route path="/feedback" element={<FeedbackPage />} />
                        <Route path="/faq" element={<FaqPage />} />
                        <Route path="/faq/manage" element={<FaqManagePage />} />
                        <Route path="/warnings" element={<Warnings />} />
                        <Route path="/placement-tests" element={<PlacementTests />} />
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
