import { useCallback, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { isAcademicId } from "./academic";
import { useRead } from "./academic-pages";
import { ClassSelect, Conflict, useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import {
    canManageCoursework, createPractice, deletePractice, getPractice, listPractice, PRACTICE_CONTENT_MAX, PRACTICE_TITLE_MAX, updatePractice,
    validatePracticeDraft, type Practice, type PracticeDraft,
} from "./coursework";
import { formatVnDateTime } from "./format";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";

// Nội dung luyện tập là văn bản thuần: chỉ render qua React text (tự escape), không bao giờ chèn HTML.
function PracticeForm({ id, initial, submitLabel, busy, children, onSubmit, onCancel }: {
    id: string; initial: PracticeDraft; submitLabel: string; busy: boolean; children?: ReactNode;
    onSubmit: (draft: PracticeDraft) => void; onCancel?: () => void;
}) {
    const [draft, setDraft] = useState(initial);
    const [attempted, setAttempted] = useState(false);
    const errors = validatePracticeDraft(draft);
    const shown = attempted ? errors : {};
    return <form className="academic-form" noValidate aria-busy={busy} onSubmit={event => {
        event.preventDefault(); setAttempted(true);
        if (!busy && !Object.keys(errors).length) onSubmit(draft);
    }}>
        <div className="academic-field">
            <label htmlFor={`${id}-title`}>Tên bài (tối đa {PRACTICE_TITLE_MAX} ký tự)</label>
            <input {...fieldProps(`${id}-title`, shown.title)} required maxLength={PRACTICE_TITLE_MAX} disabled={busy} value={draft.title} onChange={event => setDraft({ ...draft, title: event.target.value })} />
            <FieldError id={`${id}-title`} errors={shown.title} />
        </div>
        <div className="academic-field">
            <label htmlFor={`${id}-content`}>Nội dung (tối đa {PRACTICE_CONTENT_MAX} ký tự, văn bản thuần)</label>
            <textarea {...fieldProps(`${id}-content`, shown.content)} required rows={8} maxLength={PRACTICE_CONTENT_MAX} disabled={busy} value={draft.content} onChange={event => setDraft({ ...draft, content: event.target.value })} />
            <FieldError id={`${id}-content`} errors={shown.content} />
        </div>
        {children}
        <div className="academic-actions">
            <button className="primary" disabled={busy}>{busy ? "Đang lưu…" : submitLabel}</button>
            {onCancel && <button type="button" className="secondary" disabled={busy} onClick={onCancel}>Hủy</button>}
        </div>
    </form>;
}

export function PracticePage() {
    const user = identity(getSession());
    if (!user) return null;
    return <PracticeList key={`${getSessionGeneration()}:${user.id}`} manager={canManageCoursework(user.role)} />;
}

function PracticeList({ manager }: { manager: boolean }) {
    const [params, setParams] = useSearchParams();
    const classId = params.get("classId") ?? "";
    const [input, setInput] = useState("");
    const [query, setQuery] = useState({ search: "", page: 1, version: 0 });
    const [notice, setNotice] = useState("");
    const load = useCallback(() => classId ? listPractice(classId, query.search, query.page) : Promise.resolve(null), [classId, query]);
    const result = useRead(load);
    const create = useAction();
    const data = result.data;
    return <>
        <PageHeading title="Luyện tập" subtitle="Ôn nội dung theo lớp hoặc mở bài có thể làm và nộp trực tiếp." />
        <section className="practice-brand panel">
            <div><h2>Làm bài trực tuyến</h2><p>Mở bài tập đang nhận bài để đọc hoặc tải đề PDF, nhập câu trả lời và nộp cho giáo viên.</p>
                <Link className="primary inline" to={classId ? `/assignments?classId=${encodeURIComponent(classId)}` : "/assignments"}>Mở bài tập{classId ? " của lớp" : ""}</Link>
            </div>
            <img src="/study-desk.svg" width="300" height="190" alt="" />
        </section>
        <section className="panel">
            <form className="academic-filters coursework-filters" onSubmit={event => { event.preventDefault(); if (classId) setQuery(previous => ({ ...previous, search: input.trim(), page: 1 })); }}>
                <ClassSelect id="practice-class" value={classId} onChange={value => {
                    const next = new URLSearchParams(params);
                    if (value) next.set("classId", value); else next.delete("classId");
                    setParams(next); setQuery(previous => ({ ...previous, page: 1 })); setNotice("");
                }} />
                {classId && <><div className="academic-field">
                    <label htmlFor="practice-search">Tìm theo tên hoặc nội dung</label>
                    <input id="practice-search" type="search" maxLength={100} value={input} onChange={event => setInput(event.target.value)} />
                </div>
                <button className="primary">Tìm bài luyện tập</button></>}
            </form>
            {!classId && <p>Chọn một lớp để xem bài luyện tập.</p>}
            {classId && <ReadFeedback result={result} />}
            {notice && <p className="notice" role="status">{notice}</p>}
            {data && <>
                {data.items.length === 0 && <p>Lớp này chưa có bài luyện tập phù hợp.</p>}
                <ul className="catalog-list">{data.items.map(item => <li key={item.id}>
                    <h3><Link className="accent" to={`/practice/${item.id}`}>{item.title}</Link></h3>
                    <p className="assignment-description">{item.preview}</p>
                    <p className="small muted">{item.createdByName} · <time dateTime={item.createdAt}>{formatVnDateTime(item.createdAt)}</time> (UTC+7){item.updatedAt && <> · đã sửa</>}</p>
                </li>)}</ul>
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={page => setQuery(previous => ({ ...previous, page }))} label="Phân trang bài luyện tập" />
            </>}
        </section>
        {manager && classId && <section className="panel">
            <h2>Thêm bài luyện tập</h2>
            <PracticeForm key={`${classId}:${query.version}`} id="new-practice" initial={{ title: "", content: "" }} submitLabel="Tạo bài luyện tập" busy={create.busy}
                onSubmit={draft => void create.run(() => createPractice(classId, draft), () => { setNotice("Đã tạo bài luyện tập."); setQuery(previous => ({ ...previous, page: 1, version: previous.version + 1 })); })}>
                <ErrorMessage error={create.error} />
            </PracticeForm>
        </section>}
    </>;
}

export function PracticeDetails() {
    const { id = "" } = useParams();
    const user = identity(getSession());
    const navigate = useNavigate();
    const load = useCallback(() => isAcademicId(id) ? getPractice(id) : Promise.reject(new ApiError(400, "ID bài luyện tập không hợp lệ.")), [id]);
    const result = useRead(load);
    const [changed, setChanged] = useState<Practice | null>(null);
    const [editing, setEditing] = useState(false);
    const [confirming, setConfirming] = useState(false);
    const [notice, setNotice] = useState("");
    const save = useAction(); const remove = useAction();
    if (!user) return null;
    const manager = canManageCoursework(user.role);
    const item = changed ?? result.data;
    const reload = () => { setChanged(null); setEditing(false); setConfirming(false); result.retry(); };
    return <>
        <PageHeading title={item?.title ?? "Chi tiết luyện tập"} subtitle="Nội dung văn bản thuần, hiển thị nguyên văn." />
        <Link className="secondary inline" to={item ? `/practice?classId=${encodeURIComponent(item.classId)}` : "/practice"}>Về danh sách luyện tập</Link>
        <ReadFeedback result={result} />
        {item && <section className="panel">
            <h2>{item.title}</h2>
            <p className="small muted">{item.createdByName} · <time dateTime={item.createdAt}>{formatVnDateTime(item.createdAt)}</time> (UTC+7){item.updatedAt && <> · sửa lúc {formatVnDateTime(item.updatedAt)}</>}</p>
            {editing
                ? <PracticeForm key={item.version} id="edit-practice" initial={{ title: item.title, content: item.content }} submitLabel="Lưu thay đổi" busy={save.busy} onCancel={() => setEditing(false)}
                    onSubmit={draft => void save.run(() => updatePractice(item, draft), value => { setChanged(value); setEditing(false); setNotice("Đã lưu thay đổi."); })}>
                    <Conflict error={save.error} text="Bài luyện tập đã được người khác thay đổi. Hãy tải lại để xem bản mới nhất rồi sửa tiếp." onReload={reload} />
                </PracticeForm>
                : <div className="assignment-description">{item.content}</div>}
            {manager && !editing && <div className="academic-actions">
                <button type="button" className="secondary" disabled={remove.busy} onClick={() => { setEditing(true); setNotice(""); }}>Sửa</button>
                {confirming
                    ? <>
                        <button type="button" className="primary" disabled={remove.busy} onClick={() => void remove.run(() => deletePractice(item), () => navigate(`/practice?classId=${encodeURIComponent(item.classId)}`))}>{remove.busy ? "Đang xóa…" : "Xác nhận xóa"}</button>
                        <button type="button" className="secondary" disabled={remove.busy} onClick={() => setConfirming(false)}>Giữ lại</button>
                    </>
                    : <button type="button" className="secondary" onClick={() => setConfirming(true)}>Xóa</button>}
            </div>}
            <Conflict error={remove.error} text="Bài luyện tập đã được người khác thay đổi nên chưa xóa. Hãy tải lại rồi quyết định lại." onReload={reload} />
            <p role="status">{notice}</p>
        </section>}
    </>;
}
