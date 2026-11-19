export function supportEmail(value?: string) {
    const email = value?.trim() ?? "";
    return email.length <= 254 && /^[a-z\d_+.-]+@[a-z\d-]+(?:\.[a-z\d-]+)+$/i.test(email) ? email : null;
}

export function zaloSupportUrl(value?: string) {
    try {
        const url = new URL(value?.trim() ?? "");
        if (url.protocol !== "https:" || url.username || url.password || url.port) return null;
        if (url.hostname === "zalo.me" || url.hostname === "oa.zalo.me")
            return url.pathname !== "/" ? url.href : null;
        return /^https:\/\/zaloapp\.com\/qr\/p\/[a-zA-Z0-9]+$/.test(url.href) ? url.href : null;
    } catch {
        return null;
    }
}
