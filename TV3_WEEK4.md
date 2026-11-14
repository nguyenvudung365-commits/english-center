# TV3 tuần 4 — Kiểm thử và bàn giao hợp đồng Academic

## Phạm vi được xác nhận

TV2 chưa bàn giao API điểm danh, bài tập, nộp lại và chấm bài. TV3 chuẩn bị test/hợp đồng tích hợp trước, không triển khai thay các API Academic này và không tuyên bố đã kiểm thử chúng qua HTTP. Phần xử lý cảnh báo phía nhận được bàn giao riêng ở tuần 5.

## Đã có kiểm thử tự động

- `tests/EnglishCenter.IntegrationTests/Tv3Week4ContractTests.cs`: 10 ca kiểm tra hợp đồng sự kiện, chạy độc lập với code tuần 5.
- Sáu trạng thái `AttendanceRecorded`: JSON, tên trường, UTC và giữ `RecordedAtUtc` khi phát lại với EventId mới.
- `AssignmentMissed`: giữ khóa nghiệp vụ bài/lớp/học viên qua JSON và replay.
- `WarningIssued`: giữ đúng ghi danh, học viên, lớp và loại cảnh báo qua JSON.
- `AssignmentPublished`: giữ lớp và khoảng thời gian mở/đóng nộp bài. `SubmissionGraded`: giữ bài nộp, bài tập, học viên và điểm; giá trị mẫu không quyết định thay nhóm về thang điểm.
- Chỉ bổ sung contracts `AssignmentMissed` và `WarningIssued`; chưa thêm handler, migration hoặc endpoint tuần 5 vào bàn giao tuần 4. Test snapshot/bộ đếm/cảnh báo, rollback và đồng thời thuộc bàn giao tuần 5.

Chạy riêng các ca liên quan:

```powershell
dotnet test EnglishCenter.sln -c Release --filter 'FullyQualifiedName~Tv3Week4ContractTests'
```

Kiểm chứng riêng nội dung commit tuần 4 ngày 10/11/2026: xuất cây Git đã stage sang thư mục kiểm tra, không đưa code tuần 5 vào; build Release và toàn solution **296 đạt, 0 lỗi, 8 ca PostgreSQL bỏ qua** do chưa bật kết nối kiểm thử. Cả **10/10 test hợp đồng tuần 4 đạt**. TRX local lưu tại `../.tools/verification/tv3-week4-isolated-results/`. Chưa chạy CI GitHub hoặc API điểm danh/nộp/chấm của TV2.

## Hợp đồng TV2 → TV3

`AttendanceRecordedIntegrationEvent(SessionId, ClassId, StudentId, Status, RecordedAtUtc)` đã tồn tại; không đổi chữ ký. Các ID không rỗng, `Status` là đúng tên tiếng Anh, `RecordedAtUtc` là UTC. Mỗi lần sửa dùng thời điểm sửa mới hơn, không dùng thời điểm phát lại làm thời điểm nghiệp vụ. PostgreSQL lưu chính xác microsecond; hai trạng thái khác nhau cùng timestamp sau chuẩn hóa microsecond bị từ chối để không chọn kết quả phụ thuộc thứ tự nhận.

Hợp đồng xử lý phía Enrollment khi tích hợp tuần 5: lưu snapshot theo `(SessionId, StudentId)`, gắn với ghi danh của đúng học viên/lớp. Sau thay đổi tính lại bộ đếm trong transaction:

- `AbsentUnexcused` → nghỉ không phép.
- `Late`, `LateOver20Min` → đi trễ.
- `Present`, `AbsentExcused`, `LeftEarly` → không tăng hai bộ đếm trên.

Sự kiện cũ hơn snapshot bị bỏ qua; cùng timestamp/cùng trạng thái là phát lại hợp lệ. Ghi danh chưa tồn tại hoặc không Active làm handler lỗi có log; không tự tạo ghi danh, không đọc Academic để bù. Sau sửa dữ liệu nguồn cần phát lại sự kiện.

Bổ sung `Academic.Contracts/IntegrationEvents/AssignmentMissedIntegrationEvent.cs` với `(AssignmentId, ClassId, StudentId)` và metadata EventId/OccurredOnUtc. TV2 chỉ phát sau hạn nộp cho học viên không có bài nộp hợp lệ. TV3 chống đếm trùng theo `(AssignmentId, EnrollmentId)`, kể cả EventId mới. Sự kiện này biểu diễn kết luận cuối sau hạn; chưa có hợp đồng thu hồi việc bỏ bài sau hạn. Nếu nhóm cho phép nộp bù sau hạn, cần bổ sung hợp đồng điều chỉnh trước khi mở luồng đó. TV3 không tự suy bỏ bài bằng query sang Academic hoặc bằng `AssignmentPublished`.

`AssignmentPublished` và `SubmissionGraded` giữ nguyên; không dùng sự kiện chấm điểm để suy học viên đã bỏ bài. Thông báo thuộc TV4; test TV3 xác nhận sự kiện đã phát, chưa chứng minh có bản ghi thông báo khi MediaNotification chưa triển khai handler.

## Kịch bản chạy sau khi TV2 bàn giao

Chuẩn bị giáo viên A được phân công lớp A, giáo viên B không được phân công; học viên A đã ghi danh và học viên B ngoài lớp. Dùng `TimeProvider` hoặc dữ liệu thời gian cố định; không dùng sleep để vượt hạn. Khi biết route/DTO thật, chuyển các ca dưới đây sang API integration test của TV2:

1. Điểm danh: thiếu JWT → 401; Student không có quyền ghi; giáo viên ngoài lớp bị chặn; giáo viên đúng lớp ghi đủ sáu trạng thái. Kiểm tra payload có đúng SessionId/ClassId/StudentId, không chấp nhận học viên ngoài lớp.
2. Sửa điểm danh: kiểm tra ngay trước, đúng mốc và sau 24 giờ kể từ buổi học theo hợp đồng TV2. Sửa hợp lệ phát `RecordedAtUtc` mới; nhận lại event cũ không làm sai bộ đếm. Mốc 24 giờ cần TV2 xác nhận chính xác cách tính thời điểm kết thúc buổi.
3. Xem điểm danh/tổng hợp: học viên chỉ thấy của mình; giáo viên chỉ lớp được phân công; đổi ID trên URL/query không đọc được dữ liệu người khác. Tổng hợp phải khớp dữ liệu chi tiết sau sửa.
4. Bài tập: chưa gán buổi học phải ẩn với học viên; kiểm tra trước mở, đúng lúc mở, trong khoảng, đúng lúc đóng và sau đóng. TV2 cần chốt tính bao gồm tại mốc đóng trước khi viết assertion cuối.
5. Nộp/nộp lại: học viên trong lớp nộp trong hạn; nộp lại thay bài cũ, chỉ một bài cho `(AssignmentId, StudentId)`; ngoài hạn/ngoài lớp/giả StudentId/fileKey người khác bị chặn. File đúng chủ, đúng định dạng và tối đa 20,971,520 byte cần phối hợp TV4; không dùng mock file store để tuyên bố đã tích hợp upload thật.
6. Chấm: chỉ giáo viên của lớp; học viên/giáo viên khác bị chặn; nhận xét và điểm lưu đúng bài. Thang 0–10 hay IELTS 0–9 bước 0,5 chưa được chốt trong bảng phân công; không tự thay contract để chọn thay TV2/nhóm.
7. Sau hạn: chỉ bài thực sự bị bỏ phát `AssignmentMissed`; bài nộp lại hợp lệ không được coi là bỏ. Phát lại nhiều lần giữ bộ đếm, ba loại cảnh báo đúng học viên và không trùng.

Các ca trên là danh sách bàn giao chờ API TV2, **chưa phải test đã chạy**. Khi tích hợp, chạy toàn solution và architecture tests, ghi lại nhánh/commit TV2 và kết quả thực tế.
