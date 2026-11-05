using System.Text.RegularExpressions;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;

namespace EnglishCenter.Modules.Academic.Application.Classes;

public sealed partial class ClassService(IAcademicStore store, IEventBus events, TimeProvider clock)
{
    public async Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        var errors = Validate(request, out var classType);
        if (errors.Count > 0) throw Invalid(errors);

        var course = await store.FindCourseAsync(request.CourseId!.Value, ct);
        if (course is null) errors[nameof(request.CourseId)] = ["Course does not exist."];
        var teacher = await store.FindTeacherAsync(request.TeacherId!.Value, ct);
        if (teacher is null) errors[nameof(request.TeacherId)] = ["Teacher does not exist or is not synchronized to Academic yet."];
        if (errors.Count > 0) throw Invalid(errors);

        var code = Class.NormalizeCode(request.Code);
        if (await store.ClassCodeExistsAsync(code, ct)) throw DuplicateCode(code);

        var created = Class.Create(code, request.Name, course!.Id, classType, request.StartDate!.Value, request.EndDate!.Value,
            request.MaxStudents!.Value, request.TuitionFee!.Value, teacher!.Id, clock.GetUtcNow());
        store.AddClass(created);
        try
        {
            await store.SaveAsync(ct);
        }
        catch (AcademicFailure failure) when (failure.Status == 409)
        {
            // Hai request cùng mã vượt qua bước kiểm tra trước; unique index chặn request thứ hai.
            throw DuplicateCode(code);
        }

        await events.PublishAsync(new ClassCreatedIntegrationEvent(created.Id, created.Code, created.StartDate,
            created.EndDate, created.MaxStudents, created.TuitionFee, created.ClassType.ToString()), ct);
        return ToResponse(created, course, teacher);
    }

    public async Task<ClassResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var found = await store.FindClassAsync(id, ct) ?? throw new AcademicFailure(404, "Class not found.");
        var course = await store.FindCourseAsync(found.CourseId, ct);
        var teacher = await store.FindTeacherAsync(found.TeacherId, ct);
        return ToResponse(found, course, teacher);
    }

    public async Task<IReadOnlyList<CourseResponse>> CoursesAsync(CancellationToken ct) =>
        (await store.CoursesAsync(ct)).Select(x => new CourseResponse(x.Id, x.Code, x.Name, x.Description)).ToList();

    public async Task<IReadOnlyList<TeacherResponse>> TeachersAsync(CancellationToken ct) =>
        (await store.TeachersAsync(ct)).Select(x => new TeacherResponse(x.Id, x.FullName, x.Email)).ToList();

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
