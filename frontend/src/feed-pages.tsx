import { useCallback, useState } from "react";
import { ApiError, getSession, getSessionGeneration, identity, type Role } from "./api";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import {
    authorLabel, canModerateFeed, COMMENT_MAX, createComment, createPost, editComment, editPost, feedAccessMessage, hideComment, hidePost, isFeedAuthor,
    listComments, listPosts, POST_MAX, removeComment, removePost, setLike, validateFeedContent, type Post, type PostComment,
} from "./feed";
import { formatVnDateTime } from "./format";
import { ErrorMessage, FieldError, fieldProps, Pager } from "./ui";

function FeedError({ error }: { error: unknown }) {
    const message = error instanceof ApiError ? feedAccessMessage(error.status) : null;
    return message ? <div className="error" role="alert"><strong>{message}</strong></div> : <ErrorMessage error={error} />;
}

// Văn bản thuần: React tự escape nên không có đường chèn HTML.
function ContentEditor({ id, label, max, initial = "", submitLabel, busy, onSubmit, onCancel }: {
    id: string; label: string; max: number; initial?: string; submitLabel: string; busy: boolean;
    onSubmit: (text: string) => void; onCancel?: () => void;
}) {
    const [text, setText] = useState(initial);
    const [attempted, setAttempted] = useState(false);
    const error = validateFeedContent(text, max);
    return <form className="academic-form" noValidate aria-busy={busy} onSubmit={(event) => {
        event.preventDefault();
        setAttempted(true);
        if (!busy && !error) onSubmit(text);
    }}>
        <div className="academic-field">
            <label htmlFor={id}>{label} (tối đa {max} ký tự)</label>
            <textarea {...fieldProps(id, attempted && error ? [error] : undefined)} required maxLength={max} disabled={busy} value={text} onChange={(event) => setText(event.target.value)} />
            <FieldError id={id} errors={attempted && error ? [error] : undefined} />
        </div>
        <div className="academic-actions">
            <button className="primary" disabled={busy}>{busy ? "Đang gửi…" : submitLabel}</button>
            {onCancel && <button type="button" className="secondary" disabled={busy} onClick={onCancel}>Hủy</button>}
        </div>
    </form>;
}

function Meta({ role, name, authorId, userId, created, updated, hidden }: {
    role: string; name: string | null; authorId: string; userId: string; created: string; updated: string | null; hidden: boolean;
}) {
    return <p className="small muted">
        <strong>{authorLabel(name, role)}{isFeedAuthor(userId, authorId) ? " (bạn)" : ""}</strong> · <time dateTime={created}>{formatVnDateTime(created)}</time> (UTC+7)
        {updated && <> · đã sửa</>}{hidden && <> · <span className="pill">Đã ẩn</span></>}
    </p>;
}

function CommentItem({ comment: initial, userId, role, reload }: { comment: PostComment; userId: string; role: Role; reload: () => void }) {
    const [comment, setComment] = useState(initial);
    const [editing, setEditing] = useState(false);
    const [confirming, setConfirming] = useState(false);
    const action = useAction();
    const author = isFeedAuthor(userId, comment.authorId);
    return <li>
        <Meta role={comment.authorRole} name={comment.authorName} authorId={comment.authorId} userId={userId} created={comment.createdAtUtc} updated={comment.updatedAtUtc} hidden={comment.isHidden} />
        {editing
            ? <ContentEditor id={`comment-edit-${comment.id}`} label="Sửa bình luận" max={COMMENT_MAX} initial={comment.content} submitLabel="Lưu" busy={action.busy}
                onCancel={() => setEditing(false)} onSubmit={(text) => void action.run(() => editComment(comment.id, text), (updated) => { setComment(updated); setEditing(false); })} />
            : <p className="assignment-description">{comment.content}</p>}
        <FeedError error={action.error} />
        {!editing && <div className="academic-actions">
            {author && <button type="button" className="secondary" disabled={action.busy} onClick={() => setEditing(true)}>Sửa</button>}
            {author && (confirming
                ? <>
                    <button type="button" className="primary" disabled={action.busy} onClick={() => void action.run(() => removeComment(comment.id), reload)}>Xác nhận xóa</button>
                    <button type="button" className="secondary" disabled={action.busy} onClick={() => setConfirming(false)}>Giữ lại</button>
                </>
                : <button type="button" className="secondary" disabled={action.busy} onClick={() => setConfirming(true)}>Xóa</button>)}
            {canModerateFeed(role) && <button type="button" className="secondary" disabled={action.busy}
                onClick={() => void action.run(() => hideComment(comment.id, !comment.isHidden), () => setComment({ ...comment, isHidden: !comment.isHidden }))}>
                {comment.isHidden ? "Hiện bình luận" : "Ẩn bình luận"}
            </button>}
        </div>}
    </li>;
}

function Comments({ postId, userId, role, changed }: { postId: string; userId: string; role: Role; changed: (delta: number) => void }) {
    const [page, setPage] = useState(1);
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listComments(postId, page), [postId, page, version]);
    const result = useRead(load);
    const create = useAction();
    const [draftKey, setDraftKey] = useState(0);
    const data = result.data;
    return <div className="feed-comments">
        <h4>Bình luận</h4>
        <FeedError error={result.error} />
        {result.error ? <button type="button" className="secondary" onClick={result.retry}>Thử tải lại bình luận</button> : null}
        {result.loading && <p role="status">Đang tải bình luận…</p>}
        {data && <>
            {data.items.length === 0 && <p>Chưa có bình luận.</p>}
            <ul className="catalog-list">
                {data.items.map((item) => <CommentItem key={`${version}:${item.id}`} comment={item} userId={userId} role={role}
                    reload={() => { changed(-1); setVersion((value) => value + 1); }} />)}
            </ul>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={setPage} label="Phân trang bình luận" />
        </>}
        <ContentEditor key={draftKey} id={`comment-new-${postId}`} label="Viết bình luận" max={COMMENT_MAX} submitLabel="Gửi bình luận" busy={create.busy}
            onSubmit={(text) => void create.run(() => createComment(postId, text), () => { setDraftKey((value) => value + 1); changed(1); setPage(1); setVersion((value) => value + 1); })} />
        <FeedError error={create.error} />
    </div>;
}

function PostItem({ post: initial, userId, role, reload }: { post: Post; userId: string; role: Role; reload: () => void }) {
    const [post, setPost] = useState(initial);
    const [editing, setEditing] = useState(false);
    const [confirming, setConfirming] = useState(false);
    const [open, setOpen] = useState(false);
    const action = useAction();
    const author = isFeedAuthor(userId, post.authorId);
    return <li>
        <Meta role={post.authorRole} name={post.authorName} authorId={post.authorId} userId={userId} created={post.createdAtUtc} updated={post.updatedAtUtc} hidden={post.isHidden} />
        {editing
            ? <ContentEditor id={`post-edit-${post.id}`} label="Sửa bài đăng" max={POST_MAX} initial={post.content} submitLabel="Lưu" busy={action.busy}
                onCancel={() => setEditing(false)}
                onSubmit={(text) => void action.run(() => editPost(post.id, text), () => { setPost({ ...post, content: text.trim(), updatedAtUtc: new Date().toISOString() }); setEditing(false); })} />
            : <p className="assignment-description">{post.content}</p>}
        <FeedError error={action.error} />
        {!editing && <div className="academic-actions">
            <button type="button" className="secondary" aria-pressed={post.likedByMe} disabled={action.busy}
                onClick={() => void action.run(() => setLike(post.id, !post.likedByMe), (value) => setPost({ ...post, likeCount: value.likeCount, likedByMe: value.likedByMe }))}>
                {post.likedByMe ? "Bỏ thích" : "Thích"} · {post.likeCount}
            </button>
            <button type="button" className="secondary" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
                {open ? "Ẩn bình luận" : "Bình luận"} ({post.commentCount})
            </button>
            {author && <button type="button" className="secondary" disabled={action.busy} onClick={() => setEditing(true)}>Sửa</button>}
            {author && (confirming
                ? <>
                    <button type="button" className="primary" disabled={action.busy} onClick={() => void action.run(() => removePost(post.id), reload)}>Xác nhận xóa bài</button>
                    <button type="button" className="secondary" disabled={action.busy} onClick={() => setConfirming(false)}>Giữ lại</button>
                </>
                : <button type="button" className="secondary" disabled={action.busy} onClick={() => setConfirming(true)}>Xóa</button>)}
            {canModerateFeed(role) && <button type="button" className="secondary" disabled={action.busy}
                onClick={() => void action.run(() => hidePost(post.id, !post.isHidden), () => setPost({ ...post, isHidden: !post.isHidden }))}>
                {post.isHidden ? "Hiện bài" : "Ẩn bài"}
            </button>}
        </div>}
        {open && <Comments postId={post.id} userId={userId} role={role} changed={(delta) => setPost((value) => ({ ...value, commentCount: Math.max(0, value.commentCount + delta) }))} />}
    </li>;
}

export function ClassFeed({ classId }: { classId: string }) {
    const user = identity(getSession());
    if (!user) return null;
    return <FeedWorkspace key={`${getSessionGeneration()}:${user.id}:${classId}`} classId={classId} userId={user.id} role={user.role} />;
}

function FeedWorkspace({ classId, userId, role }: { classId: string; userId: string; role: Role }) {
    const [page, setPage] = useState(1);
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listPosts(classId, page), [classId, page, version]);
    const result = useRead(load);
    const create = useAction();
    const [draftKey, setDraftKey] = useState(0);
    const data = result.data;
    const denied = result.error instanceof ApiError && result.error.status === 403;
    return <section className="panel feed" aria-labelledby="class-feed-heading">
        <h2 id="class-feed-heading">Bảng tin lớp</h2>
        <p className="small muted">Bài đăng chỉ hiển thị cho học viên đang ghi danh, giáo viên phụ trách, giáo vụ và quản trị viên. Chỉ nhập văn bản thuần.</p>
        {denied ? <FeedError error={result.error} /> : <>
            <ReadFeedback result={result} />
            <ContentEditor key={draftKey} id="post-new" label="Viết bài mới" max={POST_MAX} submitLabel="Đăng bài" busy={create.busy}
                onSubmit={(text) => void create.run(() => createPost(classId, text), () => { setDraftKey((value) => value + 1); setPage(1); setVersion((value) => value + 1); })} />
            <FeedError error={create.error} />
            {data && <>
                {data.items.length === 0 && <p>Lớp chưa có bài đăng nào.</p>}
                <ul className="catalog-list">
                    {data.items.map((item) => <PostItem key={`${version}:${item.id}`} post={item} userId={userId} role={role} reload={() => setVersion((value) => value + 1)} />)}
                </ul>
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={setPage} label="Phân trang bảng tin" />
            </>}
        </>}
    </section>;
}
