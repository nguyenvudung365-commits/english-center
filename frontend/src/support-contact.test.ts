import { expect, it } from "vitest";
import { zaloSupportUrl } from "./support-contact";

it("only exposes configured HTTPS Zalo links", () => {
    expect(zaloSupportUrl(" https://zalo.me/example ")).toBe("https://zalo.me/example");
    expect(zaloSupportUrl("https://oa.zalo.me/example")).toBe("https://oa.zalo.me/example");
    for (const value of [undefined, "", "javascript:alert(1)", "http://zalo.me/example", "https://zalo.me.evil.test/x",
        "https://user@zalo.me/x", "https://zalo.me:444/x", "https://zalo.me/"])
        expect(zaloSupportUrl(value)).toBeNull();
});

it("allows only personal QR app links on zaloapp.com", () => {
    const qr = "https://zaloapp.com/qr/p/example123";
    expect(zaloSupportUrl(qr)).toBe(qr);
    for (const value of ["https://zaloapp.com/", "https://zaloapp.com/qr/p/", `${qr}/extra`,
        `${qr}?next=example`, `${qr}#chat`, "https://zaloapp.com/qr/p/a%2Fb",
        "http://zaloapp.com/qr/p/example123", "https://zaloapp.com.evil.test/qr/p/example123",
        "https://user@zaloapp.com/qr/p/example123", "https://zaloapp.com:444/qr/p/example123"])
        expect(zaloSupportUrl(value)).toBeNull();
});
