import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ApiError, getSession, getSessionGeneration, identity } from "./api";
import { canManageAcademic, isAcademicId } from "./academic";
import { ErrorMessage, PageHeading } from "./ui";
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

export function Assignments() {
    const [input, setInput] = useState("");
    const [classId, setClassId] = useState<string | null>(null);
    const user = identity(getSession())!;
    const items = useRead<Assignment[]>(classId ? `/academic/assignments?classId=${encodeURIComponent(classId)}` : null);
    return <>
        <PageHeading title="Bài tập" subtitle="Tra cứu theo ID lớp do giáo vụ cung cấp. Chỉ thành viên và giáo viên phụ trách được truy cập." />
        <section className="panel">
            <form className="academic-form" onSubmit={event => { event.preventDefault(); if (isAcademicId(input.trim())) setClassId(input.trim()); }}>
                <label htmlFor="assignment-class">ID lớp (UUID)</label>
                <input id="assignment-class" value={input} onChange={event => setInput(event.target.value)} required aria-describedby="assignment-class-help" />
                <p id="assignment-class-help">Nhập UUID khác rỗng, không phải mã lớp như IELTS-01.</p>
                <button className="primary" disabled={!isAcademicId(input.trim()) || items.loading}>Tra cứu bài tập</button>
            </form>
            {items.loading && <p role="status">Đang tải bài tập…</p>}
            <ErrorMessage error={items.error} />
            {items.error ? <button className="secondary" onClick={items.reload}>Thử tải lại</button> : null}
            {items.data?.length === 0 && <p>Lớp này chưa có bài tập.</p>}
            {items.data && <ul className="submission-list">{items.data.map(item => <li key={item.id}>
                <Link to={`/assignments/${item.id}`}>{item.title}</Link>
                <p>Hạn nộp: {new Date(item.closesAtUtc).toLocaleString("vi-VN")}</p>
            </li>)}</ul>}
            {items.data?.length === 200 && <p>Đang hiển thị tối đa 200 bài tập.</p>}
        </section>
        {user.role !== "Student" && <AssignmentCreate key={classId ?? "new"} initialClassId={classId ?? ""} />}
    </>;
}

function AssignmentCreate({ initialClassId }: { initialClassId: string }) {
    const [draft, setDraft] = useState({ classId: initialClassId, title: "", description: "", opensAtUtc: "", closesAtUtc: "" });
    const action = useAction();
    const navigate = useNavigate();
    const opens = new Date(draft.opensAtUtc).getTime(); const closes = new Date(draft.closesAtUtc).getTime();
    const valid = isAcademicId(draft.classId.trim()) && draft.title.trim().length > 0 && Number.isFinite(opens) && Number.isFinite(closes) && opens < closes;
    return <section className="panel">
        <h2>Giao bài mới</h2>
        <p>Giáo viên chỉ giao bài cho lớp phụ trách. Lưu sẽ công bố ngay; giờ nhập theo múi giờ thiết bị.</p>
        <form className="academic-form" aria-busy={action.busy} onSubmit={event => {
            event.preventDefault(); if (!valid) return;
            void action.run(signal => api<Assignment>("/academic/assignments", { method: "POST", signal, body: JSON.stringify({ ...draft, classId: draft.classId.trim(), opensAtUtc: new Date(opens).toISOString(), closesAtUtc: new Date(closes).toISOString() }) }), item => navigate(`/assignments/${item.id}`));
        }}>
            {([['classId', 'ID lớp (UUID)', 'text'], ['title', 'Tiêu đề', 'text'], ['opensAtUtc', 'Mở nhận bài', 'datetime-local'], ['closesAtUtc', 'Đóng nhận bài', 'datetime-local']] as const).map(([key, label, type]) => <div className="academic-field" key={key}>
                <label htmlFor={`new-assignment-${key}`}>{label}</label>
                <input id={`new-assignment-${key}`} type={type} value={draft[key]} required disabled={action.busy} maxLength={key === 'title' ? 200 : undefined} onChange={event => setDraft({ ...draft, [key]: event.target.value })} />
            </div>)}
            <label htmlFor="assignment-description">Hướng dẫn</label>
            <textarea id="assignment-description" value={draft.description} maxLength={5000} disabled={action.busy} onChange={event => setDraft({ ...draft, description: event.target.value })} />
            <p>Giờ đóng nhận bài phải sau giờ mở nhận bài.</p>
            <ErrorMessage error={action.error} />
            <button className="primary" disabled={action.busy || !valid}>{action.busy ? "Đang công bố…" : "Công bố bài tập"}</button>
        </form>
    </section>;
}

export function AssignmentDetails() {
    const { id = "" } = useParams();
    const item = useRead<Assignment>(isAcademicId(id) ? `/academic/assignments/${id}` : null);
    const student = identity(getSession())!.role === "Student";
    return <>
        <PageHeading title={item.data?.title ?? "Chi tiết bài tập"} subtitle="Bài nộp được lưu sau khi tất cả file đã tải lên và xác nhận thành công." />
        <Link className="secondary inline" to="/assignments">Về danh sách bài tập</Link>
        {!isAcademicId(id) && <ErrorMessage error={new Error("ID bài tập không hợp lệ.")} />}
        {item.loading && <p role="status">Đang tải bài tập…</p>}
        <ErrorMessage error={item.error} />
        {item.error ? <button className="secondary" onClick={item.reload}>Thử tải lại</button> : null}
        {item.data && <>
            <section className="panel"><h2>Thông tin bài tập</h2>
                <p className="assignment-description">{item.data.description || "Không có hướng dẫn bổ sung."}</p>
                <p>ID lớp: <code>{item.data.classId}</code></p>
                <p>Mở: {new Date(item.data.opensAtUtc).toLocaleString("vi-VN")} · Đóng: {new Date(item.data.closesAtUtc).toLocaleString("vi-VN")}</p>
            </section>
            {student ? <StudentSubmission assignment={item.data} /> : <SubmissionReview assignmentId={id} />}
        </>}
    </>;
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
    const error = validateSubmissionFiles(files);
    const conflict = action.error instanceof ApiError && action.error.status === 409;
    const [acknowledgedError, setAcknowledgedError] = useState<unknown>(null);
    const conflictBlocked = conflict && acknowledgedError !== action.error;
    const pending = action.busy || reload.busy;
    const locked = existing.loading || !!existing.error || conflictBlocked;
    const statusText = { uploading: "Đang tải", confirming: "Đang xác nhận", confirmed: "Đã xác nhận" };
    return <section className="panel">
        <h2>Bài nộp của tôi</h2>
        {existing.loading && <p role="status">Đang tải bài nộp hiện tại…</p>}
        <ErrorMessage error={existing.error} />
        {!existing.loading && !existing.error && !current && <p>Bạn chưa nộp bài.</p>}
        {current && <SubmissionAttachments key={`${current.id}:${current.revision}`} submission={current} />}
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
            <input ref={input} id="submission-files" type="file" multiple accept={submissionAccept} aria-describedby="submission-help submission-validation" disabled={pending} onChange={event => { setFiles(Array.from(event.target.files ?? [])); event.target.value = ""; setProgress({}); setNotice(""); }} />
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

function SubmissionReview({ assignmentId }: { assignmentId: string }) {
    const submissions = useRead<Submission[]>(`/academic/assignments/${assignmentId}/submissions`);
    return <section className="panel"><h2>Bài nộp của học viên</h2>
        <button className="secondary" disabled={submissions.loading} onClick={submissions.reload}>Tải lại danh sách</button>
        {submissions.loading && <p role="status">Đang tải bài nộp…</p>}
        <ErrorMessage error={submissions.error} />
        {submissions.data?.length === 0 && <p>Chưa có học viên nộp bài.</p>}
        {submissions.data?.map(item => <article className="submission-review" key={item.id}><h3>Học viên <code>{item.studentId}</code></h3><SubmissionAttachments key={`${item.id}:${item.revision}`} submission={item} /></article>)}
        {submissions.data?.length === 200 && <p>Đang hiển thị tối đa 200 bài nộp.</p>}
    </section>;
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
        <PageHeading title="Thành viên lớp" subtitle="Cấp hoặc thu hồi quyền truy cập bài tập. Đây chưa phải quy trình ghi danh có học phí, kiểm tra sĩ số hay chuyển lớp." />
        <section className="panel"><h2>Tra cứu theo lớp</h2>
            <form className="academic-form" onSubmit={event => {
                event.preventDefault(); if (!validClass) return;
                const id = classId.trim(); setNotice("");
                void action.run(signal => api<Enrollment[]>(`/enrollment/enrollments?classId=${id}`, { signal }), value => { setItems(value); setLoadedClass(id); });
            }}>
                <label htmlFor="roster-class">ID lớp (UUID)</label><input id="roster-class" value={classId} disabled={action.busy} onChange={event => { setClassId(event.target.value); setItems(null); setLoadedClass(""); }} required />
                <button className="secondary" disabled={action.busy || !validClass}>Tải thành viên</button>
            </form>
            <h2>Cấp quyền học tập</h2>
            <form className="academic-form" onSubmit={event => {
                event.preventDefault(); if (!validClass || !isAcademicId(studentId.trim())) return;
                const id = classId.trim(); setNotice("");
                void action.run(signal => api<Enrollment>("/enrollment/enrollments", { method: "POST", body: JSON.stringify({ classId: id, studentId: studentId.trim() }), signal }), value => {
                    setItems(previous => loadedClass === id && previous ? [...previous.filter(item => item.id !== value.id), value] : [value]); setLoadedClass(id);
                    setStudentId(""); setNotice("Đã cấp quyền học tập. Bấm Tải thành viên để xem toàn bộ danh sách.");
                });
            }}>
                <label htmlFor="roster-student">ID tài khoản học viên (UUID)</label><input id="roster-student" value={studentId} disabled={action.busy} onChange={event => setStudentId(event.target.value)} required />
                <p>Chỉ nhận tài khoản Student đang hoạt động và lớp đã tồn tại.</p>
                <button className="primary" disabled={action.busy || !validClass || !isAcademicId(studentId.trim())}>Cấp quyền cho lớp ở trên</button>
            </form>
            <ErrorMessage error={action.error} /><p role="status">{action.busy ? "Đang xử lý…" : notice}</p>
            {items?.length === 0 && <p>Chưa có thành viên.</p>}
            {items && <ul className="submission-list">{items.map(item => <li key={item.id}>
                <span><code>{item.studentId}</code> · {item.isActive ? "Đang hoạt động" : "Đã thu hồi"}</span>
                {item.isActive && <button className="secondary" disabled={action.busy} onClick={() => {
                    if (!window.confirm(`Thu hồi quyền học tập của ${item.studentId}?`)) return;
                    setNotice("");
                    void action.run(signal => api<void>(`/enrollment/enrollments/${item.id}`, { method: "DELETE", signal }), () => { setItems(previous => previous?.map(value => value.id === item.id ? { ...value, isActive: false } : value) ?? null); setNotice("Đã thu hồi quyền học tập."); });
                }}>Thu hồi quyền</button>}
            </li>)}</ul>}
            {items?.length === 200 && <p>Đang hiển thị tối đa 200 thành viên.</p>}
        </section>
    </>;
}
