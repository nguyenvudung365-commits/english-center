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
- Enrollment/Finance (Lịch): backend đã có API ghi danh, hóa đơn, thanh toán, giới hạn/cảnh báo và hẹn test theo [tuần 3](../TV3_WEEK3.md)/[tuần 5](../TV3_WEEK5.md). Hai trang `/enrollment` và `/invoices` vẫn là thông báo giao diện đang hoàn thiện, chưa kết nối các API này. Không gửi yêu cầu nghiệp vụ, không xác nhận thanh toán và không có số liệu hay chứng từ giả. Backend chưa có chuyển lớp/hủy ghi danh.
- Điều hướng theo Admin/Staff/Teacher/Student. Truy cập trực tiếp trang Academic quản lý bằng Teacher/Student bị chặn trước khi gọi API; quyền trên server vẫn là quyết định cuối cùng.
- Lịch, điểm danh, bài tập, tiến độ, thông báo, phản hồi, thống kê ghi rõ chưa tích hợp API; không gửi phản hồi giả hoặc hiển thị số liệu giả. Hoàn thiện các luồng này cần bổ sung backend, không chỉ giao diện.
- JWT hiện không chứa tên/email; trang tài khoản chỉ hiển thị role/ID. API hồ sơ cần bổ sung để mở phần thông tin cá nhân.

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

Web mặc định `http://127.0.0.1:8088`, API proxy cùng origin tại `/api/`; `/web-health` chỉ kiểm tra web, không chứng minh DB khỏe. Compose gốc không đổi để giữ luồng CI/backend hiện tại. Jenkins build web bằng Docker và deploy thêm overlay khi `main` + `DEPLOY`.

VPS: dùng reverse proxy HTTPS trên host đến `127.0.0.1:8088`. Ví dụ Caddy sau khi DNS A trỏ đúng VPS và mở 80/443:

```caddyfile
your-domain.example {
    reverse_proxy 127.0.0.1:8088
}
```

Thay domain bằng tên thật; Cloudflare chọn Full (strict) sau khi origin có chứng chỉ hợp lệ. Không mua domain riêng cho API. Không public PostgreSQL/MinIO console/Jenkins. Font Google có fallback system khi mạng ngoài bị chặn.

Trước mở public: kiểm tra forwarded headers/trusted proxy ở ASP.NET để rate limiter phân biệt IP thật (hiện proxy có thể khiến mọi người dùng chung quota IP); không tin header client tùy ý. Chưa có thay đổi backend cho phần này. Không đưa secret vào Vite env/bundle.

Sau deploy: login, logout, đổi mật khẩu, Student bị API chặn truy cập lớp Staff/Admin, deep link/reload, mobile 375px, API mất kết nối và 429. Chưa có VPS/domain trong phiên triển khai nên không coi cấu hình này là bản đã chạy production.
