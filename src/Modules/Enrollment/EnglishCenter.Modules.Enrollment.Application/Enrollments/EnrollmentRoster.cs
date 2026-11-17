using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Enrollment.Application.Enrollments;

internal sealed class EnrollmentRoster(IEnrollmentStore store, IStudentDirectory students) : IEnrollmentRoster
{
    public async Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid classId, CancellationToken ct)
    {
        var result = new List<Guid>();
        foreach (var id in await store.ListActiveStudentIdsAsync(classId, ct))
            if (await students.IsActiveStudentAsync(id, ct)) result.Add(id);
        return result;
    }
}
