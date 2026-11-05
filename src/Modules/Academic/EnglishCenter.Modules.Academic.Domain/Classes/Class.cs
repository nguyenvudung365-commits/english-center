namespace EnglishCenter.Modules.Academic.Domain.Classes;

public sealed class Class
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    // numeric(12,0): học phí VND là số nguyên, tối đa 12 chữ số.
    public const decimal MaxTuitionFee = 999_999_999_999m;

    private Class() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid CourseId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ClassType ClassType { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public int MaxStudents { get; private set; }
    public decimal TuitionFee { get; private set; }
    public Guid TeacherId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Class Create(string code, string name, Guid courseId, ClassType classType, DateOnly startDate,
        DateOnly endDate, int maxStudents, decimal tuitionFee, Guid teacherId, DateTimeOffset now)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length is 0 or > CodeMaxLength)
            throw new ArgumentException("Class code is required and must not exceed 50 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
            throw new ArgumentException("Class name is required and must not exceed 200 characters.", nameof(name));
        if (courseId == Guid.Empty) throw new ArgumentException("Course is required.", nameof(courseId));
        if (teacherId == Guid.Empty) throw new ArgumentException("Teacher is required.", nameof(teacherId));
        if (!Enum.IsDefined(classType)) throw new ArgumentException("Unknown class type.", nameof(classType));
        if (endDate <= startDate) throw new ArgumentException("End date must be after start date.", nameof(endDate));
        if (maxStudents <= 0) throw new ArgumentException("Max students must be greater than 0.", nameof(maxStudents));
        if (!IsValidTuitionFee(tuitionFee))
            throw new ArgumentException("Tuition fee must be a whole VND amount between 0 and 999,999,999,999.", nameof(tuitionFee));

        return new Class
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            CourseId = courseId,
            Name = name.Trim(),
            ClassType = classType,
            StartDate = startDate,
            EndDate = endDate,
            MaxStudents = maxStudents,
            TuitionFee = tuitionFee,
            TeacherId = teacherId,
            CreatedAt = now
        };
    }

    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidTuitionFee(decimal fee) => fee >= 0 && fee <= MaxTuitionFee && decimal.Truncate(fee) == fee;

    // Trước ngày bắt đầu = Sắp diễn ra, trong khoảng (tính cả hai đầu) = Đang diễn ra, sau ngày kết thúc = Kết thúc.
    public ClassStatus GetStatus(DateOnly today) =>
        today < StartDate ? ClassStatus.Upcoming
        : today > EndDate ? ClassStatus.Finished
        : ClassStatus.Ongoing;
}
