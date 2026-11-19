using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Each case owns a disposable database; no application database is migrated or cleared.
public sealed class AcademicAttendancePostgresTests : IAsyncLifetime
{
    private string server = "";
    private string database = "";
    private string Connection => new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        var settings = new NpgsqlConnectionStringBuilder(value);
        Assert.Contains(settings.Host, new[] { "127.0.0.1", "localhost", "::1" });
        server = settings.ConnectionString;
        database = "ec_attendance_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (database.Length == 0) return;
        using var owned = new NpgsqlConnection(Connection);
        NpgsqlConnection.ClearPool(owned);
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task FreshMigration_CreatesAttendanceSourceAndAudit_AndMatchesModel()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        foreach (var name in new[] { "Attendance", "AttendanceRevision", "AttendanceReplayAttempt" })
        {
            var table = Table(db, name);
            Assert.Equal("0", await ScalarAsync($"SELECT count(*)::text FROM {table}"));
        }
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task UpgradeFromAddSessions_PreservesAllExistingAcademicData()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        await fixture.InitializeAsync("20261113064719_AddSessions");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var now = new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            // Seed only columns that exist at AddSessions, not columns in the current EF model.
            var (a, s, f) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO academic.sessions (id, class_id, unit, lesson, skills, session_type, starts_at_utc, ends_at_utc, created_at_utc)
                VALUES ({Guid.NewGuid()}, {fixture.Second}, 'Unit', 'Preserved', {(int)SessionSkills.Listening}, 'Regular', {now}, {now.AddHours(1)}, {now});
                INSERT INTO academic.assignments (id, class_id, title, description, opens_at_utc, closes_at_utc)
                VALUES ({a}, {fixture.Second}, 'Preserved assignment', 'Synthetic', now(), now() + interval '1 day');
                INSERT INTO academic.submissions (id, assignment_id, student_id, revision, submitted_at)
                VALUES ({s}, {a}, {fixture.Users["student"]}, {Guid.NewGuid()}, now());
                INSERT INTO academic.submission_files (id, submission_id, file_id, file_name, content_type, size_bytes)
                VALUES ({Guid.NewGuid()}, {s}, {f}, 'x.pdf', 'application/pdf', 12);
                """);
        }
        var tables = new[] { "courses", "teachers", "classes", "class_students", "assignments", "submissions", "submission_files", "sessions" };
        var before = new Dictionary<string, string?>();
        foreach (var table in tables) before[table] = await SnapshotAsync(table);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        foreach (var table in tables) Assert.Equal(before[table], await SnapshotAsync(table));
    }

    [PostgresFact]
    public Task ConcurrentCreates_OneWinnerOneConflict_OneImmutableRevision() => RaceAsync(false);

    [PostgresFact]
    public Task ConcurrentUpdates_OneWinnerOneConflict_TwoImmutableRevisions() => RaceAsync(true);

    [PostgresFact]
    public Task TeacherLockedAfterInitialChecks_IsRejectedBeforeCommit() => RejectAccountChangeAsync("teacher", "lock");

    [PostgresFact]
    public Task StudentLockedAfterInitialChecks_IsRejectedBeforeCommit() => RejectAccountChangeAsync("student", "lock");

    [PostgresFact]
    public Task TeacherRoleChangedAfterInitialChecks_IsRejectedBeforeCommit() => RejectAccountChangeAsync("teacher", "role");

    [PostgresFact]
    public Task StudentRoleChangedAfterInitialChecks_IsRejectedBeforeCommit() => RejectAccountChangeAsync("student", "role");

    [PostgresFact]
    public async Task SavedSourceSurvivesNewScope_AndManagerReplayIsDurablyAudited()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        var session = await PrepareAsync(fixture);
        using var saved = await fixture.Client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Late", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var row = await AcademicScheduleFixture.JsonAsync(saved);
        var id = row.GetProperty("id").GetGuid();
        var changedAt = row.GetProperty("updatedAtUtc").GetDateTimeOffset();
        Assert.Equal(0, changedAt.UtcTicks % 10);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal("1", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "AttendanceRevision")}"));
        }
        await AsAsync(fixture, "admin");
        using var replay = await fixture.Client.PostAsJsonAsync($"/api/academic/attendance/{id}/replay", new { revision = 1, reason = "Synthetic recovery proof" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("Dispatched", (await AcademicScheduleFixture.JsonAsync(replay)).GetProperty("dispatchStatus").GetString());
        using var history = await fixture.Client.GetAsync($"/api/academic/attendance/{id}/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var revision = Assert.Single((await AcademicScheduleFixture.JsonAsync(history)).GetProperty("items").EnumerateArray());
        Assert.Equal(changedAt, revision.GetProperty("changedAtUtc").GetDateTimeOffset());
        using var audit = await fixture.Client.GetAsync($"/api/academic/attendance/{id}/replays");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var attempt = Assert.Single((await AcademicScheduleFixture.JsonAsync(audit)).GetProperty("items").EnumerateArray());
        Assert.Equal("Dispatched", attempt.GetProperty("dispatchStatus").GetString());
        Assert.Equal(fixture.Users["admin"], attempt.GetProperty("requestedBy").GetGuid());
        Assert.Equal(1, attempt.GetProperty("revision").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, attempt.GetProperty("completedAtUtc").ValueKind);
    }

    [PostgresFact]
    public async Task FailureAfterSave_RollsBackBothLatestAndImmutableSource()
    {
        var fault = new FailAfterAttendanceSave();
        await using var fixture = new AcademicScheduleFixture(Connection, fault);
        var session = await PrepareAsync(fixture);
        fault.Enabled = true;
        // TestServer may surface an unhandled application exception instead of an HTTP500.
        try
        {
            using var response = await fixture.Client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Present", expectedRevision = (int?)null });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException ex) when (ReferenceEquals(ex, fault.Failure)) { }
        Assert.True(fault.SawSourceInsideTransaction);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            foreach (var name in new[] { "Attendance", "AttendanceRevision" })
                Assert.Equal("0", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, name)}"));
        }
        fault.Enabled = false;
        using var retry = await fixture.Client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Present", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, (await AcademicScheduleFixture.JsonAsync(retry)).GetProperty("revision").GetInt32());
    }

    [PostgresFact]
    public async Task DispatchFailure_KeepsCommittedSource_AndFailedReplayAudit()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        var session = await PrepareAsync(fixture);
        var bus = new ThrowingBus();
        await using var failingHost = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventBus>();
            services.AddSingleton<IEventBus>(bus);
        }));
        using var client = failingHost.CreateClient();
        await AsAsync(fixture, "teacher", client);
        using var write = await client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Present", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        var id = (await AcademicScheduleFixture.JsonAsync(write)).GetProperty("id").GetGuid();
        Assert.Equal(1, bus.Calls);
        Assert.True(bus.Strict);
        using var correction = await client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Late", expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        Assert.Equal(2, (await AcademicScheduleFixture.JsonAsync(correction)).GetProperty("revision").GetInt32());
        await AsAsync(fixture, "admin", client);
        await AsAsync(fixture, "admin");
        using var replay = await client.PostAsJsonAsync($"/api/academic/attendance/{id}/replay", new { revision = 1, reason = "Synthetic failed dispatch" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, replay.StatusCode);
        Assert.Equal(3, bus.Calls);
        Assert.All(bus.Events, value => Assert.Equal("AttendanceRevisionRecordedIntegrationEvent", value.Type));
        foreach (var field in new[] { "AttendanceId", "SessionId", "ClassId", "StudentId", "Status", "Revision", "RecordedBy", "RecordedAtUtc" })
            Assert.Equal(bus.Events[0].Payload.GetProperty(field).GetRawText(), bus.Events[2].Payload.GetProperty(field).GetRawText());
        Assert.Equal("Present", bus.Events[2].Payload.GetProperty("Status").GetString());
        Assert.Equal("Late", bus.Events[1].Payload.GetProperty("Status").GetString());
        using var audit = await fixture.Client.GetAsync($"/api/academic/attendance/{id}/replays");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var attempt = Assert.Single((await AcademicScheduleFixture.JsonAsync(audit)).GetProperty("items").EnumerateArray());
        Assert.Equal("Failed", attempt.GetProperty("dispatchStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, attempt.GetProperty("completedAtUtc").ValueKind);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal("1", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "Attendance")}"));
        Assert.Equal("2", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "AttendanceRevision")}"));
    }

    [PostgresFact]
    public async Task RecordedAttendanceRestrictsSessionAndRootDeletion_AndRetainsHistory()
    {
        await using var fixture = new AcademicScheduleFixture(Connection);
        var session = await PrepareAsync(fixture);
        using var saved = await fixture.Client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Present", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var id = (await AcademicScheduleFixture.JsonAsync(saved)).GetProperty("id").GetGuid();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await using var connection = new NpgsqlConnection(Connection);
        await connection.OpenAsync();
        foreach (var (table, key) in new[] { (Table(db, "Session"), session), (Table(db, "Attendance"), id) })
        {
            await using var command = new NpgsqlCommand($"DELETE FROM {table} WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", key);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        }
        using var history = await fixture.Client.GetAsync($"/api/academic/attendance/{id}/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(1, (await AcademicScheduleFixture.JsonAsync(history)).GetProperty("total").GetInt32());
        Assert.Equal("1", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "Attendance")}"));
    }

    private async Task RejectAccountChangeAsync(string account, string change)
    {
        var probe = new ClassLockProbe();
        await using var fixture = new AcademicScheduleFixture(Connection, probe);
        var session = await PrepareAsync(fixture);
        var bus = new ThrowingBus();
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventBus>();
            services.AddSingleton<IEventBus>(bus);
        }));
        using var client = host.CreateClient();
        await AsAsync(fixture, "teacher", client);
        probe.Enabled = true;
        var write = client.PutAsJsonAsync(WriteUrl(fixture, session), new { status = "Present", expectedRevision = (int?)null });
        try
        {
            try
            {
                // The request already read both users; its class lock now gates all transaction rechecks.
                await SignalBeforeResponseAsync(probe.FirstLocked.Task, write);
                Assert.False(write.IsCompleted);
                // Commit a valid account change in a separate scope before the writer resumes.
                using var identityScope = fixture.Services.CreateScope();
                var identity = identityScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var user = await identity.Users.SingleAsync(x => x.Id == fixture.Users[account]);
                if (change == "lock") user.SetLocked(true);
                else
                {
                    identity.Entry(user).Property(x => x.Role).CurrentValue = Roles.Staff;
                    identity.Entry(user).Property(x => x.StudentCode).CurrentValue = null;
                }
                await identity.SaveChangesAsync();
            }
            finally { probe.Release.TrySetResult(); }

            using var response = await write.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal("0", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "Attendance")}"));
            Assert.Equal("0", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "AttendanceRevision")}"));
            Assert.Equal(0, bus.Calls);
        }
        finally
        {
            probe.Release.TrySetResult();
            try
            {
                // Drain the request even when the pause/change assertion fails, before disposing its host.
                using var completed = await write.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally { probe.Enabled = false; }
        }
    }

    private async Task RaceAsync(bool update)
    {
        var probe = new ClassLockProbe();
        await using var fixture = new AcademicScheduleFixture(Connection, probe);
        var session = await PrepareAsync(fixture);
        var url = WriteUrl(fixture, session);
        if (update)
        {
            using var created = await fixture.Client.PutAsJsonAsync(url, new { status = "Present", expectedRevision = (int?)null });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        }
        using var other = fixture.CreateClient();
        await AsAsync(fixture, "teacher", other);
        probe.Enabled = true;
        var first = fixture.Client.PutAsJsonAsync(url, new { status = "Late", expectedRevision = update ? (int?)1 : null });
        Task<HttpResponseMessage>? second = null;
        HttpResponseMessage[] responses = [];
        try
        {
            try
            {
                var holder = await SignalBeforeResponseAsync(probe.FirstLocked.Task, first);
                second = other.PutAsJsonAsync(url, new { status = "LeftEarly", expectedRevision = update ? (int?)1 : null });
                var contender = await SignalBeforeResponseAsync(probe.SecondAttempting.Task, second);
                Assert.NotEqual(holder, contender);
                await RequireLockWaitAsync(holder, contender);
                Assert.False(second.IsCompleted);
            }
            finally
            {
                probe.Release.TrySetResult();
                try
                {
                    responses = await Task.WhenAll(second is null ? [first] : new[] { first, second }).WaitAsync(TimeSpan.FromSeconds(30));
                }
                finally { probe.Enabled = false; }
            }
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(x => x.StatusCode).Order().ToArray());
            using var roster = await fixture.Client.GetAsync($"/api/academic/sessions/{session}/attendance");
            Assert.Equal(HttpStatusCode.OK, roster.StatusCode);
            var current = Assert.Single((await AcademicScheduleFixture.JsonAsync(roster)).GetProperty("items").EnumerateArray()).GetProperty("attendance");
            Assert.Equal(update ? 2 : 1, current.GetProperty("revision").GetInt32());
            var id = current.GetProperty("id").GetGuid();
            using var history = await fixture.Client.GetAsync($"/api/academic/attendance/{id}/history");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            Assert.Equal(update ? 2 : 1, (await AcademicScheduleFixture.JsonAsync(history)).GetProperty("total").GetInt32());
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal("1", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "Attendance")}"));
            Assert.Equal(update ? "2" : "1", await ScalarAsync($"SELECT count(*)::text FROM {Table(db, "AttendanceRevision")}"));
        }
        finally
        {
            // Dispose successful siblings even if lock assertions or the other request fail.
            foreach (var request in second is null ? new[] { first } : new[] { first, second })
            {
                if (request.IsCompletedSuccessfully) request.Result.Dispose();
                else _ = request.ContinueWith(completed =>
                {
                    if (completed.IsCompletedSuccessfully) completed.Result.Dispose();
                    else if (completed.IsFaulted) _ = completed.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private static async Task<Guid> PrepareAsync(AcademicScheduleFixture fixture)
    {
        await fixture.InitializeAsync();
        await AsAsync(fixture, "staff");
        using var response = await fixture.Client.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl, fixture.Body(fixture.Second));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = (await AcademicScheduleFixture.JsonAsync(response)).GetProperty("id").GetGuid();
        await AsAsync(fixture, "teacher");
        fixture.Clock.UtcNow = DateTimeOffset.Parse("2027-01-01T10:30:00.1234567Z", CultureInfo.InvariantCulture);
        return session;
    }

    private static string WriteUrl(AcademicScheduleFixture fixture, Guid session) =>
        $"/api/academic/sessions/{session}/attendance/{fixture.Users["student"]}";

    private static async Task AsAsync(AcademicScheduleFixture fixture, string actor, HttpClient? client = null)
    {
        var instant = fixture.Clock.UtcNow;
        fixture.Clock.UtcNow = null;
        try { await fixture.AsAsync(actor, client); }
        finally { fixture.Clock.UtcNow = instant; }
    }

    private static string Table(AcademicDbContext db, string entityName)
    {
        var entity = Assert.Single(db.Model.GetEntityTypes(), x => x.ClrType.Name == entityName);
        return $"\"{entity.GetSchema()!.Replace("\"", "\"\"")}\".\"{entity.GetTableName()!.Replace("\"", "\"\"")}\"";
    }

    // Chỉ so sánh các cột đã tồn tại ở migration AddSessions.
    private static string AddedColumns(string table) => table switch
    {
        "assignments" => "'created_at','created_by','is_legacy','kind','lesson','obligations_resolved_at','published_at','published_by','published_event_sent_at','session_id','unit','updated_at','version'",
        "submissions" => "'feedback','grade_version','graded_by','score'",
        "sessions" => "'is_cancelled','revision'",
        "courses" => "'is_retired'",
        _ => "",
    };

    private Task<string?> SnapshotAsync(string table) =>
        ScalarAsync($"SELECT coalesce(jsonb_agg(data ORDER BY data::text)::text,'[]') FROM (SELECT to_jsonb(t) - ARRAY[{AddedColumns(table)}]::text[] AS data FROM academic.\"{table}\" t) snapshot");

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int> SignalBeforeResponseAsync(Task<int> signal, Task<HttpResponseMessage> response)
    {
        await Task.WhenAny(signal, response).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(signal.IsCompletedSuccessfully, "Request did not reach the required class-row lock stage.");
        return await signal;
    }

    private async Task RequireLockWaitAsync(int holder, int contender)
    {
        await using var connection = new NpgsqlConnection(Connection);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@pid AND wait_event_type='Lock' AND @holder=ANY(pg_blocking_pids(pid)))", connection);
        command.Parameters.AddWithValue("pid", contender);
        command.Parameters.AddWithValue("holder", holder);
        try
        {
            while (!(bool)(await command.ExecuteScalarAsync(timeout.Token))!) { }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException("PostgreSQL did not report the contender blocked by the held class-row lock.");
        }
    }

    private sealed class FailAfterAttendanceSave : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool SawSourceInsideTransaction { get; private set; }
        internal InvalidOperationException Failure { get; } = new("Synthetic failure after attendance source INSERT");

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Enabled || !eventData.Context!.ChangeTracker.Entries().Any(x => x.Entity.GetType().Name == "AttendanceRevision"))
                return ValueTask.FromResult(result);
            Assert.NotNull(eventData.Context.Database.CurrentTransaction);
            SawSourceInsideTransaction = true;
            throw Failure;
        }
    }

    private sealed class ThrowingBus : IEventBus
    {
        internal int Calls { get; private set; }
        internal bool Strict { get; private set; }
        internal List<(string Type, JsonElement Payload)> Events { get; } = [];
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default,
            bool throwOnHandlerFailure = false) where TEvent : IIntegrationEvent
        {
            Calls++;
            Strict = throwOnHandlerFailure;
            Events.Add((integrationEvent.GetType().Name, JsonSerializer.SerializeToElement(integrationEvent)));
            throw new InvalidOperationException("Synthetic event dispatch failure");
        }
    }

    private sealed class ClassLockProbe : DbCommandInterceptor
    {
        private int arrivals;
        private Guid firstContext;
        internal bool Enabled { get; set; }
        internal TaskCompletionSource<int> FirstLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<int> SecondAttempting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static bool IsLock(DbCommand command)
        {
            var sql = command.CommandText.TrimStart();
            return sql.Contains("classes", StringComparison.OrdinalIgnoreCase) &&
                (sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
        }

        private void Attempt(DbCommand command, CommandEventData data)
        {
            if (!Enabled || !IsLock(command)) return;
            Assert.NotNull(command.Transaction);
            if (Interlocked.Increment(ref arrivals) == 1) firstContext = data.Context!.ContextId.InstanceId;
            else SecondAttempting.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
        }

        private async Task HoldAsync(DbCommand command, CommandExecutedEventData data, CancellationToken ct)
        {
            if (!Enabled || !IsLock(command) || data.Context!.ContextId.InstanceId != firstContext) return;
            FirstLocked.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Attempt(command, data); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Attempt(command, data); return ValueTask.FromResult(result); }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken cancellationToken = default)
        { await HoldAsync(command, data, cancellationToken); return result; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken cancellationToken = default)
        { await HoldAsync(command, data, cancellationToken); return result; }
    }
}
