import { NavLink } from "react-router-dom";
import { ArrowRight, BookOpen } from "lucide-react";
import { ApiError } from "./api";

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
    subtitle: string;
}) {
    return (
        <div className="page-heading">
            <span className="eyebrow">ENGLISH CENTER</span>
            <h1>{title}</h1>
            <p>{subtitle}</p>
        </div>
    );
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
