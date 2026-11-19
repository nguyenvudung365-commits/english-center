import { useCallback, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { getSession, getSessionGeneration, identity } from "./api";
import { useRead } from "./academic-pages";
import { academicTime, allVisibleClasses, AttendanceSummary, ReadFeedback, attendanceLabels } from "./attendance-pages";
import { attendanceStatuses } from "./attendance";
import { getClassProgress, getPersonalStatistics, type MonthPoint } from "./statistics";
import { PageHeading } from "./ui";

export function ProgressPage() {
    const user = identity(getSession());
    const { classId = "" } = useParams();
    if (!user) return null;
    return <ProgressWorkspace key={`${getSessionGeneration()}:${user.id}:${user.role}:${classId}`} classId={classId} />;
}

function ProgressWorkspace({ classId }: { classId: string }) {
    const classes = useRead(allVisibleClasses);
    const navigate = useNavigate();
    const load = useCallback(() => classId ? getClassProgress(classId) : Promise.resolve(null), [classId]);
    const progress = useRead(load);
    const selected = classes.data?.find(item => item.id === classId);
    return <>
        <PageHeading title="Tiến độ học tập" subtitle="Tiến độ lịch học của lớp hiện được phép xem. Số buổi và tỷ lệ do máy chủ tính, không suy ra từ điểm danh." />
        <div className="academic-actions"><Link className="secondary inline" to="/classes">Lớp học</Link><Link className="secondary inline" to="/schedule">Lịch học</Link></div>
        <section className="panel attendance-panel">
            <h2>Chọn lớp học</h2>
            <ReadFeedback result={classes} />
            <label htmlFor="progress-class">Tất cả lớp hiện được phép xem
                <select id="progress-class" value={classId} disabled={classes.loading || !!classes.error} onChange={event => navigate(event.target.value ? `/progress/${event.target.value}` : "/progress")}>
                    <option value="">Chọn lớp để xem tiến độ</option>
                    {classId && !selected && <option value={classId}>Lớp từ đường dẫn (máy chủ xác minh quyền)</option>}
                    {classes.data?.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}
                </select>
            </label>
            {classes.data?.length === 0 && <p>Chưa có lớp hiện được phép xem. Lớp đã ngừng ghi danh không được cấp quyền tiến độ từ lịch sử điểm danh.</p>}
            {!classId && <p>Chọn một lớp để xem số buổi hoàn thành, còn lại và tỷ lệ.</p>}
        </section>
        {classId && <section className="panel attendance-panel">
            <h2>{selected ? `${selected.code} · ${selected.name}` : "Tiến độ lớp đã chọn"}</h2>
            <ReadFeedback result={progress} />
            {progress.data && <>
                <dl className="academic-metrics">
                    <div><dt>Tổng buổi đã xếp lịch</dt><dd>{progress.data.totalSessions}</dd></div>
                    <div><dt>Đã hoàn thành</dt><dd>{progress.data.completedSessions}</dd></div>
                    <div><dt>Còn lại</dt><dd>{progress.data.remainingSessions}</dd></div>
                    <div><dt>Tỷ lệ hoàn thành</dt><dd>{progress.data.completionPercent}%</dd></div>
                </dl>
                <label htmlFor="class-completion">Tiến độ lớp: {progress.data.completionPercent}%</label>
                <progress id="class-completion" max={100} value={progress.data.completionPercent}>{progress.data.completionPercent}%</progress>
                <p className="small muted">Máy chủ chốt tại {academicTime(progress.data.asOfUtc)} (UTC+7). Buổi chỉ hoàn thành khi thời điểm kết thúc đã qua; không đồng nghĩa đã có mặt hoặc đạt điểm.</p>
                <div className="academic-actions"><Link className="secondary inline" to={`/classes/${classId}`}>Chi tiết lớp</Link><Link className="secondary inline" to={`/schedule?classId=${encodeURIComponent(classId)}`}>Lịch lớp</Link></div>
            </>}
        </section>}
    </>;
}

export function StatisticsPage() {
    const user = identity(getSession());
    if (!user || user.role !== "Student") return <>
        <PageHeading title="Không có quyền xem thống kê cá nhân" subtitle="Chức năng này chỉ dành cho học viên đang đăng nhập. Không hỗ trợ xem thay học viên khác." />
        <Link className="secondary inline" to="/progress">Xem tiến độ lớp được phép truy cập</Link>
    </>;
    return <StudentStatistics key={`${getSessionGeneration()}:${user.id}:${user.role}`} />;
}

const comparison = (value: number | null) => value === null ? "Không có cơ sở so sánh" : `${value > 0 ? "+" : ""}${value}%`;
const metricLabels: Record<string, string> = {
    gradedSubmissionCount: "Bài nộp đã chấm", averageGrade: "Điểm trung bình", issuedCount: "Cảnh báo phát sinh",
    activeCount: "Cảnh báo còn hiệu lực", submittedCount: "Phản hồi đã gửi",
};
const contributorLabels = { assessment: "Bài tập và đánh giá", warnings: "Cảnh báo học tập", feedback: "Phản hồi" };

function StudentStatistics() {
    const [month, setMonth] = useState<string | undefined>();
    const load = useCallback(() => getPersonalStatistics(month), [month]);
    const statistics = useRead(load);
    const data = statistics.data;
    return <>
        <PageHeading title="Thống kê của tôi" subtitle="Dữ liệu cá nhân theo tháng Việt Nam (UTC+7). Các phần chưa được tích hợp được ghi rõ là chưa khả dụng, không thay bằng số 0." />
        <section className="panel attendance-panel">
            <label htmlFor="statistics-month">Tháng thống kê
                <input id="statistics-month" type="month" min="0002-01" max="9998-12" value={month ?? data?.month ?? ""} onChange={event => setMonth(event.target.value || undefined)} />
            </label>
            <button type="button" className="secondary" disabled={statistics.loading} onClick={statistics.retry}>Tải lại thống kê</button>
            <ReadFeedback result={statistics} />
            {data && <>
                <p role="status" className="notice">{data.availability === "partial" ? "Dữ liệu một phần: một số mô-đun chưa khả dụng. Số liệu Academic bên dưới là dữ liệu thật." : "Tất cả các phần thống kê đã khả dụng."}</p>
                <p className="small muted">Tháng {data.month} · {data.timezone} · Máy chủ chốt tại {academicTime(data.asOfUtc)} (UTC+7).</p>
                <h2>Tổng quan học tập</h2>
                <dl className="academic-metrics">
                    <div><dt>Lớp hiện được phép xem</dt><dd>{data.academic.currentClassCount}</dd></div>
                    <div><dt>Buổi hoàn thành trong tháng</dt><dd>{data.academic.currentCompletedSessions}</dd></div>
                    <div><dt>Buổi hoàn thành tháng trước</dt><dd>{data.academic.previousCompletedSessions}</dd></div>
                    <div><dt>Thay đổi so với tháng trước</dt><dd className="metric-comparison">{comparison(data.academic.completedSessionsChangePercent)}</dd></div>
                </dl>
                <p className="small muted">Số buổi tính trên các lớp hiện còn quyền truy cập, kể cả khi xem tháng cũ. Điểm danh cá nhân vẫn giữ lịch sử của lớp đã ngừng ghi danh.</p>
            </>}
        </section>
        {data && <>
            <section className="panel attendance-panel"><AttendanceSummary data={data.academic.attendance} /></section>
            <section className="panel attendance-panel" aria-labelledby="statistics-chart-heading">
                <h2 id="statistics-chart-heading">Buổi học hoàn thành theo tháng — {data.month.slice(0, 4)}</h2>
                <p id="statistics-chart-description">Đường biểu diễn số buổi đã hoàn thành từ tháng 01 đến 12. Bảng ngay dưới biểu đồ hiển thị cùng 12 điểm dữ liệu do máy chủ trả về, kèm điểm danh thực tế.</p>
                <CompletedSessionsChart points={data.chart} />
                <div className="academic-table-scroll" role="region" tabIndex={0} aria-label="Bảng dữ liệu thống kê 12 tháng">
                    <table className="academic-data-table"><caption>Dữ liệu gốc của biểu đồ và điểm danh — 12 tháng</caption>
                        <thead><tr><th scope="col">Tháng</th><th scope="col">Buổi hoàn thành</th><th scope="col">Điểm danh đã ghi</th>{attendanceStatuses.map(status => <th scope="col" key={status}>{attendanceLabels[status]}</th>)}</tr></thead>
                        <tbody>{data.chart.map(point => <tr key={point.month}><th scope="row">{point.month}</th><td>{point.completedSessions}</td><td>{point.recordedAttendance}</td>
                            <td>{point.counts.present}</td><td>{point.counts.absentUnexcused}</td><td>{point.counts.absentExcused}</td><td>{point.counts.late}</td><td>{point.counts.lateOver20Min}</td><td>{point.counts.leftEarly}</td>
                        </tr>)}</tbody>
                    </table>
                </div>
            </section>
            <section className="panel attendance-panel" aria-labelledby="statistics-contributors-heading">
                <h2 id="statistics-contributors-heading">Các mô-đun liên quan</h2>
                {data.contributors.map(section => <section className="statistics-contributor" key={section.key} aria-label={contributorLabels[section.key]}>
                    <h3>{contributorLabels[section.key]}</h3>
                    {section.availability === "unavailable" ? <p role="status">Chưa khả dụng — {section.reason === "not_registered" ? "mô-đun chưa đăng ký tích hợp thống kê." : "không tải được nhà cung cấp dữ liệu; thử tải lại thống kê."} Không có số liệu để kết luận.</p>
                        : <div className="academic-table-scroll" role="region" tabIndex={0} aria-label={`Số liệu ${contributorLabels[section.key]}`}><table className="academic-data-table"><caption>{contributorLabels[section.key]}: tháng đang chọn và tháng trước</caption>
                            <thead><tr><th scope="col">Chỉ số</th><th scope="col">Tháng đang chọn</th><th scope="col">Tháng trước</th><th scope="col">Thay đổi</th></tr></thead>
                            <tbody>{section.metrics?.map(metric => <tr key={metric.key}><th scope="row">{metricLabels[metric.key] ?? metric.key}</th><td>{metric.current ?? "Chưa có dữ liệu"}</td><td>{metric.previous ?? "Chưa có dữ liệu"}</td><td>{comparison(metric.changePercent)}</td></tr>)}</tbody>
                        </table></div>}
                </section>)}
            </section>
        </>}
    </>;
}

function CompletedSessionsChart({ points }: { points: MonthPoint[] }) {
    // Only scale the SVG; completed counts and all table values remain server-owned.
    const maximum = Math.max(1, ...points.map(point => point.completedSessions));
    const x = (index: number) => 56 + index * 56;
    const y = (value: number) => 224 - value / maximum * 170;
    return <figure className="statistics-chart" tabIndex={0} aria-label="Biểu đồ đường số buổi hoàn thành, có thể cuộn ngang trên màn hình nhỏ">
        <svg viewBox="0 0 720 280" role="img" aria-labelledby="completed-chart-title completed-chart-desc">
            <title id="completed-chart-title">Số buổi hoàn thành theo 12 tháng</title>
            <desc id="completed-chart-desc">Trục ngang là tháng, trục dọc là số buổi hoàn thành. Đường nối các điểm tháng; mọi giá trị có trong bảng dữ liệu ngay sau biểu đồ.</desc>
            <line className="chart-axis" x1="56" y1="224" x2="672" y2="224" />
            <line className="chart-axis" x1="56" y1="42" x2="56" y2="224" />
            <text x="36" y="229" textAnchor="end">0</text><text x="36" y="58" textAnchor="end">{maximum}</text>
            <text x="56" y="24">Số buổi hoàn thành</text>
            <polyline className="chart-series" points={points.map((point, index) => `${x(index)},${y(point.completedSessions)}`).join(" ")} />
            {points.map((point, index) => <g key={point.month}>
                <circle className="chart-point" cx={x(index)} cy={y(point.completedSessions)} r="4"><title>{point.month}: {point.completedSessions} buổi hoàn thành</title></circle>
                <text x={x(index)} y={y(point.completedSessions) - 12} textAnchor="middle">{point.completedSessions}</text>
                <text x={x(index)} y="252" textAnchor="middle">{point.month.slice(5)}</text>
            </g>)}
        </svg>
        <figcaption>Đường và điểm tròn: buổi hoàn thành. Tháng (01–12); số liệu chi tiết trong bảng bên dưới.</figcaption>
    </figure>;
}
