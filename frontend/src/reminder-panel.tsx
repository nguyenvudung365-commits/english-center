import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useRead } from "./academic-pages";
import { listMyAssignments } from "./coursework";
import { formatVnDateTime } from "./format";
import { upcoming } from "./reminders";
import { listSessions } from "./schedule";
import { ErrorMessage } from "./ui";

export function ReminderPanel({ student }: { student: boolean }) {
    const [now, setNow] = useState(Date.now);
    const refresh = useCallback(() => setNow(Date.now()), []);
    useEffect(() => {
        const refreshWhenVisible = () => { if (!document.hidden) refresh(); };
        const timer = window.setInterval(refreshWhenVisible, 60_000);
        document.addEventListener("visibilitychange", refreshWhenVisible);
        return () => { window.clearInterval(timer); document.removeEventListener("visibilitychange", refreshWhenVisible); };
    }, [refresh]);
    const loadSessions = useCallback(() => listSessions({
        fromUtc: new Date(now).toISOString(), toUtc: new Date(now + 48 * 3600000 + 1).toISOString(),
    }), [now]);
    const loadAssignments = useCallback(async () => {
        if (!student) return [];
        const readStatus = async (status: "Open" | "Upcoming") => {
            const items = [];
            for (let page = 1; ; page++) {
                const data = await listMyAssignments({ status, submissionStatus: "NotSubmitted", page, pageSize: 50 });
                items.push(...data.items);
                if (!data.items.length || data.page * data.pageSize >= data.total) return items;
            }
        };
        const pages = await Promise.all([readStatus("Upcoming"), readStatus("Open")]);
        return [...new Map(pages.flat().map(item => [item.assignment.id, item])).values()];
    }, [student]);
    const sessions = useRead(loadSessions);
    const assignments = useRead(loadAssignments);
    const nextSessions = upcoming((sessions.data ?? []).filter(item => !item.isCancelled), item => item.startsAtUtc, now, 48);
    const nextAssignments = upcoming((assignments.data ?? []).filter(item => item.isObligated), item => item.assignment.closesAtUtc, now, 48);
    return <section className="panel" aria-labelledby="upcoming-heading">
        <h2 id="upcoming-heading">Nhắc việc trong 48 giờ tới</h2>
        {(sessions.loading || assignments.loading) && <p role="status">Đang tải nhắc việc…</p>}
        <ErrorMessage error={sessions.error} />
        <ErrorMessage error={assignments.error} />
        {sessions.error ? <button className="secondary" onClick={refresh}>Thử tải lại lịch</button> : null}
        {assignments.error ? <button className="secondary" onClick={assignments.retry}>Thử tải lại hạn nộp</button> : null}
        {!sessions.loading && !assignments.loading && !sessions.error && !assignments.error && !nextSessions.length && !nextAssignments.length && <p>Không có buổi học hoặc hạn nộp sắp tới.</p>}
        {(nextSessions.length > 0 || nextAssignments.length > 0) && <ul className="catalog-list">
            {nextSessions.slice(0, 5).map(item => <li key={item.id}>
                <Link to={`/schedule/${item.id}`}>{item.className}: {item.lesson}</Link>
                <p>Buổi học · {formatVnDateTime(item.startsAtUtc)}</p>
            </li>)}
            {nextAssignments.slice(0, 5).map(({ assignment: item }) => <li key={item.id}>
                <Link to={`/assignments/${item.id}`}>{item.title}</Link>
                <p>Hạn nộp · {formatVnDateTime(item.closesAtUtc)}{Date.parse(item.opensAtUtc) > now && <> · Mở {formatVnDateTime(item.opensAtUtc)}</>}</p>
            </li>)}
        </ul>}
        {nextSessions.length > 5 && <Link className="secondary inline" to="/schedule">Xem lịch học</Link>}
        {nextAssignments.length > 5 && <Link className="secondary inline" to="/assignments">Xem bài cần nộp</Link>}
    </section>;
}
