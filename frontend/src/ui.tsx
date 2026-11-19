import { NavLink } from "react-router-dom";
import { ArrowRight, BookOpen } from "lucide-react";
import { ApiError } from "./api";
import { pageCount } from "./format";

export function ErrorMessage({ error }: { error: unknown }) {
    if (!error) return null;
    return (
        <div className="error" role="alert">
            <strong>
                {error instanceof Error ? error.message : "Có lỗi xảy ra."}
            </strong>
            {error instanceof ApiError &&
                Object.entries(error.errors).map(([field, messages]) => (
                    <p key={field}>
                        {field}: {messages.join(" ")}
                    </p>
                ))}
        </div>
    );
}
export function PageHeading({
    title,
    subtitle,
}: {
    title: string;
    subtitle?: string;
}) {
    return (
        <div className="page-heading">
            <span className="eyebrow">ENGLISH CENTER</span>
            <h1>{title}</h1>
            {subtitle && <p>{subtitle}</p>}
        </div>
    );
}
export function Pager({ page, pageSize, total, change, label, disabled = false }: {
    page: number; pageSize: number; total: number; change: (page: number) => void; label: string; disabled?: boolean;
}) {
    return (
        <div className="academic-actions data-pagination" role="group" aria-label={label}>
            <span role="status">Trang {page} / {pageCount(total, pageSize)} · {total} mục</span>
            <button type="button" className="secondary" disabled={disabled || page <= 1} onClick={() => change(page - 1)}>Trang trước</button>
            <button type="button" className="secondary" disabled={disabled || page * pageSize >= total} onClick={() => change(page + 1)}>Trang sau</button>
        </div>
    );
}
export const fieldProps = (id: string, errors?: string[]) => ({
    id, "aria-invalid": !!errors?.length, "aria-describedby": errors?.length ? `${id}-error` : undefined,
});
export function FieldError({ id, errors }: { id: string; errors?: string[] }) {
    return errors?.length ? <p id={`${id}-error`} className="field-error" role="alert">{errors.join(" ")}</p> : null;
}
export function Unavailable({ title, text }: { title: string; text: string }) {
    return (
        <>
            <PageHeading
                title={title}
                subtitle="Không gian được chuẩn bị cho bước tiếp theo."
            />
            <section className="empty panel">
                <BookOpen size={44} strokeWidth={1.4} />
                <span className="pill">Chưa tích hợp API</span>
                <h2>{title} đang được hoàn thiện</h2>
                <p>{text}</p>
                <NavLink className="secondary inline" to="/">
                    Về tổng quan <ArrowRight size={16} />
                </NavLink>
            </section>
        </>
    );
}
