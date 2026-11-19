import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { canManageAcademic, isAcademicId, listClasses, type ClassDetail } from "./academic";
import { useRead } from "./academic-pages";
import {
    calendarRange, cancelSession, centerDate, getScheduledSession, listSessions,
    navigateCalendar, saveSession, sessionLocalTime, sessionSkills, sessionsForDay, validateSessionDraft,
    type CalendarMode, type ScheduledSession, type SessionDraft,
} from "./schedule";
import { ErrorMessage, PageHeading } from "./ui";

const types: Record<string, string> = { Regular: "Buổi học", MockTest: "Thi thử", FinalTest: "Thi cuối khóa" };
const skills: Record<string, string> = { Listening: "Nghe", Reading: "Đọc", Writing: "Viết", Speaking: "Nói" };
const dayLabel = (day: string) => new Intl.DateTimeFormat("vi-VN", {
    weekday: "long", day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC",
}).format(new Date(`${day}T00:00:00Z`));
const timeLabel = (instant: string) => new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit", minute: "2-digit", timeZone: "Asia/Ho_Chi_Minh", hourCycle: "h23",
}).format(new Date(instant));
const fullTime = (instant: string) => new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh", hourCycle: "h23",
}).format(new Date(instant));

async function allClasses(): Promise<ClassDetail[]> {
    const generation = getSessionGeneration();
    const items = new Map<string, ClassDetail>();
    for (let page = 1; ; page++) {
        const result = await listClasses({ page, pageSize: 100 });
        if (generation !== getSessionGeneration()) throw new ApiError(401, "Phiên đã thay đổi.");
        if (result.page !== page || result.pageSize !== 100 || !Number.isSafeInteger(result.total) || result.total < 0)
            throw new ApiError(0, "Phản hồi phân trang lớp không hợp lệ.");
        for (const item of result.items) items.set(item.id, item);
        if (page * result.pageSize >= result.total) return [...items.values()];
        if (!result.items.length) throw new ApiError(0, "Danh sách lớp thay đổi trong khi tải. Vui lòng tải lại.");
    }
}

function SessionLink({ item }: { item: ScheduledSession }) {
    const status = item.isCancelled ? "Đã hủy" : types[item.sessionType];
    return <Link className="session-link" to={`/schedule/${item.id}`}
        aria-label={`${item.classCode}, ${item.lesson}, ${status}, ${fullTime(item.startsAtUtc)} đến ${fullTime(item.endsAtUtc)}, giờ Việt Nam`}>
        <time dateTime={item.startsAtUtc}>{timeLabel(item.startsAtUtc)}</time>
        <span className="session-calendar-name">{item.classCode} · {item.lesson}</span>
        <span className="session-calendar-type">{status}</span>
    </Link>;
}

export function Schedule() {
    const [parameters, setParameters] = useSearchParams();
    const classId = parameters.get("classId") ?? "";
    const manager = canManageAcademic(identity(getSession())!.role);
    const [mode, setMode] = useState<CalendarMode>("week");
    const [anchor, setAnchor] = useState(() => {
        const date = parameters.get("date");
        if (date) {
            try { calendarRange(date, "week"); return date; } catch { /* Invalid deep-link dates fall back to today. */ }
        }
        return centerDate(new Date().toISOString());
    });
    const [showCreate, setShowCreate] = useState(manager && parameters.get("create") === "1");
    const [createMounted, setCreateMounted] = useState(showCreate);
    const range = calendarRange(anchor, mode);
    const classes = useRead(allClasses);
    const load = useCallback(() => listSessions({ fromUtc: range.fromUtc, toUtc: range.toUtc, ...(classId ? { classId } : {}) }), [range.fromUtc, range.toUtc, classId]);
    const sessions = useRead(load);
    const today = centerDate(new Date().toISOString());
    const [navigationError, setNavigationError] = useState<unknown>();
    const changeCalendar = (nextAnchor: string, nextMode: CalendarMode) => {
        try {
            calendarRange(nextAnchor, nextMode);
            setAnchor(nextAnchor);
            setMode(nextMode);
            setNavigationError(null);
        } catch { setNavigationError(new Error("Đã đến giới hạn ngày được hỗ trợ.")); }
    };
    const move = (direction: -1 | 1) => {
        try {
            changeCalendar(navigateCalendar(anchor, mode, direction), mode);
        } catch { setNavigationError(new Error("Đã đến giới hạn ngày được hỗ trợ.")); }
    };
    const classKnown = classes.data?.some((item) => item.id === classId);
    return <>
        <PageHeading title="Lịch học" subtitle="Lịch học và lịch dạy theo quyền truy cập. Tất cả ngày giờ hiển thị theo giờ Việt Nam (UTC+7)." />
        <div className="academic-actions">
            <Link className="secondary inline" to="/classes">Danh sách lớp</Link>
            {manager && <button className="primary" aria-expanded={showCreate} aria-controls="session-create" onClick={() => {
                setCreateMounted(true);
                setShowCreate((previous) => !previous);
            }}>{showCreate ? "Đóng biểu mẫu" : "Tạo buổi học"}</button>}
        </div>
        {manager && <section hidden={!showCreate} className="panel session-create" id="session-create" aria-labelledby="create-session-heading">
            <h2 id="create-session-heading">Tạo buổi học</h2>
            <p>Chọn lớp và nhập thời gian tại Việt Nam. Máy chủ kiểm tra ngày của lớp và lịch trùng giáo viên trước khi lưu.</p>
            <ClassLoadState result={classes} />
            {createMounted && <SessionCreate classes={classes.data} initialClassId={classId} reload={() => { sessions.retry(); classes.retry(); }} />}
        </section>}
        <section className="panel schedule-panel" aria-labelledby="schedule-heading">
            <div className="schedule-toolbar">
                <div>
                    <span className="eyebrow">GIỜ VIỆT NAM · UTC+7</span>
                    <h2 id="schedule-heading">{mode === "month" ? `Tháng ${anchor.slice(5, 7)}/${anchor.slice(0, 4)}` : `${range.days[0].split("-").reverse().join("/")} — ${range.days[6].split("-").reverse().join("/")}`}</h2>
                </div>
                <div className="schedule-modes" role="group" aria-label="Chế độ lịch">
                    <button className="secondary" aria-pressed={mode === "week"} onClick={() => changeCalendar(anchor, "week")}>Tuần</button>
                    <button className="secondary" aria-pressed={mode === "month"} onClick={() => changeCalendar(anchor, "month")}>Tháng</button>
                </div>
            </div>
            <div className="schedule-toolbar">
                <div className="academic-actions" role="group" aria-label="Điều hướng lịch">
                    <button className="secondary" onClick={() => move(-1)} aria-label={mode === "week" ? "Tuần trước" : "Tháng trước"}>Trước</button>
                    <button className="secondary" onClick={() => changeCalendar(today, mode)}>Hôm nay</button>
                    <button className="secondary" onClick={() => move(1)} aria-label={mode === "week" ? "Tuần sau" : "Tháng sau"}>Sau</button>
                </div>
                <label htmlFor="schedule-class" className="schedule-filter">Lọc theo lớp
                    <select id="schedule-class" value={classId} disabled={classes.loading || !!classes.error} onChange={(event) => {
                        const next = new URLSearchParams(parameters);
                        if (event.target.value) next.set("classId", event.target.value); else next.delete("classId");
                        setParameters(next, { replace: true });
                    }}>
                        <option value="">Tất cả lớp được phép xem</option>
                        {classId && !classKnown && <option value={classId}>Lớp đang chọn (chờ xác minh)</option>}
                        {classes.data?.map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
                    </select>
                </label>
            </div>
            <ClassLoadState result={classes} />
            {classId && classes.data && !classKnown && <p role="status">Lớp đã chọn không nằm trong danh sách hiện tại. Máy chủ sẽ xác minh quyền; bạn có thể chọn tất cả lớp.</p>}
            <ErrorMessage error={navigationError} />
            {sessions.loading && <p role="status">Đang tải lịch học…</p>}
            <ErrorMessage error={sessions.error} />
            {sessions.error ? <button className="secondary" onClick={sessions.retry}>Thử tải lại lịch</button> : null}
            {sessions.data && <>
                <p role="status" className="small muted">{sessions.data.length ? `${sessions.data.length} buổi trong khoảng lịch đang xem.` : "Chưa có buổi học trong khoảng lịch đang xem."}</p>
                <a className="text-button inline" href="#schedule-agenda">Đến danh sách lịch theo ngày</a>
            </>}
            <section className={`calendar calendar-${mode}`} aria-label={mode === "week" ? "Lịch tuần, bắt đầu thứ Hai" : "Lịch tháng, bắt đầu thứ Hai"} aria-busy={sessions.loading}>
                {range.days.map((day, index) => <section key={day} className={`calendar-day ${day.slice(0, 7) !== anchor.slice(0, 7) && mode === "month" ? "spillover" : ""} ${day === today ? "is-today" : ""}`} aria-label={dayLabel(day)}>
                    <h3><span className="calendar-weekday">{["T2", "T3", "T4", "T5", "T6", "T7", "CN"][index % 7]}</span><time dateTime={day} aria-current={day === today ? "date" : undefined}>{Number(day.slice(-2))}<span className="sr-only">/{day.slice(5, 7)}/{day.slice(0, 4)}</span></time></h3>
                    {sessions.data && <ul className="calendar-events">{sessionsForDay(sessions.data, day).map((item) => <li key={item.id}><SessionLink item={item} /></li>)}</ul>}
                </section>)}
            </section>
        </section>
        {sessions.data && <section className="panel schedule-agenda" id="schedule-agenda" aria-labelledby="agenda-heading" tabIndex={-1}>
            <h2 id="agenda-heading">Danh sách lịch theo ngày</h2>
            <p className="small muted">Giờ Việt Nam (UTC+7). Buổi qua nửa đêm xuất hiện ở cả hai ngày liên quan.</p>
            {!sessions.data.length && <p>Chưa có buổi học trong khoảng này.</p>}
            {range.days.map((day) => {
                const items = sessionsForDay(sessions.data!, day);
                if (!items.length) return null;
                return <section className="agenda-day" key={day}>
                    <h3><time dateTime={day}>{dayLabel(day)}</time></h3>
                    <ul className="agenda-list">{items.map((item) => <li key={item.id}>
                        <SessionLink item={item} />
                        <p>{item.className} · {item.teacherName}</p>
                        <p className="small">{fullTime(item.startsAtUtc)} — {fullTime(item.endsAtUtc)} · {item.unit} · {item.skills.map((skill) => skills[skill] ?? skill).join(", ")}</p>
                    </li>)}</ul>
                </section>;
            })}
        </section>}
    </>;
}

function ClassLoadState({ result }: { result: { loading: boolean; error: unknown; data: ClassDetail[] | null; retry: () => void } }) {
    return <div className="catalog-status">
        {result.loading && <p role="status">Đang tải tất cả lớp được phép xem…</p>}
        <ErrorMessage error={result.error} />
        {result.error ? <button className="secondary" onClick={result.retry}>Thử tải lại danh sách lớp</button> : null}
        {result.data?.length === 0 && <p>Chưa có lớp được phép xem.</p>}
    </div>;
}

function SessionCreate({ classes, initialClassId, reload, existing, onSaved }: {
    classes: ClassDetail[] | null; initialClassId: string; reload: () => void;
    existing?: ScheduledSession; onSaved?: () => void;
}) {
    const [draft, setDraft] = useState<SessionDraft>(() => ({ classId: initialClassId,
        unit: existing?.unit ?? "", lesson: existing?.lesson ?? "", skills: existing?.skills ?? [],
        sessionType: existing?.sessionType ?? "Regular", examSlot: existing?.examSlot ?? "",
        startsAtLocal: existing ? sessionLocalTime(existing.startsAtUtc) : "", endsAtLocal: existing ? sessionLocalTime(existing.endsAtUtc) : "",
    }));
    const [touched, setTouched] = useState<Record<string, boolean>>({});
    const [error, setError] = useState<unknown>();
    const [pending, setPending] = useState(false);
    const lock = useRef(false);
    const edited = useRef(false);
    const previousClassId = useRef(initialClassId);
    const alive = useRef(false);
    const form = useRef<HTMLFormElement>(null);
    const navigate = useNavigate();
    useEffect(() => {
        alive.current = true;
        return () => { alive.current = false; };
    }, []);
    useEffect(() => {
        if (previousClassId.current === initialClassId) return;
        previousClassId.current = initialClassId;
        if (!lock.current && !edited.current)
            setDraft((previous) => ({ ...previous, classId: initialClassId }));
    }, [initialClassId]);
    const errors = validateSessionDraft(draft);
    if (classes && !classes.some((item) => item.id === draft.classId)) errors.classId = ["Chọn lớp trong danh sách được phép xem."];
    const invalid = Object.keys(errors).length > 0;
    const fields = (key: keyof SessionDraft) => {
        const serverKey = key === "startsAtLocal" ? "startsAtUtc" : key === "endsAtLocal" ? "endsAtUtc" : key;
        return [...(touched[key] ? errors[key] ?? [] : []),
            ...Object.entries(error instanceof ApiError ? error.errors : {}).filter(([name]) => name.split(".").at(-1)?.toLowerCase() === serverKey.toLowerCase()).flatMap(([, messages]) => messages)];
    };
    const change = (key: keyof SessionDraft, value: string | string[]) => {
        edited.current = true;
        setDraft((previous) => ({ ...previous, [key]: value }));
        setError(null);
    };
    const attributes = (key: Exclude<keyof SessionDraft, "skills">) => ({
        id: `session-${key}`, name: key, value: draft[key], disabled: pending,
        "aria-invalid": !!fields(key).length,
        "aria-describedby": fields(key).length ? `session-error-${key}` : undefined,
        onBlur: () => setTouched((previous) => ({ ...previous, [key]: true })),
        onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => change(key, event.target.value),
    });
    const messages = (key: keyof SessionDraft) => fields(key).length ? <p className="field-error" id={`session-error-${key}`} role="alert">{fields(key).join(" ")}</p> : null;
    const field = (key: Exclude<keyof SessionDraft, "skills">, label: string, control: ReactNode) => <div className="academic-field"><label htmlFor={`session-${key}`}>{label}</label>{control}{messages(key)}</div>;
    const chosen = classes?.find((item) => item.id === draft.classId);
    return <form ref={form} className="academic-form" noValidate aria-busy={pending} onSubmit={async (event) => {
        event.preventDefault();
        if (lock.current) return;
        setTouched(Object.fromEntries(Object.keys(draft).map((key) => [key, true])));
        if ((!existing && !classes?.length) || invalid) {
            requestAnimationFrame(() => form.current?.querySelector<HTMLElement>("[aria-invalid='true']")?.focus());
            return;
        }
        lock.current = true;
        setPending(true);
        setError(null);
        const generation = getSessionGeneration();
        try {
            const created = await saveSession(draft, existing);
            if (!alive.current || generation !== getSessionGeneration()) return;
            if (existing) onSaved?.();
            else navigate(`/schedule/${created.id}`, { state: { createdId: created.id, generation } });
        } catch (failure) {
            if (alive.current && generation === getSessionGeneration()) setError(failure);
        } finally {
            if (alive.current && generation === getSessionGeneration()) {
                lock.current = false;
                setPending(false);
            }
        }
    }}>
        <div className="academic-fields">
            {!existing && field("classId", "Lớp học", <select {...attributes("classId")} required disabled={pending || !classes?.length}>
                <option value="">Chọn lớp học</option>
                {draft.classId && !classes?.some((item) => item.id === draft.classId) && <option value={draft.classId}>Lớp đang chọn (chờ xác minh)</option>}
                {classes?.map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
            </select>)}
            {field("sessionType", "Loại buổi học", <select {...attributes("sessionType")} required onChange={(event) => {
                change("sessionType", event.target.value);
                if (event.target.value === "Regular") change("examSlot", "");
            }}>{Object.entries(types).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select>)}
            {field("unit", "Unit", <input {...attributes("unit")} required maxLength={100} />)}
            {field("lesson", "Bài học", <input {...attributes("lesson")} required maxLength={200} />)}
            {field("startsAtLocal", "Bắt đầu — giờ Việt Nam (UTC+7)", <input {...attributes("startsAtLocal")} type="datetime-local" step={60} min="0001-01-01T00:00" max="9999-12-31T23:59" required />)}
            {field("endsAtLocal", "Kết thúc — giờ Việt Nam (UTC+7)", <input {...attributes("endsAtLocal")} type="datetime-local" step={60} min="0001-01-01T00:00" max="9999-12-31T23:59" required />)}
            {draft.sessionType !== "Regular" && field("examSlot", "Ca thi (không bắt buộc)", <input {...attributes("examSlot")} maxLength={100} />)}
        </div>
        {chosen && <p className="small muted">Giáo viên: {chosen.teacherName}. Ngày của lớp: {chosen.startDate} — {chosen.endDate} (Việt Nam).</p>}
        <fieldset className="session-skills" disabled={pending} aria-describedby={fields("skills").length ? "session-error-skills" : undefined}>
            <legend>Kỹ năng (chọn ít nhất một)</legend>
            <div>{sessionSkills.map((skill) => <label key={skill}><input type="checkbox" name="skills" value={skill} checked={draft.skills.includes(skill)} aria-invalid={!!fields("skills").length}
                aria-describedby={fields("skills").length ? "session-error-skills" : undefined}
                onBlur={() => setTouched((previous) => ({ ...previous, skills: true }))}
                onChange={(event) => change("skills", event.target.checked ? [...draft.skills, skill] : draft.skills.filter((value) => value !== skill))} />{skills[skill]}</label>)}</div>
            {messages("skills")}
        </fieldset>
        <ErrorMessage error={error} />
        {error instanceof ApiError && error.status === 409 && <div className="conflict-help"><p>{existing ? "Buổi học có thể đã thay đổi, trùng lịch hoặc có điểm danh. Kiểm tra thời gian hoặc tải lại trước khi thử lại." : "Lịch giáo viên đã trùng hoặc thay đổi. Dữ liệu bạn nhập vẫn được giữ; tải lại lịch rồi chọn thời gian khác, không tự gửi lại yêu cầu."}</p><button type="button" className="secondary" onClick={reload}>{existing ? "Bỏ bản sửa và tải lại" : "Tải lại lịch và lớp, giữ biểu mẫu"}</button></div>}
        {error instanceof ApiError && error.status === 0 && <p>Chưa xác nhận được yêu cầu đã lưu hay chưa. Kiểm tra lịch trước khi gửi lại để tránh tạo trùng.</p>}
        <p className="small muted" id="session-submit-help">{pending ? "Đang lưu buổi học. Vui lòng chờ kết quả." : "Ngày giờ nhập luôn là giờ Việt Nam, không phụ thuộc múi giờ của thiết bị."}</p>
        <button className="primary" disabled={pending || (!existing && !classes?.length)} aria-describedby="session-submit-help">{pending ? "Đang lưu buổi học…" : "Lưu buổi học"}</button>
    </form>;
}

export function SessionDetails() {
    const { id = "" } = useParams();
    const load = useCallback(() => getScheduledSession(id), [id]);
    const result = useRead(load);
    const location = useLocation();
    const created = location.state?.createdId === id && location.state?.generation === getSessionGeneration();
    const item = result.data;
    const manager = canManageAcademic(identity(getSession())!.role);
    return <SessionDetailView key={id} item={item} manager={manager} created={created} result={result} />;
}

function SessionDetailView({ item, manager, created, result }: {
    item: ScheduledSession | null; manager: boolean; created: boolean; result: ReturnType<typeof useRead<ScheduledSession>>;
}) {
    const [editing, setEditing] = useState(false);
    const [pending, setPending] = useState(false);
    const [error, setError] = useState<unknown>();
    const [notice, setNotice] = useState("");
    const cancelling = useRef(false);
    async function cancel() {
        if (!item || cancelling.current || !window.confirm("Hủy buổi học này? Lịch sử điểm danh vẫn được giữ và không thể mở lại buổi học.")) return;
        cancelling.current = true;
        setPending(true); setError(null); setNotice("");
        try {
            await cancelSession(item);
            setNotice("Đã hủy buổi học."); result.retry();
        } catch (failure) { setError(failure); }
        finally { cancelling.current = false; setPending(false); }
    }
    return <>
        <PageHeading title="Chi tiết buổi học" subtitle="Thông tin được tải trực tiếp từ Academic. Ngày giờ Việt Nam (UTC+7)." />
        <div className="academic-actions"><Link className="secondary inline" to="/schedule">Về lịch học</Link></div>
        {created && <p className="notice" role="status">Đã tạo buổi học thành công. {result.error ? "Chưa đọc được chi tiết; chỉ tải lại, không tạo lại buổi học." : result.loading ? "Đang đọc thông tin từ máy chủ." : "Đang hiển thị thông tin từ máy chủ."}</p>}
        {result.loading && <p role="status">Đang tải chi tiết buổi học…</p>}
        <ErrorMessage error={result.error} />
        {result.error ? <button className="secondary" onClick={result.retry}>Thử tải lại chi tiết buổi học</button> : null}
        {notice && <p className="notice" role="status">{notice}</p>}
        {item && <section className="panel class-result">
            <span className="pill">{item.isCancelled ? "Đã hủy" : types[item.sessionType]}</span>
            <h2>{item.lesson}</h2>
            <dl className="academic-detail">
                <dt>Lớp học</dt><dd><Link className="accent" to={`/classes/${item.classId}`}>{item.classCode} · {item.className}</Link></dd>
                <dt>Giáo viên</dt><dd>{item.teacherName}</dd>
                <dt>Unit</dt><dd>{item.unit}</dd>
                <dt>Kỹ năng</dt><dd>{item.skills.map((skill) => skills[skill] ?? skill).join(", ")}</dd>
                <dt>Bắt đầu (UTC+7)</dt><dd><time dateTime={item.startsAtUtc}>{fullTime(item.startsAtUtc)}</time></dd>
                <dt>Kết thúc (UTC+7)</dt><dd><time dateTime={item.endsAtUtc}>{fullTime(item.endsAtUtc)}</time></dd>
                <dt>Ca thi</dt><dd>{item.examSlot ?? "Không áp dụng"}</dd>
                <dt>Ngày tạo (UTC+7)</dt><dd>{fullTime(item.createdAtUtc)}</dd>
            </dl>
            <Link className="primary inline" to={`/schedule?classId=${encodeURIComponent(item.classId)}&date=${centerDate(item.startsAtUtc)}`}>Xem trên lịch lớp</Link>
            <Link className="secondary inline" to={`/attendance/${item.id}`}>{identity(getSession())?.role === "Student" ? "Điểm danh của tôi trong buổi này" : "Xem điểm danh buổi học"}</Link>
            {manager && !item.isCancelled && !editing && <div className="academic-actions">
                <button className="secondary" disabled={pending} onClick={() => { setEditing(true); setNotice(""); setError(null); }}>Sửa buổi học</button>
                <button className="secondary" disabled={pending} onClick={() => void cancel()}>{pending ? "Đang hủy…" : "Hủy buổi học"}</button>
                <ErrorMessage error={error} />
                {error ? <button className="secondary" onClick={() => { setError(null); result.retry(); }}>Tải lại buổi học</button> : null}
            </div>}
        </section>}
        {item && manager && !item.isCancelled && editing && <section className="panel">
            <h2>Sửa buổi học</h2>
            <button className="secondary" onClick={() => setEditing(false)}>Đóng biểu mẫu</button>
            <SessionCreate key={item.revision} classes={null} initialClassId={item.classId} existing={item}
                reload={() => { setEditing(false); result.retry(); }}
                onSaved={() => { setEditing(false); setNotice("Đã cập nhật buổi học."); result.retry(); }} />
        </section>}
    </>;
}
