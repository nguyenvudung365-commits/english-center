import { useCallback, useState } from "react";
import { Link } from "react-router-dom";
import { getSession, getSessionGeneration, identity } from "./api";
import { useRead } from "./academic-pages";
import { ReadFeedback } from "./attendance-pages";
import { enrollmentStatusLabel, listMyEnrollments } from "./enrollment";
import { formatDateOnly, formatVnd, formatVnDateTime } from "./format";
import { PageHeading, Pager } from "./ui";

export function MyEnrollments() {
    const user = identity(getSession());
    if (!user || user.role !== "Student") return null;
    return <EnrollmentList key={`${getSessionGeneration()}:${user.id}`} />;
}

function EnrollmentList() {
    const [page, setPage] = useState(1);
    const load = useCallback(() => listMyEnrollments(page), [page]);
    const result = useRead(load);
    const data = result.data;
    return <>
        <PageHeading title="Ghi danh của tôi" subtitle="Theo dõi lớp đã ghi danh, học phí và quyền truy cập học tập của bạn." />
        <section className="panel">
            <ReadFeedback result={result} />
            {data && <>
                {data.items.length === 0 && <p>Bạn chưa có ghi danh nào trong danh sách này.</p>}
                {data.items.length > 0 && <div className="academic-table-scroll" role="region" tabIndex={0} aria-label="Bảng ghi danh">
                    <table className="academic-data-table">
                        <caption>Các lớp đã ghi danh</caption>
                        <thead><tr>
                            <th scope="col">Lớp</th><th scope="col">Thời gian học</th><th scope="col">Ngày ghi danh</th>
                            <th scope="col">Học phí</th><th scope="col">Trạng thái ghi danh</th><th scope="col">Quyền học tập</th>
                        </tr></thead>
                        <tbody>{data.items.map(item => <tr key={item.id}>
                            <th scope="row">{item.isActive
                                ? <Link className="accent" to={`/classes/${item.classId}`}>{item.classCode}</Link>
                                : item.classCode}</th>
                            <td>{formatDateOnly(item.startDate)} – {formatDateOnly(item.endDate)}</td>
                            <td>{formatVnDateTime(item.enrolledAtUtc)}</td>
                            <td>{formatVnd(item.tuitionFee)}</td>
                            <td><span className="pill">{enrollmentStatusLabel(item.status)}</span></td>
                            <td>{item.isActive ? "Đang mở" : "Đã ngừng"}</td>
                        </tr>)}</tbody>
                    </table>
                </div>}
                <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={setPage} label="Phân trang ghi danh" />
                <Link className="secondary inline" to="/invoices">Xem hóa đơn học phí</Link>
            </>}
        </section>
    </>;
}
