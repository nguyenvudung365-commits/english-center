# Hợp đồng API TV2 — Bài tập, chấm điểm, luyện tập

Dành cho TV1 (frontend FE-TV2, thống kê US-29), TV3 (cảnh báo bỏ bài), TV4 (inbox). Phạm vi: US-18–22, US-32, Q-03, Q-07.
Mọi endpoint cần `Authorization: Bearer`; lỗi trả `ProblemDetails`/`ValidationProblemDetails` như [hop-dong-xac-thuc-va-loi.md](hop-dong-xac-thuc-va-loi.md).
Thời gian luôn là ISO-8601 UTC (`...Z`); frontend tự hiển thị theo giờ Việt Nam (UTC+7).

## 1. Quyền

| Vai trò | Bài tập | Chấm/sửa điểm | Luyện tập |
|---|---|---|---|
| Teacher đúng lớp | Tạo/sửa/công bố, xem bài nộp, trạng thái nộp | **Có** | Tạo/sửa/xóa |
| Staff, Admin | Tạo/sửa/công bố, xem bài nộp, phát lại event | **Không** (không chấm thay giáo viên) | Tạo/sửa/xóa |
| Student còn quyền lớp | Xem bài đã công bố/legacy, nộp, xem điểm của mình | Không | Chỉ xem |

Mỗi request đều kiểm tra lại tài khoản còn hoạt động đúng vai trò và quyền lớp hiện tại (thu hồi quyền có hiệu lực ngay, kể cả khi token cũ còn hạn).
Không có quyền lớp → 403 (giữ hành vi các endpoint bài tập đã có). Học viên mở bài nháp → 404.

## 2. Bài tập (US-18–20)

### Trường của `AssignmentResponse`

```json
{
  "id": "6f1c…", "classId": "9a2e…", "title": "Writing task 3", "description": "Viết 150 từ",
  "opensAtUtc": "2026-11-16T01:00:00Z", "closesAtUtc": "2026-11-18T16:59:00Z",
  "kind": "Homework", "unit": "Unit 3", "lesson": "Lesson 2", "sessionId": null,
  "isLegacy": false, "status": "Open", "publishedAt": "2026-11-15T03:00:00Z",
  "createdAt": "2026-11-15T02:58:00Z", "version": 2
}
```

- `kind`: `InClass` (Trên lớp) | `Homework` (Về nhà).
- `status`: `Draft` (chỉ quản lý lớp thấy) | `Upcoming` | `Open` | `Closed` — tính theo giờ server.
- `isLegacy = true`: bài tạo trước bản nâng cấp này; học viên vẫn xem/nộp theo hạn cũ, không có nghĩa vụ/missed hồi tố.
- `version`: gửi lại khi sửa/công bố để chống ghi đè (409 nếu lệch).

### Endpoint

| Method & route | Vai trò | Ghi chú |
|---|---|---|
| `POST /api/academic/assignments` | Teacher lớp, Staff, Admin | Body: `classId, title, description?, opensAtUtc, closesAtUtc, kind?, unit?, lesson?, sessionId?, publish?`. `publish` bỏ trống/`true` = công bố ngay (giữ tương thích frontend hiện tại); `false` = lưu nháp. 201 |
| `GET /api/academic/assignments?classId=&search=&status=&kind=` | Mọi vai trò có quyền lớp | Mảng tối đa 200 bài. Học viên không thấy nháp. `search` tìm trong tiêu đề/Unit/Lesson |
| `GET /api/academic/assignments/{id}` | Như trên | |
| `PUT /api/academic/assignments/{id}` | Teacher lớp, Staff, Admin | Body: `title, description?, opensAtUtc, closesAtUtc, kind?, unit?, lesson?, sessionId?, expectedVersion` (bắt buộc). Bài đã công bố/legacy chỉ được sửa tiêu đề/mô tả/Unit/Lesson; đổi loại/buổi/thời hạn → 409. `kind`/`sessionId` bỏ trống = giữ nguyên |
| `POST /api/academic/assignments/{id}/publish` | Teacher lớp, Staff, Admin | Body tùy chọn `{ "expectedVersion": 1 }`. Chụp snapshot học viên đang có quyền lớp làm danh sách nghĩa vụ (Q-07) trong cùng transaction rồi phát `AssignmentPublished`. Gọi lại khi đã công bố trả 200, không tạo snapshot/event mới |
| `GET /api/academic/assignments/{id}/recipients` | Teacher lớp, Staff, Admin | Trạng thái từng học viên: `outcome` = `Pending` / `Submitted` / `Missed` / `Exempted`, hoặc `NotObligated` (vào lớp sau khi công bố nhưng vẫn nộp) |
| `GET /api/academic/assignments/{id}/submissions?graded=` | Teacher lớp, Staff, Admin | Mảng bài nộp (giữ dạng cũ) + `score, feedback, gradeVersion, gradedAt, gradedBy` |
| `PUT /api/academic/assignments/{id}/submissions` | Student | Nộp 1–5 file (giữ nguyên quy tắc file/revision cũ). Bài đã chấm không thay file (409) |
| `GET /api/academic/assignments/{id}/submissions/mine` | Student | Bài nộp của mình, có điểm khi đã chấm |
| `POST /api/academic/assignments/{id}/events/replay` | Staff, Admin | Phát lại event đã lưu với cùng khóa (đối soát consumer). Trả `{ published, graded, missed }`; có log audit người thao tác |

**Buổi học (Session, TV1):** chưa có module Session trên develop nên server hiện **từ chối `sessionId` (409)** thay vì lưu tham chiếu chưa kiểm chứng, và cho công bố không cần buổi.
Khi TV1 đăng ký bản triển khai `ISessionCatalog` thật (`IsAvailable = true`), server tự động: kiểm tra buổi cùng lớp, lấy Unit/Lesson mặc định từ buổi, và **bắt buộc gán buổi trước khi công bố** (AC-05). Lúc đó frontend nên gửi `publish: false` khi tạo rồi công bố sau khi chọn buổi.

## 3. Chấm điểm (US-21)

`PUT /api/academic/submissions/{submissionId}/grade` — **chỉ Teacher đúng lớp**.

```json
{ "score": 8.5, "feedback": "Bài tốt, cần thêm ví dụ", "expectedGradeVersion": 0 }
```

- `score`: 0–10, bước 0,1 (8.55 → 400). Chưa chấm thì `score = null`, không phải 0.
- `expectedGradeVersion`: `gradeVersion` đang hiển thị (0 nếu chưa chấm). Lệch → **409**, frontend tải lại rồi cho sửa tiếp.
- Gửi lại đúng nội dung vừa lưu (mất response) → 200, không tạo thêm lịch sử/event.
- Trả `SubmissionResponse` mới (`gradeVersion` tăng 1).

`GET /api/academic/submissions/{submissionId}/grades` — lịch sử mới nhất trước (Teacher lớp, Staff, Admin, chính học viên):

```json
[
  { "version": 2, "score": 9.0, "previousScore": 8.5, "feedback": "Sửa sau phúc khảo",
    "gradedBy": "1b7e…", "gradedByName": "Nguyễn Văn A", "gradedAt": "2026-11-16T08:00:00Z" },
  { "version": 1, "score": 8.5, "previousScore": null, "feedback": "Bài tốt", "gradedBy": "1b7e…",
    "gradedByName": "Nguyễn Văn A", "gradedAt": "2026-11-16T07:00:00Z" }
]
```

## 4. Học viên xem bài và điểm (US-22)

`GET /api/academic/assignments/mine?classId=&search=&status=&submissionStatus=&kind=&page=1&pageSize=20` — Student.

- Không có `classId`: gộp mọi lớp học viên đang có quyền. Có `classId` mà không có quyền → 403.
- `status`: `Upcoming|Open|Closed` (`Draft` → 400). `submissionStatus`: `NotSubmitted|Submitted|Graded|Missed|Exempted` (không phân biệt hoa thường).
- Sắp xếp: bài đang mở/sắp mở theo hạn gần nhất trước, sau đó bài đã đóng mới nhất trước. `pageSize` tối đa 100.

```json
{
  "items": [
    { "assignment": { "id": "6f1c…", "title": "Writing task 3", "status": "Closed", "kind": "Homework", "…": "…" },
      "submissionStatus": "Graded", "isObligated": true, "submissionId": "c0d1…",
      "submittedAt": "2026-11-17T10:00:00Z", "score": 7.5, "feedback": "Cần thêm ví dụ", "gradedAt": "2026-11-18T02:00:00Z" }
  ],
  "total": 1, "page": 1, "pageSize": 20
}
```

## 5. Luyện tập (US-32, Q-03)

Nội dung văn bản thuần theo lớp; chưa có quiz/chấm tự động/nộp bài. **Frontend phải hiển thị dạng text (escape), không render HTML.**

| Method & route | Vai trò | Ghi chú |
|---|---|---|
| `GET /api/academic/practice?classId=&search=&page=&pageSize=` | Có quyền lớp | `{ items, total, page, pageSize }`; item: `id, classId, title, preview (≤200 ký tự), createdBy, createdByName, createdAt, updatedAt, version`. Mới nhất trước |
| `GET /api/academic/practice/{id}` | Có quyền lớp | Thêm `content, updatedBy` |
| `POST /api/academic/practice` | Teacher lớp, Staff, Admin | `{ classId, title (≤200), content (≤20000) }` → 201 |
| `PUT /api/academic/practice/{id}` | Teacher lớp, Staff, Admin | `{ title, content, expectedVersion }` (bắt buộc version; lệch → 409) |
| `DELETE /api/academic/practice/{id}?expectedVersion=` | Teacher lớp, Staff, Admin | Xóa mềm (giữ người/thời gian xóa) → 204; xóa lại vẫn 204 |

`createdByName`: tên giáo viên; bài do Staff/Admin tạo hiển thị “Nhân viên”/“Quản trị viên” (Academic không lưu hồ sơ nhân viên).

## 6. Event và hợp đồng cho module khác

Event phát **sau khi commit**; EventId cố định theo khóa nghiệp vụ nên retry/replay giữ nguyên khóa. Consumer vẫn phải dedupe theo khóa nghiệp vụ.
Các trường mới là `init` thêm vào record cũ — payload cũ vẫn deserialize được.

| Event | Khóa chống trùng | Trường |
|---|---|---|
| `AssignmentPublishedIntegrationEvent` | `AssignmentId` | `AssignmentId, ClassId, Title, OpensAtUtc, ClosesAtUtc` + `PublishedAtUtc, Kind, RecipientCount` |
| `SubmissionGradedIntegrationEvent` | `SubmissionId + GradeVersion` | `SubmissionId, AssignmentId, StudentId, Score` + `ClassId, GradeVersion, GradedAtUtc`. Inbox chỉ nên lưu link/ID, không lưu điểm |
| `AssignmentMissedIntegrationEvent` | `AssignmentId + StudentId` | `AssignmentId, ClassId, StudentId` + `PublishedAtUtc, DueAtUtc`; `OccurredOnUtc = DueAtUtc` (hạn nộp) |

Truy vấn (Academic.Contracts):
- `IAssignmentDirectory.FindAsync(assignmentId)` — tóm tắt bài đã công bố/legacy (nháp → null); `RecipientsAsync(assignmentId)` — snapshot học viên có nghĩa vụ lúc công bố (TV4 dùng để fan-out inbox AssignmentPublished, mở link vẫn phải kiểm tra quyền hiện tại).
- `ICourseworkMetrics.ForStudentAsync(studentId, fromUtc, toUtc)` — cho thống kê US-29 (TV1): `Assigned, Submitted, Graded, Missed, AverageScore` (null khi chưa có điểm) của các bài có hạn nộp trong kỳ.

## 7. Worker chốt nghĩa vụ và phát lại (Q-07)

`CourseworkWorker` chạy mỗi `Academic:Coursework:IntervalSeconds` giây (mặc định 60):
1. Luôn phát lại `AssignmentPublished`/`SubmissionGraded` đã commit nhưng chưa đánh dấu gửi (crash giữa commit và publish), sau 30 giây chờ request tự phát.
2. Chỉ khi `Academic:Coursework:ResolveMissed=true` (**mặc định tắt**): với bài đã công bố và quá hạn, chốt từng học viên trong snapshot: có bài nộp trước hạn → `Submitted`; mất quyền lớp → `Exempted`; còn lại → `Missed` và phát `AssignmentMissed`. Trạng thái cuối không mở lại; chạy lại không phát thêm.

Tắt toàn bộ worker: `Academic:Coursework:WorkerEnabled=false`. Theo kế hoạch, chỉ bật `ResolveMissed` sau khi đối soát dữ liệu staging.

## 8. Giới hạn đã biết

- Enrollment chưa lưu lịch sử thời điểm thu hồi quyền: học viên bị thu hồi trong khoảng giữa hạn nộp và lúc worker chạy cũng được miễn (thiên về không phạt sai). Cần TV3 bổ sung contract lịch sử nếu muốn chính xác tuyệt đối.
- Roster lúc công bố lấy từ read model `academic.class_students` (đồng bộ từ `StudentEnrolled`) rồi lọc bằng `IEnrollmentAccess` hiện tại. Nếu event ghi danh bị mất, dùng replay ghi danh của TV3 trước khi công bố.
- Event bus vẫn in-memory: không bảo đảm exactly-once; dữ liệu nguồn để dựng lại event (published_at, submission_grades, assignment_recipients) đã lưu bền.
