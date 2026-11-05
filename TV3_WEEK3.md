# TV3 — Tuần 3: Ghi danh và hóa đơn (US-12, US-23)

## Nghiệp vụ đã triển khai

- Staff/Admin ghi danh học viên; Student/Teacher không được ghi danh.
- Enrollment nhận `UserRegistered` (Student) và `ClassCreated`, lưu snapshot cục bộ; không đọc chéo schema.
- Chặn học viên/lớp chưa đồng bộ (400), lớp kết thúc (400), ghi danh trùng hoặc đầy (409).
- Ngày nghiệp vụ UTC+7; vẫn được ghi danh trong ngày kết thúc lớp.
- Giáo vụ nhập ba giới hạn không âm; chưa có mặc định thống nhất nên bắt buộc nhập rõ. Bộ đếm khởi tạo 0.
- Học phí lấy từ snapshot lớp, VND nguyên 0–999.999.999.999; client không quyết định số tiền.
- Tăng sĩ số có điều kiện và lưu ghi danh trong cùng transaction; lỗi insert rollback cả sĩ số. Unique(student_id, class_id) chống trùng.
- Sau commit phát `StudentEnrolled`; Academic lưu `class_students`, Finance lưu `invoices` thay recorder mẫu.
- Unique(enrollment_id) bảo đảm một hóa đơn kể cả event trùng, EventId mới hoặc nhận đồng thời. Payload xung đột bị từ chối, hóa đơn cũ giữ nguyên.
- Hạn thanh toán = ngày ghi danh theo UTC+7 + 7 ngày, không lấy ngày xử lý/replay. Ban đầu `Unpaid`; mã hóa đơn là UUID 32 ký tự có unique index.
- Student chỉ xem ghi danh/hóa đơn/lớp của mình; truy vấn ghi danh/hóa đơn người khác trả 404. Teacher không được đọc hai API này.

## API bàn giao

- `POST /api/enrollment/enrollments` — Staff/Admin, trả 201 và Location.
- `GET /api/enrollment/enrollments/{id}` — Staff/Admin hoặc đúng Student.
- `POST /api/enrollment/enrollments/{id}/replay` — Staff/Admin, trả 202, phát lại StudentEnrolled để đối soát.
- `GET /api/finance/invoices/by-enrollment/{enrollmentId}` — Staff/Admin hoặc đúng Student.
- `GET /api/academic/classes/mine` — Student, lấy StudentId từ JWT.

Body ghi danh:

```json
{
  "studentId": "UUID học viên",
  "classId": "UUID lớp",
  "maxAbsences": 3,
  "maxLates": 5,
  "maxSkippedAssignments": 2
}
```

Ba giới hạn trên chỉ là dữ liệu demo, không phải mặc định nghiệp vụ.

## Khởi chạy và demo

1. Cấu hình `ConnectionStrings__Default`, JWT và bật `Identity__InitializeDatabase`, `Academic__InitializeDatabase`, `Enrollment__InitializeDatabase`, `Finance__InitializeDatabase` = `true`. Compose đã bật cả bốn migration.
2. Staff đăng nhập → tạo Student, Teacher qua Identity → tạo lớp qua Academic. Dữ liệu cũ cần phát lại UserRegistered/ClassCreated để tạo snapshot; không tự backfill bằng query chéo schema.
3. Gọi POST ghi danh → kiểm tra GET ghi danh, GET hóa đơn: đúng học viên/lớp/tiền/hạn.
4. Đăng nhập học viên → GET classes/mine. Học viên khác không thấy lớp hoặc hóa đơn này.
5. Ghi danh lại trả 409; lớp hết chỗ trả 409; lớp qua ngày kết thúc trả 400.
6. Gọi replay hai lần → vẫn một invoice, một class_student và sĩ số không tăng.

## Kiểm thử

`dotnet test EnglishCenter.sln` chạy unit, kiến trúc và API/SQLite; ba ca đồng thời PostgreSQL chỉ chạy khi có `TV3_POSTGRES`.

Để kiểm tra database thật, đặt `TV3_POSTGRES` tới **server kiểm thử riêng**, tài khoản có quyền CREATE DATABASE; đặt thêm `ENGLISHCENTER_TEST_POSTGRES` cùng connection string để bật bốn ca PostgreSQL của Identity từ develop. Chạy lại `dotnet test EnglishCenter.sln`. Mỗi ca tuần 3 tự tạo database tên `tv3_test_<uuid>` và xóa chính database đó khi kết thúc. CI dùng PostgreSQL 17; lượt kiểm tra cục bộ dùng PostgreSQL 16.

Kết quả ngày 05/11/2026 sau khi tích hợp develop `fc3542d`: toàn solution **281/281 đạt**, không bỏ qua ca nào khi bật cả hai biến PostgreSQL. Riêng tuần 3: **19/19 đạt** trên PostgreSQL; chế độ SQLite **16 đạt, 3 ca đồng thời PostgreSQL được bỏ qua đúng cấu hình**. Đã sửa test fixture PostgreSQL của Identity để nạp connection string trước khi đăng ký module. TRX bản tích hợp lưu tại `../.tools/verification/tv3-week3/merged-final/`. Workflow CI đã được cấu hình nhưng chưa chạy trên GitHub trong phiên này.

## Giới hạn bàn giao

Event bus trong bộ nhớ vẫn bắt lỗi handler, không có outbox/retry tự động. 201 chỉ xác nhận ghi danh đã lưu; 202 chỉ xác nhận yêu cầu replay đã chạy. Sau replay phải đọc API Finance để xác nhận hóa đơn. Đã kiểm thử Finance lỗi rồi phục hồi bằng replay; chưa bảo đảm phát InvoiceCreated khi tiến trình chết ngay sau commit hóa đơn.

Phần Academic bổ sung chỉ phục vụ danh sách lớp theo ghi danh. Buổi học, lịch học/lịch dạy và kiểm tra trùng giờ thuộc TV2, chưa có trong repository này; chưa tuyên bố demo lịch học hoàn tất. Xác nhận thanh toán US-24 thuộc giai đoạn sau.
