using System.Text.RegularExpressions;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;

namespace EnglishCenter.Modules.Academic.Application.Classes;

public sealed partial class ClassService(IAcademicStore store, IEventBus events, TimeProvider clock, ClassAccess access)
{
    public async Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var errors = Validate(request, out var classType);
        if (errors.Count > 0) throw Invalid(errors);

        var course = await store.FindCourseAsync(request.CourseId!.Value, ct);
        if (course is null) errors[nameof(request.CourseId)] = ["Course does not exist."];
        else if (course.IsRetired) errors[nameof(request.CourseId)] = ["Course is retired."];
        var teacher = await store.FindTeacherAsync(request.TeacherId!.Value, ct);
        if (teacher is null) errors[nameof(request.TeacherId)] = ["Teacher does not exist or is not synchronized to Academic yet."];
        if (errors.Count > 0) throw Invalid(errors);

        var code = Class.NormalizeCode(request.Code);
        if (await store.ClassCodeExistsAsync(code, ct)) throw DuplicateCode(code);

        var created = Class.Create(code, request.Name, course!.Id, classType, request.StartDate!.Value, request.EndDate!.Value,
            request.MaxStudents!.Value, request.TuitionFee!.Value, teacher!.Id, clock.GetUtcNow());
        try
        {
            await store.SaveNewClassAsync(created, ct);
        }
        catch (AcademicFailure failure) when (failure.Status == 409)
        {
            // Hai request cùng mã vượt qua bước kiểm tra trước; unique index chặn request thứ hai.
            throw DuplicateCode(code);
        }

        await PublishClassAsync(created, ct);
        return ToResponse(created, course, teacher);
    }

    public async Task ReplayAsync(Guid id, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var found = await store.FindClassAsync(id, ct) ?? throw new AcademicFailure(404, "Class not found.");
        await PublishClassAsync(found, ct);
    }

    public async Task<RepublishClassesResponse> RepublishAsync(CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var classes = await store.ClassesAsync(ct);
        foreach (var value in classes)
            await PublishClassAsync(value, ct);
        return new RepublishClassesResponse(classes.Count);
    }

    // Dùng cùng payload cho tạo mới và backfill; chỉ đọc dữ liệu thuộc Academic.
    private Task PublishClassAsync(Class value, CancellationToken ct) =>
        events.PublishAsync(new ClassCreatedIntegrationEvent(value.Id, value.Code, value.StartDate,
            value.EndDate, value.MaxStudents, value.TuitionFee, value.ClassType.ToString()), ct);

    public async Task<ClassResponse> GetAsync(Guid id, CancellationToken ct)
    {
        await access.RequireReadAsync(id, ct);
        var found = await store.FindClassAsync(id, ct) ?? throw new AcademicFailure(404, "Class not found.");
        var course = await store.FindCourseAsync(found.CourseId, ct);
        var teacher = await store.FindTeacherAsync(found.TeacherId, ct);
        return ToResponse(found, course, teacher);
    }

    public async Task<IReadOnlyList<CourseResponse>> CoursesAsync(bool includeRetired, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        return (await store.CoursesAsync(ct)).Where(x => includeRetired || !x.IsRetired).Select(CourseResponseOf).ToList();
    }

    public async Task<CourseResponse> CourseAsync(Guid id, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        return CourseResponseOf(await FindCourseAsync(id, ct));
    }

    public async Task<CourseResponse> SaveCourseAsync(Guid? id, WriteCourseRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var code = request.Code.Trim().ToUpperInvariant();
        var errors = new Dictionary<string, string[]>();
        if (code.Length is 0 or > Course.CodeMaxLength || !CodePattern().IsMatch(code)) errors[nameof(request.Code)] = ["Invalid course code."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors[nameof(request.Name)] = ["Course name is required."];
        if (errors.Count > 0) throw Invalid(errors);
        var course = id is { } value ? await FindCourseAsync(value, ct) : Course.Create(Guid.NewGuid(), code, request.Name, request.Description);
        if (id is null) store.AddCourse(course);
        else course.Update(code, request.Name, request.Description);
        await store.SaveAsync(ct);
        return CourseResponseOf(course);
    }

    public async Task<CourseResponse> RetireCourseAsync(Guid id, RetireCourseRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var course = await FindCourseAsync(id, ct);
        course.SetRetired(request.IsRetired!.Value);
        await store.SaveAsync(ct);
        return CourseResponseOf(course);
    }

    private async Task<Course> FindCourseAsync(Guid id, CancellationToken ct) =>
        await store.FindCourseAsync(id, ct) ?? throw new AcademicFailure(404, "Course not found.");

    private static CourseResponse CourseResponseOf(Course course) => new(course.Id, course.Code, course.Name, course.Description, course.IsRetired);

    public async Task<IReadOnlyList<TeacherResponse>> TeachersAsync(CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        return (await store.TeachersAsync(ct)).Select(x => new TeacherResponse(x.Id, x.FullName, x.Email)).ToList();
    }

    public async Task<ClassPageResponse> ListAsync(ClassListQuery query, CancellationToken ct)
    {
        var visible = await access.VisibleClassesAsync(ct);
        var errors = new Dictionary<string, string[]>();
        var search = query.Search?.Trim();
        if (search?.Length > 200) errors[nameof(query.Search)] = ["Search must not exceed 200 characters."];
        if (query.Page < 1) errors[nameof(query.Page)] = ["Page must be at least 1."];
        if (query.PageSize is < 1 or > 100) errors[nameof(query.PageSize)] = ["Page size must be between 1 and 100."];
        ClassStatus? status = null;
        if (query.Status is not null)
        {
            var name = Enum.GetNames<ClassStatus>().FirstOrDefault(x => string.Equals(x, query.Status, StringComparison.OrdinalIgnoreCase));
            if (name is null) errors[nameof(query.Status)] = ["Status must be Upcoming, Ongoing or Finished."];
            else status = Enum.Parse<ClassStatus>(name);
        }
        if (errors.Count > 0) throw Invalid(errors);
        var today = AcademicCalendar.Today(clock);
        var filtered = visible.Where(x => (string.IsNullOrEmpty(search) ||
                x.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (status is null || x.GetStatus(today) == status)).OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id).ToList();
        var offset = ((long)query.Page - 1) * query.PageSize;
        var page = offset >= filtered.Count ? [] : filtered.Skip((int)offset).Take(query.PageSize).ToList();
        var courses = (await store.CoursesAsync(ct)).ToDictionary(x => x.Id);
        var teachers = (await store.TeachersAsync(ct)).ToDictionary(x => x.Id);
        return new(page.Select(x => ToResponse(x, courses.GetValueOrDefault(x.CourseId), teachers.GetValueOrDefault(x.TeacherId))).ToList(),
            query.Page, query.PageSize, filtered.Count);
    }

    private ClassResponse ToResponse(Class value, Course? course, Teacher? teacher) =>
        new(value.Id, value.Code, value.Name, value.CourseId, course?.Code ?? string.Empty, course?.Name ?? string.Empty,
            value.ClassType.ToString(), value.StartDate, value.EndDate, value.MaxStudents, value.TuitionFee, value.TeacherId,
            teacher?.FullName ?? string.Empty, value.GetStatus(AcademicCalendar.Today(clock)).ToString(), value.CreatedAt);

    private static Dictionary<string, string[]> Validate(CreateClassRequest request, out ClassType classType)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [.. errors.GetValueOrDefault(field) ?? [], message];

        var code = Class.NormalizeCode(request.Code);
        if (code.Length == 0) Add(nameof(request.Code), "Class code is required.");
        else if (code.Length > Class.CodeMaxLength) Add(nameof(request.Code), "Class code must not exceed 50 characters.");
        else if (!CodePattern().IsMatch(code))
            Add(nameof(request.Code), "Class code may only contain letters, digits, '.', '-' and '_', and must start with a letter or digit.");

        if (string.IsNullOrWhiteSpace(request.Name)) Add(nameof(request.Name), "Class name is required.");
        else if (request.Name.Trim().Length > Class.NameMaxLength) Add(nameof(request.Name), "Class name must not exceed 200 characters.");

        if (request.CourseId is null || request.CourseId == Guid.Empty) Add(nameof(request.CourseId), "Course is required.");
        if (request.TeacherId is null || request.TeacherId == Guid.Empty) Add(nameof(request.TeacherId), "Teacher is required.");

        // Chỉ nhận đúng tên (không phân biệt hoa thường), không nhận số như "1" mà Enum.TryParse vẫn chấp nhận.
        var typeName = Enum.GetNames<ClassType>()
            .FirstOrDefault(x => string.Equals(x, request.ClassType?.Trim(), StringComparison.OrdinalIgnoreCase));
        classType = typeName is null ? default : Enum.Parse<ClassType>(typeName);
        if (typeName is null) Add(nameof(request.ClassType), "Class type must be Main or Supplementary.");

        if (request.StartDate is null) Add(nameof(request.StartDate), "Start date is required.");
        if (request.EndDate is null) Add(nameof(request.EndDate), "End date is required.");
        else if (request.StartDate is not null && request.EndDate <= request.StartDate)
            Add(nameof(request.EndDate), "End date must be after start date.");

        if (request.MaxStudents is null) Add(nameof(request.MaxStudents), "Max students is required.");
        else if (request.MaxStudents <= 0) Add(nameof(request.MaxStudents), "Max students must be greater than 0.");

        if (request.TuitionFee is null) Add(nameof(request.TuitionFee), "Tuition fee is required.");
        else if (!Class.IsValidTuitionFee(request.TuitionFee.Value))
            Add(nameof(request.TuitionFee), "Tuition fee must be a whole VND amount between 0 and 999,999,999,999.");

        return errors;
    }

    private static AcademicFailure Invalid(Dictionary<string, string[]> errors) =>
        new(400, "One or more validation errors occurred.", errors);

    private static AcademicFailure DuplicateCode(string code) => new(409, $"Class code '{code}' already exists.");

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]*$")]
    private static partial Regex CodePattern();
}
