import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import {
    canManageFeedback, canSeeFeedback, createFaq, createFeedback, deleteFaq, feedbackStatuses, feedbackStatusLabel, listFaqs,
    listFeedback, nextFeedbackStatus, updateFaq, updateFeedbackStatus, validateFaqDraft, validateFeedbackDraft, type Faq,
    type FaqDraft, type Feedback, type FeedbackDraft, type FeedbackStatus,
} from "./feedback";
import { formatVnDateTime } from "./format";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";

const emptyFeedback: FeedbackDraft = { title: "", content: "", rating: "5" };
const conflictText = "Phản hồi đã được thay đổi hoặc không thể chuyển sang trạng thái này. Hãy tải lại danh sách rồi thử lại.";

export function FeedbackPage() {
    const user = identity(getSession());
    if (!user) return null;
    if (!canSeeFeedback(user.role))
        return <>
            <PageHeading title="Phản hồi" subtitle="Phản hồi của học viên được giáo vụ và quản trị viên xử lý." />
            <section className="panel"><p role="status">Tài khoản giáo viên không có quyền truy cập phản hồi của học viên.</p></section>
        </>;
    return <FeedbackWorkspace key={`${getSessionGeneration()}:${user.id}`} manager={canManageFeedback(user.role)} />;
}

function FeedbackWorkspace({ manager }: { manager: boolean }) {
    const [status, setStatus] = useState<FeedbackStatus | "">("");
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState<{ search: string; page: number }>({ search: "", page: 1 });
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listFeedback({ status: status || undefined, search: query.search, page: query.page }), [status, query, version]);
    const result = useRead(load);
    const [draft, setDraft] = useState<FeedbackDraft>(emptyFeedback);
    const [attempted, setAttempted] = useState(false);
    const [notice, setNotice] = useState("");
    const [patched, setPatched] = useState<Record<string, Feedback>>({});
    const [acting, setActing] = useState("");
    const create = useAction();
    const move = useAction();
    const data = result.data;
    useEffect(() => setPatched({}), [data]);
    const errors = validateFeedbackDraft(draft);
    const shown = attempted ? errors : {};
    const conflict = move.error instanceof ApiError && move.error.status === 409;
    return (
        <>
            <PageHeading title="Phản hồi" subtitle={manager ? "Xem, tìm kiếm và xử lý phản hồi của học viên." : "Gửi góp ý cho trung tâm và theo dõi trạng thái xử lý."} />
            {!manager && <section className="panel">
                <h2>Gửi phản hồi mới</h2>
                <form className="academic-form" noValidate aria-busy={create.busy} onSubmit={(event) => {
                    event.preventDefault();
                    setAttempted(true);
                    setNotice("");
                    if (create.busy || Object.keys(errors).length) return;
                    void create.run(() => createFeedback(draft), () => {
                        setDraft(emptyFeedback);
                        setAttempted(false);
                        setNotice("Đã gửi phản hồi. Cảm ơn bạn đã góp ý.");
                        setQuery((previous) => ({ ...previous, page: 1 }));
                        setVersion((value) => value + 1);
                    });
                }}>
                    <div className="academic-field">
                        <label htmlFor="feedback-title">Tiêu đề (tối đa 150 ký tự)</label>
                        <input {...fieldProps("feedback-title", shown.title)} required maxLength={150} disabled={create.busy} value={draft.title} onChange={(event) => setDraft({ ...draft, title: event.target.value })} />
                        <FieldError id="feedback-title" errors={shown.title} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="feedback-content">Nội dung (tối đa 2000 ký tự, văn bản thuần)</label>
                        <textarea {...fieldProps("feedback-content", shown.content)} required maxLength={2000} disabled={create.busy} value={draft.content} onChange={(event) => setDraft({ ...draft, content: event.target.value })} />
                        <FieldError id="feedback-content" errors={shown.content} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="feedback-rating">Mức hài lòng</label>
                        <select {...fieldProps("feedback-rating", shown.rating)} required disabled={create.busy} value={draft.rating} onChange={(event) => setDraft({ ...draft, rating: event.target.value })}>
                            {[5, 4, 3, 2, 1].map((value) => <option key={value} value={value}>{value} / 5{value === 5 ? " — rất hài lòng" : value === 1 ? " — rất chưa hài lòng" : ""}</option>)}
                        </select>
                        <FieldError id="feedback-rating" errors={shown.rating} />
                    </div>
                    <ErrorMessage error={create.error} />
                    {notice && <p className="notice" role="status">{notice}</p>}
                    <button className="primary" disabled={create.busy}>{create.busy ? "Đang gửi…" : "Gửi phản hồi"}</button>
                </form>
            </section>}
            <section className="panel">
                <h2>{manager ? "Tất cả phản hồi" : "Phản hồi của tôi"}</h2>
                <form className="academic-filters" onSubmit={(event) => { event.preventDefault(); setQuery({ search: search.trim(), page: 1 }); }}>
                    <label htmlFor="feedback-search">Tìm theo tiêu đề hoặc nội dung
                        <input id="feedback-search" type="search" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} />
                    </label>
                    <label htmlFor="feedback-status">Trạng thái
                        <select id="feedback-status" value={status} onChange={(event) => { setStatus(event.target.value as FeedbackStatus | ""); setQuery((previous) => ({ ...previous, page: 1 })); }}>
                            <option value="">Tất cả trạng thái</option>
                            {feedbackStatuses.map((value) => <option key={value} value={value}>{feedbackStatusLabel(value)}</option>)}
                        </select>
                    </label>
                    <button className="primary">Tìm phản hồi</button>
                </form>
                {conflict ? <div className="error" role="alert">
                    <strong>{conflictText}</strong>
                    <button type="button" className="secondary" onClick={() => { move.run(async () => undefined, () => undefined); setVersion((value) => value + 1); }}>Tải lại danh sách</button>
                </div> : <ErrorMessage error={move.error} />}
                <ReadFeedback result={result} />
                {data && <>
                    {data.items.length === 0 && <p>Chưa có phản hồi phù hợp.</p>}
                    <ul className="catalog-list">
                        {data.items.map((original) => {
                            const item = patched[original.id] ?? original;
                            const next = nextFeedbackStatus(item.status);
                            return (
                                <li key={item.id}>
                                    <span className="pill">{feedbackStatusLabel(item.status)}</span>
                                    <h3>{item.title}</h3>
                                    <p className="assignment-description">{item.content}</p>
                                    <p className="small muted">Đánh giá {item.rating}/5 · Gửi lúc <time dateTime={item.createdAtUtc}>{formatVnDateTime(item.createdAtUtc)}</time> (UTC+7)
                                        {manager && <> · Học viên {item.studentId}</>}
                                        {item.handledAtUtc && <> · Xử lý lúc {formatVnDateTime(item.handledAtUtc)}</>}</p>
                                    {manager && next && <button type="button" className="secondary" disabled={move.busy} onClick={() => {
                                        setActing(item.id);
                                        void move.run(() => updateFeedbackStatus(item, next), (updated) => setPatched((previous) => ({ ...previous, [updated.id]: updated })));
                                    }}>{move.busy && acting === item.id ? "Đang cập nhật…" : `Chuyển sang “${feedbackStatusLabel(next)}”`}</button>}
                                </li>
                            );
                        })}
                    </ul>
                    <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={(page) => setQuery((previous) => ({ ...previous, page }))} label="Phân trang phản hồi" />
                </>}
            </section>
        </>
    );
}

function FaqList({ items, children }: { items: Faq[]; children?: (item: Faq) => ReactNode }) {
    return <ul className="catalog-list">{items.map((item) => <li key={item.id}>
        <details>
            <summary><strong>{item.question}</strong>{!item.isPublished && <span className="pill"> Đang ẩn</span>}</summary>
            <p className="assignment-description">{item.answer}</p>
        </details>
        {children?.(item)}
    </li>)}</ul>;
}

export function FaqPage() {
    const user = identity(getSession());
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState({ search: "", page: 1 });
    const load = useCallback(() => listFaqs(query), [query]);
    const result = useRead(load);
    const data = result.data;
    return (
        <>
            <PageHeading title="Câu hỏi thường gặp" subtitle="Giải đáp nhanh các thắc mắc phổ biến về trung tâm." />
            {user?.role === "Admin" && <div className="academic-actions"><Link className="secondary inline" to="/faq/manage">Quản trị câu hỏi thường gặp</Link></div>}
            <section className="panel">
                <form className="academic-filters" onSubmit={(event) => { event.preventDefault(); setQuery({ search: search.trim(), page: 1 }); }}>
                    <label htmlFor="faq-search">Tìm câu hỏi
                        <input id="faq-search" type="search" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} />
                    </label>
                    <button className="primary">Tìm</button>
                </form>
                <ReadFeedback result={result} />
                {data && <>
                    {data.items.length === 0 && <p>Chưa có câu hỏi phù hợp.</p>}
                    <FaqList items={data.items} />
                    <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={(page) => setQuery((previous) => ({ ...previous, page }))} label="Phân trang câu hỏi" />
                </>}
            </section>
        </>
    );
}

const emptyFaq: FaqDraft = { question: "", answer: "", isPublished: true };

export function FaqManagePage() {
    const user = identity(getSession());
    if (user?.role !== "Admin")
        return <>
            <PageHeading title="Quản trị câu hỏi thường gặp" subtitle="Chức năng này chỉ dành cho quản trị viên." />
            <section className="panel"><p>Bạn không có quyền quản trị câu hỏi thường gặp.</p><Link className="secondary inline" to="/faq">Về câu hỏi thường gặp</Link></section>
        </>;
    return <FaqManager key={`${getSessionGeneration()}:${user.id}`} />;
}

function FaqManager() {
    const [published, setPublished] = useState<"" | "true" | "false">("");
    const [page, setPage] = useState(1);
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listFaqs({ page, published: published === "" ? undefined : published === "true" }, true), [page, published, version]);
    const result = useRead(load);
    const [draft, setDraft] = useState<FaqDraft>(emptyFaq);
    const [editing, setEditing] = useState<Faq | null>(null);
    const [attempted, setAttempted] = useState(false);
    const [confirming, setConfirming] = useState("");
    const [notice, setNotice] = useState("");
    const save = useAction();
    const act = useAction();
    const data = result.data;
    const errors = validateFaqDraft(draft);
    const shown = attempted ? errors : {};
    const reset = () => { setDraft(emptyFaq); setEditing(null); setAttempted(false); };
    return (
        <>
            <PageHeading title="Quản trị câu hỏi thường gặp" subtitle="Thêm, sửa, ẩn/hiện hoặc xóa câu hỏi. Chỉ câu hỏi đã đăng mới hiển thị cho người dùng." />
            <div className="academic-actions"><Link className="secondary inline" to="/faq">Xem như người dùng</Link></div>
            <section className="panel">
                <h2>{editing ? "Sửa câu hỏi" : "Thêm câu hỏi"}</h2>
                <form className="academic-form" noValidate aria-busy={save.busy} onSubmit={(event) => {
                    event.preventDefault();
                    setAttempted(true);
                    setNotice("");
                    if (save.busy || Object.keys(errors).length) return;
                    void save.run(() => (editing ? updateFaq(editing.id, draft) : createFaq(draft)), () => {
                        setNotice(editing ? "Đã lưu thay đổi." : "Đã thêm câu hỏi.");
                        reset();
                        setVersion((value) => value + 1);
                    });
                }}>
                    <div className="academic-field">
                        <label htmlFor="faq-question">Câu hỏi (tối đa 300 ký tự)</label>
                        <input {...fieldProps("faq-question", shown.question)} required maxLength={300} disabled={save.busy} value={draft.question} onChange={(event) => setDraft({ ...draft, question: event.target.value })} />
                        <FieldError id="faq-question" errors={shown.question} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="faq-answer">Câu trả lời (tối đa 4000 ký tự, văn bản thuần)</label>
                        <textarea {...fieldProps("faq-answer", shown.answer)} required maxLength={4000} disabled={save.busy} value={draft.answer} onChange={(event) => setDraft({ ...draft, answer: event.target.value })} />
                        <FieldError id="faq-answer" errors={shown.answer} />
                    </div>
                    <label className="check-row" htmlFor="faq-published">
                        <input id="faq-published" type="checkbox" disabled={save.busy} checked={draft.isPublished} onChange={(event) => setDraft({ ...draft, isPublished: event.target.checked })} />
                        Đăng công khai
                    </label>
                    <ErrorMessage error={save.error} />
                    {notice && <p className="notice" role="status">{notice}</p>}
                    <div className="academic-actions">
                        <button className="primary" disabled={save.busy}>{save.busy ? "Đang lưu…" : editing ? "Lưu thay đổi" : "Thêm câu hỏi"}</button>
                        {editing && <button type="button" className="secondary" disabled={save.busy} onClick={reset}>Hủy sửa</button>}
                    </div>
                </form>
            </section>
            <section className="panel">
                <h2>Danh sách câu hỏi</h2>
                <div className="academic-filters">
                    <label htmlFor="faq-published-filter">Hiển thị
                        <select id="faq-published-filter" value={published} onChange={(event) => { setPublished(event.target.value as "" | "true" | "false"); setPage(1); }}>
                            <option value="">Tất cả</option><option value="true">Đã đăng</option><option value="false">Đang ẩn</option>
                        </select>
                    </label>
                </div>
                <ErrorMessage error={act.error} />
                <ReadFeedback result={result} />
                {data && <>
                    {data.items.length === 0 && <p>Chưa có câu hỏi nào.</p>}
                    <FaqList items={data.items}>{(item) => <div className="academic-actions">
                        <button type="button" className="secondary" disabled={act.busy} onClick={() => { setEditing(item); setDraft({ question: item.question, answer: item.answer, isPublished: item.isPublished }); setAttempted(false); setNotice(""); }}>Sửa</button>
                        <button type="button" className="secondary" disabled={act.busy} onClick={() => void act.run(() => updateFaq(item.id, { question: item.question, answer: item.answer, isPublished: !item.isPublished }), () => setVersion((value) => value + 1))}>{item.isPublished ? "Ẩn" : "Đăng"}</button>
                        {confirming === item.id
                            ? <>
                                <button type="button" className="primary" disabled={act.busy} onClick={() => void act.run(() => deleteFaq(item.id), () => { setConfirming(""); if (editing?.id === item.id) reset(); setVersion((value) => value + 1); })}>Xác nhận xóa</button>
                                <button type="button" className="secondary" disabled={act.busy} onClick={() => setConfirming("")}>Giữ lại</button>
                            </>
                            : <button type="button" className="secondary" disabled={act.busy} onClick={() => setConfirming(item.id)}>Xóa</button>}
                    </div>}</FaqList>
                    <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={setPage} label="Phân trang câu hỏi quản trị" />
                </>}
            </section>
        </>
    );
}
