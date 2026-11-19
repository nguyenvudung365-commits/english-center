import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { api, ApiError, getSession, getSessionGeneration, identity } from "./api";
import { canManageAcademic, isAcademicId } from "./academic";
import { useRead as useLoad } from "./academic-pages";
import { MyEnrollments } from "./enrollment-pages";
import { allVisibleClasses, ReadFeedback } from "./attendance-pages";
import {
    assignmentKindLabel, assignmentKinds, assignmentKindLabels, assignmentStatuses, assignmentStatusLabel, assignmentStatusLabels,
    ASSIGNMENT_DESCRIPTION_MAX, ASSIGNMENT_LESSON_MAX, ASSIGNMENT_TITLE_MAX, ASSIGNMENT_UNIT_MAX, canDownloadPrompt, canGrade, createAssignment, emptyAssignmentDraft, FEEDBACK_MAX,
    formatScore, gradeSubmission, isAssignmentLocked, isExamKind, isGraded, listClassAssignments, listGradeHistory, listMyAssignments, promptDownload, publishAssignment,
    safeDownloadUrl, scoreInput, studentLabel, studentScoreText, submissionStatuses, submissionStatusLabel, submissionStatusLabels, updateAssignment, validateAssignmentDraft,
    validateGradeDraft, type AssignmentDraft, type GradeDraft,
} from "./coursework";
import { formatVnDateTime, utcToVnLocal } from "./format";
import { getManagedStudent, listManagedAccounts } from "./identity";
import { calendarRange, getScheduledSession, listSessions } from "./schedule";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";
import { ANSWER_MAX, examAccept, submissionAccept, submitAssignment, validateSubmissionFiles, type Assignment, type Submission, type UploadStatus } from "./submissions";

// All mutations have one in-flight owner and ignore completion after navigation or login changes.
export function useAction() {
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>(null);
    const owner = useRef<AbortController | null>(null);
    const alive = useRef(false);
    useEffect(() => { alive.current = true; return () => { alive.current = false; owner.current?.abort(); }; }, []);
    async function run<T>(work: (signal: AbortSignal) => Promise<T>, accept: (value: T) => void) {
        if (owner.current) return;
        const controller = new AbortController(); owner.current = controller;
        const generation = getSessionGeneration();
        const valid = () => alive.current && !controller.signal.aborted && generation === getSessionGeneration();
        setBusy(true); setError(null);
        try { const value = await work(controller.signal); if (valid()) accept(value); }
        catch (failure) { if (valid()) setError(failure); }
        finally { if (owner.current === controller) owner.current = null; if (valid()) setBusy(false); }
    }
    return { busy, error, run };
}

function useRead<T>(path: string | null, missingIsEmpty = false) {
    const [version, setVersion] = useState(0);
    const [state, setState] = useState<{ data: T | null; loading: boolean; error: unknown }>({ data: null, loading: !!path, error: null });
    useEffect(() => {
        const controller = new AbortController();
        const generation = getSessionGeneration();
        const valid = () => !controller.signal.aborted && generation === getSessionGeneration();
        setState({ data: null, loading: !!path, error: null });
        if (path) void api<T>(path, { signal: controller.signal }).then(
            data => { if (valid()) setState({ data, loading: false, error: null }); },
            error => { if (valid()) setState({ data: null, loading: false, error: missingIsEmpty && error instanceof ApiError && error.status === 404 ? null : error }); },
        );
        return () => controller.abort();
    }, [path, version, missingIsEmpty]);
    return { ...state, reload: () => setVersion(value => value + 1) };
}

// 409: dữ liệu đã đổi ở nơi khác. Hiện hướng dẫn tiếng Việt kèm thông điệp máy chủ và nút tải lại.
export function Conflict({ error, text, onReload }: { error: unknown; text: string; onReload: () => void }) {
    if (!(error instanceof ApiError && error.status === 409)) return <ErrorMessage error={error} />;
    return <div className="error" role="alert">
        <strong>{text}</strong>
        <p className="small">Máy chủ: {error.message}</p>
        <button type="button" className="secondary" onClick={onReload}>Tải lại</button>
    </div>;
}

export function ClassSelect({ id, value, onChange, allLabel, destinationOf }: { id: string; value: string; onChange: (classId: string) => void; allLabel?: string; destinationOf?: string }) {
    const classes = useLoad(allVisibleClasses);
    const options = classes.data?.filter(item => !destinationOf || (item.id !== destinationOf && item.status !== "Finished"));
    return <div className="academic-field">
        <label htmlFor={id}>{destinationOf ? "Lớp đích" : "Lớp học"}</label>
        <select id={id} value={value} disabled={classes.loading || !!classes.error} onChange={event => onChange(event.target.value)}>
            <option value="">{allLabel ?? "Chọn lớp học"}</option>
            {value && !classes.data?.some(item => item.id === value) && <option value={value}>Lớp đang chọn (máy chủ xác minh quyền)</option>}
            {options?.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
        </select>
        <ReadFeedback result={classes} />
        {options?.length === 0 && <p>{destinationOf ? "Không có lớp đích phù hợp." : "Chưa có lớp nào trong phạm vi tài khoản của bạn."}</p>}
    </div>;
}

function FilterSelect({ id, label, value, values, labels, all, onChange }: {
    id: string; label: string; value: string; values: readonly string[]; labels: Record<string, string>; all: string; onChange: (value: string) => void;
}) {
    return <div className="academic-field">
        <label htmlFor={id}>{label}</label>
        <select id={id} value={value} onChange={event => onChange(event.target.value)}>
            <option value="">{all}</option>
            {values.map(item => <option key={item} value={item}>{labels[item] ?? item}</option>)}
        </select>
    </div>;
}

function AssignmentBadges({ item }: { item: Assignment }) {
    return <>
        <span className="pill">{assignmentStatusLabel(item.status)}</span>{" "}
        <span className="pill">{assignmentKindLabel(item.kind)}</span>
        {item.isLegacy && <> <span className="pill">Bài cũ (legacy)</span></>}
    </>;
}

export function Assignments() {
    const user = identity(getSession());
    if (!user) return null;
    return user.role === "Student" ? <StudentAssignments key={`${getSessionGeneration()}:${user.id}`} /> : <ManagerAssignments key={`${getSessionGeneration()}:${user.id}`} />;
}

function StudentAssignments() {
    const [params, setParams] = useSearchParams();
    const classId = params.get("classId") ?? "";
    const [input, setInput] = useState("");
    const [filter, setFilter] = useState({ search: "", status: "", submissionStatus: "", kind: "", page: 1 });
    const load = useCallback(() => listMyAssignments({ classId, ...filter }), [classId, filter]);
    const result = useLoad(load);
    const data = result.data;
    const patch = (value: Partial<typeof filter>) => setFilter(previous => ({ ...previous, page: 1, ...value }));
    return <>
        <PageHeading title="Bài tập" subtitle="Bài tập đã công bố cho bạn, trạng thái nộp, điểm và nhận xét. Giờ hiển thị theo Việt Nam (UTC+7)." />
        <section className="panel">
            <h2>Bài tập của tôi</h2>
            <form className="academic-filters" onSubmit={event => { event.preventDefault(); patch({ search: input.trim() }); }}>
                <ClassSelect id="mine-class" value={classId} allLabel="Tất cả lớp" onChange={value => { const next = new URLSearchParams(params); if (value) next.set("classId", value); else next.delete("classId"); setParams(next); setFilter(previous => ({ ...previous, page: 1 })); }} />
                <div className="academic-field">
                    <label htmlFor="mine-search">Tìm theo tiêu đề, Unit hoặc Lesson</label>
                    <input id="mine-search" type="search" maxLength={100} value={input} onChange={event => setInput(event.target.value)} />
                </div>
                <FilterSelect id="mine-status" label="Trạng thái bài" value={filter.status} values={assignmentStatuses.filter(item => item !== "Draft")} labels={assignmentStatusLabels} all="Tất cả trạng thái" onChange={value => patch({ status: value })} />
                <FilterSelect id="mine-submission" label="Trạng thái nộp" value={filter.submissionStatus} values={submissionStatuses} labels={submissionStatusLabels} all="Tất cả" onChange={value => patch({ submissionStatus: value })} />
                <FilterSelect id="mine-kind" label="Loại bài" value={filter.kind} values={assignmentKinds} labels={assignmentKindLabels} all="Tất cả loại" onChange={value => patch({ kind: value })} />
                <button className="primary">Tìm bài tập</button>
            </form>
            <ReadFeedback result={result} />
            {data && <>
                {data.items.length === 0 && <p>Chưa có bài tập phù hợp.</p>}
                <ul className="catalog-list">
                    {data.items.map(entry => {
                        const score = studentScoreText(entry.submissionStatus, entry.score);
                        return <li key={entry.assignment.id}>
                            <h3>{entry.isTransferredHistory ? entry.assignment.title : <Link className="accent" to={`/assignments/${entry.assignment.id}`}>{entry.assignment.title}</Link>}</h3>
                            {entry.isTransferredHistory && <p className="small muted">Kết quả từ lớp cũ</p>}
                            <p><AssignmentBadges item={entry.assignment} /></p>
                            {(entry.assignment.unit || entry.assignment.lesson) && <p className="small muted">{[entry.assignment.unit, entry.assignment.lesson].filter(Boolean).join(" · ")}</p>}
                            <p>Hạn nộp: <time dateTime={entry.assignment.closesAtUtc}>{formatVnDateTime(entry.assignment.closesAtUtc)}</time> (UTC+7)</p>
                            <p>Trạng thái nộp: <strong>{submissionStatusLabel(entry.submissionStatus)}</strong>{score && <> · Điểm: <strong>{score}</strong></>}</p>
                            {entry.submissionStatus === "Graded" && entry.feedback && <p className="assignment-description">Nhận xét: {entry.feedback}</p>}
                        </li>;
                    })}
                </ul>
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={page => setFilter(previous => ({ ...previous, page }))} label="Phân trang bài tập" />
            </>}
        </section>
    </>;
}

function ManagerAssignments() {
    const [params, setParams] = useSearchParams();
    const classId = params.get("classId") ?? "";
    const [input, setInput] = useState("");
    const [filter, setFilter] = useState({ search: "", status: "", kind: "" });
    const load = useCallback(() => classId ? listClassAssignments(classId, filter) : Promise.resolve([] as Assignment[]), [classId, filter]);
    const items = useLoad(load);
    return <>
        <PageHeading title="Bài tập" subtitle="Chọn lớp để xem, giao bài và theo dõi bài nộp. Chỉ thành viên và giáo viên phụ trách được truy cập." />
        <section className="panel">
            <form className="academic-filters coursework-filters" onSubmit={event => { event.preventDefault(); if (classId) setFilter(previous => ({ ...previous, search: input.trim() })); }}>
                <ClassSelect id="assignment-class" value={classId} onChange={value => { const next = new URLSearchParams(params); if (value) next.set("classId", value); else next.delete("classId"); setParams(next); }} />
                {classId && <><div className="academic-field">
                    <label htmlFor="assignment-search">Tìm theo tiêu đề, Unit hoặc Lesson</label>
                    <input id="assignment-search" type="search" maxLength={100} value={input} onChange={event => setInput(event.target.value)} />
                </div>
                <FilterSelect id="assignment-status" label="Trạng thái" value={filter.status} values={assignmentStatuses} labels={assignmentStatusLabels} all="Tất cả trạng thái" onChange={value => setFilter(previous => ({ ...previous, status: value }))} />
                <FilterSelect id="assignment-kind" label="Loại bài" value={filter.kind} values={assignmentKinds} labels={assignmentKindLabels} all="Tất cả loại" onChange={value => setFilter(previous => ({ ...previous, kind: value }))} />
                <button className="primary">Tìm bài tập</button></>}
            </form>
            {!classId && <p>Chọn một lớp để xem danh sách bài tập.</p>}
            {classId && <ReadFeedback result={items} />}
            {classId && items.data?.length === 0 && <p>Không có bài tập phù hợp trong lớp này.</p>}
            {items.data && items.data.length > 0 && <ul className="catalog-list">{items.data.map(item => <li key={item.id}>
                <h3><Link className="accent" to={`/assignments/${item.id}`}>{item.title}</Link></h3>
                <p><AssignmentBadges item={item} /></p>
                {(item.unit || item.lesson) && <p className="small muted">{[item.unit, item.lesson].filter(Boolean).join(" · ")}</p>}
                <p>Mở: {formatVnDateTime(item.opensAtUtc)} · Hạn nộp: <time dateTime={item.closesAtUtc}>{formatVnDateTime(item.closesAtUtc)}</time> (UTC+7)</p>
            </li>)}</ul>}
            {items.data?.length === 200 && <p>Đang hiển thị tối đa 200 bài tập; hãy dùng ô tìm kiếm hoặc bộ lọc để thu hẹp.</p>}
        </section>
        {classId && <AssignmentCreate key={classId} classId={classId} />}
    </>;
}

function SessionSelect({ id, classId, value, disabled, errors, onChange }: { id: string; classId: string; value: string; disabled: boolean; errors?: string[]; onChange: (value: string) => void }) {
    const [defaultRange] = useState(() => {
        const now = Date.now();
        return { fromUtc: new Date(now - 30 * 86400000).toISOString(), toUtc: new Date(now + 60 * 86400000).toISOString() };
    });
    const [date, setDate] = useState("");
    const [dateError, setDateError] = useState<unknown>(null);
    const { fromUtc, toUtc } = date ? calendarRange(date, "month") : defaultRange;
    const load = useCallback(() => listSessions({ classId, fromUtc, toUtc }), [classId, fromUtc, toUtc]);
    const sessions = useLoad(load);
    const field = fieldProps(id, errors);
    return <div className="academic-field">
        {!disabled && <>
            <label htmlFor={`${id}-date`}>Ngày để tìm buổi học</label>
            <input id={`${id}-date`} type="date" value={date} aria-describedby={`${id}-date-help`} onChange={event => {
                const next = event.target.value;
                try {
                    if (next) calendarRange(next, "month");
                    setDate(next);
                    setDateError(null);
                } catch { setDateError(new Error("Ngày nằm ngoài khoảng lịch được hỗ trợ.")); }
            }} />
            <p id={`${id}-date-help`} className="small muted">Chọn một ngày trong tháng cần tìm (giờ Việt Nam, UTC+7). Để trống để xem từ 30 ngày trước đến 60 ngày tới.</p>
            <ErrorMessage error={dateError} />
        </>}
        <label htmlFor={id}>Buổi học (bắt buộc trước khi công bố)</label>
        <select {...field} value={value} disabled={disabled || sessions.loading} aria-describedby={[field["aria-describedby"], `${id}-help`].filter(Boolean).join(" ")} onChange={event => onChange(event.target.value)}>
            <option value="">Chưa chọn buổi</option>
            {value && !sessions.data?.some(item => item.id === value) && <option value={value}>Buổi đang gắn</option>}
            {sessions.data?.map(item => <option key={item.id} value={item.id}>{[item.unit, item.lesson].filter(Boolean).join(" · ")} · {formatVnDateTime(item.startsAtUtc)}</option>)}
        </select>
        <FieldError id={id} errors={errors} />
        <p id={`${id}-help`} className="small muted">Unit/Lesson để trống sẽ lấy theo buổi đã chọn. Lưu nháp thì có thể chọn buổi sau.</p>
        {!disabled && sessions.data?.length === 0 && <p className="small muted" role="status">Không có buổi học trong khoảng lịch này. Hãy chọn ngày khác hoặc tạo buổi ở trang Lịch học.</p>}
        <ReadFeedback result={sessions} />
    </div>;
}

// Hiển thị buổi đã gắn theo Unit/Lesson và giờ học; không tải được (ví dụ không có quyền xem lịch) thì hiện mã buổi.
function SessionLabel({ id }: { id: string }) {
    const load = useCallback(() => getScheduledSession(id), [id]);
    const session = useLoad(load);
    if (!session.data) return <code>{id}</code>;
    return <>{[session.data.unit, session.data.lesson].filter(Boolean).join(" · ")} · {formatVnDateTime(session.data.startsAtUtc)} (UTC+7)</>;
}

function AssignmentForm({ id, classId, initial, locked, create, busy, children, submitLabel, promptName, onSubmit, onCancel }: {
    id: string; classId: string; initial: AssignmentDraft; locked: boolean; create: boolean; busy: boolean; children?: ReactNode;
    submitLabel: string; promptName?: string | null; onSubmit: (draft: AssignmentDraft) => void; onCancel?: () => void;
}) {
    const [draft, setDraft] = useState(initial);
    const [attempted, setAttempted] = useState(false);
    const errors = validateAssignmentDraft(draft, locked, create && !draft.saveAsDraft, !!promptName);
    const shown = attempted ? errors : {};
    const set = (patch: Partial<AssignmentDraft>) => setDraft({ ...draft, ...patch });
    return <form className="academic-form" noValidate aria-busy={busy} onSubmit={event => {
        event.preventDefault(); setAttempted(true);
        if (!busy && !Object.keys(errors).length) onSubmit(draft);
    }}>
        {locked && <p className="notice" role="status">Bài đã công bố hoặc là bài cũ: chỉ sửa được tiêu đề, hướng dẫn, Unit và Lesson. Loại bài, buổi và thời hạn được giữ nguyên.</p>}
        <div className="academic-field">
            <label htmlFor={`${id}-title`}>Tiêu đề (tối đa {ASSIGNMENT_TITLE_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-title`, shown.title)} required maxLength={ASSIGNMENT_TITLE_MAX} disabled={busy} value={draft.title} onChange={event => set({ title: event.target.value })} />
            <FieldError id={`${id}-title`} errors={shown.title} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-description`}>Hướng dẫn (tối đa {ASSIGNMENT_DESCRIPTION_MAX} ký tự, văn bản thuần)</label>
            <textarea {...fieldProps(`${id}-description`, shown.description)} maxLength={ASSIGNMENT_DESCRIPTION_MAX} disabled={busy} value={draft.description} onChange={event => set({ description: event.target.value })} />
            <FieldError id={`${id}-description`} errors={shown.description} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-kind`}>Loại bài</label>
            <select {...fieldProps(`${id}-kind`, shown.kind)} disabled={busy || locked} value={draft.kind} onChange={event => set({ kind: event.target.value })}>
                {assignmentKinds.map(item => <option key={item} value={item}>{assignmentKindLabel(item)}</option>)}
            </select>
            <FieldError id={`${id}-kind`} errors={shown.kind} />
        </div>
        {!locked && <div className="academic-field">
            <label htmlFor={`${id}-prompt`}>{isExamKind(draft.kind) ? "Đề thi" : "Đề bài (không bắt buộc)"} · PDF, tối đa 20 MiB</label>
            {promptName && <p className="small muted">Đề hiện tại: {promptName}. Chọn file mới để thay (chỉ thay được khi bài chưa công bố).</p>}
            <input {...fieldProps(`${id}-prompt`, shown.promptFile)} type="file" accept={examAccept} disabled={busy} onChange={event => set({ promptFile: event.target.files?.[0] ?? null })} />
            <FieldError id={`${id}-prompt`} errors={shown.promptFile} />
            <p className="small muted">Học viên chỉ tải được file đề khi bài đang mở.</p>
        </div>}
        <div className="academic-field">
            <label htmlFor={`${id}-unit`}>Unit (tối đa {ASSIGNMENT_UNIT_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-unit`, shown.unit)} maxLength={ASSIGNMENT_UNIT_MAX} disabled={busy} value={draft.unit} onChange={event => set({ unit: event.target.value })} />
            <FieldError id={`${id}-unit`} errors={shown.unit} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-lesson`}>Lesson (tối đa {ASSIGNMENT_LESSON_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-lesson`, shown.lesson)} maxLength={ASSIGNMENT_LESSON_MAX} disabled={busy} value={draft.lesson} onChange={event => set({ lesson: event.target.value })} />
            <FieldError id={`${id}-lesson`} errors={shown.lesson} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-opens`}>Mở nhận bài (giờ Việt Nam, UTC+7)</label>
            <input {...fieldProps(`${id}-opens`, shown.opensLocal)} type="datetime-local" required disabled={busy || locked} value={draft.opensLocal} onChange={event => set({ opensLocal: event.target.value })} />
            <FieldError id={`${id}-opens`} errors={shown.opensLocal} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-closes`}>Đóng nhận bài (giờ Việt Nam, UTC+7)</label>
            <input {...fieldProps(`${id}-closes`, shown.closesLocal)} type="datetime-local" required disabled={busy || locked} value={draft.closesLocal} onChange={event => set({ closesLocal: event.target.value })} />
            <FieldError id={`${id}-closes`} errors={shown.closesLocal} />
        </div>
        <SessionSelect id={`${id}-session`} classId={classId} value={draft.sessionId} disabled={busy || locked} errors={shown.sessionId} onChange={value => set({ sessionId: value })} />
        {create && <label className="check-row" htmlFor={`${id}-draft`}>
            <input id={`${id}-draft`} type="checkbox" disabled={busy} checked={draft.saveAsDraft} onChange={event => set({ saveAsDraft: event.target.checked })} />
            Lưu nháp, công bố sau (học viên chưa thấy)
        </label>}
        {children}
        <div className="academic-actions">
            <button className="primary" disabled={busy}>{busy ? "Đang lưu…" : draft.saveAsDraft && create ? "Lưu nháp" : submitLabel}</button>
            {onCancel && <button type="button" className="secondary" disabled={busy} onClick={onCancel}>Hủy</button>}
        </div>
    </form>;
}

function AssignmentCreate({ classId }: { classId: string }) {
    const action = useAction();
    const navigate = useNavigate();
    return <section className="panel">
        <h2>Giao bài mới</h2>
        <p>Giáo viên chỉ giao bài cho lớp phụ trách. Mặc định công bố ngay; chọn “Lưu nháp” để công bố sau.</p>
        <AssignmentForm id="new-assignment" classId={classId} initial={emptyAssignmentDraft()} locked={false} create busy={action.busy} submitLabel="Công bố bài tập"
            onSubmit={draft => void action.run(signal => createAssignment(classId, draft, signal), item => navigate(`/assignments/${item.id}`))}>
            <ErrorMessage error={action.error} />
        </AssignmentForm>
    </section>;
}

export function AssignmentDetails() {
    const { id = "" } = useParams();
    const item = useRead<Assignment>(isAcademicId(id) ? `/academic/assignments/${id}` : null);
    const [changed, setChanged] = useState<Assignment | null>(null);
    const role = identity(getSession())!.role;
    const current = changed ?? item.data;
    const reload = () => { setChanged(null); item.reload(); };
    return <>
        <PageHeading title={current?.title ?? "Chi tiết bài tập"} subtitle="Bài nộp được lưu sau khi tất cả file đã tải lên và xác nhận thành công." />
        <Link className="secondary inline" to={current ? `/assignments?classId=${encodeURIComponent(current.classId)}` : "/assignments"}>Về danh sách bài tập</Link>
        {!isAcademicId(id) && <ErrorMessage error={new Error("ID bài tập không hợp lệ.")} />}
        {item.loading && <p role="status">Đang tải bài tập…</p>}
        <ErrorMessage error={item.error} />
        {item.error ? <button className="secondary" onClick={item.reload}>Thử tải lại</button> : null}
        {current && <>
            <section className="panel"><h2>Thông tin bài tập</h2>
                <p><AssignmentBadges item={current} /></p>
                {current.isLegacy && <p className="notice">Bài tạo trước bản nâng cấp: học viên vẫn xem và nộp theo hạn cũ, không bị tính nghĩa vụ hay bỏ bài hồi tố.</p>}
                <p className="assignment-description">{current.description || "Không có hướng dẫn bổ sung."}</p>
                <dl className="academic-detail">
                    <dt>Unit / Lesson</dt><dd>{[current.unit, current.lesson].filter(Boolean).join(" · ") || "Chưa nhập"}</dd>
                    <dt>Buổi học</dt><dd>{current.sessionId ? <SessionLabel id={current.sessionId} /> : "Chưa gắn buổi"}</dd>
                    <dt>Mở nhận bài</dt><dd>{formatVnDateTime(current.opensAtUtc)} (UTC+7)</dd>
                    <dt>Đóng nhận bài</dt><dd>{formatVnDateTime(current.closesAtUtc)} (UTC+7)</dd>
                    <dt>Công bố lúc</dt><dd>{current.publishedAt ? `${formatVnDateTime(current.publishedAt)} (UTC+7)` : current.isLegacy ? "Bài cũ, hiển thị sẵn" : "Chưa công bố"}</dd>
                    {role !== "Student" && <><dt>ID lớp</dt><dd><code>{current.classId}</code></dd><dt>Phiên bản</dt><dd>{current.version}</dd></>}
                </dl>
            </section>
            {(isExamKind(current.kind) || current.promptFileName) && <AssignmentPrompt key={`${current.id}:${current.status}:${current.version}`} assignment={current} role={role} />}
            {role !== "Student" && <AssignmentManage item={current} onChanged={setChanged} onReload={reload} />}
            {role === "Student" ? <StudentSubmission assignment={current} /> : <SubmissionReview assignmentId={id} role={role} />}
        </>}
    </>;
}

// Đề thi PDF: học viên chỉ lấy được link khi bài đang mở; giáo viên/nhân viên bất cứ lúc nào. Máy chủ kiểm tra lại mọi lần.
function AssignmentPrompt({ assignment, role }: { assignment: Assignment; role: import("./api").Role }) {
    const action = useAction();
    const [link, setLink] = useState<{ url: string; expiresAt: string } | null>(null);
    const allowed = canDownloadPrompt(assignment, role);
    const label = isExamKind(assignment.kind) ? "đề thi" : "đề bài";
    return <section className="panel"><h2>{isExamKind(assignment.kind) ? "Đề thi" : "Đề bài PDF"}</h2>
        <p>{assignment.promptFileName ?? "Bài thi chưa có đề."}</p>
        {!allowed && role === "Student" && <p className="notice" role="status">Chỉ tải được {label} khi bài đang mở (hiện: {assignmentStatusLabel(assignment.status)}).</p>}
        {allowed && <button type="button" className="secondary" disabled={action.busy} onClick={() => {
            setLink(null);
            void action.run(async signal => {
                const ticket = await promptDownload(assignment.id, signal);
                return { ...ticket, url: safeDownloadUrl(ticket) };
            }, setLink);
        }}>{action.busy ? "Đang lấy link…" : `Lấy link tải ${label}`}</button>}
        <ErrorMessage error={action.error} />
        {link && <p role="status"><a href={link.url} target="_blank" rel="noopener noreferrer">Tải {label} (mở tab mới)</a> · Link hết hạn {new Date(link.expiresAt).toLocaleTimeString("vi-VN")}</p>}
    </section>;
}

function AssignmentManage({ item, onChanged, onReload }: { item: Assignment; onChanged: (value: Assignment) => void; onReload: () => void }) {
    const [editing, setEditing] = useState(false);
    const [notice, setNotice] = useState("");
    const publish = useAction(); const save = useAction();
    const busy = publish.busy || save.busy;
    return <section className="panel"><h2>Quản lý bài tập</h2>
        {item.status === "Draft" && <>
            <p>Bài đang là nháp: học viên chưa thấy. Công bố sẽ chốt danh sách học viên hiện có quyền lớp và gửi thông báo.</p>
            {!item.sessionId && <p className="notice" role="status">Bài chưa gắn buổi học. Chọn “Sửa bài tập” để gắn buổi của lớp rồi mới công bố được.</p>}
            <button type="button" className="primary" disabled={busy || !item.sessionId} onClick={() => { setNotice(""); void publish.run(() => publishAssignment(item), value => { onChanged(value); setNotice("Đã công bố bài tập."); }); }}>
                {publish.busy ? "Đang công bố…" : "Công bố bài tập"}
            </button>
            <Conflict error={publish.error} text="Không công bố được: bài đã thay đổi hoặc chưa đủ điều kiện (ví dụ cần gắn buổi học trước khi công bố)." onReload={onReload} />
        </>}
        <div className="academic-actions">
            <button type="button" className="secondary" aria-expanded={editing} disabled={busy} onClick={() => { setEditing(value => !value); setNotice(""); }}>{editing ? "Đóng form sửa" : "Sửa bài tập"}</button>
        </div>
        {editing && <AssignmentForm key={item.version} id="edit-assignment" classId={item.classId} locked={isAssignmentLocked(item)} create={false} busy={save.busy} submitLabel="Lưu thay đổi"
            initial={{ title: item.title, description: item.description, kind: item.kind, unit: item.unit ?? "", lesson: item.lesson ?? "", opensLocal: utcToVnLocal(item.opensAtUtc), closesLocal: utcToVnLocal(item.closesAtUtc), sessionId: item.sessionId ?? "", saveAsDraft: false, promptFile: null }}
            promptName={item.promptFileName}
            onCancel={() => setEditing(false)}
            onSubmit={draft => void save.run(signal => updateAssignment(item, draft, signal), value => { onChanged(value); setEditing(false); setNotice("Đã lưu thay đổi."); })}>
            <Conflict error={save.error} text="Không lưu được: bài đã thay đổi hoặc không cho phép sửa trường này sau khi công bố." onReload={onReload} />
        </AssignmentForm>}
        <p role="status">{notice}</p>
    </section>;
}

function StudentSubmission({ assignment }: { assignment: Assignment }) {
    const path = `/academic/assignments/${assignment.id}/submissions/mine`;
    const existing = useRead<Submission>(path, true);
    const [saved, setSaved] = useState<Submission | null>(null);
    const [files, setFiles] = useState<File[]>([]);
    const [draftAnswer, setDraftAnswer] = useState<string | null>(null);
    const [progress, setProgress] = useState<Record<number, UploadStatus>>({});
    const [notice, setNotice] = useState("");
    const input = useRef<HTMLInputElement>(null);
    const action = useAction(); const reload = useAction();
    const current = saved ?? existing.data;
    const graded = !!current && isGraded(current);
    const exam = isExamKind(assignment.kind);
    const answer = exam ? "" : draftAnswer ?? current?.answerText ?? "";
    const error = !files.length && answer.trim() ? null : validateSubmissionFiles(files);
    const conflict = action.error instanceof ApiError && action.error.status === 409;
    const [acknowledgedError, setAcknowledgedError] = useState<unknown>(null);
    const conflictBlocked = conflict && acknowledgedError !== action.error;
    const pending = action.busy || reload.busy;
    const locked = existing.loading || !!existing.error || conflictBlocked || graded || assignment.status !== "Open";
    const statusText = { uploading: "Đang tải", confirming: "Đang xác nhận", confirmed: "Đã xác nhận" };
    return <section className="panel">
        <h2>Bài nộp của tôi</h2>
        {existing.loading && <p role="status">Đang tải bài nộp hiện tại…</p>}
        <ErrorMessage error={existing.error} />
        {!existing.loading && !existing.error && !current && <p>Bạn chưa nộp bài.</p>}
        {current && <SubmissionAttachments key={`${current.id}:${current.revision}`} submission={current} />}
        {current?.answerText && <div><h3>Câu trả lời đã nộp</h3><p className="assignment-description written-answer">{current.answerText}</p></div>}
        {current && <div aria-label="Điểm và nhận xét">
            <h3>Điểm và nhận xét</h3>
            <p>Điểm: <strong>{formatScore(current.score)}</strong>{graded && " / 10"}{graded && current.gradedAt && <> · Chấm lúc {formatVnDateTime(current.gradedAt)} (UTC+7)</>}</p>
            {graded && (current.feedback ? <p className="assignment-description">Nhận xét: {current.feedback}</p> : <p className="small muted">Giáo viên chưa ghi nhận xét.</p>)}
            {graded && <p className="notice" role="status">Bài đã được chấm nên không thể nộp lại.</p>}
        </div>}
        <button type="button" className="secondary" disabled={pending} onClick={() => {
            void reload.run(async signal => {
                try { return await api<Submission>(path, { signal }); }
                catch (failure) { if (failure instanceof ApiError && failure.status === 404) return null; throw failure; }
            }, value => { setSaved(value); setAcknowledgedError(action.error); existing.reload(); setNotice("Đã tải lại bản hiện tại. Hãy kiểm tra trước khi nộp thay thế."); });
        }}>{reload.busy ? "Đang tải…" : "Tải lại bài hiện tại"}</button>
        <ErrorMessage error={reload.error} />
        {assignment.status !== "Open" && <p className="notice">Chỉ nộp được trong thời gian nhận bài.</p>}
        {!graded && assignment.status === "Open" && <><p id="submission-help">{exam ? "Chọn 1–5 file." : "Nhập câu trả lời, đính kèm tối đa 5 file, hoặc cả hai."} Tối đa 20 MiB/file, tổng 50 MiB. PDF, DOCX, MP3, M4A, JPG, PNG. Nếu nộp lại, hãy chọn lại mọi file muốn giữ.</p>
        <form className="academic-form" aria-busy={pending} onSubmit={event => {
            event.preventDefault(); if (error || pending || locked) return;
            setNotice(""); setProgress({});
            void action.run(signal => submitAssignment(assignment.id, files, current?.revision ?? null, (index, status) => { if (!signal.aborted) setProgress(value => ({ ...value, [index]: status })); }, signal, answer), value => {
                setSaved(value); setDraftAnswer(null); setFiles([]); setProgress({}); if (input.current) input.current.value = "";
                setNotice("Đã lưu bài nộp.");
            });
        }}>
            {!exam && <div className="academic-field">
                <label htmlFor="submission-answer">Câu trả lời trực tiếp</label>
                <textarea id="submission-answer" rows={8} maxLength={ANSWER_MAX} aria-describedby="submission-answer-limit submission-help" disabled={pending || existing.loading} value={answer} onChange={event => { setDraftAnswer(event.target.value); setNotice(""); }} />
                <p className="small muted" id="submission-answer-limit">{answer.length.toLocaleString("vi-VN")} / {ANSWER_MAX.toLocaleString("vi-VN")} ký tự</p>
            </div>}
            <label htmlFor="submission-files">Chọn file cho bài nộp mới</label>
            <input ref={input} id="submission-files" type="file" multiple accept={submissionAccept} aria-describedby="submission-help submission-validation" disabled={pending || graded} onChange={event => { setFiles(Array.from(event.target.files ?? [])); event.target.value = ""; setProgress({}); setNotice(""); }} />
            <ul className="submission-list" aria-live="polite">{files.map((file, index) => <li key={`${index}:${file.name}`}>
                <span>{file.name} · {(file.size / 1024 / 1024).toFixed(2)} MiB {progress[index] ? `· ${statusText[progress[index]]}` : ""}</span>
                <button type="button" className="secondary" disabled={pending} aria-label={`Bỏ file ${file.name}`} onClick={() => { setFiles(value => value.filter((_, i) => i !== index)); setProgress({}); }}>Bỏ file</button>
            </li>)}</ul>
            <p id="submission-validation">{error ?? `Đã chọn ${files.length} file · ${(files.reduce((sum, file) => sum + file.size, 0) / 1024 / 1024).toFixed(2)} MiB`}</p>
            <ErrorMessage error={action.error} />
            {conflictBlocked && <p role="alert">Bản đã thay đổi hoặc ngoài giờ nộp. Tải lại bài đã lưu trước khi thử lại; câu trả lời đang nhập và file đang chọn vẫn được giữ.</p>}
            <button className="primary" disabled={pending || locked || !!error}>{action.busy ? "Đang lưu…" : current ? "Nộp lại bài" : "Nộp bài"}</button>
        </form></>}
        <p role="status">{notice}</p>
    </section>;
}

function SubmissionReview({ assignmentId, role }: { assignmentId: string; role: import("./api").Role }) {
    const [graded, setGraded] = useState("");
    const submissions = useRead<Submission[]>(`/academic/assignments/${assignmentId}/submissions${graded ? `?graded=${graded}` : ""}`);
    const [patched, setPatched] = useState<Record<string, Submission>>({});
    useEffect(() => setPatched({}), [submissions.data]);
    return <section className="panel"><h2>Bài nộp của học viên</h2>
        {!canGrade(role) && <p className="small muted">Chỉ giáo viên đúng lớp được chấm điểm. Bạn có thể xem bài nộp, điểm và lịch sử điểm.</p>}
        <div className="academic-filters">
            <div className="academic-field">
                <label htmlFor="submission-graded">Lọc theo điểm</label>
                <select id="submission-graded" value={graded} onChange={event => setGraded(event.target.value)}>
                    <option value="">Tất cả bài nộp</option><option value="false">Chưa chấm</option><option value="true">Đã chấm</option>
                </select>
            </div>
            <button className="secondary" disabled={submissions.loading} onClick={submissions.reload}>Tải lại danh sách</button>
        </div>
        {submissions.loading && <p role="status">Đang tải bài nộp…</p>}
        <ErrorMessage error={submissions.error} />
        {submissions.data?.length === 0 && <p>{graded ? "Không có bài nộp phù hợp." : "Chưa có học viên nộp bài."}</p>}
        {submissions.data?.map(original => <ReviewItem key={original.id} submission={patched[original.id] ?? original} role={role}
            onSaved={value => setPatched(previous => ({ ...previous, [value.id]: value }))} onReload={submissions.reload} />)}
        {submissions.data?.length === 200 && <p>Đang hiển thị tối đa 200 bài nộp.</p>}
    </section>;
}

function ReviewItem({ submission, role, onSaved, onReload }: { submission: Submission; role: import("./api").Role; onSaved: (value: Submission) => void; onReload: () => void }) {
    const [notice, setNotice] = useState("");
    const graded = isGraded(submission);
    const name = studentLabel(submission);
    return <article className="submission-review"><h3>{name ? <>Học viên {name}</> : <>Học viên <code>{submission.studentId}</code></>}</h3>
        {submission.answerText && <p className="assignment-description written-answer">{submission.answerText}</p>}
        <SubmissionAttachments key={`${submission.id}:${submission.revision}`} submission={submission} />
        <p>Điểm hiện tại: <strong>{formatScore(submission.score)}</strong>{graded && <> / 10 · phiên bản {submission.gradeVersion}{submission.gradedAt && <> · Chấm lúc {formatVnDateTime(submission.gradedAt)} (UTC+7)</>}</>}</p>
        {graded && submission.feedback && <p className="assignment-description">Nhận xét: {submission.feedback}</p>}
        {canGrade(role) && <GradeForm key={`${submission.revision}:${submission.gradeVersion}`} submission={submission} onReload={onReload}
            onSaved={value => { onSaved(value); setNotice("Đã lưu điểm."); }} />}
        <p role="status">{notice}</p>
        {graded && <GradeHistory key={submission.gradeVersion} submissionId={submission.id} />}
    </article>;
}

function GradeForm({ submission, onSaved, onReload }: { submission: Submission; onSaved: (value: Submission) => void; onReload: () => void }) {
    const [draft, setDraft] = useState<GradeDraft>({ score: scoreInput(submission.score), feedback: submission.feedback ?? "" });
    const [attempted, setAttempted] = useState(false);
    const action = useAction();
    const errors = validateGradeDraft(draft);
    const shown = attempted ? errors : {};
    const id = `grade-${submission.id}`;
    const graded = isGraded(submission);
    return <form className="academic-form" noValidate aria-busy={action.busy} onSubmit={event => {
        event.preventDefault(); setAttempted(true);
        if (action.busy || Object.keys(errors).length) return;
        void action.run(() => gradeSubmission(submission, draft), onSaved);
    }}>
        <h4>{graded ? "Sửa điểm" : "Chấm điểm"}</h4>
        <div className="academic-field">
            <label htmlFor={`${id}-score`}>Điểm (0–10, bước 0,1)</label>
            <input {...fieldProps(`${id}-score`, shown.score)} inputMode="decimal" autoComplete="off" required disabled={action.busy} value={draft.score} onChange={event => setDraft({ ...draft, score: event.target.value })} />
            <FieldError id={`${id}-score`} errors={shown.score} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-feedback`}>Nhận xét (tối đa {FEEDBACK_MAX} ký tự)</label>
            <textarea {...fieldProps(`${id}-feedback`, shown.feedback)} maxLength={FEEDBACK_MAX} disabled={action.busy} value={draft.feedback} onChange={event => setDraft({ ...draft, feedback: event.target.value })} />
            <FieldError id={`${id}-feedback`} errors={shown.feedback} />
        </div>
        <Conflict error={action.error} text="Điểm đã thay đổi hoặc học viên đã thay file sau khi bạn mở bài. Hãy tải lại để xem bản mới nhất rồi chấm lại." onReload={onReload} />
        <button className="primary" disabled={action.busy}>{action.busy ? "Đang lưu…" : graded ? "Lưu điểm mới" : "Lưu điểm"}</button>
    </form>;
}

function GradeHistory({ submissionId }: { submissionId: string }) {
    const [open, setOpen] = useState(false);
    return <div>
        <button type="button" className="secondary" aria-expanded={open} onClick={() => setOpen(value => !value)}>{open ? "Ẩn lịch sử điểm" : "Xem lịch sử điểm"}</button>
        {open && <GradeHistoryList submissionId={submissionId} />}
    </div>;
}

function GradeHistoryList({ submissionId }: { submissionId: string }) {
    const load = useCallback(() => listGradeHistory(submissionId), [submissionId]);
    const result = useLoad(load);
    return <>
        <ReadFeedback result={result} />
        {result.data?.length === 0 && <p>Chưa có lịch sử điểm.</p>}
        {result.data && <ul className="catalog-list" aria-label="Lịch sử điểm">{result.data.map(entry => <li key={entry.version}>
            <p><strong>Phiên bản {entry.version}</strong> · Điểm {formatScore(entry.score)} (trước đó: {entry.previousScore === null ? "chưa có" : formatScore(entry.previousScore)})</p>
            <p className="small muted">{entry.gradedByName ?? entry.gradedBy} · <time dateTime={entry.gradedAt}>{formatVnDateTime(entry.gradedAt)}</time> (UTC+7)</p>
            {entry.feedback ? <p className="assignment-description">Nhận xét: {entry.feedback}</p> : <p className="small muted">Không có nhận xét.</p>}
        </li>)}</ul>}
    </>;
}

function SubmissionAttachments({ submission }: { submission: Submission }) {
    const action = useAction();
    const [link, setLink] = useState<{ url: string; name: string; expiresAt: string } | null>(null);
    return <div><p>Nộp lúc {new Date(submission.submittedAt).toLocaleString("vi-VN")} · {submission.files.length} file</p>
        <ul className="submission-list">{submission.files.map(file => <li key={file.fileId}>
            <span>{file.fileName} · {(file.sizeBytes / 1024 / 1024).toFixed(2)} MiB</span>
            <button className="secondary" disabled={action.busy} onClick={() => {
                setLink(null);
                void action.run(async signal => {
                    const ticket = await api<{ url: string; expiresAt: string }>(`/academic/submissions/${submission.id}/files/${file.fileId}/download`, { signal });
                    const url = new URL(ticket.url);
                    if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) throw new Error("Đường dẫn tải không hợp lệ.");
                    return { ...ticket, url: url.href, name: file.fileName };
                }, setLink);
            }}>Lấy link tải {file.fileName}</button>
        </li>)}</ul>
        <ErrorMessage error={action.error} />
        {link && <p role="status"><a href={link.url} target="_blank" rel="noopener noreferrer">Tải {link.name} (mở tab mới)</a> · Link hết hạn {new Date(link.expiresAt).toLocaleTimeString("vi-VN")}</p>}
    </div>;
}

type Enrollment = { id: string; classId: string; studentId: string; isActive: boolean; status: string };
export function Enrollments() {
    const [params] = useSearchParams();
    const user = identity(getSession());
    if (!user) return null;
    if (user.role === "Student") return <MyEnrollments />;
    const requested = params.get("studentId") ?? "";
    const studentId = isAcademicId(requested) ? requested : "";
    return <ManageEnrollments key={studentId} initialStudentId={studentId} />;
}

function ManageEnrollments({ initialStudentId }: { initialStudentId: string }) {
    const [classId, setClassId] = useState(""); const [studentId, setStudentId] = useState(initialStudentId);
    const [search, setSearch] = useState({ q: "", page: 1 });
    const [notice, setNotice] = useState("");
    const [transferId, setTransferId] = useState("");
    const [targetClassId, setTargetClassId] = useState("");
    const action = useAction();
    const manager = canManageAcademic(identity(getSession())!.role);
    const validClass = manager && isAcademicId(classId);
    const loadRoster = useCallback(() => validClass ? api<Enrollment[]>(`/enrollment/memberships?classId=${classId}`) : Promise.resolve([]), [classId, validClass]);
    const loadStudents = useCallback(() => manager ? listManagedAccounts("students", search) : Promise.resolve(null), [manager, search]);
    const loadInitialStudent = useCallback(() => manager && initialStudentId ? getManagedStudent(initialStudentId) : Promise.resolve(null), [manager, initialStudentId]);
    const roster = useLoad(loadRoster);
    const students = useLoad(loadStudents);
    const initialStudent = useLoad(loadInitialStudent);
    if (!manager) return <><PageHeading title="Thành viên lớp" subtitle="Chỉ giáo vụ và quản trị viên quản lý thành viên lớp." /><p>Liên hệ giáo vụ để kiểm tra quyền học tập của bạn. Học viên không thể tự ghi danh qua trang này.</p></>;
    const options = students.data?.items ?? [];
    const linkedStudent = initialStudent.data;
    const candidates = linkedStudent && !options.some(item => item.id === linkedStudent.id) ? [linkedStudent, ...options] : options;
    const canEnroll = validClass && candidates.some(item => item.id === studentId && !item.isLocked);
    return <>
        <PageHeading title="Ghi danh và thành viên lớp" subtitle="Chọn lớp và học viên để ghi danh. Hóa đơn học phí được tạo theo mức phí của lớp; giáo vụ xác nhận khoản đã thu ở mục Hóa đơn." />
        <section className="panel"><h2>Ghi danh học viên</h2>
            <fieldset className="session-skills" disabled={action.busy}>
                <legend>Chọn lớp</legend>
                 <ClassSelect id="roster-class" value={classId} onChange={id => { setClassId(id); setNotice(""); setTransferId(""); setTargetClassId(""); }} />
            </fieldset>
            <form className="academic-form" onSubmit={event => {
                event.preventDefault();
                setStudentId(""); setSearch({ q: String(new FormData(event.currentTarget).get("search")).trim(), page: 1 });
            }}>
                <label htmlFor="roster-search">Tìm học viên theo tên, email hoặc mã</label>
                <input id="roster-search" name="search" type="search" maxLength={100} disabled={action.busy} />
                <button className="secondary" disabled={action.busy}>Tìm học viên</button>
            </form>
            <ReadFeedback result={students} />
            {initialStudentId && <ReadFeedback result={initialStudent} />}
            <form className="academic-form" onSubmit={event => {
                event.preventDefault(); if (!canEnroll) return;
                const form = new FormData(event.currentTarget);
                const body = { classId, studentId, maxAbsences: Number(form.get("maxAbsences")), maxLates: Number(form.get("maxLates")), maxSkippedAssignments: Number(form.get("maxSkippedAssignments")) };
                setNotice("");
                void action.run(signal => api<Enrollment>("/enrollment/enrollments", { method: "POST", body: JSON.stringify(body), signal }), () => {
                    setNotice("Đã ghi danh học viên."); roster.retry();
                });
            }}>
                <label htmlFor="roster-student">Học viên</label>
                <select id="roster-student" value={studentId} disabled={action.busy || students.loading || !students.data} onChange={event => setStudentId(event.target.value)} required>
                    <option value="">Chọn học viên</option>
                    {candidates.map(item => <option key={item.id} value={item.id} disabled={item.isLocked}>{item.fullName} · {item.studentCode ?? item.email}{item.isLocked ? " (đã khóa)" : ""}</option>)}
                </select>
                {students.data?.items.length === 0 && <p>Không tìm thấy học viên.</p>}
                <div className="academic-fields">
                    {([["maxAbsences", "Số buổi vắng tối đa"], ["maxLates", "Số lần đi muộn tối đa"], ["maxSkippedAssignments", "Số bài bỏ nộp tối đa"]] as const).map(([name, label]) =>
                        <div className="academic-field" key={name}><label htmlFor={name}>{label}</label><input id={name} name={name} type="number" min={0} max={2147483647} step={1} required disabled={action.busy} /></div>)}
                </div>
                <button className="primary" disabled={action.busy || !canEnroll}>Ghi danh</button>
            </form>
            {students.data && !action.busy && <Pager page={students.data.page} pageSize={students.data.pageSize} total={students.data.total}
                change={page => { setStudentId(""); setSearch(previous => ({ ...previous, page })); }} label="Phân trang học viên" />}
            <ErrorMessage error={action.error} /><p role="status">{action.busy ? "Đang xử lý…" : notice}</p>
        </section>
        {validClass && <section className="panel"><h2>Thành viên lớp đã chọn</h2>
            <ReadFeedback result={roster} />
            {roster.data?.length === 0 && <p>Chưa có thành viên.</p>}
            {!!roster.data?.length && <ul className="catalog-list">{roster.data.map(item => {
                const name = candidates.find(student => student.id === item.studentId)?.fullName ?? item.studentId;
                return <li key={item.id}>
                <p>{name} · {item.status === "Cancelled" ? "Đã chuyển lớp" : item.isActive ? "Đang hoạt động" : "Đã thu hồi"}</p>
                {item.status === "Active" && <div className="actions"><button className="secondary" disabled={action.busy} onClick={() => {
                    if (!window.confirm(item.isActive ? "Thu hồi quyền học tập của học viên này?" : "Cấp lại quyền học tập cho học viên này?")) return;
                    setNotice("");
                    void action.run(signal => item.isActive ? api<unknown>(`/enrollment/memberships/${item.id}`, { method: "DELETE", signal })
                        : api<unknown>("/enrollment/memberships", { method: "POST", body: JSON.stringify({ classId, studentId: item.studentId }), signal }), () => {
                        roster.retry(); setNotice(item.isActive ? "Đã thu hồi quyền học tập." : "Đã cấp lại quyền học tập.");
                    });
                }}>{item.isActive ? "Thu hồi quyền" : "Cấp lại quyền"}</button>
                <button className="secondary" aria-expanded={transferId === item.id} disabled={action.busy} onClick={() => {
                    setTransferId(transferId === item.id ? "" : item.id); setTargetClassId(""); setNotice("");
                }}>Chuyển lớp</button></div>}
                {transferId === item.id && item.status === "Active" && <form className="academic-form" onSubmit={event => {
                    event.preventDefault();
                    if (action.busy || !isAcademicId(targetClassId) || targetClassId === classId) return;
                    void action.run(signal => api<Enrollment>(`/enrollment/enrollments/${item.id}/transfer`, {
                        method: "POST", body: JSON.stringify({ targetClassId }), signal,
                    }), () => { setTransferId(""); setTargetClassId(""); setNotice("Đã chuyển lớp. Hóa đơn lớp cũ không thay đổi."); roster.retry(); });
                }}>
                    <fieldset className="session-skills" disabled={action.busy}>
                        <legend>Chuyển lớp cho {name}</legend>
                        <ClassSelect id="transfer-target" value={targetClassId} onChange={setTargetClassId} destinationOf={classId} />
                        <p id="transfer-policy">Giữ nguyên hóa đơn lớp cũ, kể cả chưa thu. Không lập hóa đơn mới, bù trừ hay hoàn tiền. Giáo vụ đối soát thủ công.</p>
                        <button className="primary" aria-describedby="transfer-policy" disabled={!targetClassId}>Xác nhận chuyển lớp</button>
                    </fieldset>
                </form>}
            </li>; })}</ul>}
            {roster.data?.length === 200 && <p>Đang hiển thị tối đa 200 thành viên.</p>}
        </section>}
    </>;
}
