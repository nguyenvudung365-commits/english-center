const translations: Record<string, string> = {
    "bad request": "Yêu cầu không hợp lệ.",
    "one or more validation errors occurred": "Dữ liệu chưa hợp lệ. Vui lòng kiểm tra lại.",
    "academic validation failed": "Dữ liệu chưa hợp lệ. Vui lòng kiểm tra lại.",
    "not found": "Không tìm thấy nội dung yêu cầu.",
    "notification not found": "Không tìm thấy thông báo.",
    "class not found": "Không tìm thấy lớp học.",
    "session not found": "Không tìm thấy buổi học.",
    "assignment not found": "Không tìm thấy bài tập.",
    "file not found": "Không tìm thấy tệp.",
    "feedback not found": "Không tìm thấy phản hồi.",
    "warning not found": "Không tìm thấy cảnh báo.",
    "enrollment not found": "Không tìm thấy hồ sơ ghi danh.",
    "invoice not found": "Không tìm thấy hóa đơn.",
    "invalid code": "Mã không hợp lệ.",
    "invalid page": "Số trang không hợp lệ.",
    "invalid pagination": "Thông tin phân trang không hợp lệ.",
    "teacher not found": "Không tìm thấy giáo viên.",
    "assigned teacher does not exist": "Không tìm thấy giáo viên được phân công.",
    "academic data already exists": "Dữ liệu đã tồn tại.",
    "class code already exists": "Mã lớp đã được sử dụng.",
    "schedule is being changed concurrently. reload before trying again": "Lịch đang được cập nhật. Hãy tải lại trước khi thử lại.",
    "assigned teacher changed. reload the class": "Giáo viên được phân công đã thay đổi. Hãy tải lại thông tin lớp.",
    "teacher already has an overlapping session": "Giáo viên đã có buổi học trùng giờ.",
    "selected sessions overlap each other": "Các buổi đã chọn bị trùng giờ.",
    "session changed or was cancelled. reload before trying again": "Buổi học đã thay đổi hoặc bị hủy. Hãy tải lại trước khi thử lại.",
    "a session with recorded attendance cannot be rescheduled": "Không thể đổi giờ của buổi học đã điểm danh.",
    "the class of an existing session cannot change": "Không thể đổi lớp của buổi học đã tạo.",
    "class is required": "Vui lòng chọn lớp học.",
    "a valid start with an explicit iso offset is required": "Chọn giờ bắt đầu hợp lệ (giờ Việt Nam).",
    "a valid end with an explicit iso offset is required": "Chọn giờ kết thúc hợp lệ (giờ Việt Nam).",
    "a valid range start is required": "Ngày bắt đầu của khoảng lịch không hợp lệ.",
    "a valid range end is required": "Ngày kết thúc của khoảng lịch không hợp lệ.",
    "class id must not be empty": "Vui lòng chọn lớp học.",
    "class dates cannot be represented as a utc scheduling window": "Khoảng ngày của lớp không hợp lệ để xếp lịch.",
    "timestamp must be aligned to whole microseconds (six fractional-second digits)": "Ngày giờ không hợp lệ. Vui lòng nhập lại.",
    "range must be positive and no longer than 93 elapsed days": "Khoảng lịch phải lớn hơn 0 và không quá 93 ngày.",
    "end must be after start": "Giờ kết thúc phải sau giờ bắt đầu.",
    "session starts before the class date window (utc+7)": "Buổi học bắt đầu trước ngày khai giảng (giờ Việt Nam).",
    "session ends after the class date window (utc+7)": "Buổi học kết thúc sau ngày kết thúc lớp (giờ Việt Nam).",
    "unit must contain 1–100 characters": "Tên chủ đề phải có từ 1 đến 100 ký tự.",
    "lesson must contain 1–200 characters": "Tên buổi phải có từ 1 đến 200 ký tự.",
    "select one or more distinct named skills": "Chọn ít nhất một kỹ năng và không chọn trùng.",
    "unknown skill": "Kỹ năng không hợp lệ.",
    "skills must be distinct": "Không được chọn trùng kỹ năng.",
    "session type must be regular, mocktest or finaltest": "Chọn buổi học, thi thử hoặc thi cuối khóa.",
    "exam slot is limited to 100 characters and test sessions": "Ca thi chỉ dùng cho buổi thi và tối đa 100 ký tự.",
    "the assigned teacher must be an active teacher": "Chọn giáo viên có tài khoản đang hoạt động.",
    "class is full": "Lớp đã đủ học viên.",
    "student is already enrolled in this class": "Học viên đã ghi danh vào lớp này.",
    "invoice is already paid": "Hóa đơn đã được thanh toán.",
    "file exceeds the maximum allowed size": "Tệp vượt quá dung lượng cho phép.",
    "upload ticket has expired. request a new ticket": "Phiên tải tệp đã hết hạn. Vui lòng tải lại tệp.",
    "attendance changed concurrently. reload before trying again": "Điểm danh đã thay đổi. Hãy tải lại trước khi thử lại.",
    "replay dispatch failed. the failed attempt is retained in the audit": "Không gửi lại được sự kiện điểm danh. Lần thử này đã được lưu trong nhật ký.",
};

// shortcut: translate known API text and fall back for unknown English; switch to stable API error codes when available.
export function localizeSystemMessage(value: unknown, fallback: string): string {
    if (typeof value !== "string" || !value.trim()) return fallback;
    const text = value.trim();
    const key = text.toLowerCase().replace(/\.$/, "");
    if (Object.hasOwn(translations, key)) return translations[key];
    const overlap = /^overlaps selected slot (\d+)$/.exec(key);
    if (overlap) return `Trùng giờ với buổi đã chọn số ${overlap[1]}.`;
    const dates = /^select 1–(\d+) dates$/.exec(key);
    if (dates) return `Chọn từ 1 đến ${dates[1]} ngày.`;
    return /[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]/iu.test(text.normalize("NFC")) ? text : fallback;
}
