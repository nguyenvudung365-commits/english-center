using EnglishCenter.Modules.Enrollment.Application.Registrations;
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
}
