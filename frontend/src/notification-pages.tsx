import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useRead } from "./academic-pages";
import { useAction } from "./assignment-pages";
import { ReadFeedback } from "./attendance-pages";
import { formatVnDateTime } from "./format";
import {
    adjustUnread, listNotifications, markAllNotificationsRead, markNotificationRead, notificationLink, notificationLinkLabel,
    setUnread, type NotificationItem,
} from "./notifications";
import { ErrorMessage, PageHeading, Pager } from "./ui";

export function Notifications() {
    const [unreadOnly, setUnreadOnly] = useState(false);
    const [page, setPage] = useState(1);
    const load = useCallback(() => listNotifications({ unreadOnly, page, pageSize: 10 }), [unreadOnly, page]);
    const result = useRead(load);
    const action = useAction();
    const [acting, setActing] = useState("");
    const [read, setRead] = useState<ReadonlySet<string>>(new Set());
    const [notice, setNotice] = useState("");
    const data = result.data;
    useEffect(() => {
        setRead(new Set());
        if (data) setUnread(data.unreadCount);
    }, [data]);
    const isRead = (item: NotificationItem) => item.isRead || read.has(item.id);
    const markOne = (item: NotificationItem) => {
        setActing(item.id);
        setNotice("");
        void action.run(() => markNotificationRead(item.id), () => {
            setRead((previous) => new Set(previous).add(item.id));
            adjustUnread(-1);
        });
    };
    return (
        <>
            <PageHeading title="Thông báo" subtitle="Thông báo về ghi danh, bài tập, điểm, hóa đơn và cảnh báo của riêng bạn." />
            <section className="panel">
                <div className="academic-filters">
                    <label className="check-row" htmlFor="notification-unread">
                        <input id="notification-unread" type="checkbox" checked={unreadOnly} onChange={(event) => { setUnreadOnly(event.target.checked); setPage(1); }} />
                        Chỉ hiện thông báo chưa đọc
                    </label>
                    <button type="button" className="secondary" disabled={action.busy || !data?.unreadCount} onClick={() => {
                        setActing("");
                        setNotice("");
                        void action.run(markAllNotificationsRead, (value) => {
                            setUnread(0);
                            setNotice(`Đã đánh dấu ${value.marked} thông báo là đã đọc.`);
                            result.retry();
                        });
                    }}>{action.busy && !acting ? "Đang cập nhật…" : "Đánh dấu tất cả đã đọc"}</button>
                </div>
                {notice && <p className="notice" role="status">{notice}</p>}
                <ErrorMessage error={action.error} />
                <ReadFeedback result={result} />
                {data && <>
                    <p className="small muted" role="status">{data.unreadCount} thông báo chưa đọc.</p>
                    {data.items.length === 0 && <p>{unreadOnly ? "Không có thông báo chưa đọc." : "Bạn chưa có thông báo nào."}</p>}
                    <ul className="catalog-list">
                        {data.items.map((item) => {
                            const link = notificationLink(item.targetType, item.targetId);
                            const seen = isRead(item);
                            return (
                                <li key={item.id}>
                                    <span className="pill">{seen ? "Đã đọc" : "Chưa đọc"}</span>
                                    <h2>{item.title}</h2>
                                    <p className="small muted"><time dateTime={item.createdAtUtc}>{formatVnDateTime(item.createdAtUtc)}</time> (UTC+7)</p>
                                    <div className="academic-actions">
                                        {link && <Link className="secondary inline" to={link} onClick={() => {
                                            if (!seen) void markNotificationRead(item.id).then(() => adjustUnread(-1), () => undefined);
                                        }}>{notificationLinkLabel(item.targetType)}</Link>}
                                        {!seen && <button type="button" className="secondary" disabled={action.busy} onClick={() => markOne(item)}>
                                            {action.busy && acting === item.id ? "Đang cập nhật…" : "Đánh dấu đã đọc"}
                                        </button>}
                                    </div>
                                </li>
                            );
                        })}
                    </ul>
                    <Pager page={data.page} pageSize={data.pageSize} total={data.total} change={setPage} label="Phân trang thông báo" disabled={action.busy} />
                </>}
            </section>
        </>
    );
}
