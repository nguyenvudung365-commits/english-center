using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Classes;

public sealed class ClassAccess(ICurrentUser current, IAccountAccess accounts, IEnrollmentAccess enrollment,
    IClassDirectory directory, IAcademicStore store)
{
    public async Task<Guid> RequireUserAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is not { } id || id == Guid.Empty)
            throw new AcademicFailure(401, "Authentication required.");
        if (current.Role is not (Roles.Admin or Roles.Staff or Roles.Teacher or Roles.Student) ||
            !await accounts.IsActiveInRoleAsync(id, current.Role, ct))
            throw new AcademicFailure(403, "An active account with the current role is required.");
        return id;
    }

    public async Task RequireManagerAsync(CancellationToken ct)
    {
        await RequireUserAsync(ct);
        if (current.Role is not (Roles.Admin or Roles.Staff))
            throw new AcademicFailure(403, "Only Staff and Admin manage classes and sessions.");
    }

    public async Task RequireStudentAsync(Guid studentId, CancellationToken ct)
    {
        var id = await RequireUserAsync(ct);
        if (current.Role != Roles.Student || studentId != id)
            throw new AcademicFailure(403, "Only the current student can read their classes.");
    }

    public async Task RequireReadAsync(Guid classId, CancellationToken ct)
    {
        var id = await RequireUserAsync(ct);
        var item = await directory.FindClassAsync(classId, ct) ?? throw new AcademicFailure(404, "Class not found.");
        if (!await CanReadAsync(item.Id, item.TeacherId, id, ct))
            throw new AcademicFailure(403, "You do not have access to this class.");
    }

    public async Task<IReadOnlyList<Class>> VisibleClassesAsync(CancellationToken ct)
    {
        var id = await RequireUserAsync(ct);
        var visible = new List<Class>();
        // shortcut: sequential per-class enrollment checks; use a bulk contract if measured volume requires it.
        foreach (var item in await store.ClassesAsync(ct))
            if (await CanReadAsync(item.Id, item.TeacherId, id, ct)) visible.Add(item);
        return visible;
    }

    private Task<bool> CanReadAsync(Guid classId, Guid teacherId, Guid userId, CancellationToken ct) => current.Role switch
    {
        Roles.Admin or Roles.Staff => Task.FromResult(true),
        Roles.Teacher => Task.FromResult(teacherId == userId),
        Roles.Student => enrollment.IsActiveAsync(classId, userId, ct),
        _ => Task.FromResult(false)
    };
}
