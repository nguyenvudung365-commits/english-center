namespace EnglishCenter.Modules.Academic.Domain.Sessions;

public sealed class Session
{
    public const int UnitMaxLength = 100;
    public const int LessonMaxLength = 200;
    public const int ExamSlotMaxLength = 100;
    private Session() { }

    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public string Lesson { get; private set; } = string.Empty;
    public SessionSkills Skills { get; private set; }
    public SessionType SessionType { get; private set; }
    public string? ExamSlot { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public int Revision { get; private set; }
    public bool IsCancelled { get; private set; }

    public void ApplySchedule(Session validated)
    {
        Unit = validated.Unit;
        Lesson = validated.Lesson;
        Skills = validated.Skills;
        SessionType = validated.SessionType;
        ExamSlot = validated.ExamSlot;
        StartsAtUtc = validated.StartsAtUtc;
        EndsAtUtc = validated.EndsAtUtc;
        Revision++;
    }

    public void Cancel()
    {
        IsCancelled = true;
        Revision++;
    }

    public static Session Create(Guid classId, string unit, string lesson, SessionSkills skills,
        SessionType sessionType, string? examSlot, DateTime startsAtUtc, DateTime endsAtUtc, DateTime createdAtUtc)
    {
        if (classId == Guid.Empty) throw new ArgumentException("Class is required.", nameof(classId));
        if (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > UnitMaxLength)
            throw new ArgumentException("Unit must contain 1–100 characters.", nameof(unit));
        if (string.IsNullOrWhiteSpace(lesson) || lesson.Trim().Length > LessonMaxLength)
            throw new ArgumentException("Lesson must contain 1–200 characters.", nameof(lesson));
        if ((int)skills is < 1 or > 15) throw new ArgumentException("Unknown or empty skills.", nameof(skills));
        if (!Enum.IsDefined(sessionType)) throw new ArgumentException("Unknown session type.", nameof(sessionType));
        var slot = string.IsNullOrWhiteSpace(examSlot) ? null : examSlot.Trim();
        if (slot?.Length > ExamSlotMaxLength || (sessionType == SessionType.Regular && slot is not null))
            throw new ArgumentException("Exam slot is limited to 100 characters and test sessions.", nameof(examSlot));
        if (startsAtUtc == default || startsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A UTC start is required.", nameof(startsAtUtc));
        if (endsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc <= startsAtUtc)
            throw new ArgumentException("UTC end must be after start.", nameof(endsAtUtc));
        if (startsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0)
            throw new ArgumentException("Start must have microsecond precision.", nameof(startsAtUtc));
        if (endsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0)
            throw new ArgumentException("End must have microsecond precision.", nameof(endsAtUtc));
        // PostgreSQL stores microseconds; normalize only the server-generated creation time.
        createdAtUtc = createdAtUtc.AddTicks(-(createdAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));
        if (createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A UTC creation time is required.", nameof(createdAtUtc));
        return new() { Id = Guid.NewGuid(), ClassId = classId, Unit = unit.Trim(), Lesson = lesson.Trim(),
            Skills = skills, SessionType = sessionType, ExamSlot = slot, StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc, CreatedAtUtc = createdAtUtc };
    }
}
