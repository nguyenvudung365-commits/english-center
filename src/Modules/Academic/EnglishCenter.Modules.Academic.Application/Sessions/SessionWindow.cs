using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Domain.Classes;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

public static class SessionWindow
{
    public static void ValidatePrecision(DateTime timestamp, string field)
    {
        if (timestamp.Ticks % TimeSpan.TicksPerMicrosecond != 0)
            throw Invalid(field, "Timestamp must be aligned to whole microseconds (six fractional-second digits).");
    }

    public static void Validate(Class item, DateTime startUtc, DateTime endUtc)
    {
        ValidatePrecision(startUtc, "StartsAtUtc");
        ValidatePrecision(endUtc, "EndsAtUtc");
        DateTime lower;
        DateTime upper;
        try
        {
            lower = new DateTimeOffset(item.StartDate.ToDateTime(TimeOnly.MinValue), AcademicCalendar.CenterOffset).UtcDateTime;
            upper = new DateTimeOffset(item.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), AcademicCalendar.CenterOffset).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid("EndsAtUtc", "Class dates cannot be represented as a UTC scheduling window.");
        }
        if (startUtc < lower) throw Invalid("StartsAtUtc", "Session starts before the class date window (UTC+7).");
        if (endUtc > upper) throw Invalid("EndsAtUtc", "Session ends after the class date window (UTC+7).");
        if (endUtc <= startUtc) throw Invalid("EndsAtUtc", "End must be after start.");
    }

    private static AcademicFailure Invalid(string field, string message) =>
        new(400, "One or more validation errors occurred.", new Dictionary<string, string[]> { [field] = [message] });
}
