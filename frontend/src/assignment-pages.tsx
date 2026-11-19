import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { api, ApiError, getSession, getSessionGeneration, identity } from "./api";
import { canManageAcademic, isAcademicId } from "./academic";
import { useRead as useLoad } from "./academic-pages";
import { allVisibleClasses, ReadFeedback } from "./attendance-pages";
import {
    assignmentKindLabel, assignmentKinds, assignmentKindLabels, assignmentStatuses, assignmentStatusLabel, assignmentStatusLabels,
    ASSIGNMENT_DESCRIPTION_MAX, ASSIGNMENT_LABEL_MAX, ASSIGNMENT_TITLE_MAX, canGrade, createAssignment, emptyAssignmentDraft, FEEDBACK_MAX,
    formatScore, gradeSubmission, isAssignmentLocked, isGraded, listClassAssignments, listGradeHistory, listMyAssignments, publishAssignment,
    scoreInput, studentScoreText, submissionStatuses, submissionStatusLabel, submissionStatusLabels, updateAssignment, validateAssignmentDraft,
    validateGradeDraft, type AssignmentDraft, type GradeDraft,
} from "./coursework";
import { formatVnDateTime, utcToVnLocal } from "./format";
import { listSessions } from "./schedule";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";
import { submissionAccept, submitAssignment, validateSubmissionFiles, type Assignment, type Submission, type UploadStatus } from "./submissions";

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

export function ClassSelect({ id, value, onChange, allLabel }: { id: string; value: string; onChange: (classId: string) => void; allLabel?: string }) {
    const classes = useLoad(allVisibleClasses);
    return <div className="academic-field">
        <label htmlFor={id}>Lớp học</label>
        <select id={id} value={value} disabled={classes.loading || !!classes.error} onChange={event => onChange(event.target.value)}>
            <option value="">{allLabel ?? "Chọn lớp học"}</option>
            {value && !classes.data?.some(item => item.id === value) && <option value={value}>Lớp đang chọn (máy chủ xác minh quyền)</option>}
            {classes.data?.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
        </select>
        <ReadFeedback result={classes} />
        {classes.data?.length === 0 && <p>Chưa có lớp nào trong phạm vi tài khoản của bạn.</p>}
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
                            <h3><Link className="accent" to={`/assignments/${entry.assignment.id}`}>{entry.assignment.title}</Link></h3>
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
            <form className="academic-filters" onSubmit={event => { event.preventDefault(); setFilter(previous => ({ ...previous, search: input.trim() })); }}>
                <ClassSelect id="assignment-class" value={classId} onChange={value => { const next = new URLSearchParams(params); if (value) next.set("classId", value); else next.delete("classId"); setParams(next); }} />
                <div className="academic-field">
                    <label htmlFor="assignment-search">Tìm theo tiêu đề, Unit hoặc Lesson</label>
                    <input id="assignment-search" type="search" maxLength={100} value={input} onChange={event => setInput(event.target.value)} />
                </div>
                <FilterSelect id="assignment-status" label="Trạng thái" value={filter.status} values={assignmentStatuses} labels={assignmentStatusLabels} all="Tất cả trạng thái" onChange={value => setFilter(previous => ({ ...previous, status: value }))} />
                <FilterSelect id="assignment-kind" label="Loại bài" value={filter.kind} values={assignmentKinds} labels={assignmentKindLabels} all="Tất cả loại" onChange={value => setFilter(previous => ({ ...previous, kind: value }))} />
                <button className="primary">Tìm bài tập</button>
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

function SessionSelect({ id, classId, value, disabled, onChange }: { id: string; classId: string; value: string; disabled: boolean; onChange: (value: string) => void }) {
    const [range] = useState(() => { const now = Date.now(); return { fromUtc: new Date(now - 30 * 86400000).toISOString(), toUtc: new Date(now + 60 * 86400000).toISOString() }; });
    const load = useCallback(() => listSessions({ classId, ...range }), [classId, range]);
    const sessions = useLoad(load);
    return <div className="academic-field">
        <label htmlFor={id}>Buổi học (tùy chọn)</label>
        <select id={id} value={value} disabled={disabled || sessions.loading} aria-describedby={`${id}-help`} onChange={event => onChange(event.target.value)}>
            <option value="">Không gắn buổi</option>
            {value && !sessions.data?.some(item => item.id === value) && <option value={value}>Buổi đang gắn</option>}
            {sessions.data?.map(item => <option key={item.id} value={item.id}>{item.lesson} · {formatVnDateTime(item.startsAtUtc)}</option>)}
        </select>
        <p id={`${id}-help`} className="small muted">Liệt kê buổi từ 30 ngày trước đến 60 ngày tới. Máy chủ chỉ nhận buổi khi tính năng gắn buổi đã bật; nếu chưa, hãy để trống.</p>
        <ReadFeedback result={sessions} />
    </div>;
}

function AssignmentForm({ id, classId, initial, locked, create, busy, children, submitLabel, onSubmit, onCancel }: {
    id: string; classId: string; initial: AssignmentDraft; locked: boolean; create: boolean; busy: boolean; children?: ReactNode;
    submitLabel: string; onSubmit: (draft: AssignmentDraft) => void; onCancel?: () => void;
}) {
    const [draft, setDraft] = useState(initial);
    const [attempted, setAttempted] = useState(false);
    const errors = validateAssignmentDraft(draft, locked);
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
        <div className="academic-field">
            <label htmlFor={`${id}-unit`}>Unit (tối đa {ASSIGNMENT_LABEL_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-unit`, shown.unit)} maxLength={ASSIGNMENT_LABEL_MAX} disabled={busy} value={draft.unit} onChange={event => set({ unit: event.target.value })} />
            <FieldError id={`${id}-unit`} errors={shown.unit} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-lesson`}>Lesson (tối đa {ASSIGNMENT_LABEL_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-lesson`, shown.lesson)} maxLength={ASSIGNMENT_LABEL_MAX} disabled={busy} value={draft.lesson} onChange={event => set({ lesson: event.target.value })} />
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
        <SessionSelect id={`${id}-session`} classId={classId} value={draft.sessionId} disabled={busy || locked} onChange={value => set({ sessionId: value })} />
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
            onSubmit={draft => void action.run(() => createAssignment(classId, draft), item => navigate(`/assignments/${item.id}`))}>
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
                    <dt>Buổi học</dt><dd>{current.sessionId ? <code>{current.sessionId}</code> : "Chưa gắn buổi"}</dd>
                    <dt>Mở nhận bài</dt><dd>{formatVnDateTime(current.opensAtUtc)} (UTC+7)</dd>
                    <dt>Đóng nhận bài</dt><dd>{formatVnDateTime(current.closesAtUtc)} (UTC+7)</dd>
                    <dt>Công bố lúc</dt><dd>{current.publishedAt ? `${formatVnDateTime(current.publishedAt)} (UTC+7)` : current.isLegacy ? "Bài cũ, hiển thị sẵn" : "Chưa công bố"}</dd>
                    {role !== "Student" && <><dt>ID lớp</dt><dd><code>{current.classId}</code></dd><dt>Phiên bản</dt><dd>{current.version}</dd></>}
                </dl>
            </section>
            {role !== "Student" && <AssignmentManage item={current} onChanged={setChanged} onReload={reload} />}
            {role === "Student" ? <StudentSubmission assignment={current} /> : <SubmissionReview assignmentId={id} role={role} />}
        </>}
    </>;
}

function AssignmentManage({ item, onChanged, onReload }: { item: Assignment; onChanged: (value: Assignment) => void; onReload: () => void }) {
    const [editing, setEditing] = useState(false);
    const [notice, setNotice] = useState("");
    const publish = useAction(); const save = useAction();
    const busy = publish.busy || save.busy;
    return <section className="panel"><h2>Quản lý bài tập</h2>
        {item.status === "Draft" && <>
            <p>Bài đang là nháp: học viên chưa thấy. Công bố sẽ chốt danh sách học viên hiện có quyền lớp và gửi thông báo.</p>
            <button type="button" className="primary" disabled={busy} onClick={() => { setNotice(""); void publish.run(() => publishAssignment(item), value => { onChanged(value); setNotice("Đã công bố bài tập."); }); }}>
                {publish.busy ? "Đang công bố…" : "Công bố bài tập"}
            </button>
            <Conflict error={publish.error} text="Không công bố được: bài đã thay đổi hoặc chưa đủ điều kiện (ví dụ cần gắn buổi học trước khi công bố)." onReload={onReload} />
        </>}
        <div className="academic-actions">
            <button type="button" className="secondary" aria-expanded={editing} disabled={busy} onClick={() => { setEditing(value => !value); setNotice(""); }}>{editing ? "Đóng form sửa" : "Sửa bài tập"}</button>
        </div>
        {editing && <AssignmentForm key={item.version} id="edit-assignment" classId={item.classId} locked={isAssignmentLocked(item)} create={false} busy={save.busy} submitLabel="Lưu thay đổi"
            initial={{ title: item.title, description: item.description, kind: item.kind, unit: item.unit ?? "", lesson: item.lesson ?? "", opensLocal: utcToVnLocal(item.opensAtUtc), closesLocal: utcToVnLocal(item.closesAtUtc), sessionId: item.sessionId ?? "", saveAsDraft: false }}
            onCancel={() => setEditing(false)}
            onSubmit={draft => void save.run(() => updateAssignment(item, draft), value => { onChanged(value); setEditing(false); setNotice("Đã lưu thay đổi."); })}>
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
    const [progress, setProgress] = useState<Record<number, UploadStatus>>({});
    const [notice, setNotice] = useState("");
    const input = useRef<HTMLInputElement>(null);
    const action = useAction(); const reload = useAction();
    const current = saved ?? existing.data;
    const graded = !!current && isGraded(current);
    const error = validateSubmissionFiles(files);
    const conflict = action.error instanceof ApiError && action.error.status === 409;
    const [acknowledgedError, setAcknowledgedError] = useState<unknown>(null);
    const conflictBlocked = conflict && acknowledgedError !== action.error;
    const pending = action.busy || reload.busy;
    const locked = existing.loading || !!existing.error || conflictBlocked || graded;
    const statusText = { uploading: "Đang tải", confirming: "Đang xác nhận", confirmed: "Đã xác nhận" };
    return <section className="panel">
        <h2>Bài nộp của tôi</h2>
        {existing.loading && <p role="status">Đang tải bài nộp hiện tại…</p>}
        <ErrorMessage error={existing.error} />
        {!existing.loading && !existing.error && !current && <p>Bạn chưa nộp bài.</p>}
        {current && <SubmissionAttachments key={`${current.id}:${current.revision}`} submission={current} />}
        {current && <div aria-label="Điểm và nhận xét">
            <h3>Điểm và nhận xét</h3>
            <p>Điểm: <strong>{formatScore(current.score)}</strong>{graded && " / 10"}{graded && current.gradedAt && <> · Chấm lúc {formatVnDateTime(current.gradedAt)} (UTC+7)</>}</p>
            {graded && (current.feedback ? <p className="assignment-description">Nhận xét: {current.feedback}</p> : <p className="small muted">Giáo viên chưa ghi nhận xét.</p>)}
            {graded && <p className="notice" role="status">Bài đã được chấm nên không thể nộp thay file.</p>}
        </div>}
        <button type="button" className="secondary" disabled={pending} onClick={() => {
            void reload.run(async signal => {
                try { return await api<Submission>(path, { signal }); }
                catch (failure) { if (failure instanceof ApiError && failure.status === 404) return null; throw failure; }
            }, value => { setSaved(value); setAcknowledgedError(action.error); existing.reload(); setNotice("Đã tải lại bản hiện tại. Hãy kiểm tra trước khi nộp thay thế."); });
        }}>{reload.busy ? "Đang tải…" : "Tải lại bài hiện tại"}</button>
        <ErrorMessage error={reload.error} />
        <p id="submission-help">1–5 file; tối đa 20 MiB/file, tổng 50 MiB. PDF, DOCX, MP3, M4A, JPG, PNG. Nộp lại sẽ thay toàn bộ file đính kèm; file cũ chỉ được thay khi lưu thành công. Giới hạn máy chủ có thể thấp hơn.</p>
        <form className="academic-form" aria-busy={pending} onSubmit={event => {
            event.preventDefault(); if (error || pending || locked) return;
            setNotice(""); setProgress({});
            void action.run(signal => submitAssignment(assignment.id, files, current?.revision ?? null, (index, status) => { if (!signal.aborted) setProgress(value => ({ ...value, [index]: status })); }, signal), value => {
                setSaved(value); setFiles([]); setProgress({}); if (input.current) input.current.value = "";
                setNotice("Đã lưu bài nộp cùng toàn bộ file đính kèm.");
            });
        }}>
            <label htmlFor="submission-files">Chọn file cho bài nộp mới</label>
            <input ref={input} id="submission-files" type="file" multiple accept={submissionAccept} aria-describedby="submission-help submission-validation" disabled={pending || graded} onChange={event => { setFiles(Array.from(event.target.files ?? [])); event.target.value = ""; setProgress({}); setNotice(""); }} />
            <ul className="submission-list" aria-live="polite">{files.map((file, index) => <li key={`${index}:${file.name}`}>
                <span>{file.name} · {(file.size / 1024 / 1024).toFixed(2)} MiB {progress[index] ? `· ${statusText[progress[index]]}` : ""}</span>
                <button type="button" className="secondary" disabled={pending} aria-label={`Bỏ file ${file.name}`} onClick={() => { setFiles(value => value.filter((_, i) => i !== index)); setProgress({}); }}>Bỏ file</button>
            </li>)}</ul>
            <p id="submission-validation">{error ?? `Đã chọn ${files.length} file · ${(files.reduce((sum, file) => sum + file.size, 0) / 1024 / 1024).toFixed(2)} MiB`}</p>
            <ErrorMessage error={action.error} />
            {conflictBlocked && <p role="alert">Máy chủ từ chối lưu (bản đã thay đổi hoặc ngoài giờ nộp). Tải lại bài hiện tại trước khi thử lại; file đang chọn vẫn được giữ.</p>}
            <button className="primary" disabled={pending || locked || !!error}>{action.busy ? "Đang tải và lưu…" : current ? "Nộp thay thế toàn bộ file" : "Nộp bài"}</button>
        </form>
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
    return <article className="submission-review"><h3>Học viên <code>{submission.studentId}</code></h3>
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

type Enrollment = { id: string; classId: string; studentId: string; isActive: boolean };
export function Enrollments() {
    const [classId, setClassId] = useState(""); const [studentId, setStudentId] = useState("");
    const [items, setItems] = useState<Enrollment[] | null>(null); const [loadedClass, setLoadedClass] = useState("");
    const [notice, setNotice] = useState("");
    const action = useAction();
    const manager = canManageAcademic(identity(getSession())!.role);
    if (!manager) return <><PageHeading title="Thành viên lớp" subtitle="Chỉ giáo vụ và quản trị viên quản lý thành viên lớp." /><p>Liên hệ giáo vụ để kiểm tra quyền học tập của bạn. Học viên không thể tự ghi danh qua trang này.</p></>;
    const validClass = isAcademicId(classId.trim());
    return <>
        <PageHeading title="Thành viên lớp" subtitle="Cấp hoặc thu hồi quyền truy cập bài tập cho học viên đã ghi danh. Trang này không tạo ghi danh, hóa đơn hay thay đổi sĩ số." />
        <section className="panel"><h2>Tra cứu theo lớp</h2>
            <form className="academic-form" onSubmit={event => {
                event.preventDefault(); if (!validClass) return;
                const id = classId.trim(); setNotice("");
                void action.run(signal => api<Enrollment[]>(`/enrollment/memberships?classId=${id}`, { signal }), value => { setItems(value); setLoadedClass(id); });
            }}>
                <label htmlFor="roster-class">ID lớp (UUID)</label><input id="roster-class" value={classId} disabled={action.busy} onChange={event => { setClassId(event.target.value); setItems(null); setLoadedClass(""); }} required />
                <button className="secondary" disabled={action.busy || !validClass}>Tải thành viên</button>
            </form>
            <h2>Cấp quyền học tập</h2>
            <form className="academic-form" onSubmit={event => {
                event.preventDefault(); if (!validClass || !isAcademicId(studentId.trim())) return;
                const id = classId.trim(); setNotice("");
                void action.run(signal => api<Enrollment>("/enrollment/memberships", { method: "POST", body: JSON.stringify({ classId: id, studentId: studentId.trim() }), signal }), value => {
                    setItems(previous => loadedClass === id && previous ? [...previous.filter(item => item.id !== value.id), value] : [value]); setLoadedClass(id);
                    setStudentId(""); setNotice("Đã cấp quyền học tập. Bấm Tải thành viên để xem toàn bộ danh sách.");
                });
            }}>
                <label htmlFor="roster-student">ID tài khoản học viên (UUID)</label><input id="roster-student" value={studentId} disabled={action.busy} onChange={event => setStudentId(event.target.value)} required />
                <p>Học viên phải có ghi danh còn hiệu lực trong lớp. Nếu chưa có, giáo vụ cần hoàn tất ghi danh với học phí và giới hạn vi phạm trước khi cấp quyền học tập.</p>
                <button className="primary" disabled={action.busy || !validClass || !isAcademicId(studentId.trim())}>Cấp quyền cho lớp ở trên</button>
            </form>
            <ErrorMessage error={action.error} /><p role="status">{action.busy ? "Đang xử lý…" : notice}</p>
            {items?.length === 0 && <p>Chưa có thành viên.</p>}
            {items && <ul className="submission-list">{items.map(item => <li key={item.id}>
                <span><code>{item.studentId}</code> · {item.isActive ? "Đang hoạt động" : "Đã thu hồi"}</span>
                {item.isActive && <button className="secondary" disabled={action.busy} onClick={() => {
                    if (!window.confirm(`Thu hồi quyền học tập của ${item.studentId}?`)) return;
                    setNotice("");
                    void action.run(signal => api<void>(`/enrollment/memberships/${item.id}`, { method: "DELETE", signal }), () => { setItems(previous => previous?.map(value => value.id === item.id ? { ...value, isActive: false } : value) ?? null); setNotice("Đã thu hồi quyền học tập."); });
                }}>Thu hồi quyền</button>}
            </li>)}</ul>}
            {items?.length === 200 && <p>Đang hiển thị tối đa 200 thành viên.</p>}
        </section>
    </>;
}
