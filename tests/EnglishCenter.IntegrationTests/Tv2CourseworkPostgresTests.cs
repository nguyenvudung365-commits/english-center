using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Practice;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// TV2 trên PostgreSQL thật: migration từ DB trống, ràng buộc và cạnh tranh ghi. Chạy khi đặt ENGLISHCENTER_TEST_POSTGRES.
public sealed class Tv2CourseworkPostgresTests : IAsyncLifetime
{
    private string server = string.Empty;
    private string database = string.Empty;
    private Guid submissionId, assignmentId, classId, teacherId;
    private string Connection => new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;

    private AcademicDbContext Context() => new(new DbContextOptionsBuilder<AcademicDbContext>()
        .UseNpgsql(Connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", AcademicSchema.Name)).Options);

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        server = value;
        database = "ec_tv2_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var db = Context();
        await db.Database.MigrateAsync();
        var teacher = Teacher.Create(Guid.NewGuid(), "GV PG", "gv.pg@example.test");
        var course = Course.Create(Guid.NewGuid(), "TV2PG", "Coursework PG");
        var item = Class.Create("TV2-PG", "Lớp PG", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 1, 1), 20, 0, teacher.Id, DateTimeOffset.UtcNow);
        var assignment = Assignment.Create(item.Id, "Bài PG", "", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1),
            AssignmentKind.InClass, "Unit 1", null, null, teacher.Id, DateTimeOffset.UtcNow);
        var submission = Submission.Create(assignment.Id, Guid.NewGuid());
        submission.ReplaceFiles([SubmissionFile.Create(submission.Id, Guid.NewGuid(), "bai.pdf", "application/pdf", 100)], DateTimeOffset.UtcNow);
        db.Teachers.Add(teacher); db.Courses.Add(course); db.Classes.Add(item); db.Assignments.Add(assignment); db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        (submissionId, assignmentId, classId, teacherId) = (submission.Id, assignment.Id, item.Id, teacher.Id);
    }

    public async Task DisposeAsync()
    {
        if (database.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task FreshMigration_HasNoPendingModelChanges_AndRoundTripsScore()
    {
        await using var db = Context();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var submission = await db.Submissions.SingleAsync(x => x.Id == submissionId);
        db.SubmissionGrades.Add(submission.Grade(7.5m, "Ổn", teacherId, 0, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        await using var check = Context();
        var stored = await check.Submissions.SingleAsync(x => x.Id == submissionId);
        Assert.Equal((7.5m, 1), (stored.Score!.Value, stored.GradeVersion));
        Assert.Equal(AssignmentKind.InClass, (await check.Assignments.SingleAsync(x => x.Id == assignmentId)).Kind);
    }

    [PostgresFact]
    public async Task ConcurrentGrading_FromTheSameVersion_HasExactlyOneWinnerAndOneHistoryRow()
    {
        await using var first = Context();
        await using var second = Context();
        var a = await first.Submissions.SingleAsync(x => x.Id == submissionId);
        var b = await second.Submissions.SingleAsync(x => x.Id == submissionId);
        first.SubmissionGrades.Add(a.Grade(6m, null, teacherId, 0, DateTimeOffset.UtcNow));
        second.SubmissionGrades.Add(b.Grade(9m, null, teacherId, 0, DateTimeOffset.UtcNow));
        var results = await Task.WhenAll(Save(first), Save(second));
        Assert.Equal(1, results.Count(x => x));
        await using var check = Context();
        Assert.Equal(1, await check.SubmissionGrades.CountAsync(x => x.SubmissionId == submissionId));
        var stored = await check.Submissions.SingleAsync(x => x.Id == submissionId);
        var history = await check.SubmissionGrades.SingleAsync(x => x.SubmissionId == submissionId);
        Assert.Equal((1, history.Score), (stored.GradeVersion, stored.Score!.Value));
    }

    [PostgresFact]
    public async Task DuplicateRecipientSnapshot_AndOutOfRangeScore_AreRejectedByTheDatabase()
    {
        var student = Guid.NewGuid();
        await using (var db = Context())
        {
            db.AssignmentRecipients.Add(AssignmentRecipient.Create(assignmentId, student, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            db.AssignmentRecipients.Add(AssignmentRecipient.Create(assignmentId, student, DateTimeOffset.UtcNow));
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        }
        await using var connection = new NpgsqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE academic.submissions SET score = 10.5 WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", submissionId);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
    }

    [PostgresFact]
    public async Task PracticeVersion_PreventsLostUpdate()
    {
        var practiceId = Guid.Empty;
        await using (var db = Context())
        {
            var item = PracticeExercise.Create(classId, "Luyện nghe", "Nội dung", teacherId, "Teacher", DateTimeOffset.UtcNow);
            db.PracticeExercises.Add(item);
            await db.SaveChangesAsync();
            practiceId = item.Id;
        }
        await using var first = Context();
        await using var second = Context();
        (await first.PracticeExercises.SingleAsync(x => x.Id == practiceId)).Update("A", "A", teacherId, DateTimeOffset.UtcNow);
        (await second.PracticeExercises.SingleAsync(x => x.Id == practiceId)).Update("B", "B", teacherId, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static async Task<bool> Save(DbContext db)
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException) { return false; }
    }
}
