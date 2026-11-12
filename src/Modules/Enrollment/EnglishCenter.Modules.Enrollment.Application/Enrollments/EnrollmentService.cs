using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Enrollment.Domain.Enrollments;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Enrollment.Application.Enrollments;

public sealed record CreateEnrollmentRequest(Guid ClassId, Guid StudentId);
public sealed record EnrollmentResponse(Guid Id, Guid ClassId, Guid StudentId, bool IsActive);
public sealed class EnrollmentFailure(int status, string message) : Exception(message) { public int Status { get; } = status; }
public interface IEnrollmentStore
{
    Task<ClassEnrollment?> FindAsync(Guid id, CancellationToken ct);
    Task<ClassEnrollment?> FindAsync(Guid classId, Guid studentId, CancellationToken ct);
    Task<IReadOnlyList<ClassEnrollment>> ListAsync(Guid classId, CancellationToken ct);
    void Add(ClassEnrollment item);
    Task SaveAsync(CancellationToken ct);
}

public sealed class EnrollmentService(IEnrollmentStore store, IClassDirectory classes,
    IStudentDirectory students, ICurrentUser current, TimeProvider clock, IAccountAccess accounts) : IEnrollmentAccess
{
    private async Task ManagerAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is null || current.UserId == Guid.Empty) throw new EnrollmentFailure(401, "Authentication required.");
        if (current.Role is not (Roles.Admin or Roles.Staff)) throw new EnrollmentFailure(403, "Only staff manage class membership.");
        if (!await accounts.IsActiveInRoleAsync(current.UserId.Value, current.Role, ct))
            throw new EnrollmentFailure(403, "An active staff account with the current role is required.");
    }
    public async Task<EnrollmentResponse> CreateAsync(CreateEnrollmentRequest request, CancellationToken ct)
    {
        await ManagerAsync(ct);
        if (request.ClassId == Guid.Empty || request.StudentId == Guid.Empty) throw new EnrollmentFailure(400, "Class and student are required.");
        if (await classes.FindClassAsync(request.ClassId, ct) is null) throw new EnrollmentFailure(404, "Class not found.");
        if (!await students.IsActiveStudentAsync(request.StudentId, ct)) throw new EnrollmentFailure(400, "An active student account is required.");
        var item = await store.FindAsync(request.ClassId, request.StudentId, ct);
        if (item is null) { item = ClassEnrollment.Create(request.ClassId, request.StudentId, clock.GetUtcNow()); store.Add(item); }
        else item.SetActive(true);
        await store.SaveAsync(ct);
        return Response(item);
    }
    public async Task<IReadOnlyList<EnrollmentResponse>> ListAsync(Guid classId, CancellationToken ct)
    {
        await ManagerAsync(ct);
        if (classId == Guid.Empty) throw new EnrollmentFailure(400, "Class is required.");
        return (await store.ListAsync(classId, ct)).Select(Response).ToList();
    }
    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        await ManagerAsync(ct);
        var item = await store.FindAsync(id, ct) ?? throw new EnrollmentFailure(404, "Membership not found.");
        item.SetActive(false); await store.SaveAsync(ct);
    }
    public async Task<bool> IsActiveAsync(Guid classId, Guid studentId, CancellationToken ct) =>
        await students.IsActiveStudentAsync(studentId, ct) && (await store.FindAsync(classId, studentId, ct))?.IsActive == true;
    private static EnrollmentResponse Response(ClassEnrollment item) => new(item.Id, item.ClassId, item.StudentId, item.IsActive);
}
