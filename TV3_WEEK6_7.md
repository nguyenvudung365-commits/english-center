# TV3 tuần 6–7 — Thống kê Enrollment và bàn giao

## Tuần 6 — Phần Enrollment của US-29

`GET /api/enrollment/statistics/mine` trả `200 OK` với dữ liệu của học viên đang đăng nhập.

```json
{ "completedClasses": 2 }
```

- `completedClasses` là số ghi danh `Active` vào lớp có ngày kết thúc trước ngày hiện tại theo giờ Việt Nam (UTC+7). Lớp kết thúc hôm nay được tính từ 00:00 ngày mai. Ghi danh `Cancelled` không được tính.
- API lấy mã học viên từ JWT. Query string `studentId` không thay đổi phạm vi dữ liệu. Học viên chưa hoàn thành lớp nào nhận `{"completedClasses":0}`. Thiếu JWT hợp lệ trả `401`. Teacher, Staff và Admin trả `403`.
- TV4 có thể dùng trường `completedClasses` cho mục “Lớp đã hoàn thành” trên giao diện thống kê. Số liệu được đếm trong một truy vấn từ `enrollment.enrollments` và `enrollment.class_snapshots`. API sử dụng schema và dữ liệu hiện có, không cần migration.

## Tuần 7 — Kịch bản demo

Chạy API với cơ sở dữ liệu demo và một tài khoản Staff theo [hướng dẫn tuần 3](TV3_WEEK3.md#khởi-chạy-và-demo). Tạo hai học viên A và B, một giáo viên và một lớp còn chỗ. Có thể thực hiện các thao tác trong Swagger ở môi trường Development.

1. **Ghi danh.** Đăng nhập Staff và gọi `POST /api/enrollment/enrollments` cho A với `maxAbsences: 1`, `maxLates: 5`, `maxSkippedAssignments: 2`. Ghi lại mã ghi danh từ phản hồi `201`. Ghi danh A vào cùng lớp lần nữa phải trả `409`.
2. **Hóa đơn.** Gọi `GET /api/finance/invoices/by-enrollment/{enrollmentId}`. Hóa đơn phải là `Unpaid`, đúng học viên và học phí của lớp, hạn thanh toán bằng ngày ghi danh theo giờ Việt Nam cộng 7 ngày. Dùng `POST /api/finance/invoices/{id}/pay` để xác nhận thanh toán. Kiểm tra `Paid`, thời điểm thanh toán và người xác nhận. Gọi lần hai trả `409`. Phát lại ghi danh qua `POST /api/enrollment/enrollments/{enrollmentId}/replay` rồi đọc lại hóa đơn để xác nhận mã hóa đơn và trạng thái `Paid`.
3. **Cảnh báo.** Chạy ca tự động bên dưới để phát hai sự kiện `AttendanceRecorded` trạng thái `AbsentUnexcused` cho hai buổi của A qua event bus thật. Ca kiểm thử xác nhận chưa phát cảnh báo ở buổi thứ nhất và chỉ phát một cảnh báo `Absence` ở buổi thứ hai dù sự kiện được phát lại. Có thể dùng Swagger để xem cảnh báo đã có trong dữ liệu demo qua `GET /api/enrollment/warnings?enrollmentId={id}`.
4. **Kiểm tra đúng người.** Đăng nhập A, gọi `GET /api/finance/invoices`, `GET /api/enrollment/warnings` và `GET /api/enrollment/statistics/mine`. Gọi lại với `?studentId={id-cua-B}` vẫn chỉ trả dữ liệu của A. Đăng nhập B và thử đọc ghi danh hoặc hóa đơn bằng ID của A để kiểm tra `404`. Thử thống kê với học viên có ghi danh Active vào lớp đã qua ngày kết thúc để thấy bộ đếm tăng. Ca kiểm thử tuần 6 có sẵn dữ liệu cho mốc chuyển ngày và ghi danh đã hủy.

Chạy các ca demo tự động trên dữ liệu tạm:

```powershell
dotnet test EnglishCenter.sln -c Release --filter 'FullyQualifiedName~Week6_|FullyQualifiedName~Week7_'
```

## Đối chiếu tiêu chí và chạy hồi quy

- **US-29 Enrollment.** Các ca `Week6_` kiểm tra ngày kết thúc, nửa đêm UTC+7, ghi danh đã hủy, dữ liệu rỗng, quyền Student và truy vấn kèm mã học viên khác.
- **US-12, US-23 và US-24.** Ca `Week7_` gọi API Identity, Academic, Enrollment và Finance, kiểm tra học phí/hạn thanh toán, chống trùng khi replay, chuyển trạng thái hóa đơn và quyền đọc theo học viên. Bộ hồi quy tuần 3–5 kiểm tra thêm lớp đầy, rollback, thanh toán đồng thời và nhật ký thanh toán.
- **US-13 và US-15.** Ca `Week7_` kiểm tra ngưỡng cảnh báo và chống trùng qua event bus, đối chiếu sự kiện đã phát với cảnh báo trả qua API. Bộ hồi quy tuần 4–5 kiểm tra thêm sửa điểm danh, sự kiện đến sai thứ tự, giới hạn và cập nhật đồng thời.
- **US-14.** Bộ hồi quy tuần 5 kiểm tra hẹn test, cập nhật và giới hạn quyền đọc đối với từng học viên.

```powershell
# SQLite và kiểm thử kiến trúc
dotnet test EnglishCenter.sln -c Release

# PostgreSQL trên máy chủ kiểm thử riêng, với tài khoản có quyền tạo database tạm
$env:TV3_POSTGRES = '<chuoi-ket-noi-postgresql-kiem-thu>'
$env:ENGLISHCENTER_TEST_POSTGRES = $env:TV3_POSTGRES
dotnet test EnglishCenter.sln -c Release --logger 'trx;LogFilePrefix=tv3-week67'
```

### Kết quả ngày 14/11/2026

- PostgreSQL 16.15, bản Release: **331/331 ca đạt**, gồm 187 ca tích hợp, 53 ca kiến trúc, 58 ca Academic và 33 ca BuildingBlocks.
- SQLite, bản Release: **320 ca đạt**, 11 ca chỉ chạy trên PostgreSQL được bỏ qua. Cả ba ca mới đạt trên hai hệ quản trị cơ sở dữ liệu.
- Báo cáo TRX cục bộ nằm trong `../.tools/verification/tv3-week67/release-postgres/` và `../.tools/verification/tv3-week67/release-sqlite/`.

### Phạm vi xác nhận

Kịch bản cảnh báo gửi sự kiện vào event bus từ fixture kiểm thử. Nhánh TV3 chưa có API ghi điểm danh để chạy từ Swagger hoặc handler MediaNotification để kiểm tra thông báo đến người nhận. Cảnh báo đã lưu được kiểm tra qua API Enrollment. Kết quả là trên cây làm việc `TV3` (HEAD `94a4eba`), gồm phần tuần 5 cục bộ. `develop` tại `1dbe848` có thay đổi về quyền truy cập lớp và migration Enrollment cần đối chiếu khi tích hợp. Kiểm thử bản tích hợp, giao diện thống kê và triển khai staging thuộc đợt bàn giao chung.
