using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Infrastructure;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class SubmissionPersistenceTests : IAsyncLifetime
{
    private readonly string connectionString = $"Data Source=submission_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection keeper = null!;
    private Guid assignmentId;
    private Guid submissionId;
    private Guid revision;
    private Guid originalFile;
    private AcademicDbContext Context() => new(new DbContextOptionsBuilder<AcademicDbContext>().UseSqlite(connectionString).Options);

    public async Task InitializeAsync()
    {
        keeper = new(connectionString); await keeper.OpenAsync();
        await using var db = Context(); await db.Database.EnsureCreatedAsync();
        var teacher = Teacher.Create(Guid.NewGuid(), "Teacher", "teacher@test.example");
        var course = Course.Create(Guid.NewGuid(), "MF", "Multi-file");
        var classroom = Class.Create("MF", "Multi-file", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 1, 1), 20, 0, teacher.Id, DateTimeOffset.UtcNow);
        var assignment = Assignment.Create(classroom.Id, "Work", "", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var submission = Submission.Create(assignment.Id, Guid.NewGuid());
        originalFile = Guid.NewGuid();
        submission.ReplaceFiles([SubmissionFile.Create(submission.Id, originalFile, "original.pdf", "application/pdf", 1024)], DateTimeOffset.UtcNow);
        db.Teachers.Add(teacher); db.Courses.Add(course); db.Classes.Add(classroom); db.Assignments.Add(assignment); db.Submissions.Add(submission);
        await db.SaveChangesAsync(); assignmentId = assignment.Id; submissionId = submission.Id; revision = submission.Revision;
    }
    public async Task DisposeAsync() => await keeper.DisposeAsync();

    [Fact]
    public async Task TwoContextsLoadedAtSameRevision_OnlyOneReplacementCommits()
    {
        await using var first = Context(); await using var second = Context();
        var a = await first.Submissions.Include(x => x.Files).SingleAsync(x => x.Id == submissionId);
        var b = await second.Submissions.Include(x => x.Files).SingleAsync(x => x.Id == submissionId);
        Assert.Equal(a.Revision, b.Revision);
        var winnerFile = Guid.NewGuid();
        a.ReplaceFiles([SubmissionFile.Create(a.Id, winnerFile, "winner.pdf", "application/pdf", 20)], DateTimeOffset.UtcNow);
        b.ReplaceFiles([SubmissionFile.Create(b.Id, Guid.NewGuid(), "loser.pdf", "application/pdf", 20)], DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = Context();
        var actual = await verify.Submissions.Include(x => x.Files).SingleAsync();
        Assert.Equal(a.Revision, actual.Revision); Assert.Equal(winnerFile, Assert.Single(actual.Files).FileId);
    }

    [Fact]
    public async Task FirstSubmitCollision_UniqueConstraintPreventsSecondAggregateAndItsFiles()
    {
        await using var first = Context(); await using var second = Context();
        var student = Guid.NewGuid();
        var a = Submission.Create(assignmentId, student); var b = Submission.Create(assignmentId, student);
        a.ReplaceFiles([SubmissionFile.Create(a.Id, Guid.NewGuid(), "first.pdf", "application/pdf", 20)], DateTimeOffset.UtcNow);
        b.ReplaceFiles([SubmissionFile.Create(b.Id, Guid.NewGuid(), "second.pdf", "application/pdf", 20)], DateTimeOffset.UtcNow);
        first.Submissions.Add(a); second.Submissions.Add(b);
        await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        await using var verify = Context();
        Assert.Equal(a.Id, (await verify.Submissions.SingleAsync(x => x.StudentId == student)).Id);
        Assert.False(await verify.Set<SubmissionFile>().AnyAsync(x => x.SubmissionId == b.Id));
    }

    [Fact]
    public async Task AttachmentInsertFailure_RollsBackRevisionDeletionsAndAllNewAttachments()
    {
        await using var db = Context();
        // Force a real database failure while writing the aggregate, not a pre-save mock exception.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_attachment BEFORE INSERT ON submission_files
            WHEN NEW.file_name = 'reject.pdf'
            BEGIN SELECT RAISE(ABORT, 'test attachment insert failure'); END;
            """);
        var item = await db.Submissions.Include(x => x.Files).SingleAsync();
        item.ReplaceFiles([SubmissionFile.Create(item.Id, Guid.NewGuid(), "accepted.pdf", "application/pdf", 20),
            SubmissionFile.Create(item.Id, Guid.NewGuid(), "reject.pdf", "application/pdf", 20)], DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        await using var verify = Context();
        var actual = await verify.Submissions.Include(x => x.Files).SingleAsync();
        Assert.Equal(revision, actual.Revision);
        Assert.Equal(originalFile, Assert.Single(actual.Files).FileId);
        Assert.Equal(1, await verify.Set<SubmissionFile>().CountAsync());
    }

    [Fact]
    public void PostgreSqlMigrations_MatchModelsAndKeepForeignKeysWithinAcademicSchema()
    {
        using var academic = new AcademicDesignTimeFactory().CreateDbContext([]);
        using var enrollment = new EnrollmentDesignTimeFactory().CreateDbContext([]);
        Assert.False(academic.Database.HasPendingModelChanges());
        Assert.False(enrollment.Database.HasPendingModelChanges());
        var sql = academic.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("academic.submission_files", sql);
        Assert.Contains("academic.submissions", sql);
        Assert.DoesNotContain("REFERENCES identity.", sql);
        Assert.DoesNotContain("REFERENCES media_notification.", sql);
        var rosterSql = enrollment.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("enrollment.class_enrollments", rosterSql);
        Assert.DoesNotContain("REFERENCES academic.", rosterSql);
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(6, 1024)]
    [InlineData(5, 0)]
    [InlineData(5, 52428801)]
    public void InvalidSubmissionConfiguration_FailsAtStartup(int count, long total)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Academic:Submissions:MaxFiles"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Academic:Submissions:MaxTotalBytes"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAcademicInfrastructure(config));
    }

    [Fact]
    public void LoweredSubmissionConfiguration_IsRegisteredWithoutRaisingApprovedCaps()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Academic:Submissions:MaxFiles"] = "2", ["Academic:Submissions:MaxTotalBytes"] = "1024"
        }).Build();
        using var services = new ServiceCollection().AddAcademicInfrastructure(config).BuildServiceProvider();
        Assert.Equal(new SubmissionLimits(2, 1024), services.GetRequiredService<SubmissionLimits>());
    }
}
