import { useCallback, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { getSession, getSessionGeneration, identity, type Role } from "./api";
import { useRead as useLoad } from "./academic-pages";
import { ClassSelect, useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import {
    canManageCoursework, createMaterial, emptyMaterialDraft, listMaterials, MATERIAL_DESCRIPTION_MAX, MATERIAL_TITLE_MAX,
    materialDownload, safeDownloadUrl, validateMaterialDraft, type ClassMaterial, type MaterialDraft,
} from "./coursework";
import { formatVnDateTime } from "./format";
import { submissionAccept } from "./submissions";
import { ErrorMessage, FieldError, fieldProps, PageHeading } from "./ui";

// Route: /materials?classId=<id>. Học viên còn quyền lớp xem và tải; giáo viên/nhân viên có thêm form đăng. Máy chủ kiểm tra lại mọi thao tác.
export function ClassMaterials() {
    const user = identity(getSession());
    if (!user) return null;
    return <MaterialsView key={`${getSessionGeneration()}:${user.id}`} role={user.role} />;
}

function MaterialsView({ role }: { role: Role }) {
    const [params, setParams] = useSearchParams();
    const classId = params.get("classId") ?? "";
    const load = useCallback(() => classId ? listMaterials(classId) : Promise.resolve([] as ClassMaterial[]), [classId]);
    const items = useLoad(load);
    return <>
        <PageHeading title="Tài liệu lớp" subtitle="Tài liệu giáo viên đăng cho lớp. Chỉ thành viên còn quyền học và giáo viên phụ trách xem và tải được." />
        <section className="panel">
            <ClassSelect id="material-class" value={classId} onChange={value => {
                const next = new URLSearchParams(params);
                if (value) next.set("classId", value); else next.delete("classId");
                setParams(next);
            }} />
            {!classId && <p>Chọn một lớp để xem tài liệu.</p>}
            {classId && <ReadFeedback result={items} />}
            {classId && items.data?.length === 0 && <p>Lớp này chưa có tài liệu.</p>}
            {items.data && items.data.length > 0 && <ul className="catalog-list">{items.data.map(item => <MaterialItem key={item.id} item={item} />)}</ul>}
            {items.data?.length === 200 && <p>Đang hiển thị 200 tài liệu mới nhất.</p>}
        </section>
        {classId && canManageCoursework(role) && <MaterialUpload key={classId} classId={classId} onUploaded={items.retry} />}
    </>;
}

function MaterialItem({ item }: { item: ClassMaterial }) {
    const action = useAction();
    const [link, setLink] = useState<{ url: string; expiresAt: string } | null>(null);
    return <li>
        <h3>{item.title}</h3>
        {item.description && <p className="assignment-description">{item.description}</p>}
        <p className="small muted">{item.fileName} · {(item.sizeBytes / 1024 / 1024).toFixed(2)} MiB · Đăng lúc {formatVnDateTime(item.createdAt)} (UTC+7)</p>
        <button type="button" className="secondary" disabled={action.busy} onClick={() => {
            setLink(null);
            void action.run(async signal => {
                const ticket = await materialDownload(item.id, signal);
                return { ...ticket, url: safeDownloadUrl(ticket) };
            }, setLink);
        }}>{action.busy ? "Đang lấy link…" : `Lấy link tải ${item.fileName}`}</button>
        <ErrorMessage error={action.error} />
        {link && <p role="status"><a href={link.url} target="_blank" rel="noopener noreferrer">Tải {item.fileName} (mở tab mới)</a> · Link hết hạn {new Date(link.expiresAt).toLocaleTimeString("vi-VN")}</p>}
    </li>;
}

function MaterialUpload({ classId, onUploaded }: { classId: string; onUploaded: () => void }) {
    const [draft, setDraft] = useState<MaterialDraft>(emptyMaterialDraft());
    const [attempted, setAttempted] = useState(false);
    // Đổi key để xóa file đã chọn trong ô input sau khi đăng.
    const [fileKey, setFileKey] = useState(0);
    const [notice, setNotice] = useState("");
    const action = useAction();
    const errors = validateMaterialDraft(draft);
    const shown = attempted ? errors : {};
    return <section className="panel">
        <h2>Đăng tài liệu mới</h2>
        <p>PDF, DOCX, MP3, M4A, JPG hoặc PNG, tối đa 20 MiB. Thành viên lớp tải được ngay sau khi đăng.</p>
        <form className="academic-form" noValidate aria-busy={action.busy} onSubmit={event => {
            event.preventDefault(); setAttempted(true); setNotice("");
            if (action.busy || Object.keys(errors).length) return;
            void action.run(signal => createMaterial(classId, draft, signal), () => {
                setDraft(emptyMaterialDraft()); setAttempted(false); setFileKey(value => value + 1);
                setNotice("Đã đăng tài liệu."); onUploaded();
            });
        }}>
            <div className="academic-field">
                <label htmlFor="material-title">Tiêu đề (tối đa {MATERIAL_TITLE_MAX} ký tự)</label>
                <input {...fieldProps("material-title", shown.title)} required maxLength={MATERIAL_TITLE_MAX} disabled={action.busy} value={draft.title} onChange={event => setDraft({ ...draft, title: event.target.value })} />
                <FieldError id="material-title" errors={shown.title} />
            </div>
            <div className="academic-field">
                <label htmlFor="material-description">Mô tả (tối đa {MATERIAL_DESCRIPTION_MAX} ký tự)</label>
                <textarea {...fieldProps("material-description", shown.description)} maxLength={MATERIAL_DESCRIPTION_MAX} disabled={action.busy} value={draft.description} onChange={event => setDraft({ ...draft, description: event.target.value })} />
                <FieldError id="material-description" errors={shown.description} />
            </div>
            <div className="academic-field">
                <label htmlFor="material-file">File tài liệu</label>
                <input key={fileKey} {...fieldProps("material-file", shown.file)} type="file" accept={submissionAccept} disabled={action.busy} onChange={event => setDraft({ ...draft, file: event.target.files?.[0] ?? null })} />
                <FieldError id="material-file" errors={shown.file} />
            </div>
            <ErrorMessage error={action.error} />
            <button className="primary" disabled={action.busy}>{action.busy ? "Đang tải lên…" : "Đăng tài liệu"}</button>
        </form>
        <p role="status">{notice}</p>
    </section>;
}
