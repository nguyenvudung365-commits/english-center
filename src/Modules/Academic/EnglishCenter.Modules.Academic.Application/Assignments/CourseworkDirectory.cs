using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Academic.Domain.Assignments;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

// Hợp đồng truy vấn cho module khác: chỉ đọc dữ liệu Academic, không yêu cầu người dùng hiện tại.
public sealed class CourseworkDirectory(IAssignmentStore store) : IAssignmentDirectory, ICourseworkMetrics
{
    public async Task<AssignmentSummary?> FindAsync(Guid assignmentId, CancellationToken ct)
    {
        var item = await store.FindAssignmentAsync(assignmentId, ct);
        return item is null || !item.IsVisibleToStudents ? null
            : new(item.Id, item.ClassId, item.Title, item.Kind.ToString(), item.OpensAtUtc, item.ClosesAtUtc, item.PublishedAt);
    }

    public async Task<IReadOnlyList<Guid>> RecipientsAsync(Guid assignmentId, CancellationToken ct) =>
        (await store.RecipientsAsync(assignmentId, ct)).Select(x => x.StudentId).Order().ToList();

    public async Task<StudentCourseworkMetrics> ForStudentAsync(Guid studentId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        if (studentId == Guid.Empty || fromUtc >= toUtc) throw new ArgumentException("A student and a valid period are required.");
        var obligations = await store.StudentObligationsAsync(studentId, ct);
        var assignments = (await store.AssignmentsByIdAsync(obligations.Select(x => x.AssignmentId).ToList(), ct))
            .Where(x => x.ClosesAtUtc >= fromUtc && x.ClosesAtUtc < toUtc).ToDictionary(x => x.Id);
        var inPeriod = obligations.Where(x => assignments.ContainsKey(x.AssignmentId)).ToList();
        var submissions = await store.StudentSubmissionsAsync(studentId, assignments.Keys.ToList(), ct);
        var graded = submissions.Where(x => x.Score is not null).ToList();
        decimal? average = graded.Count == 0 ? null : Math.Round(graded.Average(x => x.Score!.Value), 1, MidpointRounding.AwayFromZero);
        return new(studentId, fromUtc, toUtc, inPeriod.Count, submissions.Count, graded.Count,
            inPeriod.Count(x => x.Outcome == ObligationOutcome.Missed), average);
    }
}
