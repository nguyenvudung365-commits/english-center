# Tóm tắt và dàn ý slide báo cáo tuần

Nhóm 10 - Môn 608819 - Phát triển ứng dụng phía máy chủ nâng cao (lớp 68CS1&2)
Phiên bản: v0.1 - bản báo cáo tuần. Ngày: 2026-10-17.
Nguồn dữ liệu: [tài liệu 01](01-yeu-cau-chuc-nang-phi-chuc-nang.md), [02](02-so-do-phan-ra-chuc-nang.md), [03](03-thiet-ke-he-thong.md), [04](04-thiet-ke-co-so-du-lieu.md), [backlog](../product-backlog.md), mã nguồn hiện tại.

## 1. Tóm tắt một trang

- Đề tài: Xây dựng hệ thống quản lý trung tâm Tiếng Anh, backend .NET 10 theo kiến trúc Modular Monolith.
- Phạm vi: 33 user story. Must (20): tài khoản, lớp, lịch, ghi danh, điểm danh, bài tập/nộp bài/chấm điểm, hóa đơn tự động, thông báo, lưu file. Should (9): hồ sơ, giới hạn vi phạm, hẹn test, cảnh báo, bài đã/chưa đánh giá, thanh toán hóa đơn, thông báo tự động, phản hồi, thống kê. Could (4): FAQ, bảng tin, bài luyện tập, Xu (làm nếu còn thời gian). Đã cắt: gamification, mini game, khuyến mãi, cửa hàng, thanh toán online, nhiều chi nhánh.
- Kiến trúc: 5 module (Identity, Academic, Enrollment, Finance, MediaNotification), mỗi module có Clean Architecture, DbContext và schema riêng, giao tiếp chỉ qua integration event (Event Bus in-memory).
- Tiến độ tuần 1 (Đã có): khung solution 5 module x 5 project (Domain, Contracts, Application, Infrastructure, Api) cùng BuildingBlocks và host; Event Bus in-memory; 5 DbContext rỗng với schema riêng; architecture test; 7 integration event trong Contracts; handler mẫu Finance cho `StudentEnrolled`; Swagger, `/health` và ping từng module.
- Chưa có (Dự kiến): entity nghiệp vụ, migration, JWT, MinIO/R2 wrapper, Docker Compose, Jenkinsfile, frontend React, 3 event `AssignmentMissed`, `InvoicePaid`, `WarningIssued`.
- Kế hoạch tiếp theo (backlog mục 7): tuần 2 gồm Docker Compose, đăng nhập/đăng xuất/đổi mật khẩu, tạo tài khoản, phân quyền, tạo lớp, dịch vụ lưu file; tuần 3 gồm lịch, danh sách lớp, ghi danh, hóa đơn; tuần 4 gồm điểm danh, bài tập, nộp bài, chấm điểm, thông báo.

## 2. Thành viên nhóm

Nhóm hiện có **4 thành viên** theo cập nhật của nhóm. Chưa xác nhận ai rời nhóm nên chưa gắn tên vào phân công TV1..TV4; xem phân công gợi ý tại [backlog mục 8](product-backlog.md#8-phân-công-gợi-ý-4-thành-viên-nhánh-riêng-theo-modulecông-việc).

Danh sách dưới đây là **danh sách đăng ký ban đầu, không phải danh sách 4 thành viên hiện tại**. Nguồn: file đăng ký nhóm `Đăng ký nhóm - Đồ án phát triển ứng dụng phía máy chủ nâng cao - 68CS1&2.xlsx`, nhóm 10, đề tài "Xây dựng hệ thống quản lý trung tâm Tiếng Anh". Cần xác nhận người rời nhóm trước khi cập nhật tên trên slide.

| STT | Họ tên | Mã sinh viên | Mã trình bày | Phân công (nhóm điền) |
|---|---|---|---|---|
| 1 | Nguyễn Vũ Dũng | 0205968 | TV1 | |
| 2 | Nguyễn Hồng Lịch | 0210068 | TV2 | |
| 3 | Nguyễn Lý Tiền | 0213768 | TV3 | |
| 4 | Lê Đỗ Việt Tùng | 0215068 | TV4 | |
| 5 | Nguyễn Minh Năng | 0211668 | TV5 | |

Mã TV1..TV5 trong bảng cũ chỉ là nhãn thứ tự đăng ký, không ánh xạ sang nhãn phân công TV1..TV4 hiện tại.

## 3. Dàn ý slide (14 slide, khoảng 15-20 phút)

Cột "Người trình bày" để trống cho nhóm điền (TV1-TV4, sau khi xác nhận tên). Ảnh chụp giao diện LMS tham khảo: không chèn vào slide vì lý do bản quyền/thương hiệu; thay bằng sơ đồ tự vẽ.

| # | Tiêu đề | Ý chính | Sơ đồ dùng lại từ | Người trình bày |
|---|---|---|---|---|
| 1 | Trang bìa | Tên đề tài, nhóm 10, môn 608819, lớp 68CS1&2, 4 thành viên | - | |
| 2 | Bài toán và phạm vi | Tham khảo luồng nghiệp vụ LMS trung tâm Tiếng Anh; 33 user story; Must/Should/Could; đã cắt các tính năng gamification, cửa hàng, thanh toán online và lý do | 01 mục 1, 6 | |
| 3 | Tác nhân và yêu cầu chức năng | 3 vai trò người dùng (Giáo viên, Giáo vụ/Admin là suy đoán) + Hệ thống; 8 nhóm chức năng; quy tắc nghiệp vụ chính (6 trạng thái điểm danh, hóa đơn idempotent) | 01 mục 2, 3 | |
| 4 | Yêu cầu phi chức năng | Hiệu năng, bảo mật, dễ sử dụng, bảo trì, kiểm thử, triển khai; các con số là mục tiêu | 01 mục 4 | |
| 5 | Sơ đồ phân rã chức năng | 8 nhánh F1-F8, chức năng con F-x.y, ma trận chức năng - tác nhân | 02 mục 2 | |
| 6 | Kiến trúc tổng thể | Trình duyệt, Cloudflare, VPS, API .NET, PostgreSQL, MinIO/R2, Jenkins (phần lớn Dự kiến) | 03 mục 2 | |
| 7 | Modular Monolith và Clean Architecture | 5 module, 5 layer, ma trận tham chiếu, architecture test chặn tham chiếu chéo | 03 mục 3, 4 | |
| 8 | Integration event và luồng ghi danh | `StudentEnrolled` -> hóa đơn -> thông báo; bảng 10 event (7 Đã có Contracts, 3 Dự kiến) | 03 mục 6, 7.2 | |
| 9 | Luồng upload file bằng presigned URL | Upload thẳng lên MinIO/R2, API chỉ nhận `fileKey`, tối đa 20 MB | 03 mục 7.3 | |
| 10 | Nguyên tắc cơ sở dữ liệu | 5 schema, không FK chéo, tham chiếu logic, read model và nhất quán cuối cùng | 04 mục 1, 2 | |
| 11 | ERD | ERD Academic (schema lớn nhất) và tóm tắt ERD 4 schema còn lại | 04 mục 3 | |
| 12 | Công nghệ và triển khai | .NET 10, EF Core 10, Npgsql, PostgreSQL; Docker, Jenkins, VPS, Cloudflare (Dự kiến); dự kiến dùng Jira từ giai đoạn code | 03 mục 9, 10, 12 | |
| 13 | Tiến độ và demo | Swagger ping 5 module, `/health`, `dotnet test` xanh (xem kịch bản demo) | - | |
| 14 | Kế hoạch tuần tới và câu hỏi cho thầy | Sprint tuần 2, phân công, các điểm xin thầy góp ý | Backlog mục 7 | |

## 4. Kịch bản demo

Chạy tại thư mục gốc repo `d:\english-center` (Dự kiến thời lượng 2-3 phút):

1. Build toàn bộ solution:
   ```
   dotnet build EnglishCenter.sln
   ```
2. Chạy host:
   ```
   dotnet run --project src/Host/EnglishCenter.Api
   ```
   Theo `launchSettings.json`, ứng dụng nghe tại `http://localhost:5080` và mở Swagger ở `http://localhost:5080/swagger`.
3. Trong Swagger, gọi lần lượt `GET /api/identity/ping`, `/api/academic/ping`, `/api/enrollment/ping`, `/api/finance/ping`, `/api/media-notification/ping`: kỳ vọng 200 và tên module/schema tương ứng.
4. Gọi `GET http://localhost:5080/health`: kỳ vọng 200 `{"status":"Healthy"}`.
5. Dừng host, chạy kiểm thử:
   ```
   dotnet test EnglishCenter.sln
   ```
   Kỳ vọng: architecture test, Event Bus test và integration test đều xanh.

Nếu mạng/máy lỗi, phương án dự phòng là chụp sẵn ảnh màn hình Swagger và kết quả `dotnet test`.

## 5. Câu hỏi thầy có thể hỏi và gợi ý trả lời

| Câu hỏi | Gợi ý trả lời |
|---|---|
| Vì sao không dùng microservices? | Nhóm 5 người, một VPS, chi phí vận hành microservices cao. Modular Monolith giữ ranh giới rõ bằng architecture test và có thể tách module thành service sau vì chỉ giao tiếp qua event/contract (tài liệu 03 mục 1). |
| Thống kê khi không join chéo schema thì làm sao? | Mỗi module cung cấp số liệu của mình qua API; frontend gọi API của từng module rồi tổng hợp phía client. Không truy vấn chéo schema (tài liệu 03 mục 7.5). |
| Mất event thì sao? | Event Bus hiện in-memory nên mất event nếu tiến trình dừng. Bus hiện chỉ log lỗi handler, không retry: nếu handler lỗi thì bị bỏ qua, ghi danh vẫn thành công nhưng có thể thiếu hóa đơn. Quy ước handler idempotent chỉ giúp an toàn khi xử lý lại, chưa có cơ chế xử lý lại tự động. Hướng khắc phục dự kiến: Outbox + retry (tài liệu 03 mục 11). |
| Vì sao lại trùng dữ liệu giữa module (read model)? | Để không phải join chéo schema. Bảng bản sao thuộc về module chứa nó, chỉ cập nhật bằng event; chấp nhận nhất quán cuối cùng (tài liệu 04 mục 1). |
| Phân quyền thế nào? | JWT chứa `sub` và `role`; API trả 401 khi thiếu token, 403 khi thiếu quyền; học viên chỉ xem dữ liệu của mình. Hiện là Dự kiến (tài liệu 03 mục 8). |
| Vì sao cắt các tính năng gamification, cửa hàng? | Không phải nghiệp vụ cốt lõi, tăng độ phức tạp mà không giúp đạt mục tiêu môn học; Xu giữ ở mức Could (tài liệu 01 mục 6). |
| Vì sao vai trò Giáo viên và Giáo vụ/Admin là suy luận? | Ảnh tham khảo chỉ có giao diện học viên; hai vai trò này suy ra từ dữ liệu học viên thấy. |
