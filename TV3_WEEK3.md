# TV3 — Tuần 3: Ghi danh và hóa đơn (US-12, US-23)

## Nghiệp vụ đã triển khai

- Staff/Admin ghi danh học viên; Student/Teacher không được ghi danh.
- Enrollment nhận `UserRegistered` (Student) và `ClassCreated`, lưu snapshot cục bộ; không đọc chéo schema.
- Academic phát lại `ClassCreated` cho một lớp hoặc toàn bộ lớp cũ; chỉ Staff/Admin được gọi. Replay cùng `ClassId` không tạo snapshot trùng và không đặt lại `CurrentStudents`.
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
- `POST /api/academic/classes/{id}/replay` — Staff/Admin, không có body, trả 202; lớp không tồn tại trả 404.
- `POST /api/academic/classes/republish` — Staff/Admin, không có body, trả 200 với `{ "published": N }`; phát lại toàn bộ lớp trong Academic, không có lớp trả `{ "published": 0 }`.
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
2. Staff đăng nhập → tạo Student, Teacher qua Identity → tạo lớp qua Academic. Với dữ liệu cũ, làm theo mục backfill bên dưới trước khi ghi danh.
3. Gọi POST ghi danh → kiểm tra GET ghi danh, GET hóa đơn: đúng học viên/lớp/tiền/hạn.
4. Đăng nhập học viên → GET classes/mine. Học viên khác không thấy lớp hoặc hóa đơn này.
5. Ghi danh lại trả 409; lớp hết chỗ trả 409; lớp qua ngày kết thúc trả 400.
6. Gọi replay hai lần → vẫn một invoice, một class_student và sĩ số không tăng.

## Backfill lớp và tài khoản đã tồn tại

Thực hiện sau khi đã bật migration của cả bốn module. Ví dụ PowerShell (đổi URL và thông tin đăng nhập theo môi trường):

```powershell
$baseUrl = 'http://localhost:5080'
$login = @{ email = 'admin@example.com'; password = '<mat-khau-admin>' } | ConvertTo-Json
$token = (Invoke-RestMethod -Method Post -Uri "$baseUrl/api/identity/auth/login" -ContentType 'application/json' -Body $login).accessToken
$headers = @{ Authorization = "Bearer $token" }

# Chỉ Admin: đồng bộ tài khoản cũ; {} bao gồm cả Student và Teacher.
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/identity/users/republish" -Headers $headers -ContentType 'application/json' -Body '{}'

# Staff hoặc Admin: phát lại mọi lớp, kể cả lớp tạo trước khi Enrollment được triển khai.
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/academic/classes/republish" -Headers $headers

# Hoặc chỉ phát lại một lớp nếu đã biết classId (lấy từ phản hồi tạo/đọc lớp Academic).
$classId = '<uuid-lop>'
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/academic/classes/$classId/replay" -Headers $headers

# Ghi danh sau khi đồng bộ.
$body = @{ studentId = '<uuid-hoc-vien>'; classId = $classId; maxAbsences = 3; maxLates = 5; maxSkippedAssignments = 2 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/enrollment/enrollments" -Headers $headers -ContentType 'application/json' -Body $body
Invoke-RestMethod -Uri "$baseUrl/api/finance/invoices/by-enrollment/$($enrollment.id)" -Headers $headers
```

Student/Teacher gọi hai API phát lại lớp trả 403; chưa đăng nhập trả 401. Không cần truy vấn hay sửa dữ liệu chéo schema: Academic chỉ đọc lớp của mình rồi phát `ClassCreatedIntegrationEvent`; Enrollment nhận sự kiện để dựng snapshot.

Có thể chạy lại một hoặc cả hai API phát lại lớp nhiều lần. Enrollment kiểm tra payload theo `ClassId`, giữ nguyên snapshot và sĩ số đã ghi danh; không tạo lớp, ghi danh hoặc hóa đơn mới do replay lớp. Ví dụ lớp có một chỗ đã được ghi danh vẫn đầy sau replay. Payload cùng `ClassId` nhưng khác mã/ngày/sĩ số tối đa/học phí bị handler từ chối và ghi log; replay không phải API cập nhật lớp.

`published` đếm số lớp đã phát sự kiện, còn 202 xác nhận lệnh replay đã chạy; cả hai chưa bảo đảm handler lưu thành công vì event bus bắt và log lỗi. Sau backfill, thử ghi danh và đọc hóa đơn để xác nhận luồng. Nếu vẫn báo lớp chưa đồng bộ, kiểm tra log handler `ClassCreatedHandler`, xử lý lỗi rồi phát lại. Lớp đã kết thúc vẫn được backfill nhưng Enrollment tiếp tục từ chối ghi danh theo quy tắc ngày kết thúc.

## Kiểm thử

`dotnet test EnglishCenter.sln` chạy unit, kiến trúc và API/SQLite; bốn ca đồng thời PostgreSQL chỉ chạy khi có `TV3_POSTGRES`.

Để kiểm tra database thật, đặt `TV3_POSTGRES` tới **server kiểm thử riêng**, tài khoản có quyền CREATE DATABASE; đặt thêm `ENGLISHCENTER_TEST_POSTGRES` cùng connection string để bật bốn ca PostgreSQL của Identity từ develop. Chạy lại `dotnet test EnglishCenter.sln`. Mỗi ca tuần 3 tự tạo database tên `tv3_test_<uuid>` và xóa chính database đó khi kết thúc. CI dùng PostgreSQL 17; lượt kiểm tra cục bộ dùng PostgreSQL 16.

Kết quả ngày 05/11/2026 sau khi tích hợp develop `fc3542d`: toàn solution **281/281 đạt**, không bỏ qua ca nào khi bật cả hai biến PostgreSQL. Riêng tuần 3: **19/19 đạt** trên PostgreSQL; chế độ SQLite **16 đạt, 3 ca đồng thời PostgreSQL được bỏ qua đúng cấu hình**. Đã sửa test fixture PostgreSQL của Identity để nạp connection string trước khi đăng ký module. TRX bản tích hợp lưu tại `../.tools/verification/tv3-week3/merged-final/`. Workflow CI đã được cấu hình nhưng chưa chạy trên GitHub trong phiên này.

Kiểm chứng bổ sung ngày 10/11/2026 cho yêu cầu review backfill lớp: build Release và toàn solution trên SQLite **285 đạt, 8 bỏ qua** do chưa bật PostgreSQL; sau khi bật cả hai biến PostgreSQL, **293/293 đạt, 0 bỏ qua, 0 lỗi**. Riêng `Tv3Week3Tests`: **27/27 đạt** trên PostgreSQL 16. Bổ sung kiểm tra quyền 401/403 và Staff/Admin cho cả hai API, lớp không tồn tại trả 404, lớp cũ chỉ có trong Academic → replay/backfill → ghi danh và tạo hóa đơn thành công, replay nhiều lần giữ nguyên sĩ số và trạng thái đầy lớp; tám replay đồng thời trên PostgreSQL chỉ tạo một snapshot. Unit test xác nhận giữ payload gốc, EventId mới và không lưu lại lớp; backfill kho lớp rỗng trả published = 0. TRX lưu tại `../.tools/verification/tv3-week3/class-replay/` và `../.tools/verification/tv3-week3/class-replay-postgres/`. Đây là kết quả local; chưa chạy CI GitHub trong lượt sửa này.

## Giới hạn bàn giao

Event bus trong bộ nhớ vẫn bắt lỗi handler, không có outbox/retry tự động. 201 chỉ xác nhận ghi danh đã lưu; 202 chỉ xác nhận yêu cầu replay đã chạy. Sau replay phải đọc API Finance để xác nhận hóa đơn. Đã kiểm thử Finance lỗi rồi phục hồi bằng replay; chưa bảo đảm phát InvoiceCreated khi tiến trình chết ngay sau commit hóa đơn.

Phần Academic bổ sung chỉ phục vụ danh sách lớp theo ghi danh. Buổi học, lịch học/lịch dạy và kiểm tra trùng giờ thuộc TV2, chưa có trong repository này; chưa tuyên bố demo lịch học hoàn tất. Xác nhận thanh toán US-24 được bổ sung trong [tuần 5](TV3_WEEK5.md).
