import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import { AccountCreate, errorAttributes, FormError, InlineError, VietnamMobileField } from "./identity-pages";
import {
    canCreateAccounts, getManagedStudent, listManagedAccounts, normalizeVietnamMobile, setManagedAccountLock,
    updateManagedStudent, type ManagedAccount, type ManagedAccountDirectory,
} from "./identity";
import { PageHeading, Pager } from "./ui";

export function StudentAdministration() {
    const user = identity(getSession());
    if (!user || !canCreateAccounts(user.role)) return <DirectoryDenied />;
    return <AccountDirectory key={`${getSessionGeneration()}:${user.id}`} directory="students" isAdmin={user.role === "Admin"} />;
}

export function StaffAdministration() {
    const user = identity(getSession());
    if (!user || user.role !== "Admin") return <DirectoryDenied />;
    return <AccountDirectory key={`${getSessionGeneration()}:${user.id}`} directory="staff" isAdmin />;
}

function DirectoryDenied() {
    return <><PageHeading title="Không có quyền truy cập" /><p role="status">Tài khoản của bạn không có quyền quản lý danh sách này.</p></>;
}

function AccountDirectory({ directory, isAdmin }: { directory: ManagedAccountDirectory; isAdmin: boolean }) {
    const students = directory === "students";
    const label = students ? "học viên" : "giáo vụ";
    const [input, setInput] = useState("");
    const [query, setQuery] = useState({ q: "", page: 1 });
    const [selected, setSelected] = useState("");
    const [notice, setNotice] = useState("");
    const returnFocus = useRef<HTMLButtonElement | null>(null);
    const searchInput = useRef<HTMLInputElement>(null);
    const load = useCallback(() => listManagedAccounts(directory, query), [directory, query]);
    const result = useRead(load);
    const data = result.data;
    const staffLock = useAction();

    function toggleStaff(item: ManagedAccount) {
        if (!confirmLock(item)) return;
        setNotice("");
        void staffLock.run(signal => setManagedAccountLock("staff", item.id, !item.isLocked, item.revision, signal), saved => {
            setNotice(lockNotice(saved));
            result.retry();
        });
    }

    return <>
        <PageHeading title={students ? "Quản lý học viên" : "Quản trị giáo vụ"}
            subtitle={students ? "Tra cứu hồ sơ và cập nhật thông tin liên hệ của học viên." : "Cấp, khóa hoặc mở khóa tài khoản giáo vụ. Vai trò giáo vụ không thể thay đổi tại đây."} />
        {students && <p><Link className="secondary inline" to="/users/new">Tạo tài khoản học viên</Link></p>}
        <section className="panel" aria-label={`Danh sách ${label}`}>
            <form className="academic-filters" onSubmit={event => { event.preventDefault(); setQuery({ q: input.trim(), page: 1 }); }}>
                <div className="academic-field"><label htmlFor={`${directory}-search`}>{students ? "Tìm theo tên, mã học viên, email hoặc số di động" : "Tìm theo tên, email hoặc số di động"}</label>
                    <input ref={searchInput} id={`${directory}-search`} type="search" maxLength={200} value={input} onChange={event => setInput(event.target.value)} />
                </div>
                <button className="primary" type="submit">Tìm kiếm</button>
            </form>
            <ReadFeedback result={result} />
            <FormError error={staffLock.error} prefix="staff-lock" />
            {isConflict(staffLock.error) && <button className="secondary" type="button" onClick={() => {
                void staffLock.run(async () => undefined, result.retry);
            }}>Tải lại danh sách</button>}
            {notice && <p className="notice" role="status">{notice}</p>}
            {data && <>
                <p role="status">{data.total ? `${data.total} tài khoản ${label}` : `Không tìm thấy ${label} phù hợp.`}</p>
                {!!data.items.length && <div className="academic-table-scroll" tabIndex={0} role="region" aria-label={`Bảng ${label}`}>
                    <table className="academic-data-table">
                        <thead><tr><th scope="col">Họ và tên</th>{students && <th scope="col">Mã học viên</th>}<th scope="col">Email</th><th scope="col">Trạng thái</th><th scope="col">Thao tác</th></tr></thead>
                        <tbody>{data.items.map(item => <tr key={item.id}>
                            <th scope="row">{item.fullName}</th>{students && <td>{item.studentCode ?? "—"}</td>}
                            <td>{item.email}</td><td><span className="pill">{item.isLocked ? "Đã khóa" : "Hoạt động"}</span></td>
                            <td>{students
                                ? <button className="secondary" type="button" onClick={event => { returnFocus.current = event.currentTarget; setSelected(item.id); }}>Xem hồ sơ<span className="sr-only"> {item.fullName}</span></button>
                                : <button className="secondary" type="button" disabled={staffLock.busy} onClick={() => toggleStaff(item)}>{lockLabel(item)}<span className="sr-only"> {item.fullName}</span></button>}
                            </td>
                        </tr>)}</tbody>
                    </table>
                </div>}
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} label={`Phân trang ${label}`} change={page => setQuery(previous => ({ ...previous, page }))} />
            </>}
        </section>
        {students && selected && <StudentDetails key={selected} id={selected} isAdmin={isAdmin} refreshList={result.retry} close={() => {
            setSelected("");
            (returnFocus.current?.isConnected ? returnFocus.current : searchInput.current)?.focus();
        }} />}
        {!students && <AccountCreate staffOnly onCreated={result.retry} />}
    </>;
}

function StudentDetails({ id, isAdmin, refreshList, close }: { id: string; isAdmin: boolean; refreshList: () => void; close: () => void }) {
    const load = useCallback(() => getManagedStudent(id), [id]);
    const result = useRead(load);
    const heading = useRef<HTMLHeadingElement>(null);
    useEffect(() => { heading.current?.focus(); }, []);
    return <section className="panel identity-panel" aria-labelledby="student-contact-heading">
        <div className="identity-section-heading"><h2 id="student-contact-heading" tabIndex={-1} ref={heading}>Hồ sơ học viên</h2>
            <button type="button" className="secondary" onClick={close}>Đóng hồ sơ</button></div>
        <ReadFeedback result={result} />
        {result.data && <StudentContactForm key={result.data.revision} initial={result.data} isAdmin={isAdmin}
            refreshList={refreshList} reload={result.retry} />}
    </section>;
}

function StudentContactForm({ initial, isAdmin, refreshList, reload }: {
    initial: ManagedAccount; isAdmin: boolean; refreshList: () => void; reload: () => void;
}) {
    const [student, setStudent] = useState(initial);
    const [name, setName] = useState(initial.fullName);
    const [email, setEmail] = useState(initial.email);
    const [phone, setPhone] = useState(initial.phone ?? "");
    const [notice, setNotice] = useState("");
    const action = useAction();
    const prefix = "student-contact";
    const dirty = name !== student.fullName || email !== student.email || phone !== (student.phone ?? "");

    function save(event: FormEvent) {
        event.preventDefault();
        setNotice("");
        void action.run(signal => updateManagedStudent(student.id, {
            fullName: name.trim(), email: email.trim(), phone: normalizeVietnamMobile(phone, student.phone), expectedRevision: student.revision,
        }, signal), saved => {
            setStudent(saved); setName(saved.fullName); setEmail(saved.email); setPhone(saved.phone ?? "");
            setNotice("Đã lưu thông tin liên hệ."); refreshList();
        });
    }

    const filter = new URLSearchParams({ studentId: student.id });
    return <>
        <p>Mã học viên: <strong>{student.studentCode ?? "—"}</strong> · {student.isLocked ? "Đã khóa" : "Hoạt động"}</p>
        <div className="identity-actions"><Link className="secondary inline" to={`/enrollment?${filter}`}>Quản lý thành viên lớp</Link>
            <Link className="secondary inline" to={`/invoices?${filter}`}>Xem hóa đơn</Link></div>
        <form onSubmit={save} aria-busy={action.busy}>
            <FormError error={action.error} prefix={prefix} />
            {notice && <p className="notice" role="status">{notice}</p>}
            <fieldset disabled={action.busy}>
                <legend className="sr-only">Thông tin liên hệ</legend>
                <label htmlFor={`${prefix}-fullName`}>Họ và tên
                    <input id={`${prefix}-fullName`} required maxLength={200} value={name} onChange={event => setName(event.target.value)} {...errorAttributes(action.error, "fullName", prefix)} />
                    <InlineError error={action.error} field="fullName" prefix={prefix} />
                </label>
                <label htmlFor={`${prefix}-email`}>Email đăng nhập
                    <input id={`${prefix}-email`} required type="email" maxLength={254} value={email} onChange={event => setEmail(event.target.value)} {...errorAttributes(action.error, "email", prefix)} />
                    <InlineError error={action.error} field="email" prefix={prefix} />
                </label>
                <VietnamMobileField prefix={prefix} error={action.error} value={phone} change={setPhone} />
                <div className="identity-actions"><button className="primary" disabled={!dirty}>Lưu liên hệ</button>
                    {isAdmin && <button className="secondary" type="button" onClick={() => {
                        if (!confirmLock(student)) return;
                        setNotice("");
                        void action.run(signal => setManagedAccountLock("students", student.id, !student.isLocked, student.revision, signal), saved => {
                            setStudent(saved); setNotice(lockNotice(saved)); refreshList();
                        });
                    }}>{lockLabel(student)}</button>}
                    {isConflict(action.error) && <button className="text-button" type="button" onClick={() => {
                        if (!dirty || window.confirm("Tải lại sẽ bỏ các thay đổi chưa lưu. Tiếp tục?")) reload();
                    }}>Tải lại hồ sơ</button>}
                </div>
            </fieldset>
        </form>
    </>;
}

const isConflict = (error: unknown) => error instanceof ApiError && error.status === 409;
const lockLabel = (account: ManagedAccount) => account.isLocked ? "Mở khóa tài khoản" : "Khóa tài khoản";
const lockNotice = (account: ManagedAccount) => `${account.isLocked ? "Đã khóa" : "Đã mở khóa"} tài khoản của ${account.fullName}.`;
const confirmLock = (account: ManagedAccount) => window.confirm(account.isLocked
    ? `Mở khóa tài khoản của ${account.fullName}?` : `Khóa tài khoản của ${account.fullName} và thu hồi phiên làm mới?`);
