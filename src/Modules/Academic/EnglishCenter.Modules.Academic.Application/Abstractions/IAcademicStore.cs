using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;

namespace EnglishCenter.Modules.Academic.Application.Abstractions;

public interface IAcademicStore
{
    Task<bool> ClassCodeExistsAsync(string normalizedCode, CancellationToken ct);
    Task<Class?> FindClassAsync(Guid id, CancellationToken ct);
    Task<Course?> FindCourseAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Course>> CoursesAsync(CancellationToken ct);
    Task<Teacher?> FindTeacherAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Teacher>> TeachersAsync(CancellationToken ct);
    void AddClass(Class academicClass);
    void AddTeacher(Teacher teacher);

    /// <summary>Lưu thay đổi; vi phạm ràng buộc unique được đổi thành <see cref="AcademicFailure"/> 409.</summary>
    Task SaveAsync(CancellationToken ct);
}
