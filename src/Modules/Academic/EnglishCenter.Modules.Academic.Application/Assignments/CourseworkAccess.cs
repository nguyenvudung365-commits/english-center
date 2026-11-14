using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public enum ClassAccess
{
    // Staff/Admin, Teacher đúng lớp, Student còn quyền lớp.
    Read,
    // Staff/Admin, Teacher đúng lớp.
    Manage,
    // Chỉ Teacher đúng lớp (chấm điểm). Admin/Staff không chấm thay lớp khác.
    Teach
}

/// <summary>
/// Kiểm tra quyền dùng chung cho bài tập/chấm điểm/luyện tập. Mỗi request đều kiểm tra tài khoản còn hoạt động đúng vai trò
/// và quyền lớp hiện tại (không tin token cũ hay trạng thái UI).
/// </summary>
public sealed class CourseworkAccess(IAcademicStore classes, IEnrollmentAccess enrollment, ICurrentUser current, IAccountAccess accounts)
{
    public string? Role => current.Role;
    public bool IsManager => current.Role is Roles.Admin or Roles.Staff;
    public bool IsStudent => current.Role == Roles.Student;
    public bool IsTeacher => current.Role == Roles.Teacher;

    public Guid UserId() => current.IsAuthenticated && current.UserId is { } id && id != Guid.Empty
        ? id : throw new AcademicFailure(401, "Authentication required.");

    public async Task<Guid> EnsureActiveAsync(CancellationToken ct)
    {
        var userId = UserId();
        if (current.Role is not (Roles.Admin or Roles.Staff or Roles.Teacher or Roles.Student) ||
            !await accounts.IsActiveInRoleAsync(userId, current.Role, ct))
            throw new AcademicFailure(403, "An active account with the current role is required.");
        return userId;
    }

    public async Task<Class> AuthorizeAsync(Guid classId, ClassAccess mode, CancellationToken ct)
    {
        var userId = await EnsureActiveAsync(ct);
        var item = await classes.FindClassAsync(classId, ct) ?? throw new AcademicFailure(404, "Class not found.");
        var teachesClass = IsTeacher && item.TeacherId == userId;
        if (mode == ClassAccess.Teach)
        {
            if (teachesClass) return item;
            throw new AcademicFailure(403, "Only the class teacher can grade this class.");
        }
        if (IsManager || teachesClass) return item;
        if (mode == ClassAccess.Read && IsStudent && await enrollment.IsActiveAsync(classId, userId, ct)) return item;
        throw new AcademicFailure(403, "You do not have access to this class.");
    }

    public Task<bool> StudentHasAccessAsync(Guid classId, Guid studentId, CancellationToken ct) =>
        enrollment.IsActiveAsync(classId, studentId, ct);

    public static PageResponse<T> Page<T>(IReadOnlyList<T> all, int page, int pageSize)
    {
        if (page is < 1 or > 1_000_000 || pageSize < 1)
            throw new AcademicFailure(400, "Invalid pagination.", new Dictionary<string, string[]>
            { ["page"] = ["page >= 1 và pageSize >= 1."] });
        pageSize = Math.Min(pageSize, 100);
        return new(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count, page, pageSize);
    }

    public static bool Matches(string? search, params string?[] fields) =>
        string.IsNullOrWhiteSpace(search) ||
        fields.Any(x => x?.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) == true);
}
