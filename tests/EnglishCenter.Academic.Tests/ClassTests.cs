using EnglishCenter.Modules.Academic.Domain.Classes;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public class ClassTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);
    private static readonly DateOnly End = new(2027, 1, 30);

    private static Class Create(string code = "IE-01", string name = "IELTS Foundation K1", ClassType type = ClassType.Main,
        DateOnly? start = null, DateOnly? end = null, int max = 20, decimal fee = 4_500_000m, Guid? courseId = null,
        Guid? teacherId = null) =>
        Class.Create(code, name, courseId ?? Guid.NewGuid(), type, start ?? Start, end ?? End, max, fee,
            teacherId ?? Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Create_TrimsAndUppercasesCode_AndKeepsValues()
    {
        var value = Create(code: "  ie-01 ", name: "  IELTS K1 ");
        Assert.Equal("IE-01", value.Code);
        Assert.Equal("IELTS K1", value.Name);
        Assert.Equal(ClassType.Main, value.ClassType);
        Assert.Equal(4_500_000m, value.TuitionFee);
        Assert.NotEqual(Guid.Empty, value.Id);
    }

    public static TheoryData<string, Func<Class>> InvalidCases => new()
    {
        { "code", () => Create(code: "   ") },
        { "code", () => Create(code: new string('A', 51)) },
        { "name", () => Create(name: "") },
        { "endDate", () => Create(end: Start) },
        { "endDate", () => Create(end: Start.AddDays(-1)) },
        { "maxStudents", () => Create(max: 0) },
        { "tuitionFee", () => Create(fee: -1m) },
        { "tuitionFee", () => Create(fee: 1_500_000.5m) },
        { "tuitionFee", () => Create(fee: 1_000_000_000_000m) },
        { "classType", () => Create(type: (ClassType)99) },
        { "courseId", () => Create(courseId: Guid.Empty) },
        { "teacherId", () => Create(teacherId: Guid.Empty) },
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Create_RejectsBrokenInvariants(string parameter, Func<Class> create)
    {
        var ex = Assert.Throws<ArgumentException>(create);
        Assert.Equal(parameter, ex.ParamName);
    }

    [Theory]
    [InlineData("2026-11-01", ClassStatus.Upcoming)]
    [InlineData("2026-11-02", ClassStatus.Ongoing)]
    [InlineData("2026-12-15", ClassStatus.Ongoing)]
    [InlineData("2027-01-30", ClassStatus.Ongoing)]
    [InlineData("2027-01-31", ClassStatus.Finished)]
    public void GetStatus_IsDerivedFromToday_InclusiveOfBothEnds(string today, ClassStatus expected) =>
        Assert.Equal(expected, Create().GetStatus(DateOnly.Parse(today)));
}
