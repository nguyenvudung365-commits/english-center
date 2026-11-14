namespace EnglishCenter.Modules.Enrollment.Contracts;

public interface IEnrollmentAccess
{
    Task<bool> IsActiveAsync(Guid classId, Guid studentId, CancellationToken ct);
}
