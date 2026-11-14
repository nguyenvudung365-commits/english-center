using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class MonitoringStore(EnrollmentDbContext db) : IMonitoringStore
{
    private static DateTime Precision(DateTime at) => new(at.Ticks / 10 * 10, DateTimeKind.Utc);

    public Task<MonitoringResult> SetLimitsAsync(Guid id, LimitsRequest request, DateTime now, CancellationToken ct) =>
        ChangeAsync(db.Enrollments.Where(x => x.Id == id), now, (x, _) =>
        {
            x.SetLimits(request.MaxAbsences!.Value, request.MaxLates!.Value, request.MaxSkippedAssignments!.Value);
            return Task.CompletedTask;
        }, ct);

    public Task<MonitoringResult> RecordAttendanceAsync(Guid session, Guid student, Guid classId, string status,
        DateTime at, DateTime now, CancellationToken ct) => ChangeAsync(
        db.Enrollments.Where(x => x.StudentId == student && x.ClassId == classId), now, async (x, token) =>
        {
            at = Precision(at);
            var previous = await db.AttendanceSnapshots.SingleOrDefaultAsync(y => y.SessionId == session && y.StudentId == student, token);
            if (previous is null) db.AttendanceSnapshots.Add(new(session, student, x.Id, status, at));
            else
            {
                if (previous.EnrollmentId != x.Id) throw new InvalidOperationException("Session/student belongs to another enrollment.");
                if (at == previous.RecordedAt && status != previous.Status)
                    throw new InvalidOperationException("Conflicting attendance at the same timestamp.");
                if (at > previous.RecordedAt) previous.Set(status, at);
            }
        }, ct);

    public Task<MonitoringResult> RecordMissedAssignmentAsync(Guid assignment, Guid student, Guid classId,
        DateTime at, DateTime now, CancellationToken ct) => ChangeAsync(
        db.Enrollments.Where(x => x.StudentId == student && x.ClassId == classId), now, async (x, token) =>
        {
            if (!await db.MissedAssignments.AnyAsync(y => y.AssignmentId == assignment && y.EnrollmentId == x.Id, token))
                db.MissedAssignments.Add(new(assignment, x.Id, Precision(at)));
        }, ct);

    private async Task<MonitoringResult> ChangeAsync(IQueryable<Registration> query, DateTime now,
        Func<Registration, CancellationToken, Task> change, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Khóa bản ghi ghi danh trước khi đọc snapshot/cập nhật giới hạn. Mọi writer dùng cùng khóa.
        query = query.Where(x => x.Status == "Active");
        if (await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.AbsenceCount, x => x.AbsenceCount), ct) == 0)
            throw new EnrollmentFailure(404, "Active enrollment not found; replay after enrollment synchronization.");
        var enrollment = await query.SingleAsync(ct);
        await change(enrollment, ct);
        await db.SaveChangesAsync(ct);
        var snapshots = db.AttendanceSnapshots.Where(x => x.EnrollmentId == enrollment.Id);
        enrollment.SetCounts(await snapshots.CountAsync(x => x.Status == "AbsentUnexcused", ct),
            await snapshots.CountAsync(x => x.Status == "Late" || x.Status == "LateOver20Min", ct),
            await db.MissedAssignments.CountAsync(x => x.EnrollmentId == enrollment.Id, ct));
        var existing = await db.Warnings.Where(x => x.EnrollmentId == enrollment.Id).Select(x => x.WarningType).ToListAsync(ct);
        var warnings = new List<Warning>();
        void Check(string type, int used, int max, string label)
        {
            if (used > max && !existing.Contains(type))
                warnings.Add(new(enrollment.Id, type, $"{label}: {used}, vượt giới hạn {max}.", Precision(now)));
        }
        Check("Absence", enrollment.AbsenceCount, enrollment.MaxAbsences, "Số buổi nghỉ không phép");
        Check("Late", enrollment.LateCount, enrollment.MaxLates, "Số lần đi trễ");
        Check("SkippedAssignment", enrollment.SkippedAssignmentCount, enrollment.MaxSkippedAssignments, "Số bài bỏ");
        db.Warnings.AddRange(warnings);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(enrollment, warnings);
    }

    public async Task<PageResult<WarningResponse>> WarningsAsync(Guid? student, Guid? enrollment, int page, int size, CancellationToken ct)
    {
        var query = from w in db.Warnings.AsNoTracking()
                    join e in db.Enrollments.AsNoTracking() on w.EnrollmentId equals e.Id
                    select new { Warning = w, Enrollment = e };
        if (student.HasValue) query = query.Where(x => x.Enrollment.StudentId == student);
        if (enrollment.HasValue) query = query.Where(x => x.Enrollment.Id == enrollment);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Warning.IssuedAt).ThenBy(x => x.Warning.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new WarningResponse(x.Warning.Id, x.Enrollment.Id, x.Enrollment.StudentId, x.Enrollment.ClassId,
                x.Warning.WarningType, x.Warning.Message, x.Warning.IssuedAt)).ToListAsync(ct);
        return new(items, total, page, size);
    }
    public Task<WarningResponse?> FindWarningAsync(Guid id, CancellationToken ct) =>
        (from w in db.Warnings.AsNoTracking()
         join e in db.Enrollments.AsNoTracking() on w.EnrollmentId equals e.Id
         where w.Id == id
         select new WarningResponse(w.Id, e.Id, e.StudentId, e.ClassId, w.WarningType, w.Message, w.IssuedAt)).SingleOrDefaultAsync(ct);
}
