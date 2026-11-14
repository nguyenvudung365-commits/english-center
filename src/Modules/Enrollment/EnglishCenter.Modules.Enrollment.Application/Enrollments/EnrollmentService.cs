using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Enrollment.Application.Enrollments;

public sealed record CreateEnrollmentRequest(Guid ClassId, Guid StudentId);
public sealed record EnrollmentStatusRequest(bool IsActive);

// Coursework access requires an existing financial registration; it never creates a seat or invoice.
public sealed class EnrollmentService(IEnrollmentStore store, Registrations.EnrollmentService registrations,
    IClassDirectory classes, IStudentDirectory students) : IEnrollmentAccess
{
    public async Task<EnrollmentResponse> CreateAsync(CreateEnrollmentRequest request, CancellationToken ct)
    {
        await registrations.EnsureManagerAsync(ct);
        if (request.ClassId == Guid.Empty || request.StudentId == Guid.Empty)
            throw new EnrollmentFailure(400, "Class and student are required.");
        if (await classes.FindClassAsync(request.ClassId, ct) is null)
            throw new EnrollmentFailure(404, "Class not found.");
        if (!await students.IsActiveStudentAsync(request.StudentId, ct))
            throw new EnrollmentFailure(400, "An active student account is required.");

        var existing = await store.FindAsync(request.ClassId, request.StudentId, ct)
            ?? throw new EnrollmentFailure(409,
                "Create a financial registration through /api/enrollment/enrollments with approved violation limits before granting coursework access.");
        if (existing.Status != "Active")
            throw new EnrollmentFailure(409, "An active financial registration is required for coursework access.");
        return await registrations.SetActiveAsync(existing.Id, true, ct);
    }

    public async Task<IReadOnlyList<EnrollmentResponse>> ListAsync(Guid classId, CancellationToken ct)
    {
        await registrations.EnsureManagerAsync(ct);
        if (classId == Guid.Empty) throw new EnrollmentFailure(400, "Class is required.");
        return (await store.ListAsync(classId, ct)).Select(Registrations.EnrollmentService.Response).ToList();
    }

    public async Task<EnrollmentResponse> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await registrations.EnsureManagerAsync(ct);
        var item = await store.FindAsync(id, ct) ?? throw new EnrollmentFailure(404, "Enrollment not found.");
        if (active && !await students.IsActiveStudentAsync(item.StudentId, ct))
            throw new EnrollmentFailure(400, "An active student account is required.");
        return await registrations.SetActiveAsync(id, active, ct);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct) => await SetActiveAsync(id, false, ct);

    public async Task<bool> IsActiveAsync(Guid classId, Guid studentId, CancellationToken ct) =>
        await students.IsActiveStudentAsync(studentId, ct) &&
        await store.FindAsync(classId, studentId, ct) is { Status: "Active", IsActive: true };
}
