# English Center Web

React/Vite/TypeScript. UI tiếng Việt, responsive, sidebar theo không gian làm việc; định hướng navy/blue từ UI/UX Pro Max, không dùng số liệu giả.

## Chạy local

Chạy API theo README gốc trên `http://127.0.0.1:5080`, sau đó:

```sh
cd frontend
npm ci
npm run dev
```

Mở URL Vite in ra. `/api` được proxy đến backend, không cần mở CORS. Dùng tài khoản đã bootstrap hoặc giáo vụ tạo; không có mật khẩu mặc định.

## Phạm vi

- Đăng nhập, đăng xuất, đổi mật khẩu; lỗi HTTP và validation; refresh token single-flight, retry tối đa một lần.
- Access/refresh token chỉ nằm trong bộ nhớ, không lưu localStorage/sessionStorage. Reload yêu cầu đăng nhập lại. JWT decode chỉ phục vụ hiển thị/điều hướng; server quyết định quyền.
- Academic (Tùng): Admin/Staff tra cứu lớp theo UUID tại `/classes`, tạo lớp tại `/classes/new`, xem chi tiết bằng đường dẫn `/classes/:id`. Tạo thành công chuyển sang đọc chi tiết từ API; lỗi đọc sau đó không tự gửi lại yêu cầu tạo. Chưa có API danh sách/chỉnh sửa/xóa lớp.
- Danh mục `/courses` và `/teachers` chỉ đọc, tìm kiếm trong dữ liệu API đã tải. Form tạo lớp lấy lựa chọn thật từ hai danh mục; không thể tạo khi danh mục chưa tải được hoặc trống.
- Enrollment/Finance (Lịch): `/enrollment` và `/invoices` chỉ giải thích trạng thái chưa có API ghi danh/chuyển lớp/hủy ghi danh/hóa đơn/thanh toán. Không gửi yêu cầu nghiệp vụ, không xác nhận thanh toán và không có số liệu hay chứng từ giả.
- Điều hướng theo Admin/Staff/Teacher/Student. Truy cập trực tiếp trang Academic quản lý bằng Teacher/Student bị chặn trước khi gọi API; quyền trên server vẫn là quyết định cuối cùng.
- Lịch, điểm danh, bài tập, tiến độ, thông báo, phản hồi, thống kê ghi rõ chưa tích hợp API; không gửi phản hồi giả hoặc hiển thị số liệu giả. Hoàn thiện các luồng này cần bổ sung backend, không chỉ giao diện.
- Identity: Admin/Staff tạo tài khoản Student/Teacher tại `/users/new`, không đổi phiên của người tạo và không mở đăng ký công khai. Trang `/account` đọc/lưu hồ sơ riêng qua `/api/identity/me`: họ tên, email, SĐT tùy chọn, ảnh JPG/PNG tối đa 2 MiB; mã học viên chỉ đọc. Lưu kèm `expectedRevision`, giữ bản nháp khi lỗi 409 và cho tải lại hồ sơ mới nhất. JWT vẫn không chứa tên/email.
- Ảnh đại diện dùng vé tải `purpose=avatar`, PUT trực tiếp lên storage không kèm token ứng dụng, xác nhận file rồi mới lưu hồ sơ; cần backend Media và storage/CORS hoạt động. Preview/kiểm thử API giả lập không chứng minh upload thật thành công.

## Kiểm tra

```sh
npm run build
npm test
```

## Docker và domain

Từ repo root, cấu hình `.env` theo `.env.example` rồi chạy:

```sh
docker compose -f compose.yaml -f compose.web.yaml up -d --build
```

Web mặc định `http://127.0.0.1:8088`, API proxy cùng origin tại `/api/`; `/web-health` chỉ kiểm tra web, không chứng minh DB hay storage khỏe. Compose gốc dùng **R2**, hai endpoint trong `.env` dùng S3 API HTTPS của R2 (không thêm tên bucket), region `auto`; không chạy MinIO. Bucket private, token Object Read & Write giới hạn bucket, CORS cấu hình trực tiếp trên Cloudflare theo origin frontend. Không dùng domain website/`r2.dev` làm endpoint ký URL.

MinIO chỉ cho local/test: dùng `.env.minio.example` làm mẫu cho `.env.minio`, rồi `docker compose --env-file .env.minio --project-name english-center-local -f compose.minio.yaml -f compose.web.yaml up -d --build`. Đây là stack riêng, không ghép với `compose.yaml`, không dùng database/secret VPS. Jenkins build web bằng Docker và deploy R2 với overlay khi `main` + `DEPLOY`, trên Docker host của agent; push GitHub không tự cập nhật VPS nếu chưa cấu hình Jenkins.

VPS: dùng reverse proxy HTTPS trên host đến `127.0.0.1:8088`. Ví dụ Caddy sau khi DNS A trỏ đúng VPS và mở 80/443:

```caddyfile
your-domain.example {
    reverse_proxy 127.0.0.1:8088
}
```

Thay domain bằng tên thật; Cloudflare chọn Full (strict) sau khi origin có chứng chỉ hợp lệ. Không mua domain riêng cho API. Không public PostgreSQL/MinIO console/Jenkins. Font Google có fallback system khi mạng ngoài bị chặn.

Trước mở public: kiểm tra forwarded headers/trusted proxy ở ASP.NET để rate limiter phân biệt IP thật (hiện proxy có thể khiến mọi người dùng chung quota IP); không tin header client tùy ý. Chưa có thay đổi backend cho phần này. Không đưa secret vào Vite env/bundle.

Sau deploy: login, logout, đổi mật khẩu, Student bị API chặn truy cập lớp Staff/Admin, deep link/reload, mobile 375px, API mất kết nối và 429. Kiểm chứng R2 riêng bằng PUT/confirm/download và so sánh hash; giao diện lên hay đăng nhập được chưa chứng minh storage hoạt động. Bản sửa repo không tự thay cấu hình hoặc dữ liệu VPS đang chạy.
