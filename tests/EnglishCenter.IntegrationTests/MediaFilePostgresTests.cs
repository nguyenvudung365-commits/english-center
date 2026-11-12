using EnglishCenter.Modules.MediaNotification.Domain.Files;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class MediaFilePostgresTests : IAsyncLifetime
{
    private string _serverConnection = string.Empty;
    private string _database = string.Empty;
    private string DbConnection => new NpgsqlConnectionStringBuilder(_serverConnection) { Database = _database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        _serverConnection = value;
        var database = "ec_media_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync();
        _database = database;
    }

    public async Task DisposeAsync()
    {
        if (_database.Length == 0) return;
        // Clear only this fixture's pool; other test fixtures may be running concurrently.
        using var ownConnection = new NpgsqlConnection(DbConnection);
        NpgsqlConnection.ClearPool(ownConnection);
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider CreateServices(SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MediaNotificationDbContext>(options =>
        {
            options.UseNpgsql(DbConnection,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaNotificationSchema.Name));
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        services.AddScoped(sp => new FileStore(sp.GetRequiredService<MediaNotificationDbContext>(), NullLogger<FileStore>.Instance));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [PostgresFact]
    public async Task Migration_CreatesOnlyMediaSchemaAndOwnHistory()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>();
        await db.Database.MigrateAsync();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(MediaNotificationSchema.Name, db.Model.GetDefaultSchema());
        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Equal(MediaNotificationSchema.Name, entity.GetSchema()));

        await using var connection = new NpgsqlConnection(DbConnection);
        await connection.OpenAsync();
        await using var tables = new NpgsqlCommand("""
            SELECT table_schema, table_name FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name
            """, connection);
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            var actual = new List<(string Schema, string Table)>();
            while (await reader.ReadAsync()) actual.Add((reader.GetString(0), reader.GetString(1)));
            Assert.Equal(new[]
            {
                (MediaNotificationSchema.Name, "__EFMigrationsHistory"),
                (MediaNotificationSchema.Name, "stored_files")
            }, actual);
        }

        await using var history = new NpgsqlCommand(
            $"SELECT count(*) FROM \"{MediaNotificationSchema.Name}\".\"__EFMigrationsHistory\"", connection);
        Assert.Equal((long)applied.Length, await history.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task TryComplete_ConcurrentSameRevision_OneWinner_StaleAndForeignOwnerCannotUpdate()
    {
        await using var services = CreateServices();
        var owner = Guid.NewGuid();
        // Whole seconds avoid PostgreSQL's microsecond timestamp precision changing equality.
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var pending = StoredFile.Create(owner, "proof.pdf", "application/pdf", 128, "Document", now, now.AddMinutes(10));
        var revision = pending.Revision;
        await using (var setup = services.CreateAsyncScope())
        {
            await setup.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().Database.MigrateAsync();
            await setup.ServiceProvider.GetRequiredService<FileStore>().AddAsync(pending, CancellationToken.None);
        }

        // Check ownership while still pending, so completion state cannot mask a missing owner predicate.
        await using (var foreign = services.CreateAsyncScope())
        {
            var store = foreign.ServiceProvider.GetRequiredService<FileStore>();
            Assert.Null(await store.FindOwnedAsync(pending.Id, Guid.NewGuid(), CancellationToken.None));
            Assert.False(await store.TryCompleteAsync(pending.Id, Guid.NewGuid(), revision,
                "final/foreign.pdf", now.AddSeconds(1), CancellationToken.None));
        }
        await using (var verification = services.CreateAsyncScope())
        {
            var unchanged = await verification.ServiceProvider.GetRequiredService<FileStore>()
                .FindOwnedAsync(pending.Id, owner, CancellationToken.None);
            Assert.NotNull(unchanged);
            Assert.Equal(revision, unchanged.Revision);
            Assert.Null(unchanged.FileKey);
            Assert.Null(unchanged.UploadedAt);
        }

        var gate = new ConcurrentSaveGate();
        await using var raceServices = CreateServices(gate);
        await using var firstScope = raceServices.CreateAsyncScope();
        await using var secondScope = raceServices.CreateAsyncScope();
        Assert.NotSame(firstScope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>(),
            secondScope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>());
        var keys = new[] { "final/first.pdf", "final/second.pdf" };
        var uploadedAt = now.AddSeconds(2);

        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<FileStore>().TryCompleteAsync(
                pending.Id, owner, revision, keys[0], uploadedAt, CancellationToken.None),
            secondScope.ServiceProvider.GetRequiredService<FileStore>().TryCompleteAsync(
                pending.Id, owner, revision, keys[1], uploadedAt, CancellationToken.None));

        Assert.Equal(2, gate.Arrivals);
        Assert.Equal(1, gate.ConcurrencyConflicts);
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        var winner = keys[Array.FindIndex(results, result => result)];
        Guid completedRevision;
        await using (var verification = services.CreateAsyncScope())
        {
            var store = verification.ServiceProvider.GetRequiredService<FileStore>();
            var persisted = await store.FindOwnedAsync(pending.Id, owner, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(winner, persisted.FileKey);
            Assert.Equal(uploadedAt, persisted.UploadedAt);
            Assert.NotEqual(revision, persisted.Revision);
            completedRevision = persisted.Revision;
            Assert.True(await store.IsReferencedAsync(winner, CancellationToken.None));
            Assert.False(await store.IsReferencedAsync(keys.Single(key => key != winner), CancellationToken.None));
        }

        await using (var stale = services.CreateAsyncScope())
        {
            Assert.False(await stale.ServiceProvider.GetRequiredService<FileStore>().TryCompleteAsync(
                pending.Id, owner, revision, "final/stale.pdf", uploadedAt, CancellationToken.None));
        }
        await using (var verification = services.CreateAsyncScope())
        {
            var persisted = await verification.ServiceProvider.GetRequiredService<FileStore>()
                .FindOwnedAsync(pending.Id, owner, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(winner, persisted.FileKey);
            Assert.Equal(completedRevision, persisted.Revision);
            Assert.Equal(uploadedAt, persisted.UploadedAt);
        }
    }

    private sealed class ConcurrentSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private int _concurrencyConflicts;
        public int Arrivals => Volatile.Read(ref _arrivals);
        public int ConcurrencyConflicts => Volatile.Read(ref _concurrencyConflicts);

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(ConcurrencyExceptionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _concurrencyConflicts);
            return ValueTask.FromResult(result); // Observe the real EF conflict without suppressing it.
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // Both stores have read the pending revision and attached their update before either SQL UPDATE runs.
            Assert.NotNull(eventData.Context);
            var entry = Assert.Single(eventData.Context.ChangeTracker.Entries<StoredFile>());
            Assert.Equal(EntityState.Modified, entry.State);
            Assert.NotEqual(entry.Property(file => file.Revision).OriginalValue, entry.Entity.Revision);
            if (Interlocked.Increment(ref _arrivals) == 2) _bothReady.TrySetResult();
            await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
