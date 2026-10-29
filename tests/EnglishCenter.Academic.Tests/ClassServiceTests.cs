using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public class ClassServiceTests
{
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid TeacherId = Guid.NewGuid();
    // 2026-10-28 10:00 giờ Việt Nam.
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeAcademicStore _store = new();
    private readonly RecordingEventBus _events = new();

    public ClassServiceTests()
    {
        _store.Courses.Add(Course.Create(CourseId, "IELTS-FOUNDATION", "IELTS Foundation"));
        _store.Teachers.Add(Teacher.Create(TeacherId, "Nguyen Van Giao", "teacher@example.test"));
    }

    private ClassService Service(DateTimeOffset? now = null) => new(_store, _events, new FixedClock(now ?? Now));

    private static CreateClassRequest Valid(string code = "IE-2611-A") => new(code, "IELTS Foundation K1", CourseId, "Main",
        new DateOnly(2026, 11, 2), new DateOnly(2027, 1, 30), 20, 4_500_000m, TeacherId);

    [Fact]
    public async Task Create_ValidRequest_SavesClass_AndPublishesClassCreatedOnce()
    {
        var response = await Service().CreateAsync(Valid(), default);

        var saved = Assert.Single(_store.Classes);
        Assert.Equal(saved.Id, response.Id);
        Assert.Equal("IE-2611-A", response.Code);
        Assert.Equal("IELTS-FOUNDATION", response.CourseCode);
        Assert.Equal("Nguyen Van Giao", response.TeacherName);
        Assert.Equal("Upcoming", response.Status);
        Assert.Equal(Now, response.CreatedAt);

        var published = Assert.IsType<ClassCreatedIntegrationEvent>(Assert.Single(_events.Published));
        Assert.Equal(saved.Id, published.ClassId);
        Assert.Equal("IE-2611-A", published.ClassCode);
        Assert.Equal(new DateOnly(2026, 11, 2), published.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 30), published.EndDate);
        Assert.Equal(20, published.MaxStudents);
        Assert.Equal(4_500_000m, published.TuitionFee);
        Assert.Equal("Main", published.ClassType);
    }

    [Fact]
    public async Task Create_AcceptsClassTypeIgnoringCase_AndNormalizesCode()
    {
        var response = await Service().CreateAsync(Valid(" ie-2611-b ") with { ClassType = "supplementary" }, default);
        Assert.Equal("IE-2611-B", response.Code);
        Assert.Equal("Supplementary", response.ClassType);
    }

    [Fact]
    public async Task Create_DuplicateCodeIgnoringCase_Returns409_WithoutSavingOrPublishing()
    {
        await Service().CreateAsync(Valid("IE-01"), default);
        _events.Published.Clear();

        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().CreateAsync(Valid("ie-01"), default));

        Assert.Equal(409, failure.Status);
        Assert.Single(_store.Classes);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Create_ConcurrentDuplicateDetectedOnSave_Returns409_WithoutPublishing()
    {
        _store.ConflictOnSave = true;
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().CreateAsync(Valid(), default));
        Assert.Equal(409, failure.Status);
        Assert.Contains("IE-2611-A", failure.Message);
        Assert.Empty(_events.Published);
    }

    public static TheoryData<CreateClassRequest, string> InvalidRequests => new()
    {
        { Valid() with { Code = "" }, "Code" },
        { Valid() with { Code = "IE 01" }, "Code" },
        { Valid() with { Code = "-IE01" }, "Code" },
        { Valid() with { Code = new string('A', 51) }, "Code" },
        { Valid() with { Name = "   " }, "Name" },
        { Valid() with { CourseId = null }, "CourseId" },
        { Valid() with { TeacherId = Guid.Empty }, "TeacherId" },
        { Valid() with { ClassType = "Online" }, "ClassType" },
        { Valid() with { ClassType = "1" }, "ClassType" },
        { Valid() with { StartDate = null }, "StartDate" },
        { Valid() with { EndDate = new DateOnly(2026, 11, 2) }, "EndDate" },
        { Valid() with { EndDate = new DateOnly(2026, 11, 1) }, "EndDate" },
        { Valid() with { MaxStudents = 0 }, "MaxStudents" },
        { Valid() with { MaxStudents = -5 }, "MaxStudents" },
        { Valid() with { TuitionFee = -1m }, "TuitionFee" },
        { Valid() with { TuitionFee = 4_500_000.5m }, "TuitionFee" },
        { Valid() with { TuitionFee = 1_000_000_000_000m }, "TuitionFee" },
        { Valid() with { TuitionFee = null }, "TuitionFee" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidField_Returns400ForThatField(CreateClassRequest request, string field)
    {
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().CreateAsync(request, default));

        Assert.Equal(400, failure.Status);
        Assert.NotNull(failure.Errors);
        Assert.Equal(new[] { field }, failure.Errors!.Keys);
        Assert.Empty(_store.Classes);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Create_ReportsAllFieldErrorsTogether()
    {
        var request = Valid() with { Code = "", MaxStudents = 0, EndDate = new DateOnly(2026, 1, 1) };
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().CreateAsync(request, default));
        Assert.Equal(new[] { "Code", "EndDate", "MaxStudents" }, failure.Errors!.Keys.Order());
    }

    [Fact]
    public async Task Create_UnknownCourseAndTeacher_Returns400ForBoth()
    {
        var request = Valid() with { CourseId = Guid.NewGuid(), TeacherId = Guid.NewGuid() };
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().CreateAsync(request, default));
        Assert.Equal(400, failure.Status);
        Assert.Equal(new[] { "CourseId", "TeacherId" }, failure.Errors!.Keys.Order());
    }

    [Theory]
    // 2026-11-01 16:59 UTC = 23:59 ngày 01/11 giờ Việt Nam: lớp chưa bắt đầu.
    [InlineData("2026-11-01T16:59:00Z", "Upcoming")]
    // 2026-11-01 17:00 UTC = 00:00 ngày 02/11 giờ Việt Nam: đúng ngày khai giảng.
    [InlineData("2026-11-01T17:00:00Z", "Ongoing")]
    [InlineData("2027-01-30T16:59:00Z", "Ongoing")]
    [InlineData("2027-01-30T17:00:00Z", "Finished")]
    public async Task Get_StatusUsesVietnamDate(string utcNow, string expected)
    {
        var created = await Service().CreateAsync(Valid(), default);
        var response = await Service(DateTimeOffset.Parse(utcNow)).GetAsync(created.Id, default);
        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task Get_UnknownClass_Returns404()
    {
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().GetAsync(Guid.NewGuid(), default));
        Assert.Equal(404, failure.Status);
    }
}
