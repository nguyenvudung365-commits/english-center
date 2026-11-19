import { useEffect, useRef, useState, type FormEvent } from "react";
import { Check, Eye, EyeOff, ShieldCheck, Upload, UserPlus, UserRound } from "lucide-react";
import { api, ApiError, getSession, identity } from "./api";
import { useRead } from "./academic-pages";
import { PageHeading } from "./ui";
import {
    canCreateAccounts, createAccount, getProfile, normalizeVietnamMobile, provisionStaff, updateProfile, uploadAvatar, validateAvatar, vietnamMobileHint,
    type CreatedAccount, type Profile,
} from "./identity";

const roleLabels = { Admin: "Quản trị viên", Staff: "Giáo vụ", Teacher: "Giáo viên", Student: "Học viên" };
type ManagedTeacher = { id: string; fullName: string; email: string; phone: string | null; isLocked: boolean; revision: string };
const teachersPath = "/identity/users/teachers";
const loadTeachers = () => api<ManagedTeacher[]>(teachersPath);

export function TeacherAdministration() {
    const result = useRead(loadTeachers);
    const [query, setQuery] = useState("");
    const [selected, setSelected] = useState<ManagedTeacher | null>(null);
    const [originalPhone, setOriginalPhone] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const [notice, setNotice] = useState("");

    async function save(lock = false) {
        if (!selected || busy) return;
        if (lock && !window.confirm(selected.isLocked ? "Mở khóa tài khoản này?" : "Khóa tài khoản này và thu hồi phiên đăng nhập?")) return;
        const { id, fullName, email, phone, isLocked, revision: expectedRevision } = selected;
        setBusy(true); setError(null); setNotice("");
        try {
            const value = await api<ManagedTeacher>(`${teachersPath}/${id}${lock ? "/lock" : ""}`, {
                method: "PUT", body: JSON.stringify(lock ? { expectedRevision, isLocked: !isLocked } : { expectedRevision, fullName, email, phone: normalizeVietnamMobile(phone, originalPhone) }),
            });
            setSelected(value); setOriginalPhone(value.phone); result.retry();
            setNotice(lock ? (value.isLocked ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản.") : "Đã lưu hồ sơ giáo viên.");
        } catch (err) { setError(err); }
        finally { setBusy(false); }
    }

    const visible = result.data?.filter(x => `${x.fullName} ${x.email}`.toLocaleLowerCase("vi").includes(query.trim().toLocaleLowerCase("vi")));
    return <>
        <PageHeading title="Quản trị giáo viên" />
        <section className="panel">
            <label htmlFor="managed-teacher-search">Tìm tên hoặc email</label>
            <input id="managed-teacher-search" type="search" value={query} onChange={e => setQuery(e.target.value)} />
            {result.loading && <p role="status">Đang tải tài khoản…</p>}
            {result.error ? <><FormError error={result.error} prefix="managed-teacher" />
                <button className="secondary" onClick={result.retry}>Thử lại</button></> : null}
            {visible?.length === 0 && <p role="status">Không có giáo viên phù hợp.</p>}
            {visible?.length ? <ul className="catalog-list">{visible.map(x => <li key={x.id}>
                <h2>{x.fullName}</h2><p>{x.email} · {x.isLocked ? "Đã khóa" : "Hoạt động"}</p>
                <button className="secondary" disabled={busy} onClick={() => { setSelected(x); setOriginalPhone(x.phone); setError(null); setNotice(""); }}>
                    Quản lý<span className="sr-only"> {x.fullName}</span>
                </button>
            </li>)}</ul> : null}
        </section>
        {selected && <form className="panel" onSubmit={e => { e.preventDefault(); void save(); }}>
            <h2>Hồ sơ: {selected.fullName}</h2>
            <fieldset disabled={busy}>
                <label htmlFor="managed-teacher-fullName">Họ và tên</label>
                <input id="managed-teacher-fullName" required maxLength={200} value={selected.fullName} onChange={e => setSelected({ ...selected, fullName: e.target.value })} />
                <label htmlFor="managed-teacher-email">Email</label>
                <input id="managed-teacher-email" required type="email" maxLength={254} value={selected.email} onChange={e => setSelected({ ...selected, email: e.target.value })} />
                <VietnamMobileField prefix="managed-teacher" error={error} value={selected.phone ?? ""} change={phone => setSelected({ ...selected, phone })} />
                <button className="primary" type="submit">Lưu hồ sơ</button>{" "}
                <button className="secondary" type="button" onClick={() => void save(true)}>{selected.isLocked ? "Mở khóa" : "Khóa tài khoản"}</button>{" "}
                <button className="secondary" type="button" onClick={() => { setSelected(null); result.retry(); }}>Đóng và tải lại danh sách</button>
            </fieldset>
            <FormError error={error} prefix="managed-teacher" />
            <p role="status">{notice}</p>
        </form>}
    </>;
}
const fieldLabels: Record<string, string> = {
    fullName: "Họ và tên", email: "Email", phone: "Số điện thoại", password: "Mật khẩu tạm thời",
    confirm: "Xác nhận mật khẩu", role: "Vai trò", avatarFileId: "Ảnh đại diện", expectedRevision: "Phiên bản hồ sơ",
};

function fieldErrors(error: unknown, field: string) {
    if (!(error instanceof ApiError)) return [];
    return Object.entries(error.errors).filter(([key]) => key.toLowerCase() === field.toLowerCase()).flatMap(([, messages]) => messages);
}

export function InlineError({ error, field, prefix }: { error: unknown; field: string; prefix: string }) {
    const messages = fieldErrors(error, field);
    return messages.length ? <span className="field-error" id={`${prefix}-${field}-error`}>{messages.join(" ")}</span> : null;
}

export function FormError({ error, prefix }: { error: unknown; prefix: string }) {
    const summary = useRef<HTMLDivElement>(null);
    useEffect(() => { if (error) summary.current?.focus(); }, [error]);
    if (!error) return null;
    return <div className="error" role="alert" tabIndex={-1} ref={summary}>
        <strong>{error instanceof ApiError && error.status === 409
            ? "Không thể lưu: email đã được sử dụng hoặc hồ sơ đã thay đổi. Kiểm tra email; nếu có xung đột, tải lại hồ sơ trước khi thử lại."
            : error instanceof Error ? error.message : "Không thể hoàn tất yêu cầu. Vui lòng thử lại."}</strong>
        {error instanceof ApiError && Object.entries(error.errors).map(([field, messages]) => {
            const normalized = Object.keys(fieldLabels).find((key) => key.toLowerCase() === field.toLowerCase());
            return <p key={field}>{normalized ? <a href={`#${prefix}-${normalized}`}>{fieldLabels[normalized]}: {messages.join(" ")}</a> : messages.join(" ")}</p>;
        })}
    </div>;
}

export function errorAttributes(error: unknown, field: string, prefix: string) {
    return {
        "aria-invalid": fieldErrors(error, field).length > 0 || undefined,
        "aria-describedby": fieldErrors(error, field).length ? `${prefix}-${field}-error` : undefined,
    };
}

export function VietnamMobileField({ prefix, error, value, change, autoComplete = "off" }: {
    prefix: string; error: unknown; value: string; change: (value: string) => void; autoComplete?: string;
}) {
    const attrs = errorAttributes(error, "phone", prefix);
    const id = `${prefix}-phone`;
    return <label htmlFor={id}>Số di động <span className="optional">(không bắt buộc)</span>
        <input id={id} type="tel" autoComplete={autoComplete} maxLength={32} value={value} onChange={event => change(event.target.value)}
            {...attrs} aria-describedby={[`${id}-help`, attrs["aria-describedby"]].filter(Boolean).join(" ")} />
        <small id={`${id}-help`}>{vietnamMobileHint}</small>
        <InlineError error={error} field="phone" prefix={prefix} />
    </label>;
}

export function ProfilePanel() {
    const [profile, setProfile] = useState<Profile | null>(null);
    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [phone, setPhone] = useState("");
    const [avatarId, setAvatarId] = useState<string | null>(null);
    const [file, setFile] = useState<File | null>(null);
    const [preview, setPreview] = useState<string | null>(null);
    const [imageFailed, setImageFailed] = useState(false);
    const [error, setError] = useState<unknown>();
    const [loadError, setLoadError] = useState<unknown>();
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [progress, setProgress] = useState("");
    const [notice, setNotice] = useState("");
    const [attempt, setAttempt] = useState(0);
    const [refreshingImage, setRefreshingImage] = useState(false);
    const operation = useRef<AbortController | null>(null);
    const fileInput = useRef<HTMLInputElement>(null);
    const prefix = "profile";

    function apply(value: Profile) {
        setProfile(value); setName(value.fullName); setEmail(value.email); setPhone(value.phone ?? "");
        setAvatarId(value.avatarFileId); setFile(null); setImageFailed(false);
        if (fileInput.current) fileInput.current.value = "";
    }

    useEffect(() => {
        const controller = new AbortController();
        setLoading(true); setLoadError(null);
        getProfile(controller.signal).then((value) => {
            if (!controller.signal.aborted) apply(value);
        }).catch((reason: unknown) => {
            if (!controller.signal.aborted) setLoadError(reason);
        }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
        return () => controller.abort();
    }, [attempt]);

    useEffect(() => () => operation.current?.abort(), []);
    useEffect(() => {
        if (!file) { setPreview(null); return; }
        const url = URL.createObjectURL(file);
        setPreview(url);
        return () => URL.revokeObjectURL(url);
    }, [file]);

    async function save(event: FormEvent) {
        event.preventDefault();
        if (!profile || busy) return;
        setError(null); setNotice(""); setBusy(true);
        const controller = new AbortController();
        operation.current = controller;
        try {
            const mobile = normalizeVietnamMobile(phone, profile.phone);
            const uploadedId = file ? await uploadAvatar(file, setProgress, controller.signal) : avatarId;
            setProgress("Đang lưu hồ sơ…");
            const saved = await updateProfile({
                fullName: name.trim(), email: email.trim(), phone: mobile,
                avatarFileId: uploadedId, expectedRevision: profile.revision,
            }, controller.signal);
            if (!controller.signal.aborted) { apply(saved); setNotice("Đã lưu hồ sơ cá nhân."); }
        } catch (reason) {
            if (!controller.signal.aborted) setError(reason);
        } finally {
            if (!controller.signal.aborted) { setBusy(false); setProgress(""); }
        }
    }

    async function renewImage() {
        if (refreshingImage || busy) return;
        setRefreshingImage(true); setError(null);
        const controller = new AbortController();
        operation.current = controller;
        try {
            const current = await getProfile(controller.signal);
            if (!controller.signal.aborted) {
                if (current.avatarFileId !== profile?.avatarFileId)
                    throw new ApiError(409, "Ảnh đại diện đã thay đổi ở phiên khác. Tải lại hồ sơ mới nhất.");
                setProfile((previous) => previous ? { ...previous, avatarUrl: current.avatarUrl, avatarUrlExpiresAt: current.avatarUrlExpiresAt } : previous);
                setImageFailed(false);
            }
        } catch (reason) { if (!controller.signal.aborted) setError(reason); }
        finally { if (!controller.signal.aborted) setRefreshingImage(false); }
    }

    if (loading) return <section className="panel" aria-busy="true"><h2>Hồ sơ cá nhân</h2><p role="status">Đang tải hồ sơ…</p></section>;
    if (loadError || !profile) return <section className="panel"><h2>Hồ sơ cá nhân</h2>
        <FormError error={loadError} prefix={prefix} />
        <button className="secondary" onClick={() => setAttempt((value) => value + 1)}>Tải lại hồ sơ</button>
    </section>;

    const image = preview ?? (avatarId ? profile.avatarUrl : null);
    const dirty = name !== profile.fullName || email !== profile.email || phone !== (profile.phone ?? "") || avatarId !== profile.avatarFileId || file !== null;
    return <section className="panel identity-panel" aria-labelledby="profile-heading">
        <div className="identity-section-heading"><div><span className="eyebrow">THÔNG TIN CỦA BẠN</span><h2 id="profile-heading">Hồ sơ cá nhân</h2></div><span className="pill">{roleLabels[profile.role]}</span></div>
        <p className="muted">Cập nhật thông tin để trung tâm liên hệ đúng với bạn.</p>
        <form onSubmit={save} aria-busy={busy}>
            <FormError error={error} prefix={prefix} />
            {notice && <p className="notice" role="status">{notice}</p>}
            <fieldset disabled={busy || refreshingImage}>
                <legend className="sr-only">Thông tin hồ sơ</legend>
                <div className="profile-avatar-row">
                    <div className="profile-avatar">{image && !imageFailed
                        ? <img src={image} alt="Ảnh đại diện của bạn" width={80} height={80} referrerPolicy="no-referrer" onError={() => setImageFailed(true)} />
                        : <UserRound size={34} aria-hidden="true" />}</div>
                    <div className="profile-avatar-controls">
                        <label className="file-picker" htmlFor="profile-avatarFileId"><Upload size={16} aria-hidden="true" /> Chọn ảnh đại diện</label>
                        <input ref={fileInput} id="profile-avatarFileId" className="avatar-file-input" type="file" accept=".jpg,.png,image/jpeg,image/png"
                            {...errorAttributes(error, "avatarFileId", prefix)} aria-describedby={`profile-avatar-help${fieldErrors(error, "avatarFileId").length ? " profile-avatarFileId-error" : ""}`} onChange={(event) => {
                                const selected = event.target.files?.[0];
                                if (!selected) return;
                                setError(null); setNotice("");
                                const invalid = validateAvatar(selected);
                                if (invalid) { setError(new ApiError(400, invalid, { AvatarFileId: [invalid] })); event.target.value = ""; return; }
                                setFile(selected); setImageFailed(false);
                            }} />
                        <small id="profile-avatar-help">JPG hoặc PNG, tối đa 2 MB. Ảnh được cập nhật khi lưu hồ sơ.</small>
                        <InlineError error={error} field="avatarFileId" prefix={prefix} />
                        {(file || avatarId) && <button className="text-button" type="button" onClick={() => {
                            setFile(null); setAvatarId(null); setImageFailed(false); setNotice("");
                            if (fileInput.current) fileInput.current.value = "";
                        }}>Xóa ảnh</button>}
                        {imageFailed && !file && <button className="text-button" type="button" onClick={renewImage} disabled={refreshingImage}>Tải lại ảnh</button>}
                    </div>
                </div>
                {profile.studentCode && <div className="student-code"><div><small>Mã học viên · chỉ đọc</small><strong>{profile.studentCode}</strong></div><ShieldCheck size={24} aria-hidden="true" /></div>}
                <label htmlFor="profile-fullName">Họ và tên
                    <input id="profile-fullName" autoComplete="name" required maxLength={200} value={name} onChange={(event) => setName(event.target.value)} {...errorAttributes(error, "fullName", prefix)} />
                    <InlineError error={error} field="fullName" prefix={prefix} />
                </label>
                <label htmlFor="profile-email">Email
                    <input id="profile-email" type="email" autoComplete="email" required maxLength={254} value={email} onChange={(event) => setEmail(event.target.value)} {...errorAttributes(error, "email", prefix)} />
                    <InlineError error={error} field="email" prefix={prefix} />
                </label>
                <VietnamMobileField prefix={prefix} error={error} value={phone} change={setPhone} autoComplete="tel" />
                <div className="identity-actions"><button className="primary" disabled={!dirty}><Check size={18} aria-hidden="true" /> Lưu thay đổi</button>
                    <button className="secondary" type="button" disabled={!dirty} onClick={() => { apply(profile); setError(null); setNotice(""); }}>Hủy thay đổi</button></div>
                {error instanceof ApiError && (error.status === 409 || error.status === 503) && <button className="text-button" type="button" onClick={() => {
                    if (!dirty || window.confirm("Tải lại sẽ bỏ các thay đổi chưa lưu. Tiếp tục?")) { setError(null); setNotice(""); setAttempt((value) => value + 1); }
                }}>Tải lại hồ sơ mới nhất</button>}
            </fieldset>
            {busy && <p className="muted" role="status">{progress || "Đang xử lý…"}</p>}
        </form>
    </section>;
}

export function AccountCreate({ staffOnly = false, onCreated }: { staffOnly?: boolean; onCreated?: () => void } = {}) {
    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [phone, setPhone] = useState("");
    const [role, setRole] = useState<"Student" | "Teacher">("Student");
    const [password, setPassword] = useState("");
    const [confirm, setConfirm] = useState("");
    const [show, setShow] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const [created, setCreated] = useState<CreatedAccount | null>(null);
    const operation = useRef<AbortController | null>(null);
    const nameInput = useRef<HTMLInputElement>(null);
    useEffect(() => () => operation.current?.abort(), []);
    const user = identity(getSession());
    const prefix = "create";
    if (!user || (staffOnly ? user.role !== "Admin" : !canCreateAccounts(user.role))) return <section className="panel"><h1>Không có quyền truy cập</h1><p>{staffOnly ? "Chỉ Quản trị viên được tạo tài khoản giáo vụ." : "Chỉ Giáo vụ hoặc Quản trị viên được tạo tài khoản."}</p></section>;

    async function submit(event: FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null); setCreated(null);
        if (password !== confirm) { setError(new ApiError(400, "Mật khẩu xác nhận chưa khớp.", { Confirm: ["Nhập lại đúng mật khẩu tạm thời."] })); return; }
        setBusy(true);
        const controller = new AbortController();
        operation.current = controller;
        try {
            const request = { fullName: name.trim(), email: email.trim(), phone: normalizeVietnamMobile(phone), password };
            const result = staffOnly ? await provisionStaff(request, controller.signal) : await createAccount({ ...request, role }, controller.signal);
            if (!controller.signal.aborted) {
                setCreated(result); setName(""); setEmail(""); setPhone(""); setPassword(""); setConfirm(""); setShow(false);
                onCreated?.();
            }
        } catch (reason) { if (!controller.signal.aborted) setError(reason); }
        finally { if (!controller.signal.aborted) setBusy(false); }
    }

    const form = <section className="panel identity-panel">
                <div className="identity-section-heading"><h2>{staffOnly ? "Tạo tài khoản giáo vụ" : "Thông tin tài khoản mới"}</h2><UserPlus size={24} className="accent" aria-hidden="true" /></div>
                {created && <div className="notice identity-created" role="status"><strong>Đã tạo tài khoản {roleLabels[created.role].toLowerCase()}.</strong><p>{created.fullName} · {created.email}</p>{created.studentCode && <p>Mã học viên: <strong>{created.studentCode}</strong></p>}<p>Bạn vẫn đang sử dụng tài khoản quản trị hiện tại.</p><button type="button" className="secondary" onClick={() => { setCreated(null); nameInput.current?.focus(); }}>Tạo tài khoản tiếp theo</button></div>}
                <form onSubmit={submit} aria-busy={busy}>
                    <FormError error={error} prefix={prefix} />
                    <fieldset disabled={busy}>
                        <legend className="sr-only">Thông tin tài khoản mới</legend>
                        {!staffOnly && <label htmlFor="create-role">Vai trò
                            <select id="create-role" value={role} onChange={(event) => setRole(event.target.value as "Student" | "Teacher")} {...errorAttributes(error, "role", prefix)}><option value="Student">Học viên</option><option value="Teacher">Giáo viên</option></select>
                            <InlineError error={error} field="role" prefix={prefix} />
                        </label>}
                        <label htmlFor="create-fullName">Họ và tên
                            <input ref={nameInput} id="create-fullName" required maxLength={200} autoComplete="off" value={name} onChange={(event) => setName(event.target.value)} {...errorAttributes(error, "fullName", prefix)} />
                            <InlineError error={error} field="fullName" prefix={prefix} />
                        </label>
                        <label htmlFor="create-email">Email đăng nhập
                            <input id="create-email" type="email" required maxLength={254} autoComplete="off" value={email} onChange={(event) => setEmail(event.target.value)} {...errorAttributes(error, "email", prefix)} />
                            <InlineError error={error} field="email" prefix={prefix} />
                        </label>
                        <VietnamMobileField prefix={prefix} error={error} value={phone} change={setPhone} />
                        <label htmlFor="create-password">Mật khẩu tạm thời
                            <span className="password-input"><input id="create-password" type={show ? "text" : "password"} autoComplete="new-password" required minLength={8} maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} {...errorAttributes(error, "password", prefix)} />
                                <button type="button" aria-label={show ? "Ẩn mật khẩu tạm thời" : "Hiện mật khẩu tạm thời"} aria-pressed={show} onClick={() => setShow((value) => !value)}>{show ? <EyeOff size={18} /> : <Eye size={18} />}</button></span>
                            <small>Từ 8 đến 128 ký tự. Cấp mật khẩu qua kênh riêng, không gửi vào nhóm chung.</small>
                            <InlineError error={error} field="password" prefix={prefix} />
                        </label>
                        <label htmlFor="create-confirm">Xác nhận mật khẩu
                            <input id="create-confirm" type="password" autoComplete="new-password" required minLength={8} maxLength={128} value={confirm} onChange={(event) => setConfirm(event.target.value)} {...errorAttributes(error, "confirm", prefix)} />
                            <InlineError error={error} field="confirm" prefix={prefix} />
                        </label>
                        <button className="primary"><UserPlus size={18} aria-hidden="true" />{busy ? "Đang tạo tài khoản…" : "Tạo tài khoản"}</button>
                    </fieldset>
                </form>
            </section>;
    if (staffOnly) return form;
    return <>
        <PageHeading title="Tạo tài khoản" subtitle="Cấp quyền truy cập cho học viên và giáo viên của trung tâm." />
        <div className="identity-create-grid">
            {form}
            <aside className="panel identity-guide" aria-labelledby="account-guide"><ShieldCheck size={30} className="accent" aria-hidden="true" /><h2 id="account-guide">Tài khoản do trung tâm cấp</h2><p>Không mở đăng ký công khai. Giáo vụ hoặc Quản trị viên xác nhận thông tin và cấp tài khoản cho người dùng.</p><ol><li>Chọn đúng vai trò và email đăng nhập.</li><li>Cấp email và mật khẩu tạm thời qua kênh riêng.</li><li>Hướng dẫn người nhận đăng nhập và đổi mật khẩu tại <strong>Tài khoản</strong>.</li></ol><p className="muted small">Tạo tài khoản không tự ghi danh vào lớp. Không thể cấp vai trò Admin hoặc Giáo vụ tại đây.</p></aside>
        </div>
    </>;
}
