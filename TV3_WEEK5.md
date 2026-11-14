# TV3 tuần 5 — Giới hạn, hẹn test, cảnh báo và thanh toán

## Phạm vi

Triển khai backend US-13, US-14, US-15, US-24 trên nhánh TV3, kế thừa ghi danh/hóa đơn tuần 3. Frontend hiện có từ develop chưa kết nối các màn hình Enrollment/Finance mới; có thể dùng Swagger/API theo hướng dẫn này. Kiểm thử tuần 4 và hợp đồng bàn giao TV2 ở [TV3_WEEK4.md](TV3_WEEK4.md).

## US-13 và US-15: giới hạn, bộ đếm, cảnh báo

- `GET /api/enrollment/enrollments/{id}/limits`: Staff/Admin hoặc đúng học viên; trả ba giới hạn và ba bộ đếm hiện tại. Đọc của người khác trả 404.
- `PUT /api/enrollment/enrollments/{id}/limits`: Staff/Admin; cả ba giới hạn bắt buộc, số nguyên không âm; ghi danh không tồn tại/không Active trả 404. Đổi giới hạn đánh giá lại cảnh báo ngay trong transaction.
- `GET /api/enrollment/warnings?studentId=&enrollmentId=&page=1&pageSize=20`: Staff/Admin lọc tùy chọn; Student luôn bị giới hạn theo JWT của mình dù truyền studentId người khác. Trả `{ items, total, page, pageSize }`, mới nhất trước; pageSize tối đa 100, page/pageSize <= 0 trả 400.
- `POST /api/enrollment/warnings/{id}/replay`: Staff/Admin; 202, phát lại cảnh báo đã lưu; ID không tồn tại trả 404.

Body cập nhật giới hạn:

```json
{ "maxAbsences": 3, "maxLates": 5, "maxSkippedAssignments": 2 }
```

Handler `AttendanceRecorded` giữ trạng thái mới nhất và tính lại bộ đếm từ snapshot, không tăng dồn khi replay. Handler `AssignmentMissed` chỉ đếm một lần theo bài/ghi danh. Xem chi tiết sáu trạng thái và điều kiện phát sự kiện ở tài liệu tuần 4.

Chỉ cảnh báo khi `count > max`; bằng giới hạn chưa cảnh báo. Mỗi `(EnrollmentId, WarningType)` có unique index; loại là `Absence`, `Late`, `SkippedAssignment`. Thay snapshot, bộ đếm và cảnh báo trong cùng transaction; khóa bản ghi ghi danh trước khi đọc để các request đồng thời không làm mất cập nhật. Không dùng khóa trong bộ nhớ phụ thuộc một tiến trình.

Cảnh báo là lịch sử đã phát: sửa điểm danh giảm bộ đếm hoặc nâng giới hạn không xóa cảnh báo cũ, không phát lại cảnh báo cùng loại lần hai. Hạ giới hạn có thể tạo cảnh báo lần đầu nếu bộ đếm hiện tại vượt giới hạn mới. Không tự hủy ghi danh hay giảm sĩ số khi vượt ngưỡng.

`WarningIssuedIntegrationEvent(WarningId, EnrollmentId, StudentId, ClassId, WarningType)` được phát sau commit. EventId = WarningId và OccurredOnUtc = IssuedAt; replay giữ nguyên hai giá trị để TV4 chống thông báo trùng bằng `(source_event_id, recipient_id)`.

## US-14: hẹn test đầu vào

- `POST /api/enrollment/placement-tests`: Staff/Admin, trả 201 và Location, luôn tạo trạng thái Pending.
- `PUT /api/enrollment/placement-tests/{id}`: Staff/Admin, trả 200; cập nhật thông tin lịch/trạng thái, giữ nguyên StudentId/CreatedBy/CreatedAt. ID không tồn tại trả 404.
- `GET /api/enrollment/placement-tests/{id}`: Staff/Admin hoặc đúng học viên; dữ liệu người khác trả 404.
- `GET /api/enrollment/placement-tests?studentId=&page=1&pageSize=20`: Staff/Admin lọc tùy chọn, Student chỉ xem của mình; phân trang giống cảnh báo, lịch mới nhất trước.

Body tạo:

```json
{
  "studentId": "<uuid-hoc-vien>",
  "location": "Phòng 101",
  "mode": "Offline",
  "scheduledAt": "2027-01-20T09:00:00+07:00",
  "testName": "IELTS đầu vào",
  "teacherId": null
}
```

Body cập nhật có cùng các trường lịch, không có studentId; thêm `status`: `Pending`, `Done` hoặc `Cancelled`. `mode` là `Offline`/`Online`. Location/TestName bắt buộc, trim, tối đa 200 ký tự; Pending cần thời gian trong tương lai. Có thể chuyển trạng thái hợp lệ qua PUT; chưa tự áp đặt quy tắc đóng/mở lại hoặc kiểm tra trùng lịch giảng dạy ngoài đặc tả. Giờ có offset chuyển về UTC và lưu microsecond. CreatedBy lấy từ JWT.

Student phải có trong read model Enrollment. TeacherId tùy chọn; nếu có phải là giáo viên được đồng bộ từ `UserRegistered`. `enrollment.test_teachers` chỉ giữ ID giáo viên để kiểm tra, không truy vấn Identity/Academic. Dữ liệu tài khoản cũ: Admin gọi `POST /api/identity/users/republish` với `{}` trước khi tạo lịch, để đồng bộ cả học viên và giáo viên.

## US-24: thanh toán và xem hóa đơn

- `GET /api/finance/invoices?studentId=&page=1&pageSize=20`: Staff/Admin hoặc Student; học viên chỉ nhận hóa đơn của mình, không tin bộ lọc giả. Phân trang tối đa 100, mới nhất trước.
- `GET /api/finance/invoices/{id}` và API cũ `GET /api/finance/invoices/by-enrollment/{enrollmentId}`: Staff/Admin hoặc đúng học viên; người khác trả 404.
- `POST /api/finance/invoices/{id}/pay`: Staff/Admin, không cần body. 200 khi chuyển Unpaid → Paid; đã Paid trả 409; không tồn tại trả 404.
- `POST /api/finance/invoices/{id}/payment/replay`: Staff/Admin, 202 khi phát lại sự kiện hóa đơn đã thanh toán; Unpaid trả 409; không tồn tại trả 404.

Payment là xác nhận thủ công, không tích hợp cổng thanh toán. PaidAtUtc lấy từ đồng hồ server; ConfirmedBy lấy từ JWT, không nhận số tiền hoặc người xác nhận từ client. Cập nhật có điều kiện `WHERE status = 'Unpaid'` bảo đảm nhiều request cùng lúc chỉ một request thành công; giữ nguyên người/thời gian xác nhận đầu tiên. GET trả thêm PaidAtUtc và ConfirmedBy.

Sau lưu phát `InvoicePaidIntegrationEvent(InvoiceId, EnrollmentId, StudentId, PaidAtUtc)`. EventId = InvoiceId và OccurredOnUtc = PaidAtUtc; replay giữ nguyên identity của sự kiện. Replay ghi danh tuần 3 không tạo thêm invoice và không chuyển Paid trở lại Unpaid. Tất cả API mới: thiếu JWT trả 401; Teacher không có quyền; Student không được sửa giới hạn/lịch hoặc xác nhận/replay.

## Migration và chạy demo

Migration `20261110003346_AddMonitoringAndPlacementTests` chỉ thêm bảng thuộc schema Enrollment: attendance_snapshots, missed_assignments, warnings, test_teachers, placement_tests và ràng buộc local. Không sửa/xóa dữ liệu ghi danh, không có FK/query chéo schema. Finance đã có PaidAt/ConfirmedBy và constraint từ tuần 3 nên không cần thêm cột.

Bật `Enrollment__InitializeDatabase=true` như Compose hiện có để áp dụng migration lúc khởi động, hoặc chạy EF migration bằng context Enrollment. Không đặt connection string kiểm thử trỏ vào database nghiệp vụ.

Demo qua Swagger (`/swagger` khi Development) hoặc PowerShell sau khi đăng nhập Staff/Admin, chuẩn bị `$baseUrl`, `$headers`, `$enrollmentId`, `$studentId` theo [tuần 3](TV3_WEEK3.md):

```powershell
$limits = @{ maxAbsences = 1; maxLates = 1; maxSkippedAssignments = 1 } | ConvertTo-Json
Invoke-RestMethod -Method Put -Uri "$baseUrl/api/enrollment/enrollments/$enrollmentId/limits" -Headers $headers -ContentType 'application/json' -Body $limits
Invoke-RestMethod -Uri "$baseUrl/api/enrollment/enrollments/$enrollmentId/limits" -Headers $headers

$appointment = @{ studentId = $studentId; location = 'Phòng 101'; mode = 'Offline'; scheduledAt = [DateTimeOffset]::UtcNow.AddDays(2).ToString('o'); testName = 'IELTS đầu vào'; teacherId = $null } | ConvertTo-Json
$test = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/enrollment/placement-tests" -Headers $headers -ContentType 'application/json' -Body $appointment
Invoke-RestMethod -Uri "$baseUrl/api/enrollment/placement-tests/$($test.id)" -Headers $headers
Invoke-RestMethod -Uri "$baseUrl/api/enrollment/warnings?enrollmentId=$enrollmentId" -Headers $headers

$invoice = Invoke-RestMethod -Uri "$baseUrl/api/finance/invoices/by-enrollment/$enrollmentId" -Headers $headers
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/finance/invoices/$($invoice.id)/pay" -Headers $headers
Invoke-RestMethod -Uri "$baseUrl/api/finance/invoices/$($invoice.id)" -Headers $headers
```

Đăng nhập Student để gọi GET các danh sách/chi tiết và kiểm tra chỉ dữ liệu của mình; dùng Student khác để xác nhận không thấy lịch/hóa đơn/cảnh báo. Gọi pay lần hai phải trả 409. Để demo sinh cảnh báo trước khi có producer Academic, dùng các test tự động gửi integration event qua handler thật; không mở endpoint HTTP cho người dùng tự ghi sự kiện điểm danh.

## Kiểm thử và giới hạn

Chạy `dotnet test EnglishCenter.sln -c Release` để build và chạy hồi quy SQLite/architecture. Đặt `TV3_POSTGRES` và `ENGLISHCENTER_TEST_POSTGRES` tới server PostgreSQL kiểm thử riêng để bật toàn bộ test; fixture tạo/xóa database tạm. Các ca mới nằm ở `Tv3Week4ContractTests.cs` và `Tv3Week45Tests.cs`, dùng chung fixture API tuần 3.

Kết quả local ngày 10/11/2026 sau khi tích hợp develop `f4489ef`: **325/325 đạt trên PostgreSQL 16, 0 lỗi, 0 bỏ qua** (BuildingBlocks 33, Academic 58, Architecture 53, Integration 181). Chế độ SQLite: **314 đạt, 11 ca PostgreSQL bỏ qua đúng cấu hình**. Có 31 ca mới cho tuần 4–5; đã kiểm chứng 12 sự kiện điểm danh/bài bỏ đồng thời, 8 yêu cầu thanh toán đồng thời, snapshot mới nhất, chống trùng cảnh báo và migration nâng cấp từ schema tuần 3 giữ nguyên ghi danh/sĩ số. TRX ở `../.tools/verification/tv3-week45/sqlite-final/` và `../.tools/verification/tv3-week45/postgres/`. Đây là kết quả local, chưa phải kết quả CI GitHub hoặc tích hợp API TV2.

Event bus vẫn in-memory, bắt/log lỗi handler; chưa có outbox/retry tự động. Snapshot và cảnh báo có tính nguyên tử trong Enrollment, xác nhận thanh toán có tính nguyên tử trong Finance; chưa bảo đảm nguyên tử giữa các module. Replay cảnh báo/thanh toán giúp đối soát event đã mất sau commit, nhưng 202 không chứng minh TV4 đã lưu thông báo. Chưa có producer AssignmentMissed, HTTP điểm danh/nộp/chấm bài hoặc handler thông báo của TV4 trong repo hiện tại; không tuyên bố các luồng đó đã hoàn tất.
