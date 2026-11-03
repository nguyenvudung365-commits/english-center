# Hợp đồng xác thực, phân quyền và dạng lỗi API

Dành cho frontend và các module (TV2/TV3/TV4). Người phụ trách: TV1 (Identity, BuildingBlocks, Host).

## 1. Luồng token

1. `POST /api/identity/auth/login` với `{ "email", "password" }` trả `{ accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt }`.
2. Gửi `Authorization: Bearer <accessToken>` cho mọi endpoint cần quyền. Access token sống 15 phút, refresh token sống 7 ngày (cấu hình `JwtOptions`).
3. Khi access token hết hạn: `POST /api/identity/auth/refresh` với `{ "refreshToken" }`. Refresh token cũ bị thu hồi, nhận cặp token mới (xoay vòng).
4. `POST /api/identity/auth/logout` (Bearer + `{ "refreshToken" }` của chính người dùng) thu hồi phiên đó.
5. `POST /api/identity/auth/change-password` (Bearer) thu hồi mọi refresh token của người dùng.
6. Access token đã phát không bị thu hồi trước hạn. Auth endpoint giới hạn 20 yêu cầu/phút/IP (429).

Thông điệp đăng nhập/refresh sai luôn là `"Invalid credentials or refresh token."`, không phân biệt email sai hay mật khẩu sai.

## 2. Claim và role

| Claim | Ý nghĩa |
|---|---|
| `sub` | GUID người dùng (khóa để kiểm tra dữ liệu thuộc chủ sở hữu) |
| `role` | Một trong `Student`, `Teacher`, `Staff`, `Admin` |

Tên claim giữ nguyên (`MapInboundClaims = false`), không phải URI của Microsoft.

## 3. Dạng lỗi (`application/problem+json`)

| Tình huống | Status | Body |
|---|---|---|
| Dữ liệu sai (Identity) | 400 | `ValidationProblemDetails { status, title: "One or more validation errors occurred.", errors: { "<Field>": ["..."] } }` |
| Lỗi nghiệp vụ Identity (đổi mật khẩu sai, role không được tạo...) | 400/403/409 | `ProblemDetails { status, title, type, instance }` |
| Không có token / token sai | 401 | `ProblemDetails { status: 401 }` |
| Sai role | 403 | `ProblemDetails { status: 403 }` |
| Route không tồn tại | 404 | `ProblemDetails { status: 404 }` |
| Vượt giới hạn | 429 | `ProblemDetails { status: 429 }` |
| Lỗi chưa xử lý | 500 | `ProblemDetails { status: 500, title: "An error occurred while processing your request." }`; 500 không lộ stack trace ở cả Production lẫn Development |

Academic tiếp tục trả `ValidationProblemDetails` cho lỗi validation (cùng dạng với Identity).

Quy ước truy cập dữ liệu của người khác (học viên xem dữ liệu học viên khác, giáo viên thao tác lớp không phải của mình): trả **404**, không tiết lộ tài nguyên có tồn tại.

## 4. Ma trận endpoint x role

| Endpoint | Ẩn danh | Student | Teacher | Staff | Admin |
|---|---|---|---|---|---|
| `POST /api/identity/auth/login`, `refresh` | Có | Có | Có | Có | Có |
| `POST /api/identity/auth/logout`, `change-password` | 401 | Có | Có | Có | Có |
| `POST /api/identity/users` | 401 | 403 | 403 | Có | Có |
| `POST /api/identity/users/republish` | 401 | 403 | 403 | 403 | Có |
| `POST /api/academic/classes`, `GET /api/academic/classes/{id}` | 401 | 403 | 403 | Có | Có |
| `GET /api/academic/courses`, `GET /api/academic/teachers` | 401 | 403 | 403 | Có | Có |
| `GET /api/{identity,academic,enrollment,finance,media-notification}/ping` | Có | Có | Có | Có | Có |
| `GET /health` | Có | Có | Có | Có | Có |

Quy tắc: mọi action mới phải khai báo `[Authorize]` (kèm `Roles` nếu cần) hoặc `[AllowAnonymous]` tường minh. Hệ thống chưa bật `FallbackPolicy` toàn cục, nên endpoint quên khai báo sẽ mở cho ẩn danh. Ngoại lệ hiện tại: 5 ping controller của module.

## 5. Dùng `ICurrentUser` trong module

`EnglishCenter.BuildingBlocks.Security.ICurrentUser` (`IsAuthenticated`, `UserId`, `Role`, `IsInRole`) đã đăng ký Scoped ở Host. Application của module nhận qua constructor, hoặc controller truyền `userId` xuống. Ngoài ra có extension `TryGetUserId`, `GetRole`, `IsStaffOrAdmin` trên `ClaimsPrincipal`.

- Học viên chỉ xem dữ liệu có `student_id == UserId`; dữ liệu người khác trả 404.
- Giáo viên chỉ thao tác lớp có `teacher_id == UserId`.
- Không đưa `IHttpContextAccessor` hay kiểu ASP.NET Core vào Domain/Application.

## 6. Đồng bộ lại tài khoản (`republish`)

`POST /api/identity/users/republish`, chỉ `Admin`. Body `{ "role": "Teacher" }` hoặc `{}` (mọi role). Role không hợp lệ trả 400. Trả `{ "published": <số>, "role": <role|null> }`; không trả email hay hash.

Endpoint phát lại `UserRegisteredIntegrationEvent` cho từng tài khoản đã có, để module khác dựng bản sao cho tài khoản tạo từ trước (kể cả admin bootstrap). Do đó **mọi handler `UserRegistered` phải idempotent theo `UserId`** và bỏ qua role không thuộc module. Event bus in-memory nuốt lỗi handler, nên số `published` chỉ là số event đã phát, không phải số đã xử lý thành công; xem log để biết lỗi.
