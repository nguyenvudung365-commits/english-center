namespace EnglishCenter.Modules.Enrollment.Contracts;

public interface IEnrollmentRoster
{
    Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid classId, CancellationToken ct);
}
