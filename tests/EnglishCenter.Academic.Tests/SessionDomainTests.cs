using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public sealed class SessionDomainTests
{
    internal static readonly DateTime Start = new(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(SessionType.Regular, null)]
    [InlineData(SessionType.Regular, " ")]
    [InlineData(SessionType.MockTest, " Ca sáng ")]
    [InlineData(SessionType.FinalTest, null)]
    public void Create_TrimsText_PreservesUtcAndFlags_AndGeneratesIdentity(SessionType type, string? slot)
    {
        var classId = Guid.NewGuid();
        var value = Session.Create(classId, "  Unit ' 🗓  ", " Bài học ", (SessionSkills)15, type, slot,
            Start, Start.AddHours(1), Start.AddDays(-1));
        Assert.NotEqual(Guid.Empty, value.Id);
        Assert.Equal(classId, value.ClassId);
        Assert.Equal("Unit ' 🗓", value.Unit);
        Assert.Equal("Bài học", value.Lesson);
        Assert.Equal((SessionSkills)15, value.Skills);
        Assert.Equal(type, value.SessionType);
        Assert.Equal(string.IsNullOrWhiteSpace(slot) ? null : slot.Trim(), value.ExamSlot);
        Assert.Equal(Start, value.StartsAtUtc);
        Assert.Equal(Start.AddHours(1), value.EndsAtUtc);
        Assert.Equal(Start.AddDays(-1), value.CreatedAtUtc);
        Assert.All(new[] { value.StartsAtUtc, value.EndsAtUtc, value.CreatedAtUtc }, x => Assert.Equal(DateTimeKind.Utc, x.Kind));
    }

    [Fact]
    public void Create_AcceptsExactTextCeilings_AndSingleMicrosecondDuration()
    {
        var value = Session.Create(Guid.NewGuid(), new('u', 100), new('l', 200), SessionSkills.Reading,
            SessionType.FinalTest, new('s', 100), Start, Start.AddTicks(10), Start);
        Assert.Equal(100, value.Unit.Length);
        Assert.Equal(200, value.Lesson.Length);
        Assert.Equal(100, value.ExamSlot!.Length);
        Assert.Equal(TimeSpan.FromTicks(10), value.EndsAtUtc - value.StartsAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_RejectsSubmicrosecondStartOrEnd(bool start)
    {
        var failure = Assert.Throws<ArgumentException>(() => Session.Create(Guid.NewGuid(), "Unit", "Lesson", SessionSkills.Reading,
            SessionType.Regular, null, Start.AddTicks(start ? 1 : 0), Start.AddHours(1).AddTicks(start ? 0 : 1), Start));
        Assert.Equal(start ? "startsAtUtc" : "endsAtUtc", failure.ParamName);
    }

    [Fact]
    public void Create_TruncatesServerGeneratedCreationTime_ToMicrosecondsOnly()
    {
        var value = Session.Create(Guid.NewGuid(), "Unit", "Lesson", SessionSkills.Listening, SessionType.Regular,
            null, Start, Start.AddTicks(10), Start.AddTicks(1234567));
        Assert.Equal(Start.AddTicks(1234560), value.CreatedAtUtc);
        Assert.Equal(Start, value.StartsAtUtc);
        Assert.Equal(Start.AddTicks(10), value.EndsAtUtc);
    }

    [Theory]
    [InlineData("classId")]
    [InlineData("null-unit")]
    [InlineData("empty-unit")]
    [InlineData("long-unit")]
    [InlineData("null-lesson")]
    [InlineData("blank-lesson")]
    [InlineData("long-lesson")]
    [InlineData("zero-skills")]
    [InlineData("negative-skills")]
    [InlineData("unknown-skills")]
    [InlineData("type")]
    [InlineData("regular-slot")]
    [InlineData("long-slot")]
    [InlineData("default-start")]
    [InlineData("local-start")]
    [InlineData("unspecified-start")]
    [InlineData("local-end")]
    [InlineData("unspecified-end")]
    [InlineData("equal-end")]
    [InlineData("before-end")]
    [InlineData("default-created")]
    [InlineData("local-created")]
    [InlineData("unspecified-created")]
    public void Create_RejectsInvalidDomainValues(string scenario)
    {
        var id = scenario == "classId" ? Guid.Empty : Guid.NewGuid();
        var unit = scenario switch { "null-unit" => null, "empty-unit" => "", "long-unit" => new string('u', 101), _ => "Unit" };
        var lesson = scenario switch { "null-lesson" => null, "blank-lesson" => "  ", "long-lesson" => new string('l', 201), _ => "Lesson" };
        var skills = scenario switch { "zero-skills" => 0, "negative-skills" => -1, "unknown-skills" => 17, _ => 1 };
        var type = scenario == "type" ? (SessionType)99 : scenario == "long-slot" ? SessionType.MockTest : SessionType.Regular;
        var slot = scenario switch { "regular-slot" => "Test", "long-slot" => new string('s', 101), _ => null };
        var start = scenario switch { "default-start" => default, "local-start" => DateTime.SpecifyKind(Start, DateTimeKind.Local),
            "unspecified-start" => DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), _ => Start };
        var end = scenario switch { "local-end" => DateTime.SpecifyKind(Start.AddHours(1), DateTimeKind.Local),
            "unspecified-end" => DateTime.SpecifyKind(Start.AddHours(1), DateTimeKind.Unspecified),
            "equal-end" => Start, "before-end" => Start.AddTicks(-1), _ => Start.AddHours(1) };
        var created = scenario switch { "default-created" => default, "local-created" => DateTime.SpecifyKind(Start, DateTimeKind.Local),
            "unspecified-created" => DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), _ => Start };
        Assert.Throws<ArgumentException>(() => Session.Create(id, unit!, lesson!, (SessionSkills)skills, type, slot, start, end, created));
    }

    internal static Class NewClass(Guid teacher, string code = "A", DateOnly? from = null, DateOnly? to = null) =>
        Class.Create(code, "Class", Guid.NewGuid(), ClassType.Main, from ?? new(2026, 12, 31),
            to ?? new(2027, 1, 2), 20, 0, teacher, new DateTimeOffset(Start));

    [Fact]
    public void Window_AcceptsInclusiveClassDates_AsHalfOpenUtcBounds()
    {
        var item = NewClass(Guid.NewGuid());
        SessionWindow.Validate(item, new(2026, 12, 30, 17, 0, 0, DateTimeKind.Utc), new(2027, 1, 2, 17, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("lower", "StartsAtUtc")]
    [InlineData("upper", "EndsAtUtc")]
    [InlineData("equal", "EndsAtUtc")]
    [InlineData("reverse", "EndsAtUtc")]
    [InlineData("overflow", "EndsAtUtc")]
    [InlineData("submicro-start", "StartsAtUtc")]
    [InlineData("submicro-end", "EndsAtUtc")]
    public void Window_RejectsOutOfWindowOrNonPositiveTimes_WithFieldError(string scenario, string field)
    {
        var item = NewClass(Guid.NewGuid(), to: scenario == "overflow" ? DateOnly.MaxValue : null);
        var start = scenario == "lower" ? new DateTime(2026, 12, 30, 16, 59, 59, DateTimeKind.Utc) : Start.AddTicks(scenario == "submicro-start" ? 1 : 0);
        var end = scenario switch { "upper" => new DateTime(2027, 1, 2, 17, 0, 0, DateTimeKind.Utc).AddTicks(10),
            "equal" => start, "reverse" => start.AddTicks(-10), "submicro-end" => Start.AddHours(1).AddTicks(1), _ => Start.AddHours(1) };
        var error = Assert.Throws<AcademicFailure>(() => SessionWindow.Validate(item, start, end));
        Assert.Equal(400, error.Status);
        Assert.Contains(field, error.Errors!.Keys);
    }
}
