namespace EnglishCenter.Modules.Academic.Application.Classes;

public static class AcademicCalendar
{
    // Trung tâm hoạt động theo giờ Việt Nam (UTC+7, không có giờ mùa hè). Server/container chạy UTC,
    // nên "hôm nay" phải quy đổi để trạng thái lớp không lệch từ 0h đến 7h sáng.
    public static readonly TimeSpan CenterOffset = TimeSpan.FromHours(7);

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(CenterOffset).DateTime);
}
