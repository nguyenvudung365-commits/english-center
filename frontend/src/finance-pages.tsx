import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import { isAcademicId } from "./academic";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import { canPayInvoice, canReadInvoices, invoiceStatusLabel, listInvoices, payInvoice, type Invoice } from "./finance";
import { formatDateOnly, formatVnDateTime, formatVnd } from "./format";
import { ErrorMessage, FieldError, fieldProps, PageHeading, Pager } from "./ui";

export function Invoices() {
    const user = identity(getSession());
    if (!user) return null;
    if (!canReadInvoices(user.role))
        return <>
            <PageHeading title="Hóa đơn & học phí" subtitle="Hóa đơn được giáo vụ, quản trị viên và học viên liên quan xem." />
            <section className="panel"><p role="status">Tài khoản giáo viên không có quyền xem hóa đơn.</p></section>
        </>;
    return <InvoiceWorkspace key={`${getSessionGeneration()}:${user.id}`} role={user.role} />;
}

function InvoiceWorkspace({ role }: { role: import("./api").Role }) {
    const manager = role !== "Student";
    const [input, setInput] = useState("");
    const [query, setQuery] = useState({ studentId: "", page: 1 });
    const [version, setVersion] = useState(0);
    const load = useCallback(() => listInvoices({ studentId: query.studentId, page: query.page }), [query, version]);
    const result = useRead(load);
    const action = useAction();
    const [confirming, setConfirming] = useState("");
    const [patched, setPatched] = useState<Record<string, Invoice>>({});
    const [notice, setNotice] = useState("");
    const data = result.data;
    useEffect(() => setPatched({}), [data]);
    const filterError = input.trim() && !isAcademicId(input.trim()) ? ["ID học viên phải là UUID hợp lệ."] : undefined;
    const conflict = action.error instanceof ApiError && action.error.status === 409;
    return (
        <>
            <PageHeading title={manager ? "Hóa đơn & học phí" : "Học phí của tôi"} subtitle={manager ? "Tra cứu hóa đơn và xác nhận học viên đã thanh toán." : "Hóa đơn học phí của riêng bạn. Thanh toán được giáo vụ xác nhận."} />
            <section className="panel">
                {manager && <form className="academic-filters" noValidate onSubmit={(event) => {
                    event.preventDefault();
                    if (filterError) return;
                    setQuery({ studentId: input.trim(), page: 1 });
                }}>
                    <div className="academic-field">
                        <label htmlFor="invoice-student">Lọc theo ID học viên (UUID, để trống để xem tất cả)</label>
                        <input {...fieldProps("invoice-student", filterError)} value={input} onChange={(event) => setInput(event.target.value)} />
                        <FieldError id="invoice-student" errors={filterError} />
                    </div>
                    <button className="primary" disabled={!!filterError}>Lọc hóa đơn</button>
                </form>}
                {notice && <p className="notice" role="status">{notice}</p>}
                {conflict
                    ? <div className="error" role="alert"><strong>Hóa đơn đã được xác nhận thanh toán trước đó. Hãy tải lại danh sách.</strong>
                        <button type="button" className="secondary" onClick={() => { void action.run(async () => undefined, () => undefined); setVersion((value) => value + 1); }}>Tải lại danh sách</button></div>
                    : <ErrorMessage error={action.error} />}
                <ReadFeedback result={result} />
                {data && <>
                    {data.items.length === 0 && <p>Chưa có hóa đơn nào.</p>}
                    {data.items.length > 0 && <div className="academic-table-scroll" role="region" tabIndex={0} aria-label="Bảng hóa đơn">
                        <table className="academic-data-table">
                            <caption>Hóa đơn học phí (tiền VND, giờ Việt Nam UTC+7)</caption>
                            <thead><tr>
                                <th scope="col">Số hóa đơn</th>{manager && <th scope="col">Học viên</th>}<th scope="col">Lớp</th><th scope="col">Số tiền</th>
                                <th scope="col">Hạn thanh toán</th><th scope="col">Trạng thái</th><th scope="col">Thanh toán lúc</th>{manager && <th scope="col">Thao tác</th>}
                            </tr></thead>
                            <tbody>{data.items.map((original) => {
                                const item = patched[original.id] ?? original;
                                return <tr key={item.id}>
                                    <th scope="row">{item.invoiceNo}<br /><span className="small muted">Phát hành {formatVnDateTime(item.issuedAtUtc)}</span></th>
                                    {manager && <td className="id">{item.studentId}</td>}
                                    <td><Link className="accent" to={`/classes/${item.classId}`}>Xem lớp</Link></td>
                                    <td>{formatVnd(item.amount)}</td>
                                    <td>{formatDateOnly(item.dueDate)}</td>
                                    <td><span className="pill">{invoiceStatusLabel(item.status)}</span></td>
                                    <td>{formatVnDateTime(item.paidAtUtc)}</td>
                                    {manager && <td>{canPayInvoice(role, item)
                                        ? confirming === item.id
                                            ? <div className="academic-actions">
                                                <button type="button" className="primary" disabled={action.busy} onClick={() => void action.run(() => payInvoice(item.id), (updated) => {
                                                    setPatched((previous) => ({ ...previous, [updated.id]: updated }));
                                                    setConfirming("");
                                                    setNotice(`Đã xác nhận thanh toán hóa đơn ${updated.invoiceNo}.`);
                                                })}>{action.busy ? "Đang xác nhận…" : "Xác nhận đã thu"}</button>
                                                <button type="button" className="secondary" disabled={action.busy} onClick={() => setConfirming("")}>Hủy</button>
                                            </div>
                                            : <button type="button" className="secondary" disabled={action.busy} onClick={() => { setNotice(""); setConfirming(item.id); }}>Xác nhận thanh toán</button>
                                        : "—"}</td>}
                                </tr>;
                            })}</tbody>
                        </table>
                    </div>}
                    <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={(page) => setQuery((previous) => ({ ...previous, page }))} label="Phân trang hóa đơn" disabled={action.busy} />
                </>}
            </section>
        </>
    );
}
