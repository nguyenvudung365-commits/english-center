# Bộ tài liệu báo cáo tuần

Nhóm 10 - Môn 608819 - Phát triển ứng dụng phía máy chủ nâng cao (lớp 68CS1&2)
Đề tài: Xây dựng hệ thống quản lý trung tâm Tiếng Anh (Modular Monolith, .NET 10).
Phiên bản: v0.1 - bản báo cáo tuần. Ngày: 2026-10-17.
Nguồn dữ liệu: `docs/product-backlog.md`, `CLAUDE.md`, mã nguồn trong `src/` và `tests/`.

## Mục lục

| File | Nội dung |
|---|---|
| [00-tom-tat-va-dan-y-slide.md](00-tom-tat-va-dan-y-slide.md) | Tóm tắt một trang, dàn ý 14 slide, kịch bản demo, câu hỏi dự kiến của thầy |
| [01-yeu-cau-chuc-nang-phi-chuc-nang.md](01-yeu-cau-chuc-nang-phi-chuc-nang.md) | Tác nhân, yêu cầu chức năng FR-01..FR-33, yêu cầu phi chức năng NFR, ràng buộc, ngoài phạm vi, ma trận truy vết |
| [02-so-do-phan-ra-chuc-nang.md](02-so-do-phan-ra-chuc-nang.md) | Sơ đồ phân rã chức năng cấp 0-1-2, bảng mô tả, ma trận chức năng - tác nhân |
| [03-thiet-ke-he-thong.md](03-thiet-ke-he-thong.md) | Kiến trúc, thành phần, giao tiếp qua event, luồng tương tác, công nghệ, triển khai |
| [04-thiet-ke-co-so-du-lieu.md](04-thiet-ke-co-so-du-lieu.md) | Nguyên tắc dữ liệu theo module, ERD 5 schema, bảng thuộc tính, ràng buộc, enum |

Tài liệu gốc tham chiếu: [Product Backlog](../product-backlog.md).

## Quy ước

- Trạng thái: "Đã có" = có trong code hiện tại; "Dự kiến" = sprint sau. "(suy đoán)" là nội dung suy luận từ màn hình học viên, không có trong ảnh tham khảo.
- Mã định danh: US/TS/FE lấy từ backlog; FR/NFR/F-x.y là mã mới của bộ tài liệu này.
- Markdown là nguồn duy nhất cần sửa. Tài liệu nộp ở dạng Markdown.

## Cách xem sơ đồ Mermaid

- Trên GitHub: sơ đồ tự hiển thị khi mở file `.md`.
- Trong VS Code: cài extension "Markdown Preview Mermaid Support", mở Markdown Preview.
- Kiểm tra nhanh một khối: dán nội dung vào https://mermaid.live.
