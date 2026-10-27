# Yêu cầu chức năng và phi chức năng

Nhóm 10 - Môn 608819 - Phát triển ứng dụng phía máy chủ nâng cao (lớp 68CS1&2)
Phiên bản: v0.1 - bản báo cáo tuần. Ngày: 2026-10-17.
Nguồn dữ liệu: `docs/product-backlog.md`, `CLAUDE.md`, 23 ảnh tham khảo giao diện LMS trong `lms-tiw-anh-chup/`, mã nguồn hiện tại trong `src/`.

Quy ước: "Đã có" = có trong code hiện tại; "Dự kiến" = sprint sau. Nội dung đánh dấu "(suy đoán)" là suy luận từ dữ liệu màn hình học viên, không có trong ảnh tham khảo.

## 1. Giới thiệu và phạm vi

Nguồn: backlog mục 1, 6.

Hệ thống là backend cho trung tâm Tiếng Anh: quản lý tài khoản và phân quyền, lớp học, lịch học, ghi danh, điểm danh, bài tập và nộp bài (file), điểm, học phí đơn giản và thông báo. Kiến trúc Modular Monolith (.NET 10) gồm 5 module giao tiếp qua integration event. Giao diện web React (Vite) tách riêng, làm ở giai đoạn sau (Dự kiến).

Giao diện LMS tham khảo (23 ảnh trong `lms-tiw-anh-chup/`, từ `01-tien-do-hoc-tap.png` đến `21-menu-tai-khoan.png`) chỉ dùng để quan sát nghiệp vụ; tài liệu không sao chép logo, ảnh hay nội dung thương hiệu của đơn vị khác.

Phạm vi theo MoSCoW:

| Mức | Số story | Nội dung chính |
|---|---|---|
| Must | 20 US + TS-01..05 | Đăng nhập/đăng xuất/đổi mật khẩu, tạo tài khoản, phân quyền, lớp, lịch, ghi danh, điểm danh, bài tập, nộp bài, chấm điểm, hóa đơn tự động, thông báo, dịch vụ lưu file |
| Should | 9 US + TS-06 + FE-01..06 | Hồ sơ, giới hạn vi phạm, hẹn test, cảnh báo, bài đã đánh giá, thanh toán hóa đơn, thông báo tự động, phản hồi, thống kê |
| Could | 4 US | FAQ, bảng tin, bài luyện tập, Xu (làm nếu còn thời gian) |

## 2. Tác nhân

Nguồn: backlog mục 2.

| Tác nhân | Nguồn xác định | Mô tả |
|---|---|---|
| Học viên | Quan sát từ ảnh | Xem lớp, lịch, điểm danh, bài tập, điểm; nộp bài; gửi phản hồi; nhận thông báo |
| Giáo viên | (suy đoán) ảnh không có | Xem lịch dạy, điểm danh, tạo bài tập, chấm bài |
| Giáo vụ / Admin | (suy đoán) ảnh không có | Quản lý tài khoản, lớp, lịch, ghi danh, học phí, FAQ, xử lý phản hồi. Hai vai trò này được gộp thành một vai trò `Staff`. |
| Hệ thống | Tác nhân nội bộ | Xử lý integration event: tạo hóa đơn, tạo thông báo, cập nhật bản sao dữ liệu, phát cảnh báo |
| Dịch vụ lưu trữ file | Tác nhân ngoài | MinIO (môi trường dev) / Cloudflare R2 (triển khai), truy cập qua presigned URL |

## 3. Yêu cầu chức năng

Nguồn: backlog mục 5 (user story và tiêu chí chấp nhận). Mỗi FR ứng với đúng 1 US (FR-01 = US-01, ..., FR-33 = US-33). Cột "Ảnh" là số thứ tự ảnh tham khảo.

### 3.1 Tài khoản và phân quyền (Identity)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-01 | Đăng nhập | Đăng nhập bằng email và mật khẩu, nhận JWT | Mọi người dùng | Identity | Must | US-01 | 21 |
| FR-02 | Đăng xuất | Thu hồi refresh token | Mọi người dùng | Identity | Must | US-02 | 21 |
| FR-03 | Đổi mật khẩu | Nhập mật khẩu cũ, mật khẩu mới tối thiểu 8 ký tự | Mọi người dùng | Identity | Must | US-03 | 21 |
| FR-04 | Hồ sơ cá nhân | Xem/cập nhật họ tên, email, SĐT, ảnh đại diện; xem mã học viên | Học viên | Identity | Should | US-04 | 01, 21 |
| FR-05 | Tạo tài khoản và gán vai trò | Tạo tài khoản học viên/giáo viên (suy đoán) | Giáo vụ/Admin | Identity | Must | US-05 | - |
| FR-06 | Phân quyền truy cập API | Chặn theo vai trò: 401 khi thiếu token, 403 khi thiếu quyền | Hệ thống | Identity | Must | US-06 | - |

### 3.2 Lớp học và lịch học (Academic)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-07 | Tạo lớp | Mã lớp, khóa, ngày bắt đầu/kết thúc, sĩ số tối đa, loại lớp, giáo viên, học phí | Giáo vụ | Academic | Must | US-07 | 11 |
| FR-08 | Lập lịch buổi học | Ngày, giờ, Unit-Lesson, kỹ năng, loại buổi, ca thi | Giáo vụ | Academic | Must | US-08 | 10 |
| FR-09 | Danh sách lớp của học viên | Lọc Sắp diễn ra/Đang diễn ra/Kết thúc, số buổi đã học x/y | Học viên | Academic | Must | US-09 | 11 |
| FR-10 | Lịch học tháng/tuần | Phân biệt buổi đã học/chưa học | Học viên | Academic | Must | US-10 | 10 |
| FR-11 | Lịch dạy | Giáo viên xem buổi/lớp được phân công (suy đoán) | Giáo viên | Academic | Must | US-11 | - |

### 3.3 Ghi danh, cảnh báo, hẹn test (Enrollment)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-12 | Ghi danh học viên vào lớp | Chặn trùng, chặn lớp đầy; phát `StudentEnrolled` | Giáo vụ | Enrollment | Must | US-12 | 11 |
| FR-13 | Giới hạn vi phạm | Giới hạn số buổi nghỉ, lần đi trễ, bài tập được bỏ | Giáo vụ | Enrollment | Should | US-13 | 01 |
| FR-14 | Hẹn test đầu vào | Học viên xem; giáo vụ tạo/cập nhật | Học viên, Giáo vụ | Enrollment | Should | US-14 | 13 |
| FR-15 | Cảnh báo vi phạm | Tạo và hiển thị cảnh báo khi vượt giới hạn | Học viên, Hệ thống | Enrollment | Should | US-15 | 14, 03 |

### 3.4 Điểm danh (Academic)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-16 | Điểm danh buổi học | 6 trạng thái; sửa lại trong 24h | Giáo viên | Academic | Must | US-16 | 01 |
| FR-17 | Tổng hợp điểm danh | Tỷ lệ % và số buổi theo trạng thái, lịch sử | Học viên | Academic | Must | US-17 | 01 |

### 3.5 Bài tập, nộp bài, điểm (Academic + MediaNotification)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-18 | Tạo và công bố bài tập | Gắn buổi học, loại Trên lớp/Về nhà, thời gian mở/đóng | Giáo viên | Academic | Must | US-18 | 01 |
| FR-19 | Xem bài tập theo Unit-Lesson | Trạng thái Đã làm/Chưa làm/Đã bỏ | Học viên | Academic | Must | US-19 | 01 |
| FR-20 | Nộp bài kèm file | Upload qua presigned URL, gửi `fileKey` | Học viên | Academic, MediaNotification | Must | US-20 | 01 |
| FR-21 | Chấm điểm và nhận xét | Điểm + nhận xét; học viên xem ở tab Điểm bài tập | Giáo viên, Học viên | Academic | Must | US-21 | 01 |
| FR-22 | Bài đã/chưa được đánh giá | Danh sách có tìm kiếm | Học viên | Academic | Should | US-22 | 19 |

### 3.6 Học phí (Finance)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-23 | Tự tạo hóa đơn khi ghi danh | Idempotent theo `EnrollmentId`; hạn +7 ngày | Hệ thống | Finance | Must | US-23 | - |
| FR-24 | Xác nhận thanh toán và xem hóa đơn | Giáo vụ đánh dấu đã thanh toán thủ công; học viên xem hóa đơn | Giáo vụ, Học viên | Finance | Should | US-24 | - |

### 3.7 Thông báo và lưu trữ file (MediaNotification)

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-25 | Xem thông báo, số chưa đọc, đánh dấu đã đọc | Phân trang, mới nhất trước | Mọi người dùng | MediaNotification | Must | US-25 | 21 |
| FR-26 | Tự tạo thông báo theo sự kiện | Ghi danh, bài tập mới, bài được chấm, hóa đơn, cảnh báo | Hệ thống | MediaNotification | Should | US-26 | - |
| FR-27 | Dịch vụ lưu file | Cấp presigned URL tải lên/tải về | Hệ thống, Dịch vụ lưu trữ file | MediaNotification | Must | US-27 | - |

### 3.8 Tương tác và thống kê

| Mã | Chức năng | Mô tả ngắn | Tác nhân | Module | Ưu tiên | US | Ảnh |
|---|---|---|---|---|---|---|---|
| FR-28 | Phản hồi | Tiêu đề, nội dung, rating 1-5; giáo vụ xử lý | Học viên, Giáo vụ | MediaNotification | Should | US-28 | 04 |
| FR-29 | Thống kê cá nhân | Số liệu lấy từ API từng module, không join chéo schema | Học viên | Nhiều module | Should | US-29 | 03 |
| FR-30 | FAQ | Học viên xem; admin quản lý | Học viên, Admin | MediaNotification | Could | US-30 | 16 |
| FR-31 | Bảng tin | Đăng bài, thích, nhận xét | Học viên | MediaNotification | Could | US-31 | 02 |
| FR-32 | Bài luyện tập | Danh sách bài luyện tập (tên, người tạo, ngày tạo) | Học viên | Academic | Could | US-32 | 12 |
| FR-33 | Điểm danh nhận Xu | Làm cuối cùng nếu còn thời gian | Học viên | Enrollment (tạm) | Could | US-33 | 05 |

### 3.9 Yêu cầu kỹ thuật nền (không phải FR)

TS-01..TS-04 Đã có (khung solution, Event Bus, DbContext/schema riêng, architecture test). TS-05 (Docker Compose) và TS-06 (Jenkinsfile) là Dự kiến. FE-01..FE-06 (frontend React) là Dự kiến.

### 3.10 Quy tắc nghiệp vụ chính

Nguồn: tiêu chí chấp nhận trong backlog.

- Điểm danh có 6 trạng thái: `Present` (Có mặt), `AbsentUnexcused` (Vắng không phép), `AbsentExcused` (Vắng có phép), `Late` (Đi trễ), `LateOver20Min` (Đi trễ hơn 20 phút), `LeftEarly` (Về sớm). Sửa được trong 24h sau buổi học; mỗi lần sửa phát lại `AttendanceRecorded` với `EventId` mới nên Enrollment phải tính lại bộ đếm vi phạm từ trạng thái mới nhất, không cộng dồn (xem tài liệu 04 mục 6.1).
- Trạng thái lớp suy ra từ ngày hiện tại (không lưu): trước ngày bắt đầu = Sắp diễn ra; trong khoảng = Đang diễn ra; sau ngày kết thúc = Kết thúc.
- Ghi danh trùng (cùng học viên, cùng lớp) hoặc lớp đầy: từ chối (409); lớp đã kết thúc: 400.
- Hóa đơn idempotent theo `EnrollmentId`; hạn thanh toán = ngày ghi danh + 7 ngày; trạng thái chỉ chuyển Chưa thanh toán sang Đã thanh toán.
- File tải lên tối đa 20 MB; định dạng cho phép: pdf, docx, mp3, m4a, jpg, png.
- Rating phản hồi từ 1 đến 5.
- Thang điểm bài tập: 0-10, lưu kiểu `numeric(4,2)`.

## 4. Yêu cầu phi chức năng

Các con số dưới đây là mục tiêu thiết kế.

| Mã | Nhóm | Yêu cầu | Chỉ số đo / Cách kiểm chứng | Nguồn |
|---|---|---|---|---|
| NFR-01 | Hiệu năng | API đọc có p95 < 500 ms với khoảng 100 người dùng đồng thời trên VPS 2 vCPU / 4 GB (mục tiêu) | Đo bằng công cụ tải (ví dụ k6) trên môi trường triển khai | Mục tiêu thiết kế |
| NFR-02 | Hiệu năng | Mọi API danh sách có phân trang: mặc định 20, tối đa 100 (mục tiêu) | Kiểm thử API: `pageSize` > 100 bị giới hạn | Mục tiêu thiết kế |
| NFR-03 | Hiệu năng | File tải lên đi thẳng lên object storage qua presigned URL, không đi qua API | Quan sát luồng mạng; API không nhận payload file | Backlog US-20, US-27 |
| NFR-04 | Bảo mật | Xác thực JWT + refresh token; thu hồi khi đăng xuất hoặc đổi mật khẩu (Dự kiến) | Test: dùng lại refresh token đã thu hồi trả 401 | Backlog US-01..03 |
| NFR-05 | Bảo mật | Mật khẩu lưu dạng hash, không trả về trong API; thuật toán: `PasswordHasher` của ASP.NET Core (PBKDF2) | Kiểm tra CSDL và response không chứa mật khẩu gốc | Backlog US-01 |
| NFR-06 | Bảo mật | Phân quyền theo vai trò: thiếu token 401, thiếu quyền 403; học viên chỉ xem dữ liệu của mình | Test phân quyền từng endpoint | Backlog US-06 |
| NFR-07 | Bảo mật | Thông báo đăng nhập sai không tiết lộ email có tồn tại | Test: response sai email và sai mật khẩu giống nhau | Backlog US-01 |
| NFR-08 | Bảo mật | Presigned URL có hạn dùng cấu hình được; không lộ access key; chặn file > 20 MB và sai định dạng (cách thực hiện ở tài liệu 03 mục 7.3) | Test: URL hết hạn bị từ chối; file 21 MB hoặc đuôi .exe bị từ chối | Backlog US-27 |
| NFR-09 | Bảo mật | Không commit secret; cấu hình qua biến môi trường; HTTPS qua Cloudflare | Rà soát repo, quét secret trong CI | CLAUDE.md |
| NFR-10 | Dễ sử dụng | Giao diện tiếng Việt; bố cục sidebar trái và header như ảnh 20, 21 | Đối chiếu ảnh tham khảo | Ảnh 20, 21 |
| NFR-11 | Dễ sử dụng | Responsive desktop và mobile; thông báo lỗi rõ ràng | Kiểm tra thủ công trên 2 kích thước màn hình | Mục tiêu thiết kế |
| NFR-12 | Bảo trì / Mở rộng | Modular Monolith; mỗi module có DbContext và schema riêng; chỉ giao tiếp qua integration event; có thể tách module thành service sau này | Architecture test (NetArchTest) xanh (Đã có) | CLAUDE.md |
| NFR-13 | Kiểm thử | Unit test tầng Application, architecture test, integration test với `WebApplicationFactory`; CI chạy test trên Jenkins | `dotnet test` xanh; Jenkins là Dự kiến | Backlog mục 4 |
| NFR-14 | Triển khai / Vận hành | (a) Endpoint `GET /health` (Đã có). (b) `docker compose up` chạy API + PostgreSQL + MinIO và multi-stage build (Dự kiến). (c) Log có cấu trúc (Dự kiến) | (a) Gọi `/health` trả 200. (b) Chạy một lệnh trên máy sạch | CLAUDE.md, TS-05 |
| NFR-15 | Tin cậy / Toàn vẹn | Xử lý event idempotent (nhận trùng không tạo bản ghi thứ hai). Hạn chế đã biết: Event Bus in-memory mất event khi tiến trình dừng, và chỉ log lỗi handler chứ không retry (handler lỗi bị bỏ qua); hướng nâng cấp là Outbox + retry | Test: publish cùng event 2 lần chỉ tạo 1 hóa đơn | Backlog US-23 |
| NFR-16 | Tương thích | Chrome/Edge/Firefox bản mới; API RESTful JSON, có tài liệu Swagger/OpenAPI (Đã có Swagger ở host) | Mở Swagger UI | Mã nguồn host |
| NFR-17 | Tin cậy / Vận hành | Xử lý ngoại lệ tập trung bằng middleware, trả lỗi theo chuẩn ProblemDetails (Dự kiến) | Test: lỗi nghiệp vụ trả mã 400/409 kèm ProblemDetails, không lộ stack trace | Đề cương môn học (tiêu chí middleware xử lý ngoại lệ) |
| NFR-18 | Triển khai / Vận hành | Dữ liệu PostgreSQL lưu trên Docker volume để không mất khi khởi động lại container (Dự kiến) | Khởi động lại `docker compose` rồi kiểm tra dữ liệu còn nguyên | Mục tiêu thiết kế |

## 5. Ràng buộc

Nguồn: `CLAUDE.md`, `Directory.Packages.props`.

- .NET 10 (`net10.0`), EF Core 10, PostgreSQL 16/17 (chưa có DB chạy thật), React + Vite (Dự kiến), Docker Compose, Jenkins, VPS + Cloudflare (Dự kiến).
- Mỗi module có DbContext và schema riêng; không join/truy vấn chéo schema; không tham chiếu trực tiếp project của module khác.
- Controller chỉ gọi tầng Application.
- Quy trình: Git feature branching, mỗi người một nhánh theo module, merge qua Pull Request. Quản lý tiến độ dự kiến dùng Jira từ giai đoạn code (xem tài liệu 03).

## 6. Ngoài phạm vi

Nguồn: backlog mục 6.

| Tính năng | Ảnh | Lý do |
|---|---|---|
| Tpoint | header | Gamification, không phải nghiệp vụ cốt lõi |
| Xu / điểm danh nhận Xu | 05, header | Chuyển thành Could (FR-33), chỉ làm cuối nếu còn thời gian; không thuộc phạm vi cam kết |
| Giải đấu, bảng xếp hạng | 06 | Gamification |
| Lật thẻ bài | 07 | Mini game |
| Vòng quay may mắn | 08 | Mini game |
| Khuyến mãi | 09 | Marketing, làm phức tạp học phí |
| Thư viện online | 15 | Cắt theo phạm vi thống nhất |
| Cửa hàng: Bộ đề, Mã kích hoạt, Giỏ hàng | 17, 18, header | Thương mại điện tử, đã chốt cắt |
| Thanh toán online, nhiều chi nhánh/trung tâm | 03 | Giữ học phí đơn giản |

## 7. Ma trận truy vết

Cột "Bảng CSDL chính" liên kết sang [tài liệu 04](04-thiet-ke-co-so-du-lieu.md) (bảng là thiết kế, chưa có migration). Mã F-x.y xem [tài liệu 02](02-so-do-phan-ra-chuc-nang.md).

| FR | US | Chức năng phân rã | Module | Bảng CSDL chính |
|---|---|---|---|---|
| FR-01 | US-01 | F1.1 | Identity | `identity.users`, `identity.refresh_tokens` |
| FR-02 | US-02 | F1.2 | Identity | `identity.refresh_tokens` |
| FR-03 | US-03 | F1.3 | Identity | `identity.users`, `identity.refresh_tokens` |
| FR-04 | US-04 | F1.4 | Identity | `identity.users` |
| FR-05 | US-05 | F1.5 | Identity | `identity.users`, `identity.user_roles`, `identity.roles` |
| FR-06 | US-06 | F1.6 | Identity | `identity.roles`, `identity.user_roles` |
| FR-07 | US-07 | F2.1 | Academic | `academic.classes`, `academic.courses` |
| FR-08 | US-08 | F2.2 | Academic | `academic.sessions` |
| FR-09 | US-09 | F2.3 | Academic | `academic.classes`, `academic.class_students`, `academic.sessions` |
| FR-10 | US-10 | F2.4 | Academic | `academic.sessions`, `academic.class_students` |
| FR-11 | US-11 | F2.5 | Academic | `academic.sessions`, `academic.classes` |
| FR-12 | US-12 | F3.1 | Enrollment | `enrollment.enrollments`, `enrollment.class_snapshots` |
| FR-13 | US-13 | F3.2 | Enrollment | `enrollment.enrollments` |
| FR-14 | US-14 | F3.3 | Enrollment | `enrollment.placement_tests` |
| FR-15 | US-15 | F3.4 | Enrollment | `enrollment.warnings`, `enrollment.enrollments` |
| FR-16 | US-16 | F4.1, F4.2 | Academic | `academic.attendances` |
| FR-17 | US-17 | F4.3 | Academic | `academic.attendances` |
| FR-18 | US-18 | F5.1 | Academic | `academic.assignments` |
| FR-19 | US-19 | F5.2 | Academic | `academic.assignments`, `academic.submissions` |
| FR-20 | US-20 | F5.3, F7.1 | Academic, MediaNotification | `academic.submissions`, `media_notification.stored_files` |
| FR-21 | US-21 | F5.4 | Academic | `academic.submissions` |
| FR-22 | US-22 | F5.5 | Academic | `academic.submissions` |
| FR-23 | US-23 | F6.1 | Finance | `finance.invoices` |
| FR-24 | US-24 | F6.2, F6.3 | Finance | `finance.invoices` |
| FR-25 | US-25 | F7.3 | MediaNotification | `media_notification.notifications` |
| FR-26 | US-26 | F7.2 | MediaNotification | `media_notification.notifications`, `media_notification.recipients_by_class` |
| FR-27 | US-27 | F7.1 | MediaNotification | `media_notification.stored_files` |
| FR-28 | US-28 | F8.1 | MediaNotification | `media_notification.feedbacks` |
| FR-29 | US-29 | F8.2 | Nhiều module | Không có bảng riêng; đọc từ API từng module |
| FR-30 | US-30 | F8.3 | MediaNotification | `media_notification.faqs` (Could) |
| FR-31 | US-31 | F8.4 | MediaNotification | `media_notification.posts`, `post_likes`, `post_comments` (Could) |
| FR-32 | US-32 | F5.6 | Academic | `academic.practice_exercises` (Could) |
| FR-33 | US-33 | F3.5 | Enrollment (tạm) | `enrollment.coin_wallets`, `coin_transactions` (Could) |
