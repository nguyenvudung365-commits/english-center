import { useCallback, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { isAcademicId } from "./academic";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import { formatVnDateTime } from "./format";
import {
    canManagePlacement, canReadMonitoring, createPlacementTest, emptyPlacementDraft, listPlacementTests, listWarnings,
    placementModeLabel, placementServerErrors, placementStatusLabel, placementToDraft, updatePlacementTest, validatePlacementDraft,
    warningLabel, type PlacementDraft, type PlacementTest,
} from "./monitoring";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";

function StudentFilter({ onApply, busy = false }: { onApply: (studentId: string) => void; busy?: boolean }) {
    const [input, setInput] = useState("");
    const error = input.trim() && !isAcademicId(input.trim()) ? ["ID học viên phải là UUID hợp lệ."] : undefined;
    return <form className="academic-filters" noValidate onSubmit={(event) => { event.preventDefault(); if (!error) onApply(input.trim()); }}>
        <div className="academic-field">
            <label htmlFor="monitoring-student">Lọc theo ID học viên (UUID, để trống để xem tất cả)</label>
            <input {...fieldProps("monitoring-student", error)} value={input} onChange={(event) => setInput(event.target.value)} />
            <FieldError id="monitoring-student" errors={error} />
        </div>
        <button className="primary" disabled={!!error || busy}>Lọc</button>
    </form>;
}

function Denied({ title }: { title: string }) {
    return <>
        <PageHeading title={title} subtitle="Mục này dành cho học viên, giáo vụ và quản trị viên." />
        <section className="panel"><p role="status">Tài khoản giáo viên không có quyền truy cập mục này.</p></section>
    </>;
}

export function Warnings() {
    const user = identity(getSession());
    if (!user) return null;
    if (!canReadMonitoring(user.role)) return <Denied title="Cảnh báo học tập" />;
    return <WarningList key={`${getSessionGeneration()}:${user.id}`} manager={user.role !== "Student"} />;
}

function WarningList({ manager }: { manager: boolean }) {
    const [query, setQuery] = useState({ studentId: "", page: 1 });
    const load = useCallback(() => listWarnings(query), [query]);
    const result = useRead(load);
    const data = result.data;
    return <>
        <PageHeading title={manager ? "Cảnh báo học tập" : "Cảnh báo của tôi"} subtitle="Cảnh báo phát sinh khi vắng mặt, đi trễ hoặc bỏ bài tập vượt giới hạn của ghi danh." />
        <section className="panel">
            {manager && <StudentFilter onApply={(studentId) => setQuery({ studentId, page: 1 })} />}
            <ReadFeedback result={result} />
            {data && <>
                {data.items.length === 0 && <p>{manager ? "Chưa có cảnh báo phù hợp." : "Bạn chưa có cảnh báo học tập nào."}</p>}
                <ul className="catalog-list">
                    {data.items.map((item) => <li key={item.id}>
                        <span className="pill">{warningLabel(item.warningType)}</span>
                        <p>{item.message}</p>
                        <p className="small muted">Phát sinh lúc <time dateTime={item.issuedAtUtc}>{formatVnDateTime(item.issuedAtUtc)}</time> (UTC+7)
                            {manager && <> · Học viên {item.studentId}</>}</p>
                        <Link className="secondary inline" to={`/classes/${item.classId}`}>Xem lớp</Link>
                    </li>)}
                </ul>
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={(page) => setQuery((previous) => ({ ...previous, page }))} label="Phân trang cảnh báo" />
            </>}
        </section>
    </>;
}

export function PlacementTests() {
    const user = identity(getSession());
    if (!user) return null;
    if (!canReadMonitoring(user.role)) return <Denied title="Hẹn test đầu vào" />;
    return <PlacementWorkspace key={`${getSessionGeneration()}:${user.id}`} manager={canManagePlacement(user.role)} />;
}

function PlacementWorkspace({ manager }: { manager: boolean }) {
    const [query, setQuery] = useState({ studentId: "", page: 1 });
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listPlacementTests(query), [query, version]);
    const result = useRead(load);
    const [draft, setDraft] = useState<PlacementDraft>(emptyPlacementDraft);
    const [editing, setEditing] = useState<PlacementTest | null>(null);
    const [attempted, setAttempted] = useState(false);
    const [notice, setNotice] = useState("");
    const save = useAction();
    const data = result.data;
    const creating = editing === null;
    const errors = validatePlacementDraft(draft, creating);
    const serverFields = save.error instanceof ApiError && save.error.status === 400 ? placementServerErrors(save.error.serverTitle ?? "") : {};
    const shown: Record<string, string[] | undefined> = { ...(attempted ? errors : {}), ...serverFields };
    const set = (patch: Partial<PlacementDraft>) => setDraft((previous) => ({ ...previous, ...patch }));
    const reset = () => { setDraft(emptyPlacementDraft); setEditing(null); setAttempted(false); };
    return <>
        <PageHeading title={manager ? "Hẹn test đầu vào" : "Lịch test đầu vào của tôi"} subtitle="Thời gian hiển thị theo giờ Việt Nam (UTC+7)." />
        {manager && <section className="panel">
            <h2>{editing ? "Sửa lịch hẹn" : "Tạo lịch hẹn"}</h2>
            <form className="academic-form" noValidate aria-busy={save.busy} onSubmit={(event) => {
                event.preventDefault();
                setAttempted(true);
                setNotice("");
                if (save.busy || Object.keys(errors).length) return;
                void save.run(() => (editing ? updatePlacementTest(editing.id, draft) : createPlacementTest(draft)), () => {
                    setNotice(editing ? "Đã lưu lịch hẹn." : "Đã tạo lịch hẹn.");
                    reset();
                    setVersion((value) => value + 1);
                });
            }}>
                <div className="academic-fields">
                    <div className="academic-field">
                        <label htmlFor="placement-student">ID học viên (UUID)</label>
                        <input {...fieldProps("placement-student", shown.studentId)} required disabled={save.busy || !creating} value={draft.studentId} onChange={(event) => set({ studentId: event.target.value })} />
                        <FieldError id="placement-student" errors={shown.studentId} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="placement-name">Tên bài kiểm tra</label>
                        <input {...fieldProps("placement-name", shown.testName)} required maxLength={200} disabled={save.busy} value={draft.testName} onChange={(event) => set({ testName: event.target.value })} />
                        <FieldError id="placement-name" errors={shown.testName} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="placement-location">Địa điểm</label>
                        <input {...fieldProps("placement-location", shown.location)} required maxLength={200} disabled={save.busy} value={draft.location} onChange={(event) => set({ location: event.target.value })} />
                        <FieldError id="placement-location" errors={shown.location} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="placement-mode">Hình thức</label>
                        <select {...fieldProps("placement-mode", shown.mode)} disabled={save.busy} value={draft.mode} onChange={(event) => set({ mode: event.target.value })}>
                            <option value="Offline">Trực tiếp</option><option value="Online">Trực tuyến</option>
                        </select>
                        <FieldError id="placement-mode" errors={shown.mode} />
                    </div>
                    <div className="academic-field">
                        <label htmlFor="placement-time">Ngày giờ hẹn (giờ Việt Nam)</label>
                        <input {...fieldProps("placement-time", shown.scheduledAt)} type="datetime-local" required disabled={save.busy} value={draft.scheduledAt} onChange={(event) => set({ scheduledAt: event.target.value })} />
                        <FieldError id="placement-time" errors={shown.scheduledAt} />
                    </div>
                    {editing && <div className="academic-field">
                        <label htmlFor="placement-status">Trạng thái</label>
                        <select {...fieldProps("placement-status", shown.status)} disabled={save.busy} value={draft.status} onChange={(event) => set({ status: event.target.value })}>
                            {["Pending", "Done", "Cancelled"].map((value) => <option key={value} value={value}>{placementStatusLabel(value)}</option>)}
                        </select>
                        <FieldError id="placement-status" errors={shown.status} />
                    </div>}
                    <div className="academic-field">
                        <label htmlFor="placement-teacher">ID giáo viên (UUID, không bắt buộc)</label>
                        <input {...fieldProps("placement-teacher", shown.teacherId)} disabled={save.busy} value={draft.teacherId} onChange={(event) => set({ teacherId: event.target.value })} />
                        <FieldError id="placement-teacher" errors={shown.teacherId} />
                    </div>
                </div>
                {Object.keys(serverFields).length === 0 && <ErrorMessage error={save.error} />}
                {notice && <p className="notice" role="status">{notice}</p>}
                <div className="academic-actions">
                    <button className="primary" disabled={save.busy}>{save.busy ? "Đang lưu…" : editing ? "Lưu lịch hẹn" : "Tạo lịch hẹn"}</button>
                    {editing && <button type="button" className="secondary" disabled={save.busy} onClick={reset}>Hủy sửa</button>}
                </div>
            </form>
        </section>}
        <section className="panel">
            <h2>Danh sách lịch hẹn</h2>
            {manager && <StudentFilter busy={save.busy} onApply={(studentId) => setQuery({ studentId, page: 1 })} />}
            <ReadFeedback result={result} />
            {data && <>
                {data.items.length === 0 && <p>Chưa có lịch hẹn test đầu vào.</p>}
                {data.items.length > 0 && <div className="academic-table-scroll" role="region" tabIndex={0} aria-label="Bảng lịch hẹn test đầu vào">
                    <table className="academic-data-table">
                        <caption>Lịch hẹn test đầu vào (giờ Việt Nam UTC+7)</caption>
                        <thead><tr>
                            <th scope="col">Bài kiểm tra</th><th scope="col">Thời gian</th><th scope="col">Địa điểm</th><th scope="col">Hình thức</th>
                            <th scope="col">Trạng thái</th>{manager && <th scope="col">Học viên</th>}{manager && <th scope="col">Thao tác</th>}
                        </tr></thead>
                        <tbody>{data.items.map((item) => <tr key={item.id}>
                            <th scope="row">{item.testName}</th>
                            <td><time dateTime={item.scheduledAtUtc}>{formatVnDateTime(item.scheduledAtUtc)}</time></td>
                            <td>{item.location}</td>
                            <td>{placementModeLabel(item.mode)}</td>
                            <td><span className="pill">{placementStatusLabel(item.status)}</span></td>
                            {manager && <td className="id">{item.studentId}</td>}
                            {manager && <td><button type="button" className="secondary" disabled={save.busy} onClick={() => {
                                setEditing(item); setDraft(placementToDraft(item)); setAttempted(false); setNotice("");
                                document.getElementById("placement-name")?.focus();
                            }}>Sửa</button></td>}
                        </tr>)}</tbody>
                    </table>
                </div>}
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={(page) => setQuery((previous) => ({ ...previous, page }))} label="Phân trang lịch hẹn" />
            </>}
        </section>
    </>;
}
