# Phân công 4 thành viên và kế hoạch theo tuần

Nhóm 10 - Môn 608819 - Hệ thống quản lý trung tâm Tiếng Anh.

Nguồn: [Product Backlog](bao-cao/product-backlog.md). Tuần 1–4 bám kế hoạch sprint hiện tại; tuần 5–7 là đề xuất chia nhỏ phần "Tuần 5+", cần điều chỉnh theo hạn nộp của giảng viên. Đây là tài liệu làm việc nội bộ, chưa phải xác nhận các tính năng đã hoàn thành.

## 1. Vai trò chung

| Thành viên | Họ tên | Vai trò chính | Phạm vi chịu trách nhiệm |
|---|---|---|---|
| TV1 | Chờ xác nhận | Tích hợp kỹ thuật, tài khoản và bảo mật | Identity, BuildingBlocks, xác thực JWT, phân quyền, hợp đồng dùng chung và tích hợp API |
| TV2 | Chờ xác nhận | Nghiệp vụ đào tạo | Academic: lớp, lịch, điểm danh, bài tập, bài nộp, điểm và API học tập |
| TV3 | Chờ xác nhận | Ghi danh, học phí và kiểm thử tích hợp | Enrollment, Finance: ghi danh, giới hạn vi phạm, cảnh báo, hẹn test, hóa đơn và phối hợp kiểm thử xuyên module |
| TV4 | Chờ xác nhận | Lưu trữ, thông báo và vận hành | MediaNotification, MinIO/R2, Docker Compose, Jenkins, triển khai và theo dõi hệ thống |

TV1–TV4 là nhãn phân công, không mặc định tương ứng với thứ tự tên trong danh sách đăng ký cũ. Nhóm tự chọn trưởng nhóm; vai trò tích hợp của TV1 không mặc định là trưởng nhóm.

Mỗi người thực hiện entity, Application, persistence, API, migration, kiểm thử và tài liệu API cho phần mình. TV3 hỗ trợ kiểm thử tích hợp, không chịu trách nhiệm viết toàn bộ test thay các thành viên khác.

Academic có khối lượng lớn, đặc biệt tuần 4. TV1 hỗ trợ frontend học tập và rà soát phân quyền; TV3 hỗ trợ kiểm thử luồng bài tập; TV4 hỗ trợ upload và thông báo. Khi cần chia code Academic, tạo task/PR riêng và thống nhất với TV2 trước khi sửa cùng file.

## 2. Tuần 1 - Khung dự án và thiết kế

Tuần này dùng để đối chiếu và nhận bàn giao khung đã có, không phải phân công hồi tố đóng góp cá nhân.

| Thành viên | Công việc |
|---|---|
| TV1 | Rà soát solution, BuildingBlocks, Event Bus, Identity và quy tắc tham chiếu; thống nhất cách tổ chức API và lỗi |
| TV2 | Rà soát khung Academic; thiết kế dữ liệu lớp, buổi học, điểm danh, bài tập và bài nộp |
| TV3 | Rà soát Enrollment/Finance, hợp đồng `StudentEnrolled`, handler mẫu hóa đơn và architecture test |
| TV4 | Rà soát MediaNotification, cấu hình môi trường, Swagger, health check; chuẩn bị phương án Docker và lưu file |

**Bàn giao:** backlog, thiết kế hệ thống/CSDL, solution build được, ping các module và kiểm thử nền tảng chạy được. Hiện repo có khung và tài liệu tuần 1; cần chạy kiểm tra lại khi bàn giao.

## 3. Tuần 2 - Xác thực, tạo lớp và lưu file

| Thành viên | Công việc | Mã backlog |
|---|---|---|
| TV1 | Đăng nhập email/mật khẩu, JWT và refresh token; đăng xuất; đổi mật khẩu; tạo tài khoản; phân quyền; cấu hình tài khoản quản trị ban đầu qua môi trường | US-01, US-02, US-03, US-05, US-06 |
| TV2 | Tạo lớp: mã duy nhất, khóa học, ngày học, sĩ số, loại lớp, giáo viên, học phí; kiểm tra dữ liệu; phát `ClassCreated` | US-07 |
| TV3 | Kiểm thử độc lập phân quyền và tạo lớp; rà soát payload `ClassCreated`/`StudentEnrolled`; chuẩn bị thiết kế read model ghi danh và hóa đơn cho tuần 3 | Hỗ trợ sprint, chuẩn bị US-12/US-23 |
| TV4 | Docker multi-stage và Compose API/PostgreSQL/MinIO; cấu hình bằng biến môi trường; wrapper lưu file; cấp presigned URL upload/download và kiểm tra quyền truy cập file | TS-05, US-27 |

**Bàn giao:** chạy stack theo hướng dẫn bằng Docker Compose; đăng nhập đúng vai trò; chặn truy cập trái quyền; tạo lớp hợp lệ; nhận URL lưu file có hạn dùng. Có kiểm thử thu hồi refresh token khi đăng xuất/đổi mật khẩu.

## 4. Tuần 3 - Lịch học, ghi danh và hóa đơn

| Thành viên | Công việc | Mã backlog |
|---|---|---|
| TV1 | Hỗ trợ kiểm tra quyền học viên/giáo viên/giáo vụ trên API mới; kiểm tra `sub` và dữ liệu chỉ thuộc người dùng; hỗ trợ kiểm thử tích hợp và chuẩn bị hợp đồng frontend | Hỗ trợ sprint |
| TV2 | Tạo lịch buổi học, kiểm tra trùng giờ giáo viên; danh sách lớp học viên theo trạng thái; lịch học và lịch dạy; cập nhật danh sách lớp từ sự kiện ghi danh | US-08, US-09, US-10, US-11 |
| TV3 | Ghi danh: chặn trùng, lớp đầy, lớp kết thúc; phát `StudentEnrolled`; Finance tạo hóa đơn đúng một lần theo `EnrollmentId`, đúng học phí và hạn thanh toán | US-12, US-23 |
| TV4 | Bảo đảm stack và migration chạy khi tích hợp; chuẩn bị nhận sự kiện cho thông báo tuần 4; kiểm tra upload/download qua địa chỉ truy cập thực tế | Hỗ trợ sprint |

**Bàn giao:** demo tạo lớp → tạo lịch → ghi danh → xem lớp/lịch đúng người → tạo đúng một hóa đơn. Không join chéo schema và không tạo trùng hóa đơn khi xử lý lại event.

## 5. Tuần 4 - Điểm danh, bài tập, chấm điểm và thông báo

| Thành viên | Công việc | Mã backlog |
|---|---|---|
| TV1 | Hỗ trợ kiểm tra quyền giáo viên của lớp, quyền học viên; rà soát validation và lỗi API; phối hợp kiểm thử các trường hợp truy cập dữ liệu người khác | Hỗ trợ sprint |
| TV2 | Điểm danh 6 trạng thái, tổng hợp/lịch sử; tạo và xem bài tập; nộp/nộp lại bài trong hạn; chấm điểm và nhận xét; phát các sự kiện liên quan | US-16, US-17, US-18, US-19, US-20, US-21 |
| TV3 | Kiểm thử luồng điểm danh và bài tập, hạn nộp, nộp lại, quyền chấm điểm; kiểm tra payload sự kiện phục vụ cảnh báo; hỗ trợ TV2 theo task đã thống nhất | Hỗ trợ sprint |
| TV4 | Danh sách thông báo có phân trang, số chưa đọc, đánh dấu đã đọc; tạo thông báo từ sự kiện; tích hợp upload bài nộp với Academic | US-25, US-26; tích hợp US-20 |

**Cần chốt trước khi làm US-21:** dùng thang điểm 0–10 hay IELTS 0–9 bước 0,5; backlog hiện còn để mở quyết định này.

**Bàn giao:** demo giáo viên giao bài → học viên upload/nộp bài → giáo viên chấm → học viên nhận thông báo; demo điểm danh và tổng hợp đúng dữ liệu. File giới hạn 20 MB, đúng định dạng cho phép; kiểm tra hạn nộp và quyền giáo viên.

## 6. Tuần 5 - Tính năng Should và frontend nền tảng (đề xuất)

| Thành viên | Công việc | Mã backlog |
|---|---|---|
| TV1 | Hồ sơ cá nhân; khung React/Vite, định tuyến và API client; giao diện đăng nhập/đăng xuất/đổi mật khẩu | US-04, FE-01, FE-05 |
| TV2 | Bộ lọc bài đã/chưa đánh giá; phối hợp hợp đồng dữ liệu cho giao diện lớp, lịch, tiến độ học tập | US-22; hỗ trợ FE-02, FE-03, FE-04 |
| TV3 | Giới hạn vi phạm, hẹn test, cảnh báo; đánh dấu hóa đơn đã thanh toán và học viên xem hóa đơn của mình | US-13, US-14, US-15, US-24 |
| TV4 | Phản hồi và xử lý phản hồi; Jenkins build/test; chuẩn bị cấu hình VPS, domain và lưu trữ triển khai | US-28, TS-06 |

**Bàn giao:** frontend đăng nhập và gọi API thật; các tính năng Should có kiểm thử theo quyền; pipeline chạy build/test. Ưu tiên hoàn thiện mọi Must còn lỗi trước khi mở rộng.

## 7. Tuần 6 - Giao diện nghiệp vụ và triển khai thử (đề xuất)

| Thành viên | Công việc | Mã backlog |
|---|---|---|
| TV1 | Trang tiến độ học tập, lịch và danh sách lớp; tích hợp API thật, trạng thái tải/lỗi/rỗng; phối hợp TV2 kiểm tra dữ liệu hiển thị | FE-02, FE-03, FE-04 |
| TV2 | Cung cấp số liệu Academic cho thống kê; sửa lỗi học tập và kiểm tra phân quyền khi tích hợp frontend | Phần Academic của US-29 |
| TV3 | Cung cấp số liệu Enrollment cho thống kê; kiểm thử tích hợp ghi danh/học phí/cảnh báo và dữ liệu người dùng | Phần Enrollment của US-29 |
| TV4 | Cung cấp số liệu phản hồi; làm giao diện thống kê/phản hồi/thông báo; triển khai staging/VPS, Jenkins deploy và kiểm tra MinIO/R2 | Phần MediaNotification của US-29, FE-06, TS-06 |

**Bàn giao:** bản triển khai thử truy cập được qua URL, frontend nối API thật, CI/CD chạy được. Thống kê lấy số liệu từ API của module sở hữu, không dùng truy vấn join chéo schema.

## 8. Tuần 7 - Ổn định và bàn giao (đề xuất)

| Thành viên | Công việc |
|---|---|
| TV1 | Rà soát xác thực, phân quyền, tích hợp frontend; tổng hợp hướng dẫn chạy và kịch bản demo chung |
| TV2 | Hoàn thiện kiểm thử và tài liệu Academic; chuẩn bị demo lớp, lịch, điểm danh, bài tập và điểm |
| TV3 | Chạy kiểm thử hồi quy tích hợp; đối chiếu tiêu chí chấp nhận; chuẩn bị demo ghi danh, hóa đơn, cảnh báo |
| TV4 | Kiểm tra triển khai, biến môi trường, lưu file, log và quy trình sao lưu/khôi phục; chuẩn bị demo thông báo và pipeline |

**Bàn giao:** bản phát hành ổn định, hướng dẫn chạy, tài liệu API/CSDL cập nhật, kết quả kiểm thử, báo cáo và bài trình bày. Mỗi người cập nhật tài liệu và trình bày phần mình thực hiện.

FAQ (US-30), bảng tin (US-31), bài luyện tập (US-32) chỉ làm khi Must/Should đã ổn định. Xu (US-33) làm cuối cùng nếu còn thời gian; không đưa vào cam kết hoàn thành của kế hoạch này.

## 9. Cách phối hợp và điều kiện hoàn thành

- GitHub: mỗi task có nhánh riêng, ví dụ `feature/us-07-create-class`; mở Pull Request và nhờ một thành viên khác review trước khi merge.
- Gợi ý review chéo: TV1 ↔ TV4 cho xác thực/hạ tầng; TV2 ↔ TV3 cho học tập/ghi danh/học phí. Đổi người review khi task liên quan module khác.
- Thay đổi event phải thống nhất payload với bên nhận trước khi code; không tham chiếu trực tiếp implementation của module khác.
- Mỗi module có DbContext/schema riêng. Controller chỉ gọi Application; migration và dữ liệu do module sở hữu.
- Một task hoàn thành khi đạt tiêu chí backlog, build/test liên quan và architecture test đạt, API xuất hiện trong Swagger, có hướng dẫn kiểm tra và không chứa secret.
- Cuối tuần demo luồng chạy thật, ghi rõ phần hoàn thành, lỗi còn lại và việc chuyển sang tuần sau; không đánh dấu Done chỉ vì đã viết code.
- Jira là công cụ theo dõi tùy chọn trong kế hoạch hiện tại; có thể dùng GitHub Issues/Projects để quản lý task, assignee và trạng thái.
- Phân công này mô tả trách nhiệm dự kiến, không thay thế bằng chứng đóng góp thực tế từ task, commit, Pull Request và kết quả bàn giao.
