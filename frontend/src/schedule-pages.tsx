import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { CircleUserRound } from "lucide-react";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { localizeSystemMessage } from "./api-messages";
import { canManageAcademic, isAcademicId, listClasses, type ClassDetail } from "./academic";
import { useRead } from "./academic-pages";
import {
    calendarRange, cancelSession, centerDate, createSessionBatch, getScheduledSession, listSessions,
    navigateCalendar, previewSessionBatch, recurringBatchDates, saveSession, sessionLocalTime, sessionSkills, sessionsForDay, toggleBatchDate,
    validateBatchDraft, validateSessionDraft,
    type BatchDraft, type BatchPreview, type CalendarMode, type ScheduledSession, type SessionDraft,
} from "./schedule";
import { ErrorMessage, PageHeading } from "./ui";
import "./schedule-batch.css";

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

function SessionLink({ item, classType }: { item: ScheduledSession; classType: ClassDetail["classType"] | undefined }) {
    const type = types[item.sessionType];
    const classLabel = classType === "Main" ? "Lớp chính" : classType === "Supplementary" ? "Lớp bổ trợ" : null;
    const overnight = centerDate(item.startsAtUtc) !== centerDate(item.endsAtUtc);
    return <Link className="session-link" data-type={item.sessionType} to={`/schedule/${item.id}`}>
        <span className="session-card-header">
            <time dateTime={item.startsAtUtc}><span className="sr-only">Bắt đầu </span>{timeLabel(item.startsAtUtc)}</time>
            <time dateTime={item.endsAtUtc}><span className="sr-only">Kết thúc </span>{timeLabel(item.endsAtUtc)}</time>
            <CircleUserRound size={16} aria-hidden="true" />
        </span>
        <span className="session-card-body">
            <strong>{item.lesson}</strong>
            {overnight && <span>{fullTime(item.startsAtUtc)} – {fullTime(item.endsAtUtc)}</span>}
            {item.examSlot && <span className="session-card-slot">{item.examSlot}</span>}
            {item.skills.length > 0 && <span className="session-card-skills">
                <span className="sr-only">Kỹ năng: </span>
                {item.skills.map(skill => <span key={skill}>{skills[skill] ?? skill}</span>)}
            </span>}
            {item.teacherName && <span><span className="sr-only">Giáo viên: </span>{item.teacherName}</span>}
            <span className="session-card-code"><span className="sr-only">Mã lớp: </span>{item.classCode}</span>
            <span className="session-card-badges">
                {classLabel && <span className="session-card-class-type" data-class-type={classType}>{classLabel}</span>}
                <span>{type}</span>
                {item.isCancelled && <span className="session-card-cancelled">Đã hủy</span>}
            </span>
        </span>
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
    const classTypes = new Map(classes.data?.map((item) => [item.id, item.classType]));
    const classKnown = classTypes.has(classId);
    const [selecting, setSelecting] = useState(false);
    const [selected, setSelected] = useState<string[]>([]);
    const [batchNotice, setBatchNotice] = useState("");
    const toggleDate = (day: string) => {
        try { setSelected(toggleBatchDate(selected, day)); setNavigationError(null); } catch (failure) { setNavigationError(failure); }
    };
    return <>
        <PageHeading title="Lịch học" subtitle="Lịch học và lịch dạy theo quyền truy cập. Tất cả ngày giờ hiển thị theo giờ Việt Nam (UTC+7)." />
        <div className="academic-actions">
            {manager && <button className="primary" aria-expanded={showCreate} aria-controls="session-create" onClick={() => {
                setCreateMounted(true);
                setShowCreate((previous) => !previous);
            }}>{showCreate ? "Đóng biểu mẫu" : "Tạo buổi học"}</button>}
            {manager && <button className="secondary" aria-pressed={selecting} aria-controls="session-batch" onClick={() => {
                setSelecting((previous) => !previous);
                setSelected([]);
                setBatchNotice("");
            }}>{selecting ? "Thoát chế độ chọn nhiều ngày" : "Thêm nhiều buổi từ lịch"}</button>}
        </div>
        {batchNotice && <p className="notice" role="status">{batchNotice}</p>}
        {manager && selecting && <RecurringDates today={today} onAdd={(from, to, weekdays) => setSelected(recurringBatchDates(selected, from, to, weekdays))} />}
        {manager && selecting && <SessionBatch classes={classes.data} initialClassId={classId} dates={selected} clearDates={() => setSelected([])}
            onCreated={(count) => { setSelected([]); setBatchNotice(`Đã tạo ${count} buổi học.`); sessions.retry(); }} />}
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
            {mode === "month" && <p className="calendar-scroll-hint small muted">Cuộn ngang để xem đủ 7 ngày hoặc mở danh sách lịch theo ngày.</p>}
            <section className={mode === "month" ? "calendar-scroll" : undefined} tabIndex={mode === "month" ? 0 : undefined}
                aria-label={mode === "week" ? "Lịch tuần, bắt đầu thứ Hai" : "Lịch tháng, bắt đầu thứ Hai"} aria-busy={sessions.loading}>
                <div className={`calendar calendar-${mode}`}>
                {range.days.map((day, index) => <section key={day} className={`calendar-day ${day.slice(0, 7) !== anchor.slice(0, 7) && mode === "month" ? "spillover" : ""} ${day === today ? "is-today" : ""} ${selecting && selected.includes(day) ? "is-selected" : ""}`} aria-label={dayLabel(day)}>
                    <h3><span className="calendar-weekday">{["T2", "T3", "T4", "T5", "T6", "T7", "CN"][index % 7]}</span><time dateTime={day} aria-current={day === today ? "date" : undefined}>{Number(day.slice(-2))}<span className="sr-only">/{day.slice(5, 7)}/{day.slice(0, 4)}</span></time></h3>
                    {manager && selecting && <button type="button" className="secondary day-select" aria-pressed={selected.includes(day)}
                        aria-label={`${selected.includes(day) ? "Bỏ chọn" : "Chọn"} ${dayLabel(day)}`} onClick={() => toggleDate(day)}>{selected.includes(day) ? "Đã chọn" : "Chọn"}</button>}
                    {sessions.data && <ul className="calendar-events">{sessionsForDay(sessions.data, day).map((item) => <li key={item.id}><SessionLink item={item} classType={classTypes.get(item.classId)} /></li>)}</ul>}
                </section>)}
                </div>
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
                        <SessionLink item={item} classType={classTypes.get(item.classId)} />
                        <p className="small">{item.className} · {item.unit}</p>
                    </li>)}</ul>
                </section>;
            })}
        </section>}
    </>;
}

function RecurringDates({ today, onAdd }: { today: string; onAdd: (from: string, to: string, weekdays: number[]) => void }) {
    const [error, setError] = useState<unknown>(null);
    return <details className="panel">
        <summary>Lặp theo thứ trong tuần</summary>
        <form className="academic-form" onSubmit={event => {
            event.preventDefault();
            const fields = new FormData(event.currentTarget);
            try {
                onAdd(String(fields.get("from")), String(fields.get("to")), fields.getAll("weekday").map(Number));
                setError(null);
            } catch (failure) { setError(failure); }
        }}>
            <div className="academic-fields">
                <label htmlFor="repeat-from">Từ ngày<input id="repeat-from" name="from" type="date" defaultValue={today} required /></label>
                <label htmlFor="repeat-to">Đến ngày<input id="repeat-to" name="to" type="date" defaultValue={today} required /></label>
            </div>
            <fieldset className="session-skills">
                <legend>Lặp vào</legend>
                <div className="academic-actions">
                    {[1, 2, 3, 4, 5, 6, 0].map(day => <label key={day}><input name="weekday" type="checkbox" value={day} /> {day ? `Thứ ${day + 1}` : "Chủ nhật"}</label>)}
                </div>
            </fieldset>
            <ErrorMessage error={error} />
            <button className="secondary">Thêm ngày vào danh sách</button>
        </form>
    </details>;
}

function ClassLoadState({ result }: { result: { loading: boolean; error: unknown; data: ClassDetail[] | null; retry: () => void } }) {
    return <div className="catalog-status">
        {result.loading && <p role="status">Đang tải tất cả lớp được phép xem…</p>}
        <ErrorMessage error={result.error} />
        {result.error ? <button className="secondary" onClick={result.retry}>Thử tải lại danh sách lớp</button> : null}
        {result.data?.length === 0 && <p>Chưa có lớp được phép xem.</p>}
    </div>;
}

const slotStatus: Record<string, string> = {
    Ok: "Có thể tạo",
    Invalid: "Không hợp lệ",
    BatchOverlap: "Trùng với một ngày khác đã chọn",
    TeacherOverlap: "Giáo viên đã có buổi trùng giờ",
};

function SessionBatch({ classes, initialClassId, dates, clearDates, onCreated }: {
    classes: ClassDetail[] | null; initialClassId: string; dates: string[]; clearDates: () => void; onCreated: (count: number) => void;
}) {
    const [fields, setFields] = useState({ classId: initialClassId, unit: "", lesson: "", skills: [] as string[],
        sessionType: "Regular", examSlot: "", startTime: "08:00", endTime: "09:30" });
    const draft: BatchDraft = { ...fields, dates };
    const key = JSON.stringify(draft);
    const [preview, setPreview] = useState<{ key: string; data: BatchPreview } | null>(null);
    const [attempted, setAttempted] = useState(false);
    const [error, setError] = useState<unknown>();
    const [pending, setPending] = useState(false);
    const lock = useRef(false);
    const alive = useRef(false);
    const form = useRef<HTMLFormElement>(null);
    useEffect(() => {
        alive.current = true;
        return () => { alive.current = false; };
    }, []);
    const errors = validateBatchDraft(draft);
    if (classes && !classes.some((item) => item.id === draft.classId)) errors.classId = ["Chọn lớp trong danh sách được phép xem."];
    // A preview only counts for the exact inputs it was computed from, so any edit or date change forces a new preview.
    const current = preview?.key === key ? preview.data : null;
    const set = (changes: Partial<typeof fields>) => { setFields((previous) => ({ ...previous, ...changes })); setError(null); };
    const shown = (name: string) => attempted && errors[name] ? <p className="field-error" id={`batch-error-${name}`} role="alert">{errors[name].join(" ")}</p> : null;
    const attributes = (name: keyof typeof fields & string) => ({
        id: `batch-${name}`, disabled: pending, "aria-invalid": attempted && !!errors[name],
        "aria-describedby": attempted && errors[name] ? `batch-error-${name}` : undefined,
    });
    const chosen = classes?.find((item) => item.id === draft.classId);

    async function run(kind: "preview" | "create") {
        if (lock.current) return;
        setAttempted(true);
        if (Object.keys(errors).length) {
            requestAnimationFrame(() => form.current?.querySelector<HTMLElement>("[aria-invalid='true']")?.focus());
            return;
        }
        lock.current = true;
        setPending(true);
        setError(null);
        const generation = getSessionGeneration();
        try {
            if (kind === "preview") {
                const data = await previewSessionBatch(draft);
                if (alive.current && generation === getSessionGeneration()) setPreview({ key, data });
            } else {
                const created = await createSessionBatch(draft);
                if (alive.current && generation === getSessionGeneration()) { setPreview(null); onCreated(created.length); }
            }
        } catch (failure) {
            if (alive.current && generation === getSessionGeneration()) {
                setError(failure);
                // After a failed write the old preview is no longer trustworthy; the user must preview again.
                if (kind === "create") setPreview(null);
            }
        } finally {
            if (alive.current && generation === getSessionGeneration()) { lock.current = false; setPending(false); }
        }
    }

    const problems = current?.items.filter((item) => item.status !== "Ok").length ?? 0;
    return <section className="panel" id="session-batch" aria-labelledby="batch-heading">
        <h2 id="batch-heading">Thêm nhiều buổi học từ lịch</h2>
        <p>Bấm “Chọn” ở các ngày trên lịch (có thể chuyển tuần hoặc tháng), nhập thông tin chung rồi xem trước. Chỉ khi toàn bộ buổi hợp lệ và không trùng lịch giáo viên bạn mới xác nhận tạo; hệ thống tạo tất cả hoặc không tạo buổi nào.</p>
        <form ref={form} className="academic-form" noValidate aria-busy={pending} onSubmit={(event) => { event.preventDefault(); void run("preview"); }}>
            <p id="batch-dates-label"><strong>Ngày đã chọn: {dates.length}</strong></p>
            {dates.length ? <ul className="batch-dates" aria-labelledby="batch-dates-label">{dates.map((day) => <li key={day}><time dateTime={day}>{day.split("-").reverse().join("/")}</time></li>)}</ul>
                : <p className="small muted">Chưa chọn ngày nào.</p>}
            {shown("dates")}
            <div className="academic-fields">
                <div className="academic-field"><label htmlFor="batch-classId">Lớp học</label>
                    <select {...attributes("classId")} required value={fields.classId} disabled={pending || !classes?.length} onChange={(event) => set({ classId: event.target.value })}>
                        <option value="">Chọn lớp học</option>
                        {fields.classId && !classes?.some((item) => item.id === fields.classId) && <option value={fields.classId}>Lớp đang chọn (chờ xác minh)</option>}
                        {classes?.map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
                    </select>{shown("classId")}</div>
                <div className="academic-field"><label htmlFor="batch-sessionType">Loại buổi học</label>
                    <select {...attributes("sessionType")} value={fields.sessionType} onChange={(event) => set({ sessionType: event.target.value, ...(event.target.value === "Regular" ? { examSlot: "" } : {}) })}>
                        {Object.entries(types).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select>{shown("sessionType")}</div>
                <div className="academic-field"><label htmlFor="batch-unit">Unit</label>
                    <input {...attributes("unit")} required maxLength={100} value={fields.unit} onChange={(event) => set({ unit: event.target.value })} />{shown("unit")}</div>
                <div className="academic-field"><label htmlFor="batch-lesson">Bài học</label>
                    <input {...attributes("lesson")} required maxLength={200} value={fields.lesson} onChange={(event) => set({ lesson: event.target.value })} />{shown("lesson")}</div>
                <div className="academic-field"><label htmlFor="batch-startTime">Giờ bắt đầu mỗi ngày — giờ Việt Nam (UTC+7)</label>
                    <input {...attributes("startTime")} type="time" step={60} required value={fields.startTime} onChange={(event) => set({ startTime: event.target.value })} />{shown("startTime")}</div>
                <div className="academic-field"><label htmlFor="batch-endTime">Giờ kết thúc mỗi ngày — giờ Việt Nam (UTC+7)</label>
                    <input {...attributes("endTime")} type="time" step={60} required value={fields.endTime} onChange={(event) => set({ endTime: event.target.value })} />{shown("endTime")}</div>
                {fields.sessionType !== "Regular" && <div className="academic-field"><label htmlFor="batch-examSlot">Ca thi (không bắt buộc)</label>
                    <input {...attributes("examSlot")} maxLength={100} value={fields.examSlot} onChange={(event) => set({ examSlot: event.target.value })} />{shown("examSlot")}</div>}
            </div>
            {chosen && <p className="small muted">Giáo viên: {chosen.teacherName}. Ngày của lớp: {chosen.startDate} — {chosen.endDate} (Việt Nam).</p>}
            <fieldset className="session-skills" disabled={pending}>
                <legend>Kỹ năng (chọn ít nhất một)</legend>
                <div>{sessionSkills.map((skill) => <label key={skill}><input type="checkbox" name="skills" value={skill} checked={fields.skills.includes(skill)} aria-invalid={attempted && !!errors.skills}
                    onChange={(event) => set({ skills: event.target.checked ? [...fields.skills, skill] : fields.skills.filter((value) => value !== skill) })} />{skills[skill]}</label>)}</div>
                {shown("skills")}
            </fieldset>
            <ErrorMessage error={error} />
            {error instanceof ApiError && error.status === 409 && <p role="status">Lịch giáo viên đã thay đổi hoặc bị trùng nên không có buổi nào được tạo. Hãy xem trước lại để cập nhật kết quả.</p>}
            {error instanceof ApiError && error.status === 0 && <p role="status">Chưa xác nhận được các buổi đã lưu hay chưa. Bấm “Xem trước” để kiểm tra trước khi tạo lại, tránh tạo trùng.</p>}
            <div className="academic-actions">
                <button className="secondary" disabled={pending}>{pending ? "Đang xử lý…" : "Xem trước lịch"}</button>
                <button type="button" className="secondary" disabled={pending || !dates.length} onClick={clearDates}>Bỏ chọn tất cả ngày</button>
            </div>
        </form>
        {preview && !current && <p role="status">Nội dung hoặc ngày chọn đã thay đổi sau lần xem trước. Hãy xem trước lại.</p>}
        {current && <section aria-labelledby="batch-preview-heading">
            <h3 id="batch-preview-heading">Xem trước {current.items.length} buổi</h3>
            <p role="status">{current.canCreate ? "Tất cả buổi hợp lệ và không trùng lịch giáo viên. Kiểm tra rồi xác nhận để tạo."
                : `${problems} buổi có vấn đề nên chưa thể tạo. Bỏ chọn các ngày đó trên lịch hoặc đổi giờ, rồi xem trước lại. Chưa có buổi nào được lưu.`}</p>
            <ul className="batch-preview">{current.items.map((item) => <li key={item.index} className={item.status === "Ok" ? "" : "is-bad"}>
                <strong>{dates[item.index] ? dayLabel(dates[item.index]) : `Buổi ${item.index + 1}`}</strong>
                {item.startsAtUtc && item.endsAtUtc && <span>{timeLabel(item.startsAtUtc)} — {timeLabel(item.endsAtUtc)}</span>}
                <span>{slotStatus[item.status] ?? "Chưa xác định"}{item.status === "Invalid" && item.message ? `: ${localizeSystemMessage(item.message, "Kiểm tra ngày và giờ đã nhập.")}` : ""}</span>
            </li>)}</ul>
            <button type="button" className="primary" disabled={pending || !current.canCreate} onClick={() => void run("create")}>
                {pending ? "Đang tạo…" : `Xác nhận tạo ${current.items.length} buổi`}</button>
        </section>}
    </section>;
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
