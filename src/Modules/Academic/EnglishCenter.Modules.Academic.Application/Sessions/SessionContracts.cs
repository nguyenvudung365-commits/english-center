namespace EnglishCenter.Modules.Academic.Application.Sessions;

public sealed record CreateSessionRequest(Guid? ClassId, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc,
    string? Unit, string? Lesson, string[]? Skills, string? SessionType, string? ExamSlot = null);

public sealed record SessionRangeQuery(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? ClassId = null);

public sealed record SessionResponse(Guid Id, Guid ClassId, string ClassCode, string ClassName, Guid TeacherId,
    string TeacherName, string Unit, string Lesson, IReadOnlyList<string> Skills, string SessionType, string? ExamSlot,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, DateTimeOffset CreatedAtUtc);
