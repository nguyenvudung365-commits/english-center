import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Link, useLocation, useParams, useSearchParams } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity, type Role } from "./api";
import { canManageAcademic, listClasses, type ClassDetail } from "./academic";
import { useRead } from "./academic-pages";
import {
    attendanceStatuses, getAttendanceSummary, listAttendance, listAttendanceHistory, listAttendanceReplays,
    listSessionAttendance, recordAttendance, replayAttendance,
    type AttendanceRow, type AttendanceStatus, type Page, type RosterItem, type Summary, type SummaryQuery,
} from "./attendance";
import { calendarRange, centerDate, listSessions } from "./schedule";
import { ErrorMessage, PageHeading } from "./ui";

export const attendanceLabels: Record<AttendanceStatus, string> = {
    Present: "Có mặt", AbsentUnexcused: "Vắng không phép", AbsentExcused: "Vắng có phép",
    Late: "Đi muộn", LateOver20Min: "Muộn trên 20 phút", LeftEarly: "Về sớm",
};
const countKeys = ["present", "absentUnexcused", "absentExcused", "late", "lateOver20Min", "leftEarly"] as const;
export const academicTime = (instant: string) => new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh", hourCycle: "h23",
}).format(new Date(instant));

export async function allVisibleClasses(): Promise<ClassDetail[]> {
    const generation = getSessionGeneration();
    const items = new Map<string, ClassDetail>();
    for (let page = 1; ; page++) {
        const result = await listClasses({ page, pageSize: 100 });
        if (generation !== getSessionGeneration()) throw new ApiError(401, "Phiên đã thay đổi.");
        if (result.page !== page || result.pageSize !== 100 || !Number.isSafeInteger(result.total) || result.total < 0)
            throw new ApiError(0, "Phản hồi phân trang lớp không hợp lệ.");
        for (const item of result.items) items.set(item.id, item);
        if (page * result.pageSize >= result.total) return [...items.values()];
        if (!result.items.length) throw new ApiError(0, "Danh sách lớp thay đổi trong khi tải. Vui lòng tải lại.");
    }
}

export function ReadFeedback({ result }: { result: { loading: boolean; error: unknown; retry: () => void } }) {
    return <>
        {result.loading && <p role="status">Đang tải dữ liệu…</p>}
        <ErrorMessage error={result.error} />
        {result.error ? <button className="secondary" onClick={result.retry}>Thử tải lại dữ liệu</button> : null}
    </>;
}

function Pager({ data, change, label }: { data: Omit<Page<unknown>, "items">; change: (page: number) => void; label: string }) {
    return <div className="academic-actions data-pagination" role="group" aria-label={label}>
        <span role="status">{data.total} mục · Trang {data.page} / {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
        <button className="secondary" disabled={data.page <= 1} onClick={() => change(data.page - 1)}>Trang trước</button>
        <button className="secondary" disabled={data.page * data.pageSize >= data.total} onClick={() => change(data.page + 1)}>Trang sau</button>
    </div>;
}

function DataTable({ label, children }: { label: string; children: ReactNode }) {
    return <div className="academic-table-scroll" role="region" aria-label={label} tabIndex={0}>{children}</div>;
}

export function AttendanceSummary({ data }: { data: Summary }) {
    return <section className="attendance-summary" aria-label="Tổng hợp điểm danh toàn bộ bộ lọc">
        <h2>Tổng hợp điểm danh</h2>
        <p>{data.recordedCount} bản ghi trong toàn bộ bộ lọc, không chỉ trang đang xem. Chưa ghi nhận không được tính là vắng.</p>
        <dl className="academic-metrics">
            {attendanceStatuses.map((status, index) => <div key={status}><dt>{attendanceLabels[status]}</dt><dd>{data.counts[countKeys[index]]} <span>({data.percentages[countKeys[index]]}%)</span></dd></div>)}
        </dl>
        <p className="small muted">Tổng vắng: {data.absentCount} · Tổng muộn: {data.lateCount} · Về sớm: {data.leftEarlyCount}. Tỷ lệ do máy chủ cung cấp, làm tròn riêng từng trạng thái.</p>
    </section>;
}

export function AttendancePage() {
    const user = identity(getSession());
    const { sessionId = "" } = useParams();
    const location = useLocation();
    if (!user) return null;
    return <AttendanceWorkspace key={`${getSessionGeneration()}:${user.id}:${user.role}:${sessionId}:${location.search}`} role={user.role} sessionId={sessionId} />;
}

function AttendanceWorkspace({ role, sessionId }: { role: Role; sessionId: string }) {
    const [parameters] = useSearchParams();
    const classId = parameters.get("classId") ?? "";
    return <>
        <PageHeading title={role === "Student" ? "Điểm danh của tôi" : "Điểm danh"} subtitle={role === "Student"
            ? "Chỉ hiển thị bản ghi của bạn, kể cả lớp đã ngừng ghi danh. Không truy vấn danh sách học viên khác."
            : "Giáo viên ghi nhận trong thời hạn máy chủ cho phép. Giáo vụ và quản trị viên chỉ xem, kiểm tra lịch sử và phát lại sự kiện."} />
        <div className="academic-actions"><Link className="secondary inline" to="/schedule">Lịch học</Link>{sessionId && <Link className="secondary inline" to="/attendance">Về danh sách điểm danh</Link>}</div>
        {!sessionId && role !== "Student" && <AttendancePicker classId={classId} />}
        {sessionId && role !== "Student" ? <SessionRoster sessionId={sessionId} role={role} />
            : <AttendanceRecords role={role} sessionId={sessionId} classId={classId} />}
    </>;
}

function AttendancePicker({ classId }: { classId: string }) {
    const classes = useRead(allVisibleClasses);
    const [parameters, setParameters] = useSearchParams();
    const [anchor, setAnchor] = useState(() => centerDate(new Date().toISOString()).slice(0, 7));
    const loadSessions = useCallback(async () => {
        if (!classId) return [];
        const range = calendarRange(`${anchor}-01`, "month");
        return listSessions({ classId, fromUtc: range.fromUtc, toUtc: range.toUtc });
    }, [classId, anchor]);
    const sessions = useRead(loadSessions);
    return <section className="panel attendance-panel">
        <h2>Chọn lớp và buổi học</h2>
        <ReadFeedback result={classes} />
        <div className="academic-filters">
            <label htmlFor="attendance-class">Lớp được phép xem
                <select id="attendance-class" value={classId} disabled={classes.loading || !!classes.error} onChange={(event) => {
                    const next = new URLSearchParams(parameters);
                    if (event.target.value) next.set("classId", event.target.value); else next.delete("classId");
                    setParameters(next);
                }}>
                    <option value="">Chọn lớp học</option>
                    {classId && !classes.data?.some(item => item.id === classId) && <option value={classId}>Lớp đang chọn (máy chủ xác minh quyền)</option>}
                    {classes.data?.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
                </select>
            </label>
            <label htmlFor="attendance-session-month">Tháng lịch — giờ Việt Nam
                <input id="attendance-session-month" type="month" min="0002-01" max="9998-12" value={anchor} onChange={(event) => {
                    if (event.target.validity.valid && event.target.value) setAnchor(event.target.value);
                }} />
            </label>
        </div>
        {classes.data?.length === 0 && <p>Chưa có lớp được phép xem.</p>}
        {classId && <>
            <ReadFeedback result={sessions} />
            {sessions.data?.length === 0 && <p>Chưa có buổi học trong khoảng lịch tháng này.</p>}
            <ul className="catalog-list">{sessions.data?.map(item => <li key={item.id}>
                <Link className="accent" to={`/attendance/${item.id}`}>{item.classCode} · {item.lesson} · {academicTime(item.startsAtUtc)} (UTC+7)</Link>
            </li>)}</ul>
        </>}
    </section>;
}

function AttendanceRecords({ role, sessionId, classId }: { role: Role; sessionId: string; classId: string }) {
    const [parameters, setParameters] = useSearchParams();
    const [page, setPage] = useState(1);
    const status = parameters.get("status") ?? "";
    const filterSession = sessionId || parameters.get("sessionId") || "";
    const filterStudent = role === "Student" ? "" : parameters.get("studentId") || "";
    const query = useMemo<SummaryQuery>(() => ({
        ...(classId ? { classId } : {}), ...(filterSession ? { sessionId: filterSession } : {}),
        ...(filterStudent ? { studentId: filterStudent } : {}), ...(status ? { status: status as AttendanceStatus } : {}),
    }), [classId, filterSession, filterStudent, status]);
    const canRead = role !== "Teacher" || !!classId;
    const load = useCallback(() => canRead ? listAttendance({ ...query, page }) : Promise.resolve(null), [canRead, query, page]);
    const loadSummary = useCallback(() => canRead ? getAttendanceSummary(query) : Promise.resolve(null), [canRead, query]);
    const records = useRead(load);
    const summary = useRead(loadSummary);
    const [selected, setSelected] = useState<AttendanceRow | null>(null);
    return <>
        <section className="panel attendance-panel">
            <h2>{role === "Student" ? "Bản ghi của tôi" : "Tra cứu bản ghi"}</h2>
            <form className="academic-filters" onSubmit={(event) => {
                event.preventDefault();
                const values = new FormData(event.currentTarget);
                const next = new URLSearchParams();
                for (const key of ["classId", "sessionId", "studentId", "status"]) {
                    const value = String(values.get(key) ?? "").trim();
                    if (value && !(role === "Student" && key === "studentId")) next.set(key, value);
                }
                setParameters(next);
            }}>
                <label htmlFor="attendance-filter-class">ID lớp (không bắt buộc)
                    <input id="attendance-filter-class" name="classId" defaultValue={classId} maxLength={36} />
                </label>
                {!sessionId && <label htmlFor="attendance-filter-session">ID buổi học (không bắt buộc)
                    <input id="attendance-filter-session" name="sessionId" defaultValue={filterSession} maxLength={36} />
                </label>}
                {role !== "Student" && <label htmlFor="attendance-filter-student">ID học viên (không bắt buộc)
                    <input id="attendance-filter-student" name="studentId" defaultValue={filterStudent} maxLength={36} />
                </label>}
                <label htmlFor="attendance-filter-status">Trạng thái
                    <select id="attendance-filter-status" name="status" defaultValue={status}>
                        <option value="">Tất cả trạng thái</option>
                        {attendanceStatuses.map(value => <option key={value} value={value}>{attendanceLabels[value]}</option>)}
                    </select>
                </label>
                <button className="primary">Lọc bản ghi</button>
            </form>
            {role === "Student" && <p className="small muted">Để trống ID lớp để xem cả lịch sử sau khi ngừng ghi danh. Tất cả bộ lọc luôn chỉ áp dụng cho chính bạn.</p>}
            {!canRead ? <p>Chọn lớp được phân công để tra cứu điểm danh.</p> : <>
                <ReadFeedback result={records} />
                {records.data && <>
                    {!records.data.items.length && <p>Không có bản ghi phù hợp. Không có bản ghi không đồng nghĩa với vắng học.</p>}
                    {!!records.data.items.length && <DataTable label="Danh sách bản ghi điểm danh">
                        <table className="academic-data-table"><caption>Trạng thái mới nhất của các bản ghi phù hợp</caption>
                            <thead><tr><th scope="col">Buổi học / lớp</th><th scope="col">{role === "Student" ? "Trạng thái" : "Học viên / trạng thái"}</th><th scope="col">Cập nhật (UTC+7)</th><th scope="col">Lịch sử</th></tr></thead>
                            <tbody>{records.data.items.map(row => <tr key={row.id}>
                                <td><Link className="accent" to={`/attendance/${row.sessionId}`}>Buổi {row.sessionId}</Link><br /><span className="small">Lớp {row.classId}</span></td>
                                <td>{role !== "Student" && <><span className="small">{row.studentId}</span><br /></>}{attendanceLabels[row.status]} · v{row.revision}</td>
                                <td><time dateTime={row.updatedAtUtc}>{academicTime(row.updatedAtUtc)}</time></td>
                                <td><button className="secondary" aria-expanded={selected?.id === row.id} aria-controls="attendance-history" onClick={() => setSelected(selected?.id === row.id ? null : row)}>Xem lịch sử</button></td>
                            </tr>)}</tbody>
                        </table>
                    </DataTable>}
                    <Pager data={records.data} change={(value) => { setSelected(null); setPage(value); }} label="Phân trang bản ghi điểm danh" />
                </>}
            </>}
        </section>
        {canRead && <section className="panel attendance-panel"><ReadFeedback result={summary} />{summary.data && <AttendanceSummary data={summary.data} />}</section>}
        {selected && <AttendanceHistory key={selected.id} row={selected} manager={canManageAcademic(role)} />}
    </>;
}

function SessionRoster({ sessionId, role }: { sessionId: string; role: Role }) {
    const [page, setPage] = useState(1);
    const [notice, setNotice] = useState("");
    const load = useCallback(() => listSessionAttendance(sessionId, { page }), [sessionId, page]);
    const roster = useRead(load);
    const classId = roster.data?.classId;
    const loadSummary = useCallback(() => classId ? getAttendanceSummary({ sessionId, classId }) : Promise.resolve(null), [sessionId, classId]);
    const summary = useRead(loadSummary);
    const [selected, setSelected] = useState<AttendanceRow | null>(null);
    const reload = () => { roster.retry(); summary.retry(); setSelected(null); };
    return <>
        <section className="panel attendance-panel">
            <h2>Danh sách điểm danh buổi học</h2>
            <p className="small">ID buổi học: {sessionId}</p>
            <p role="status">{notice}</p>
            <ReadFeedback result={roster} />
            <button className="secondary" onClick={reload}>Tải lại danh sách và tổng hợp</button>
            {roster.data && <>
                <p>Hạn ghi nhận do máy chủ cung cấp: <time dateTime={roster.data.writeClosesAtUtc}>{academicTime(roster.data.writeClosesAtUtc)}</time> (UTC+7).</p>
                <p>{role !== "Teacher" ? "Chế độ quản lý: chỉ đọc và kiểm tra lịch sử, không ghi điểm danh."
                    : roster.data.canWrite ? "Máy chủ hiện cho phép ghi nhận cho học viên còn hoạt động." : "Máy chủ hiện không cho phép ghi nhận. Chỉ xem dữ liệu."}</p>
                <p className="small muted">Thời điểm đọc: {academicTime(roster.data.asOfUtc)} (UTC+7). Máy chủ kiểm tra lại quyền và thời hạn khi lưu.</p>
                {!roster.data.items.length && <p>Chưa có học viên hoặc bản ghi trong trang này.</p>}
                {!!roster.data.items.length && <DataTable label="Danh sách điểm danh học viên">
                    <table className="academic-data-table"><caption>Học viên hiện tại và bản ghi lịch sử của buổi học</caption>
                        <thead><tr><th scope="col">Học viên</th><th scope="col">Trạng thái mới nhất</th><th scope="col">Thao tác</th></tr></thead>
                        <tbody>{roster.data.items.map(item => <RosterRow key={`${item.studentId}:${item.attendance?.revision ?? 0}`} item={item} sessionId={sessionId}
                            writable={role === "Teacher" && roster.data!.canWrite && item.isActive}
                            saved={() => { setSelected(null); summary.retry(); }} history={setSelected} conflict={() => {
                                setNotice("Xung đột phiên bản (409): bản ghi đã thay đổi. Đang tải lại dữ liệu mới nhất; không tự ghi đè hoặc gửi lại. Hãy kiểm tra rồi chọn trạng thái lại.");
                                reload();
                            }} />)}</tbody>
                    </table>
                </DataTable>}
                <Pager data={roster.data} label="Phân trang danh sách học viên" change={(value) => { setSelected(null); setNotice(""); setPage(value); }} />
            </>}
        </section>
        <section className="panel attendance-panel"><ReadFeedback result={summary} />{summary.data && <AttendanceSummary data={summary.data} />}</section>
        {selected && <AttendanceHistory key={selected.id} row={selected} manager={canManageAcademic(role)} />}
    </>;
}

function RosterRow({ item, sessionId, writable, saved, history, conflict }: {
    item: RosterItem; sessionId: string; writable: boolean; saved: () => void;
    history: (row: AttendanceRow) => void; conflict: () => void;
}) {
    const [row, setRow] = useState(item.attendance);
    const [status, setStatus] = useState<AttendanceStatus | "">(row?.status ?? "");
    const [pending, setPending] = useState(false);
    const [error, setError] = useState<unknown>();
    const [notice, setNotice] = useState("");
    const [mustReload, setMustReload] = useState(false);
    const alive = useRef(false);
    const lock = useRef(false);
    useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
    const label = item.fullName ?? "Tên không còn khả dụng";
    return <tr>
        <th scope="row"><span>{label}</span><br /><span className="small">{item.studentCode ?? "Mã học viên không còn khả dụng"}</span><br />
            <span className="small muted">{item.isActive ? "Đang hoạt động" : "Không còn hoạt động · chỉ đọc"}</span>
        </th>
        <td><p>{row ? `${attendanceLabels[row.status]} · phiên bản ${row.revision}` : "Chưa ghi nhận (Unrecorded)"}</p>
            {row && <p className="small">Cập nhật {academicTime(row.updatedAtUtc)} (UTC+7)</p>}
            {writable && <form className="attendance-row-form" aria-busy={pending} onSubmit={async (event) => {
                event.preventDefault();
                if (lock.current || !status || !writable || mustReload) return;
                lock.current = true; setPending(true); setError(null); setNotice("");
                const generation = getSessionGeneration();
                try {
                    const updated = await recordAttendance(sessionId, item.studentId, { status, expectedRevision: row?.revision ?? null });
                    if (!alive.current || generation !== getSessionGeneration()) return;
                    setRow(updated); setStatus(updated.status); setNotice("Đã lưu trên máy chủ."); saved();
                } catch (failure) {
                    if (!alive.current || generation !== getSessionGeneration()) return;
                    if (failure instanceof ApiError && failure.status === 409) conflict();
                    else {
                        setError(failure);
                        if (failure instanceof ApiError && [0, 403, 503].includes(failure.status)) setMustReload(true);
                    }
                } finally {
                    if (alive.current && generation === getSessionGeneration()) { lock.current = false; setPending(false); }
                }
            }}>
                <label htmlFor={`attendance-status-${item.studentId}`}>Trạng thái cho {label}</label>
                <select id={`attendance-status-${item.studentId}`} value={status} disabled={pending || mustReload} required aria-invalid={error instanceof ApiError && error.status === 400}
                    aria-describedby={error ? `attendance-error-${item.studentId}` : undefined} onChange={event => { setStatus(event.target.value as AttendanceStatus | ""); setError(null); setNotice(""); }}>
                    <option value="" disabled>Chưa ghi nhận — chọn trạng thái</option>
                    {attendanceStatuses.map(value => <option key={value} value={value}>{attendanceLabels[value]}</option>)}
                </select>
                <button className="primary" disabled={pending || !status || mustReload}>{pending ? "Đang lưu…" : "Lưu điểm danh"}</button>
            </form>}
            <div id={`attendance-error-${item.studentId}`}><ErrorMessage error={error} /></div>
            {mustReload && <p role="alert">Chưa thể tiếp tục ghi nhận. Hãy dùng “Tải lại danh sách và tổng hợp” để kiểm tra quyền và kết quả trước khi lưu lại.</p>}
            <p role="status">{notice}</p>
        </td>
        <td>{row ? <button className="secondary" disabled={pending} aria-controls="attendance-history" onClick={() => history(row)}>Xem lịch sử</button> : "Chưa có lịch sử"}</td>
    </tr>;
}

function AttendanceHistory({ row, manager }: { row: AttendanceRow; manager: boolean }) {
    const heading = useRef<HTMLHeadingElement>(null);
    useEffect(() => { heading.current?.focus(); }, []);
    const [page, setPage] = useState(1);
    const load = useCallback(() => listAttendanceHistory(row.id, { page }), [row.id, page]);
    const history = useRead(load);
    return <section id="attendance-history" className="panel attendance-panel" aria-labelledby="attendance-history-title">
        <h2 id="attendance-history-title" tabIndex={-1} ref={heading}>Lịch sử phiên bản điểm danh</h2>
        <p className="small">Bản ghi {row.id} · học viên {row.studentId}</p>
        <ReadFeedback result={history} />
        {history.data && <>
            {!history.data.items.length && <p>Không có phiên bản trong trang này.</p>}
            <DataTable label="Lịch sử phiên bản"><table className="academic-data-table"><caption>Lịch sử bất biến, phiên bản mới nhất trước</caption>
                <thead><tr><th scope="col">Phiên bản</th><th scope="col">Trước</th><th scope="col">Sau</th><th scope="col">Người thay đổi</th><th scope="col">Thời điểm (UTC+7)</th></tr></thead>
                <tbody>{history.data.items.map(item => <tr key={item.revision}><th scope="row">{item.revision}</th>
                    <td>{item.previousStatus === null ? "Chưa ghi nhận" : attendanceLabels[item.previousStatus as AttendanceStatus] ?? item.previousStatus}</td>
                    <td>{attendanceLabels[item.status]}</td><td>{item.changedBy}</td><td><time dateTime={item.changedAtUtc}>{academicTime(item.changedAtUtc)}</time></td>
                </tr>)}</tbody>
            </table></DataTable>
            <Pager data={history.data} change={setPage} label="Phân trang lịch sử phiên bản" />
        </>}
        {manager && <ReplayAudit row={row} />}
    </section>;
}

function ReplayAudit({ row }: { row: AttendanceRow }) {
    const [page, setPage] = useState(1);
    const load = useCallback(() => listAttendanceReplays(row.id, { page }), [row.id, page]);
    const audit = useRead(load);
    const [revision, setRevision] = useState(String(row.revision));
    const [reason, setReason] = useState("");
    const [pending, setPending] = useState(false);
    const [error, setError] = useState<unknown>();
    const [notice, setNotice] = useState("");
    const lock = useRef(false);
    const alive = useRef(false);
    useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
    return <section className="replay-audit" aria-labelledby="replay-heading">
        <h3 id="replay-heading">Khôi phục phát sự kiện và nhật ký</h3>
        <p>Phát lại không sửa điểm danh. “Đã phát” chỉ xác nhận lời gọi bus nội bộ, không bảo đảm bên nhận đã xử lý. “Đang chờ” có thể cần kiểm tra sau gián đoạn.</p>
        <form className="academic-form" aria-busy={pending} onSubmit={async (event) => {
            event.preventDefault();
            if (lock.current) return;
            lock.current = true; setPending(true); setError(null); setNotice("");
            const generation = getSessionGeneration();
            try {
                const result = await replayAttendance(row.id, { revision: Number(revision), reason });
                if (!alive.current || generation !== getSessionGeneration()) return;
                setNotice(`Đã phát lại phiên bản ${result.revision} qua bus nội bộ. Mã lần thử: ${result.attemptId}.`);
                setReason(""); audit.retry();
            } catch (failure) {
                if (alive.current && generation === getSessionGeneration()) setError(failure);
            } finally {
                if (alive.current && generation === getSessionGeneration()) { lock.current = false; setPending(false); }
            }
        }}>
            <label htmlFor="replay-revision">Phiên bản cần phát lại (có thể là phiên bản cũ)
                <input id="replay-revision" type="number" required min={1} step={1} value={revision} disabled={pending} aria-describedby={error ? "replay-error" : undefined} onChange={event => { setRevision(event.target.value); setError(null); setNotice(""); }} />
            </label>
            <label htmlFor="replay-reason">Lý do phát lại
                <textarea id="replay-reason" required maxLength={500} value={reason} disabled={pending} aria-describedby={error ? "replay-error" : undefined} onChange={event => { setReason(event.target.value); setError(null); setNotice(""); }} />
            </label>
            <div id="replay-error"><ErrorMessage error={error} /></div>
            {error instanceof ApiError && error.status === 503 && <p role="alert">Phát lại thất bại (503). Lần thử thất bại đã được máy chủ lưu vào nhật ký; nguồn sự kiện còn để khôi phục. Tải lại nhật ký, kiểm tra trước khi chủ động thử lại. Không tự gửi lại yêu cầu.</p>}
            {error instanceof ApiError && error.status === 0 && <p role="alert">Chưa xác nhận kết quả phát lại. Tải nhật ký và kiểm tra trước khi thử lại để tránh phát trùng.</p>}
            <p role="status">{notice}</p>
            <button className="primary" disabled={pending || !reason.trim() || !/^\d+$/.test(revision) || Number(revision) < 1}>{pending ? "Đang phát lại…" : "Phát lại phiên bản"}</button>
        </form>
        <h3>Nhật ký phát lại</h3>
        <button className="secondary" disabled={pending} onClick={audit.retry}>Tải lại nhật ký phát lại</button>
        <ReadFeedback result={audit} />
        {audit.data && <>
            {!audit.data.items.length && <p>Chưa có lần phát lại nào trong trang này.</p>}
            <DataTable label="Nhật ký phát lại"><table className="academic-data-table"><caption>Các lần thử khôi phục sự kiện</caption>
                <thead><tr><th scope="col">Phiên bản / trạng thái</th><th scope="col">Lý do / người yêu cầu</th><th scope="col">Yêu cầu (UTC+7)</th><th scope="col">Hoàn tất (UTC+7)</th></tr></thead>
                <tbody>{audit.data.items.map(item => <tr key={item.id}><th scope="row">v{item.revision} · {({ Pending: "Đang chờ", Failed: "Thất bại", Dispatched: "Đã phát qua bus nội bộ" })[item.dispatchStatus]}</th>
                    <td>{item.reason}<br /><span className="small">{item.requestedBy}</span></td><td>{academicTime(item.requestedAtUtc)}</td><td>{item.completedAtUtc ? academicTime(item.completedAtUtc) : "Chưa xác nhận hoàn tất"}</td>
                </tr>)}</tbody>
            </table></DataTable>
            <Pager data={audit.data} change={setPage} label="Phân trang nhật ký phát lại" />
        </>}
    </section>;
}
