# Product Backlog - Hệ thống quản lý trung tâm Tiếng Anh

Nhóm 10 - Môn 608819 - Phát triển ứng dụng phía máy chủ nâng cao.
Phiên bản: Tuần 1. Nguồn: 23 ảnh chụp LMS The IELTS Workshop (thư mục `lms-tiw-anh-chup/`) + phạm vi thống nhất với giảng viên/nhóm.

## 1. Tầm nhìn sản phẩm
Backend cho trung tâm Tiếng Anh: quản lý tài khoản và phân quyền, lớp học, lịch học, ghi danh, điểm danh, bài tập và nộp bài (file), điểm, học phí đơn giản và thông báo. Kiến trúc Modular Monolith (.NET), 5 module giao tiếp qua integration event. Frontend React (Vite) tách riêng trong `src/web`, làm ở lượt giao việc sau.

## 2. Vai trò người dùng
| Vai trò | Nguồn | Mô tả |
|---|---|---|
| Học viên | Quan sát từ ảnh | Xem lớp, lịch, điểm danh, bài tập, điểm, nộp bài, gửi phản hồi, nhận thông báo |
| Giáo viên | **Suy đoán** (ảnh không có) | Xem lịch dạy, điểm danh, tạo bài tập, chấm bài |
| Giáo vụ / Admin | **Suy đoán** (ảnh không có) | Quản lý tài khoản, lớp, lịch, ghi danh, học phí, FAQ, xử lý phản hồi |

Ghi chú: mọi user story của Giáo viên và Giáo vụ/Admin là suy luận từ dữ liệu mà màn hình học viên hiển thị (ví dụ học viên thấy điểm danh thì phải có người điểm danh).

## 3. Ánh xạ module
| Module | Trách nhiệm | Schema |
|---|---|---|
| Identity | Tài khoản, đăng nhập, vai trò, hồ sơ | `identity` |
| Academic | Lớp, buổi học/lịch, điểm danh, bài tập, bài nộp, điểm, luyện tập | `academic` |
| Enrollment | Ghi danh học viên vào lớp, giới hạn vi phạm, cảnh báo, hẹn test đầu vào | `enrollment` |
| Finance | Học phí, hóa đơn (đơn giản) | `finance` |
| MediaNotification | Lưu file (MinIO/R2, presigned URL), thông báo, phản hồi, FAQ, tin tức | `media_notification` |

Thống kê (US-29) không thuộc riêng module nào: mỗi module tự cung cấp số liệu của mình, không join chéo schema.

## 4. Thang ưu tiên và ước lượng
- Ưu tiên MoSCoW: Must / Should / Could / Won't.
- Story point: Fibonacci 1, 2, 3, 5, 8.
- Định nghĩa hoàn thành (DoD): code qua review PR, có unit test cho Application, architecture test xanh, endpoint có trong Swagger, không có secret trong repo.

## 5. Product Backlog

### Epic E0 - Nền tảng kỹ thuật (Technical stories, Sprint 1 / Tuần 1)
| ID | Story | Module | Ưu tiên | SP |
|---|---|---|---|---|
| TS-01 | Là nhóm phát triển, chúng tôi cần khung solution 5 module Clean Architecture để làm song song | Tất cả | Must | 5 |
| TS-02 | Là nhóm phát triển, chúng tôi cần in-memory Event Bus dùng chung để module giao tiếp không tham chiếu trực tiếp | BuildingBlocks | Must | 3 |
| TS-03 | Là nhóm phát triển, chúng tôi cần mỗi module có DbContext và schema riêng | Tất cả | Must | 2 |
| TS-04 | Là nhóm phát triển, chúng tôi cần architecture test chặn tham chiếu chéo module | Tests | Must | 3 |
| TS-05 | Là nhóm phát triển, chúng tôi cần Docker Compose (API + PostgreSQL + MinIO) chạy bằng một lệnh | Hạ tầng | Must | 3 (Sprint 2) |
| TS-06 | Là nhóm phát triển, chúng tôi cần Jenkinsfile build + test + deploy VPS | Hạ tầng | Should | 5 (Sprint sau) |

Tiêu chí chấp nhận (Must):
- TS-01: `dotnet build EnglishCenter.sln` thành công; mỗi module có đủ Domain, Contracts, Application, Infrastructure, Api; mỗi module có endpoint `GET /api/<module>/ping` trả 200.
- TS-02: Publish không handler không lỗi; nhiều handler được gọi tuần tự; handler lỗi được log và không chặn handler khác; mỗi lần publish dùng scope DI mới.
- TS-03: `Model.GetDefaultSchema()` của 5 DbContext lần lượt là `identity`, `academic`, `enrollment`, `finance`, `media_notification`; bảng lịch sử migration nằm trong schema riêng.
- TS-04: Test ma trận chặn mọi tham chiếu chéo module (Domain/Application/Infrastructure/Api) và kiểm tra `ProjectReference` trong csproj; phát hiện được vi phạm thật.
- TS-05: `docker compose up` khởi động API + PostgreSQL + MinIO; build multi-stage; cấu hình qua biến môi trường, không có secret trong repo.

### Epic E1 - Tài khoản và phân quyền (Identity)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-01 | Là người dùng, tôi muốn đăng nhập bằng email và mật khẩu để truy cập chức năng theo vai trò | Must | 3 | 21 |
| US-02 | Là người dùng, tôi muốn đăng xuất để bảo vệ tài khoản | Must | 1 | 21 |
| US-03 | Là người dùng, tôi muốn đổi mật khẩu | Must | 2 | 21 |
| US-04 | Là học viên, tôi muốn xem và cập nhật thông tin cá nhân (họ tên, email, SĐT, ảnh đại diện) và thấy mã học viên | Should | 2 | 01, 21 |
| US-05 | Là giáo vụ/admin, tôi muốn tạo tài khoản học viên/giáo viên và gán vai trò (suy đoán) | Must | 3 | - |
| US-06 | Là hệ thống, tôi muốn chặn truy cập API theo vai trò (Học viên, Giáo viên, Giáo vụ/Admin) | Must | 3 | - |

Tiêu chí chấp nhận:
- US-01: (1) Đúng email/mật khẩu -> trả JWT có claim `sub`, `role`, hạn dùng cấu hình được. (2) Sai thông tin -> 401, thông báo chung không tiết lộ email có tồn tại hay không. (3) Tài khoản bị khóa -> 403. (4) Mật khẩu lưu dạng hash, không bao giờ trả về trong API.
- US-02: Sau đăng xuất, refresh token bị thu hồi; dùng lại -> 401.
- US-03: Phải nhập đúng mật khẩu cũ; mật khẩu mới tối thiểu 8 ký tự, khác mật khẩu cũ; thành công -> các refresh token cũ bị thu hồi.
- US-04: Học viên chỉ sửa được hồ sơ của chính mình; email trùng -> 409; mã học viên (dạng `HVxxxxxx-xx`) chỉ đọc.
- US-05: Email là duy nhất; tạo thành công phát event `UserRegistered`; chỉ Giáo vụ/Admin gọi được.
- US-06: Gọi endpoint không đủ quyền -> 403; không có token -> 401.

### Epic E2 - Lớp học và lịch học (Academic)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-07 | Là giáo vụ, tôi muốn tạo lớp (mã lớp, khóa, ngày bắt đầu/kết thúc, sĩ số tối đa, loại Lớp chính/Lớp bổ trợ, giáo viên, học phí) | Must | 5 | 11 |
| US-08 | Là giáo vụ, tôi muốn tạo lịch buổi học cho lớp (ngày, giờ bắt đầu/kết thúc, Unit-Lesson, kỹ năng Listening/Reading/Writing/Speaking, loại buổi: thường/Mock Test/Final Test, ca thi) | Must | 5 | 10 |
| US-09 | Là học viên, tôi muốn xem danh sách lớp của mình, lọc theo Sắp diễn ra/Đang diễn ra/Kết thúc, thấy số buổi đã học x/y, sĩ số, giáo viên | Must | 3 | 11 |
| US-10 | Là học viên, tôi muốn xem lịch học theo tháng/tuần, phân biệt buổi đã học/chưa học | Must | 3 | 10 |
| US-11 | Là giáo viên, tôi muốn xem lịch dạy và các lớp tôi phụ trách (suy đoán) | Must | 2 | - |

Tiêu chí chấp nhận:
- US-07: Mã lớp duy nhất; ngày kết thúc > ngày bắt đầu; sĩ số tối đa > 0; tạo thành công phát `ClassCreated`. Trạng thái lớp suy ra từ ngày hiện tại: trước ngày bắt đầu = Sắp diễn ra, trong khoảng = Đang diễn ra, sau ngày kết thúc = Kết thúc.
- US-08: Giờ kết thúc > giờ bắt đầu; ngày buổi học nằm trong khoảng ngày của lớp; giáo viên không bị trùng giờ với buổi khác -> nếu trùng trả 409.
- US-09: Chỉ trả các lớp học viên đã ghi danh; bộ lọc trả đúng số lượng từng trạng thái (ví dụ "Tất cả (7)"); "Đã học" = số buổi có ngày < hiện tại.
- US-10: API nhận khoảng ngày `from`, `to`; mỗi buổi trả về giờ, chủ đề, giáo viên, mã lớp, loại lớp, cờ `isCompleted`; chỉ trả buổi của lớp học viên tham gia.
- US-11: Giáo viên chỉ thấy buổi/lớp mình được phân công.

### Epic E3 - Ghi danh, cảnh báo, hẹn test (Enrollment)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-12 | Là giáo vụ, tôi muốn ghi danh học viên vào lớp | Must | 5 | 11 |
| US-13 | Là giáo vụ, tôi muốn đặt giới hạn số buổi nghỉ, số lần đi trễ, số bài tập được bỏ cho mỗi ghi danh | Should | 2 | 01 |
| US-14 | Là học viên, tôi muốn xem lịch hẹn test đầu vào (địa điểm, hình thức, thời gian, tên đề, trạng thái, giáo viên); giáo vụ tạo/cập nhật lịch hẹn | Should | 3 | 13 |
| US-15 | Là học viên, tôi muốn xem danh sách cảnh báo khi tôi vượt giới hạn nghỉ/đi trễ/bỏ bài | Should | 3 | 14, 03 |

Tiêu chí chấp nhận:
- US-12: Không ghi danh trùng (cùng học viên, cùng lớp) -> 409; lớp đầy (sĩ số hiện tại = tối đa) -> 409; lớp đã kết thúc -> 400; thành công phát `StudentEnrolled` (kèm học phí) -> Finance tạo hóa đơn, Academic cập nhật danh sách lớp, MediaNotification gửi thông báo.
- US-13: Giới hạn >= 0; hiển thị dạng "đã dùng/giới hạn" (ví dụ 0/0).
- US-14: Trạng thái gồm Chờ kiểm tra, Đã kiểm tra, Hủy; học viên chỉ thấy lịch hẹn của mình; có phân trang và sắp xếp theo thời gian.
- US-15: Khi nhận `AttendanceRecorded` hoặc `AssignmentMissed` làm số vi phạm vượt giới hạn -> tạo cảnh báo và phát `WarningIssued`; có tìm kiếm và lọc theo lớp/loại.

### Epic E4 - Điểm danh (Academic)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-16 | Là giáo viên, tôi muốn điểm danh từng học viên trong buổi học với 6 trạng thái | Must | 5 | 01 |
| US-17 | Là học viên, tôi muốn xem tổng hợp điểm danh theo lớp (tỷ lệ % và số buổi theo từng trạng thái) và lịch sử điểm danh | Must | 3 | 01 |

Tiêu chí chấp nhận:
- US-16: 6 trạng thái: Có mặt (Present), Vắng không phép (AbsentUnexcused), Vắng có phép (AbsentExcused), Đi trễ (Late), Đi trễ hơn 20 phút (LateOver20Min), Về sớm (LeftEarly). Chỉ giáo viên của lớp được điểm danh; chỉ điểm danh học viên có trong danh sách lớp; sửa lại được trong 24h sau buổi học; mỗi bản ghi phát `AttendanceRecorded`.
- US-17: Tổng số buổi theo trạng thái cộng lại = số buổi đã điểm danh; tỷ lệ làm tròn đến số nguyên; học viên chỉ xem được dữ liệu của mình.

### Epic E5 - Bài tập, nộp bài, điểm (Academic + MediaNotification)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-18 | Là giáo viên, tôi muốn tạo bài tập gắn với buổi học (Unit-Lesson), loại Trên lớp/Về nhà, thời gian mở/đóng | Must | 5 | 01 |
| US-19 | Là học viên, tôi muốn xem bài tập theo Unit-Lesson và trạng thái Đã làm/Chưa làm/Đã bỏ | Must | 3 | 01 |
| US-20 | Là học viên, tôi muốn nộp bài kèm file (tải lên MinIO/R2 qua presigned URL) | Must | 5 | 01 |
| US-21 | Là giáo viên, tôi muốn chấm điểm và nhận xét bài nộp; học viên xem điểm ở tab "Điểm bài tập" | Must | 3 | 01 |
| US-22 | Là học viên, tôi muốn xem danh sách bài đã được đánh giá / chưa được đánh giá, có tìm kiếm | Should | 2 | 19 |

Tiêu chí chấp nhận:
- US-18: Thời gian đóng > thời gian mở; bài tập chưa gán buổi thì ẩn với học viên (thông báo "Bài tập đang ẩn do chưa được gán buổi"); công bố bài tập phát `AssignmentPublished`.
- US-19: Đã làm = có bài nộp; Chưa làm = chưa nộp và chưa đóng; Đã bỏ = chưa nộp và đã qua thời gian đóng (phát `AssignmentMissed`).
- US-20: Chỉ nộp trong khoảng mở-đóng (ngoài khoảng -> 400); file tối đa 20 MB, định dạng cho phép: pdf, docx, mp3, m4a, jpg, png; client xin presigned URL từ MediaNotification, tải file lên, rồi gửi `fileKey` cho Academic; nộp lại thì bản mới thay bản cũ trước hạn; URL tải về là presigned, hết hạn sau thời gian cấu hình.
- US-21: Điểm trong khoảng 0-10 (hoặc band 0-9 bước 0.5 với bài IELTS - chốt ở sprint 2); chỉ giáo viên lớp chấm được; chấm xong phát `SubmissionGraded`.
- US-22: Lọc theo đã/chưa đánh giá; học viên chỉ thấy bài của mình.

### Epic E6 - Học phí (Finance, đơn giản)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-23 | Là hệ thống, tôi muốn tự tạo hóa đơn học phí khi học viên được ghi danh | Must | 3 | - |
| US-24 | Là giáo vụ, tôi muốn đánh dấu hóa đơn đã thanh toán; học viên xem hóa đơn của mình | Should | 2 | - |

Tiêu chí chấp nhận:
- US-23: Nhận `StudentEnrolled` -> tạo đúng 1 hóa đơn (idempotent theo `EnrollmentId`, nhận trùng event không tạo hóa đơn thứ hai); số tiền = học phí trong event; trạng thái Chưa thanh toán; hạn = ngày ghi danh + 7 ngày; phát `InvoiceCreated`.
- US-24: Chỉ chuyển Chưa thanh toán -> Đã thanh toán; không cổng thanh toán online; phát `InvoicePaid`.

### Epic E7 - Thông báo và lưu trữ file (MediaNotification)
| ID | User story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| US-25 | Là người dùng, tôi muốn xem thông báo trong ứng dụng (biểu tượng chuông, số chưa đọc) và đánh dấu đã đọc | Must | 3 | 21 |
| US-26 | Là hệ thống, tôi muốn tự tạo thông báo khi: được ghi danh, có bài tập mới, bài được chấm, có hóa đơn, có cảnh báo | Should | 3 | - |
| US-27 | Là hệ thống, tôi cần dịch vụ lưu file cấp presigned URL tải lên/tải về (MinIO khi dev, R2 khi triển khai) | Must | 3 | - |

Tiêu chí chấp nhận:
- US-25: Danh sách phân trang, mới nhất trước; số chưa đọc đúng; chỉ đọc được thông báo của mình.
- US-26: Mỗi event liệt kê tạo đúng 1 thông báo cho đúng người nhận.
- US-27: Không lộ access key; URL hết hạn theo cấu hình; khóa file có tiền tố theo loại (`submissions/`, `avatars/`).

### Epic E8 - Tương tác và thống kê
| ID | User story | Module | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|---|
| US-28 | Là học viên, tôi muốn gửi phản hồi (tiêu đề, nội dung, rating 1-5) và theo dõi trạng thái; giáo vụ xử lý phản hồi | MediaNotification | Should | 3 | 04 |
| US-29 | Là học viên, tôi muốn xem thống kê cá nhân: tổng buổi học trong tháng, đã học, vắng mặt, phản hồi đã gửi, số lần bị cảnh báo, số lớp đang học/đã hoàn thành, biểu đồ số buổi theo tháng | Nhiều module | Should | 5 | 03 |
| US-30 | Là học viên, tôi muốn xem câu hỏi thường gặp; admin quản lý FAQ | MediaNotification | Could | 1 | 16 |
| US-31 | Là học viên, tôi muốn đăng bài, thích, nhận xét trên bảng tin | MediaNotification | Could | 5 | 02 |
| US-32 | Là học viên, tôi muốn xem danh sách bài luyện tập (tên bài, người tạo, ngày tạo) | Academic | Could | 3 | 12 |
| US-33 | Là học viên, tôi muốn điểm danh nhận Xu (làm cuối cùng nếu còn thời gian) | Enrollment (tạm) | Could | 3 | 05, header |

Tiêu chí chấp nhận:
- US-28: Rating 1-5; trạng thái Mới / Đang xử lý / Đã xử lý; tìm kiếm theo tiêu đề.
- US-29: Mỗi số liệu lấy từ API của module sở hữu (Academic: buổi học/vắng; Enrollment: cảnh báo, số lớp; MediaNotification: phản hồi); so sánh % với tháng trước; không có truy vấn join chéo schema.
- US-30, US-31, US-32, US-33: tiêu chí chi tiết bổ sung khi đưa vào sprint. US-33 chỉ làm sau khi mọi story Must/Should hoàn tất.

### Epic FE - Frontend React (Vite) - làm ở lượt giao việc sau
Dự án `src/web` tách riêng, gọi API của host `EnglishCenter.Api`. Dùng tên/logo trung tính của nhóm, KHÔNG sao chép logo/ảnh/nội dung của The IELTS Workshop. Backend vẫn là phần chính; chỉ bắt đầu sau khi khung solution và API ping chạy được.
| ID | Story | Ưu tiên | SP | Ảnh |
|---|---|---|---|---|
| FE-01 | Khung ứng dụng React (Vite): sidebar trái (Thông tin chung, Học tập), header (chuông thông báo, menu tài khoản), định tuyến, gọi API host | Should | 5 | 20, 21 |
| FE-02 | Trang Tiến độ học tập: tab chọn lớp, hồ sơ học viên, biểu đồ điểm danh 6 trạng thái, biểu đồ bài tập, danh sách Unit-Lesson (dữ liệu giả giai đoạn đầu) | Should | 5 | 01 |
| FE-03 | Trang Lịch học dạng lịch tháng/tuần, nút Hôm nay, màu Chưa học/Đã học | Should | 5 | 10 |
| FE-04 | Trang Danh sách lớp học: lọc trạng thái, chế độ lưới/danh sách | Should | 3 | 11 |
| FE-05 | Đăng nhập/đăng xuất, menu tài khoản (Thông tin, Đổi mật khẩu, Đăng xuất) | Should | 3 | 21 |
| FE-06 | Trang Thống kê, Phản hồi, Thông báo | Should | 5 | 03, 04 |

Tiêu chí chấp nhận: FE-01..FE-06 bổ sung chi tiết khi đưa vào sprint; giai đoạn đầu chỉ cần khung + một trang mẫu dữ liệu giả.

## 6. Ngoài phạm vi (Won't trong đồ án này)
| Tính năng | Ảnh | Lý do |
|---|---|---|
| Tpoint | header | Gamification, không phải nghiệp vụ cốt lõi |
| Điểm danh nhận xu, Xu | 05, header | Đã chốt với người dùng: chuyển thành Could (US-33), chỉ làm cuối nếu còn thời gian; không thuộc phạm vi cam kết |
| Giải đấu, bảng xếp hạng | 06 | Gamification |
| Lật thẻ bài | 07 | Mini game |
| Vòng quay may mắn | 08 | Mini game |
| Khuyến mãi | 09 | Marketing, làm phức tạp học phí |
| Thư viện online | 15 | Cắt theo phạm vi thống nhất |
| Cửa hàng: Bộ đề, Mã kích hoạt, Giỏ hàng | 17, 18, header | Thương mại điện tử, đã chốt cắt |
| Thanh toán online, nhiều chi nhánh/trung tâm | 03 (bộ lọc Trung tâm) | Giữ học phí đơn giản |

## 7. Kế hoạch sprint đề xuất
| Sprint | Nội dung |
|---|---|
| Tuần 1 | Backlog + user story (file này), TS-01..TS-04 |
| Tuần 2 | TS-05, US-01, US-02, US-03, US-05, US-06, US-07, US-27 |
| Tuần 3 | US-08, US-09, US-10, US-11, US-12, US-23 |
| Tuần 4 | US-16, US-17, US-18, US-19, US-20, US-21, US-25, US-26 |
| Tuần 5+ | Should/Could còn lại, FE-01..FE-06, TS-06, triển khai VPS; US-33 (Xu) nếu còn thời gian |

## 8. Phân công gợi ý (4 thành viên, nhánh riêng theo module/công việc)
| Thành viên | Module |
|---|---|
| TV1 | Identity + BuildingBlocks |
| TV2 | Academic (lớp, lịch, điểm danh, bài tập, nộp bài, điểm) |
| TV3 | Enrollment + Finance |
| TV4 | MediaNotification (lưu file, thông báo, phản hồi) + hạ tầng Docker/Jenkins |

Đây là phân công gợi ý cho nhóm hiện có 4 người, chưa gắn với tên thành viên. TV2 phụ trách toàn bộ Academic; TV3 phụ trách luồng ghi danh và học phí. Enrollment và Finance vẫn là hai module độc lập, giao tiếp qua integration event.

Phạm vi tuần 2 theo phân công trên:

| Thành viên | Công việc tuần 2 |
|---|---|
| TV1 | US-01, US-02, US-03, US-05, US-06: đăng nhập, đăng xuất, đổi mật khẩu, tạo tài khoản, phân quyền |
| TV2 | US-07: tạo lớp, kiểm tra dữ liệu và phát `ClassCreated` |
| TV3 | Chuẩn bị luồng Enrollment/Finance cho tuần 3; hỗ trợ kiểm thử hợp đồng `ClassCreated`, `StudentEnrolled` và tính độc lập của module |
| TV4 | TS-05, US-27: Docker Compose API/PostgreSQL/MinIO và dịch vụ cấp presigned URL |

TV1..TV4 là nhãn phân công, nhóm tự xác nhận tên và điều chỉnh khối lượng. Mỗi người kiểm thử phần mình và review PR chéo; người phụ trách hạ tầng phối hợp với các module khi tích hợp.

## 9. Integration event giữa các module
| Event | Phát bởi | Tiêu thụ bởi |
|---|---|---|
| UserRegistered | Identity | Enrollment, MediaNotification |
| ClassCreated | Academic | Enrollment |
| StudentEnrolled | Enrollment | Finance, Academic, MediaNotification |
| AttendanceRecorded | Academic | Enrollment |
| AssignmentPublished | Academic | MediaNotification |
| AssignmentMissed | Academic | Enrollment |
| SubmissionGraded | Academic | MediaNotification |
| InvoiceCreated, InvoicePaid | Finance | MediaNotification |
| WarningIssued | Enrollment | MediaNotification |
