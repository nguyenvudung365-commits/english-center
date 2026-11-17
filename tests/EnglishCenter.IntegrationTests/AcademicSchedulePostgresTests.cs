using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Opt-in, real PostgreSQL. Never migrate or clear a shared/application database.
public sealed class AcademicSchedulePostgresTests : IAsyncLifetime
{
    private const string BaselineMigration = "20261112120100_AddTeacherProfileVersion";
    private string server = "";
    private string database = "";
    private string Connection => new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        server = value;
        var name = "ec_schedule_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        database = name;
    }

    public async Task DisposeAsync()
    {
        if (database.Length == 0) return;
        using var ownConnection = new NpgsqlConnection(Connection);
        NpgsqlConnection.ClearPool(ownConnection);
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task FreshMigration_CreatesSessions_RepeatIsSafe_AndModelMatches()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal("academic.sessions", await ScalarAsync("SELECT to_regclass('academic.sessions')::text"));
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal("3", await ScalarAsync("SELECT count(*)::text FROM academic.classes"));
    }

    [PostgresFact]
    public async Task UpgradeFromLastBaseline_PreservesExistingAcademicRows_AndCreatesSessions()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync(BaselineMigration);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            // Schema cũ chưa có cột chấm điểm: chèn thẳng bằng SQL thay vì model hiện tại.
            var (a, s, f) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO academic.assignments (id, class_id, title, description, opens_at_utc, closes_at_utc)
                VALUES ('{a}', '{fixture.First}', 'Preserved assignment', 'Synthetic', now(), now() + interval '1 day');
                INSERT INTO academic.submissions (id, assignment_id, student_id, revision, submitted_at)
                VALUES ('{s}', '{a}', '{fixture.Users["student"]}', '{Guid.NewGuid()}', now());
                INSERT INTO academic.submission_files (id, submission_id, file_id, file_name, content_type, size_bytes)
                VALUES ('{Guid.NewGuid()}', '{s}', '{f}', 'x.pdf', 'application/pdf', 12);
                """);
            await db.SaveChangesAsync();
        }
        var tables = new[] { "courses", "teachers", "classes", "class_students", "assignments", "submissions", "submission_files" };
        var before = new Dictionary<string, string?>();
        foreach (var table in tables) before[table] = await TableSnapshotAsync(table);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        Assert.Equal("academic.sessions", await ScalarAsync("SELECT to_regclass('academic.sessions')::text"));
        foreach (var table in tables) Assert.Equal(before[table], await TableSnapshotAsync(table));
        Assert.Equal("0", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));
    }

    [PostgresFact]
    public Task SameTeacherDifferentClasses_ConcurrentOverlap_OnlyOneBookingCommits() => RaceAsync("overlap");

    [PostgresFact]
    public Task SameTeacherDifferentClasses_ConcurrentAdjacent_BothCommit() => RaceAsync("adjacent");

    [PostgresFact]
    public Task DifferentTeachers_ConcurrentIdenticalWindow_BothCommit() => RaceAsync("different-teachers");

    [PostgresFact]
    public async Task Precision_RejectsSubmicrosecondCreateAndRange_WithField400_AndNoWrites()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync(); await fixture.AsAsync("staff");
        foreach (var (start, end, fields) in new[]
        {
            ("2027-01-01T10:00:00.0000001Z", "2027-01-01T10:00:00.0000002Z", new[] { "StartsAtUtc" }),
            ("2027-01-01T10:00:00.0000001Z", "2027-01-01T11:00:00Z", new[] { "StartsAtUtc" }),
            ("2027-01-01T10:00:00Z", "2027-01-01T11:00:00.0000001Z", new[] { "EndsAtUtc" })
        })
        {
            using var response = await fixture.Client.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl, fixture.Body(start: start, end: end));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await AcademicScheduleFixture.JsonAsync(response);
            foreach (var field in fields) Assert.NotEmpty(body.GetProperty("errors").GetProperty(field).EnumerateArray());
            Assert.Equal("0", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));
        }
        foreach (var (from, to, field) in new[]
        {
            ("2027-01-01T10:00:00.0000001Z", "2027-01-01T11:00:00Z", "FromUtc"),
            ("2027-01-01T10:00:00Z", "2027-01-01T11:00:00.0000001Z", "ToUtc")
        })
        {
            using var response = await fixture.Client.GetAsync(AcademicScheduleFixture.Range(from, to));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await AcademicScheduleFixture.JsonAsync(response);
            Assert.NotEmpty(body.GetProperty("errors").GetProperty(field).EnumerateArray());
        }
        // Direct store callers must not silently truncate range bounds through Npgsql either.
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var instant = new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        foreach (var submicroStart in new[] { true, false })
        {
            var failure = await Assert.ThrowsAsync<AcademicFailure>(() => store.RangeAsync(instant.AddTicks(submicroStart ? 1 : 0),
                instant.AddHours(1).AddTicks(submicroStart ? 0 : 1), new[] { fixture.First }, default));
            Assert.Equal(400, failure.Status);
            Assert.Contains(submicroStart ? "fromUtc" : "toUtc", failure.Errors!.Keys);
        }
    }

    [PostgresFact]
    public async Task Precision_MicrosecondAdjacentBookings_HalfOpenRanges_AndAllTimestampReadbackMatch()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync(); await fixture.AsAsync("staff");
        // Freeze after login: authentication keeps its real wall-clock validity.
        fixture.Clock.UtcNow = DateTimeOffset.Parse("2026-11-13T00:00:00.1234567Z", CultureInfo.InvariantCulture);
        var first = await Create("2027-01-01T10:00:00.0000010Z", "2027-01-01T10:00:00.0000020Z");
        var second = await Create("2027-01-01T10:00:00.000002Z", "2027-01-01T10:00:00.000003Z");
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        foreach (var (start, end, expected) in new[]
        {
            ("2027-01-01T10:00:00.000000Z", "2027-01-01T10:00:00.000001Z", (Guid?)null),
            ("2027-01-01T10:00:00.000001Z", "2027-01-01T10:00:00.000002Z", (Guid?)first.GetProperty("id").GetGuid()),
            ("2027-01-01T10:00:00.0000020Z", "2027-01-01T10:00:00.0000030Z", (Guid?)second.GetProperty("id").GetGuid()),
            ("2027-01-01T10:00:00.000003Z", "2027-01-01T10:00:00.000004Z", (Guid?)null)
        })
        {
            using var response = await fixture.Client.GetAsync(AcademicScheduleFixture.Range(start, end));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = (await AcademicScheduleFixture.JsonAsync(response)).EnumerateArray().ToArray();
            if (expected is null) Assert.Empty(values);
            else Assert.Equal(expected.Value, Assert.Single(values).GetProperty("id").GetGuid());
        }
        Assert.Equal("2", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));

        async Task<JsonElement> Create(string start, string end)
        {
            using var response = await fixture.Client.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl, fixture.Body(start: start, end: end));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var posted = await AcademicScheduleFixture.JsonAsync(response);
            Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), posted.GetProperty("startsAtUtc").GetDateTimeOffset());
            Assert.Equal(DateTimeOffset.Parse(end, CultureInfo.InvariantCulture), posted.GetProperty("endsAtUtc").GetDateTimeOffset());
            Assert.Equal(DateTimeOffset.Parse("2026-11-13T00:00:00.123456Z", CultureInfo.InvariantCulture), posted.GetProperty("createdAtUtc").GetDateTimeOffset());
            using var get = await fixture.Client.GetAsync($"{AcademicScheduleFixture.SessionsUrl}/{posted.GetProperty("id").GetGuid()}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var fetched = await AcademicScheduleFixture.JsonAsync(get);
            foreach (var field in new[] { "startsAtUtc", "endsAtUtc", "createdAtUtc" })
                Assert.Equal(posted.GetProperty(field).GetString(), fetched.GetProperty(field).GetString());
            return posted;
        }
    }

    [PostgresFact]
    public async Task UnexpectedFailureAfterInsert_RollsBackRealTransaction_AndPropagatesWithoutOverlapMapping()
    {
        var fault = new FailAfterSessionSave();
        await using var fixture = new AcademicScheduleFixture(Connection, fault);
        await fixture.InitializeAsync();
        var teachers = await TableSnapshotAsync("teachers");
        var start = new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        fault.Enabled = true;
        using (var scope = fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            var value = Session.Create(fixture.First, "Unit", "Lesson", SessionSkills.Listening,
                SessionType.Regular, null, start, start.AddHours(1), start);
            Assert.Same(fault.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.CreateWithoutTeacherOverlapAsync(value, fixture.Users["teacher"], default)));
        }
        Assert.True(fault.SawInsertedRowInsideTransaction);
        Assert.Equal("0", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));
        Assert.Equal(teachers, await TableSnapshotAsync("teachers"));
        // A fresh scope can book the same window, proving no leaked lock/partial booking.
        fault.Enabled = false;
        using var retryScope = fixture.Services.CreateScope();
        await retryScope.ServiceProvider.GetRequiredService<ISessionStore>().CreateWithoutTeacherOverlapAsync(
            Session.Create(fixture.First, "Unit", "Lesson", SessionSkills.Listening, SessionType.Regular, null, start, start.AddHours(1), start),
            fixture.Users["teacher"], default);
        Assert.Equal("1", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));
    }

    private sealed class FailAfterSessionSave : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool SawInsertedRowInsideTransaction { get; private set; }
        internal InvalidOperationException Failure { get; } = new("Synthetic failure after real session INSERT");
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled) return result;
            var db = Assert.IsType<AcademicDbContext>(eventData.Context);
            Assert.NotNull(db.Database.CurrentTransaction);
            SawInsertedRowInsideTransaction = await db.Sessions.AsNoTracking().CountAsync(cancellationToken) == 1;
            throw Failure;
        }
    }

    private async Task RaceAsync(string kind)
    {
        var rendezvous = new TeacherLockProbe();
        await using var fixture = new AcademicScheduleFixture(Connection, rendezvous);
        await fixture.InitializeAsync();
        using var firstClient = fixture.CreateClient();
        using var secondClient = fixture.CreateClient();
        await fixture.AsAsync("staff", firstClient);
        await fixture.AsAsync("admin", secondClient);
        var teachersBefore = await TableSnapshotAsync("teachers");
        rendezvous.Enabled = true;
        // Hold the first transaction AFTER its real teacher UPDATE has acquired the row lock.
        var first = firstClient.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl, fixture.Body());
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        try
        {
            try
            {
                var holder = await RequireSignalAsync(rendezvous.FirstLocked.Task, first);
                second = secondClient.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl,
                    fixture.Body(kind == "different-teachers" ? fixture.Other : fixture.Second,
                        kind == "adjacent" ? "2027-01-01T11:00:00Z" : "2027-01-01T10:00:00Z",
                        kind == "adjacent" ? "2027-01-01T12:00:00Z" : "2027-01-01T11:00:00Z"));
                var contender = await RequireSignalAsync(rendezvous.SecondAttempting.Task, second);
                Assert.NotEqual(holder.ProcessId, contender.ProcessId);
                if (kind == "different-teachers")
                {
                    // Independent teacher must commit while the first teacher remains locked.
                    Assert.Equal(HttpStatusCode.Created, (await second.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
                }
                else
                {
                    await RequirePostgresRowLockWaitAsync(holder.ProcessId, contender.ProcessId);
                    Assert.False(second.IsCompleted, "Contender finished while PostgreSQL reported its teacher-row UPDATE blocked.");
                    Assert.DoesNotContain(rendezvous.Commands, x => x.Context == contender.Context && x.Kind is "Overlap" or "Insert");
                }
            }
            finally
            {
                rendezvous.Release.TrySetResult();
                responses = await Task.WhenAll(second is null ? new[] { first } : new[] { first, second }).WaitAsync(TimeSpan.FromSeconds(30));
            }
            var statuses = responses.Select(x => x.StatusCode).Order().ToArray();
            Assert.Equal(kind == "overlap" ? new[] { HttpStatusCode.Created, HttpStatusCode.Conflict } :
                new[] { HttpStatusCode.Created, HttpStatusCode.Created }, statuses);
            AssertTransactions(rendezvous.Commands.ToArray(), kind == "overlap" ? 1 : 2);
            Assert.Equal(kind == "overlap" ? "1" : "2", await ScalarAsync("SELECT count(*)::text FROM academic.sessions"));
            Assert.Equal("0", await ScalarAsync("""
                SELECT count(*)::text FROM academic.sessions a
                JOIN academic.classes ca ON ca.id = a.class_id
                JOIN academic.sessions b ON a.id < b.id
                JOIN academic.classes cb ON cb.id = b.class_id
                WHERE ca.teacher_id = cb.teacher_id AND a.starts_at_utc < b.ends_at_utc AND b.starts_at_utc < a.ends_at_utc
                """));
            Assert.Equal(teachersBefore, await TableSnapshotAsync("teachers"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static async Task<TeacherLockProbe.LockAttempt> RequireSignalAsync(
        Task<TeacherLockProbe.LockAttempt> signal, Task<HttpResponseMessage> request)
    {
        await Task.WhenAny(signal, request).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(signal.IsCompletedSuccessfully,
            "HTTP create completed without reaching the required real PostgreSQL teacher-lock stage.");
        return await signal;
    }

    private async Task RequirePostgresRowLockWaitAsync(int holder, int contender)
    {
        await using var observer = new NpgsqlConnection(Connection);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await observer.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
              WHERE pid = @contender AND wait_event_type = 'Lock'
                AND @holder = ANY(pg_blocking_pids(pid))
                AND query ILIKE '%UPDATE%' AND query ILIKE '%teachers%')
            """, observer);
        command.Parameters.AddWithValue("holder", holder);
        command.Parameters.AddWithValue("contender", contender);
        try
        {
            // Observe actual server lock state, not elapsed time or a delayed HTTP response.
            while (!(bool)(await command.ExecuteScalarAsync(deadline.Token))!) { }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException("PostgreSQL never reported contender waiting on the held teacher-row UPDATE.");
        }
    }

    private static void AssertTransactions(TeacherLockProbe.CommandEvidence[] commands, int expectedInserts)
    {
        var locks = commands.Where(x => x.Kind == "Lock").ToArray();
        Assert.Equal(2, locks.Length);
        Assert.Equal(2, locks.Select(x => x.Context).Distinct().Count());
        Assert.Equal(2, locks.Select(x => x.Connection).Distinct().Count());
        Assert.Equal(expectedInserts, commands.Count(x => x.Kind == "Insert"));
        foreach (var rowLock in locks)
        {
            Assert.NotNull(rowLock.Transaction);
            var inContext = commands.Where(x => x.Context == rowLock.Context).ToArray();
            var overlap = Assert.Single(inContext, x => x.Kind == "Overlap");
            Assert.Same(rowLock.Transaction, overlap.Transaction);
            Assert.True(rowLock.Order < overlap.Order, "Overlap SELECT must follow the lock UPDATE in its transaction.");
            Assert.Equal(rowLock.Order, inContext.Where(x => ReferenceEquals(x.Transaction, rowLock.Transaction)).Min(x => x.Order));
            foreach (var insert in inContext.Where(x => x.Kind == "Insert"))
            {
                Assert.Same(rowLock.Transaction, insert.Transaction);
                Assert.True(overlap.Order < insert.Order, "INSERT must follow overlap SELECT in the same transaction.");
            }
        }
    }

    // Cột do migration chấm điểm (tuần 5+) thêm vào; chỉ so sánh phần dữ liệu có từ trước nâng cấp.
    private static string AddedColumns(string table) => table switch
    {
        "assignments" => "'created_at','created_by','is_legacy','kind','lesson','obligations_resolved_at','published_at','published_by','published_event_sent_at','session_id','unit','updated_at','version'",
        "submissions" => "'feedback','grade_version','graded_by','score'",
        _ => "",
    };

    private Task<string?> TableSnapshotAsync(string table) =>
        ScalarAsync($"SELECT coalesce(jsonb_agg((row_to_json(t)::jsonb - ARRAY[{AddedColumns(table)}]::text[]) ORDER BY row_to_json(t)::text)::text, '[]') FROM academic.\"{table}\" t");

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string;
    }

    private sealed class TeacherLockProbe : DbCommandInterceptor
    {
        private int arrivals;
        private int order;
        private Guid firstContext;
        internal bool Enabled { get; set; }
        internal TaskCompletionSource<LockAttempt> FirstLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<LockAttempt> SecondAttempting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<CommandEvidence> Commands { get; } = [];
        internal sealed record LockAttempt(Guid Context, int ProcessId);
        internal sealed record CommandEvidence(Guid Context, DbConnection Connection, DbTransaction? Transaction, string Kind, int Order);

        private void Record(DbCommand command, CommandEventData data)
        {
            if (!Enabled) return;
            var sql = command.CommandText.TrimStart();
            var kind = sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && sql.Contains("teachers", StringComparison.OrdinalIgnoreCase) ? "Lock" :
                sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && sql.Contains("sessions", StringComparison.OrdinalIgnoreCase) ? "Overlap" :
                sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) && sql.Contains("sessions", StringComparison.OrdinalIgnoreCase) ? "Insert" : "Other";
            var context = data.Context!.ContextId.InstanceId;
            Commands.Enqueue(new(context, command.Connection!, command.Transaction, kind, Interlocked.Increment(ref order)));
            if (kind != "Lock") return;
            Assert.NotNull(command.Transaction);
            var attempt = new LockAttempt(context, ((NpgsqlConnection)command.Connection!).ProcessID);
            if (Interlocked.Increment(ref arrivals) == 1) firstContext = context;
            else SecondAttempting.TrySetResult(attempt);
        }

        private async Task HoldFirstLockAsync(DbCommand command, CommandExecutedEventData data, CancellationToken ct)
        {
            if (!Enabled || data.Context!.ContextId.InstanceId != firstContext ||
                !command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains("teachers", StringComparison.OrdinalIgnoreCase)) return;
            FirstLocked.TrySetResult(new(firstContext, ((NpgsqlConnection)command.Connection!).ProcessID));
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command, eventData); return ValueTask.FromResult(result); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Record(command, eventData); return ValueTask.FromResult(result); }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        { await HoldFirstLockAsync(command, eventData, cancellationToken); return result; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        { await HoldFirstLockAsync(command, eventData, cancellationToken); return result; }
    }
}
