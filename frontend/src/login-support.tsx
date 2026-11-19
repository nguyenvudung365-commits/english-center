import { MessageCircleMore, Phone } from "lucide-react";
import { zaloSupportUrl } from "./support-contact";

const phone = "0985653116";
const zalo = zaloSupportUrl(import.meta.env.VITE_SUPPORT_ZALO_URL) ?? `https://zalo.me/${phone}`;

export function LoginSupport() {
    return <aside className="login-support" aria-label="Liên hệ trung tâm">
        <details onKeyDown={event => {
            if (event.key !== "Escape" || !event.currentTarget.open) return;
            event.currentTarget.open = false;
            event.currentTarget.querySelector("summary")?.focus();
        }}>
            <summary aria-label="Tham gia trung tâm"><MessageCircleMore size={27} aria-hidden="true" /></summary>
            <div className="login-support-body">
                <h3>Tham gia trung tâm</h3>
                <a href={zalo} target="_blank" rel="noopener noreferrer" aria-label="Zalo (mở tab mới)">
                    <svg className="zalo-icon" viewBox="0 0 28 28" width="28" height="28" aria-hidden="true"><path fill="currentColor" d="M14 2a12 11 0 0 0-9 18L4 26l6-3a12 11 0 1 0 4-21Z"/><path d="M9 9h10l-10 9h10" fill="none" stroke="white" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"/></svg> Zalo
                </a>
                <a href="tel:+84985653116"><Phone className="phone-icon" size={23} aria-hidden="true" /> {phone}</a>
            </div>
        </details>
    </aside>;
}
