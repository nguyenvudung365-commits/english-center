using System.ComponentModel.DataAnnotations;

namespace EnglishCenter.Modules.Academic.Application.Classes;

// Kiểu nullable để thiếu trường trả 400 thay vì nhận giá trị mặc định (0, Guid.Empty, 0001-01-01).
public sealed record CreateClassRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    [Required] Guid? CourseId,
    [Required] string ClassType,
    [Required] DateOnly? StartDate,
    [Required] DateOnly? EndDate,
    [Required] int? MaxStudents,
    [Required] decimal? TuitionFee,
    [Required] Guid? TeacherId);

public sealed record ClassResponse(Guid Id, string Code, string Name, Guid CourseId, string CourseCode, string CourseName,
    string ClassType, DateOnly StartDate, DateOnly EndDate, int MaxStudents, decimal TuitionFee, Guid TeacherId,
    string TeacherName, string Status, DateTimeOffset CreatedAt);

public sealed record CourseResponse(Guid Id, string Code, string Name, string? Description);

public sealed record TeacherResponse(Guid Id, string FullName, string Email);
