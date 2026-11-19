using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class EnrollmentStore(EnrollmentDbContext db) : IEnrollmentStore
{
    public async Task AddStudentAsync(Student student, CancellationToken ct)
    {
        if (await db.Students.AnyAsync(x => x.StudentId == student.StudentId, ct)) return;
        db.Students.Add(student);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.Students.AnyAsync(x => x.StudentId == student.StudentId, ct)) throw;
        }
    }

    public async Task AddClassAsync(ClassSnapshot value, CancellationToken ct)
    {
        var existing = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == value.ClassId, ct);
        // Replay chỉ kiểm tra payload gốc; không ghi đè snapshot hay đặt lại CurrentStudents.
        if (existing is not null) { CheckSnapshot(existing, value); return; }
        db.Classes.Add(value);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == value.ClassId, ct);
            if (existing is null) throw;
            CheckSnapshot(existing, value);
        }
    }

    private static void CheckSnapshot(ClassSnapshot x, ClassSnapshot y)
    {
        if (x.ClassCode != y.ClassCode || x.StartDate != y.StartDate || x.EndDate != y.EndDate ||
            x.MaxStudents != y.MaxStudents || x.TuitionFee != y.TuitionFee)
            throw new InvalidOperationException($"Conflicting ClassCreated for {x.ClassId}.");
    }

    public Task<Registration?> FindAsync(Guid id, CancellationToken ct) => db.Enrollments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PageResult<MyEnrollmentResponse>> ListMineAsync(Guid studentId, int page, int size, CancellationToken ct)
    {
        var query = from enrollment in db.Enrollments.AsNoTracking()
                    join snapshot in db.Classes.AsNoTracking() on enrollment.ClassId equals snapshot.ClassId
                    where enrollment.StudentId == studentId
                    select new { Enrollment = enrollment, Class = snapshot };
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Enrollment.EnrolledAt).ThenBy(x => x.Enrollment.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new MyEnrollmentResponse(x.Enrollment.Id, x.Class.ClassId, x.Class.ClassCode,
                x.Class.StartDate, x.Class.EndDate, x.Enrollment.Status, x.Enrollment.EnrolledAt, x.Enrollment.TuitionFee,
                x.Enrollment.Status == "Active" && x.Enrollment.IsActive)).ToListAsync(ct);
        return new(items, total, page, size);
    }

    public Task<Registration?> FindAsync(Guid classId, Guid studentId, CancellationToken ct) =>
        db.Enrollments.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == classId && x.StudentId == studentId, ct);

    public async Task<IReadOnlyList<Registration>> ListAsync(Guid classId, CancellationToken ct) =>
        await db.Enrollments.AsNoTracking().Where(x => x.ClassId == classId).OrderBy(x => x.Id).Take(200).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListActiveStudentIdsAsync(Guid classId, CancellationToken ct) =>
        await db.Enrollments.AsNoTracking().Where(x => x.ClassId == classId && x.Status == "Active" && x.IsActive)
            .Select(x => x.StudentId).Distinct().OrderBy(x => x).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> TransferredClassIdsAsync(Guid studentId, CancellationToken ct) =>
        await db.Enrollments.Where(x => x.StudentId == studentId && x.Status == "Cancelled" &&
            db.Enrollments.Any(target => target.TransferredFromEnrollmentId == x.Id && target.StudentId == studentId))
            .Select(x => x.ClassId).ToListAsync(ct);

    public async Task<Registration> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var changed = await db.Enrollments.Where(x => x.Id == id && (!active || x.Status == "Active"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, active), ct);
        if (changed == 0)
        {
            if (await FindAsync(id, ct) is null) throw new EnrollmentFailure(404, "Enrollment not found.");
            throw new EnrollmentFailure(409, "Cancelled enrollment cannot grant coursework access.");
        }
        return (await FindAsync(id, ct))!;
    }

    public Task<int> CountCompletedClassesAsync(Guid studentId, DateOnly today, CancellationToken ct) =>
        (from enrollment in db.Enrollments
         join snapshot in db.Classes on enrollment.ClassId equals snapshot.ClassId
         where enrollment.StudentId == studentId && enrollment.Status == "Active" && snapshot.EndDate < today
         select enrollment.Id).CountAsync(ct);

    public async Task<Registration> EnrollAsync(EnrollRequest request, DateTime now, DateOnly today, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Students.AnyAsync(x => x.StudentId == request.StudentId, ct))
            throw new EnrollmentFailure(400, "Student does not exist or is not synchronized yet.");
        var snapshot = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == request.ClassId, ct)
            ?? throw new EnrollmentFailure(400, "Class does not exist or is not synchronized yet.");
        if (today > snapshot.EndDate) throw new EnrollmentFailure(400, "Class has ended.");
        if (await db.Enrollments.AnyAsync(x => x.StudentId == request.StudentId && x.ClassId == request.ClassId, ct))
            throw new EnrollmentFailure(409, "Student is already enrolled in this class.");

        // UPDATE lấy row lock trên PostgreSQL; điều kiện được đánh giá lại sau khi request khác commit.
        var reserved = await db.Classes.Where(x => x.ClassId == request.ClassId && x.CurrentStudents < x.MaxStudents)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentStudents, x => x.CurrentStudents + 1), ct);
        if (reserved == 0) throw new EnrollmentFailure(409, "Class is full.");
        var value = new Registration(request.StudentId, request.ClassId, snapshot.TuitionFee, now,
            request.MaxAbsences!.Value, request.MaxLates!.Value, request.MaxSkippedAssignments!.Value);
        db.Enrollments.Add(value);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw new EnrollmentFailure(409, "Student is already enrolled in this class.");
        }
        return value;
    }

    public async Task<Registration> TransferAsync(Guid sourceId, Guid targetClassId, DateTime now, DateOnly today, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var source = await FindAsync(sourceId, ct) ?? throw new EnrollmentFailure(404, "Enrollment not found.");
        if (source.ClassId == targetClassId) throw new EnrollmentFailure(400, "Choose a different class.");
        var target = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.ClassId == targetClassId, ct)
            ?? throw new EnrollmentFailure(404, "Destination class not found.");
        if (target.EndDate < today) throw new EnrollmentFailure(400, "Destination class has ended.");
        if (await db.Enrollments.AnyAsync(x => x.StudentId == source.StudentId && x.ClassId == targetClassId, ct))
            throw new EnrollmentFailure(409, "Student already has an enrollment in the destination class.");

        var cancelled = await db.Enrollments.Where(x => x.Id == sourceId && x.Status == "Active")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Cancelled").SetProperty(x => x.IsActive, false), ct);
        if (cancelled == 0) throw new EnrollmentFailure(409, "Source enrollment is no longer active.");
        source = (await FindAsync(sourceId, ct))!;

        // Use one lock order for both classes even when transfers run in opposite directions.
        foreach (var id in new[] { source.ClassId, targetClassId }.Order())
        {
            var delta = id == targetClassId ? 1 : -1;
            var updated = await db.Classes.Where(x => x.ClassId == id &&
                    (delta == 1 ? x.CurrentStudents < x.MaxStudents : x.CurrentStudents > 0))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentStudents, x => x.CurrentStudents + delta), ct);
            if (updated == 0) throw new EnrollmentFailure(409, delta == 1 ? "Destination class is full." : "Source seat could not be released.");
        }

        var value = new Registration(source.StudentId, target.ClassId, target.TuitionFee, now,
            source.MaxAbsences, source.MaxLates, source.MaxSkippedAssignments, source.Id);
        db.Enrollments.Add(value);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw new EnrollmentFailure(409, "Student already has an enrollment in the destination class.");
        }
        return value;
    }
}
