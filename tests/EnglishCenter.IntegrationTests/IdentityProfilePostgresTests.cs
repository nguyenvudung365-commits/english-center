using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Opt-in real PostgreSQL only. Every test owns a freshly created database; no shared schema is modified.
public sealed class IdentityProfilePostgresTests : IAsyncLifetime
{
    private const string InitialMigration = "20261027100830_InitialIdentity";
    private string serverConnection = string.Empty;
    private string database = string.Empty;
    private string DbConnection => new NpgsqlConnectionStringBuilder(serverConnection) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        serverConnection = value;
        var name = "ec_profile_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        database = name;
    }

    public async Task DisposeAsync()
    {
        if (database.Length == 0) return;
        using var ownConnection = new NpgsqlConnection(DbConnection);
        NpgsqlConnection.ClearPool(ownConnection);
        await using var connection = new NpgsqlConnection(serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = DbConnection,
            ["Jwt:SigningKey"] = new string('t', 48),
            ["Identity:InitializeDatabase"] = "false"
        }).Build());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [PostgresFact]
    public async Task Migration_BackfillsOriginalUtcYear_AndPreservesUsersHashesAndSessions()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(InitialMigration);
        var seeds = new[]
        {
            User.Create("old@example.test", "Old Student", Roles.Student, "preserved-hash-1", new DateTimeOffset(2020, 12, 31, 23, 30, 0, TimeSpan.Zero)),
            // Local 2025 is UTC 2024: the suffix must use the stored UTC creation year.
            User.Create("boundary@example.test", "Boundary Student", Roles.Student, "preserved-hash-2", new DateTimeOffset(2025, 1, 1, 0, 30, 0, TimeSpan.FromHours(7)).ToUniversalTime()),
            User.Create("recent@example.test", "Recent Student", Roles.Student, "preserved-hash-3", new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)),
            User.Create("teacher@example.test", "Teacher", Roles.Teacher, "preserved-hash-4", new DateTimeOffset(2021, 2, 3, 0, 0, 0, TimeSpan.Zero))
        };
        await using var connection = new NpgsqlConnection(DbConnection);
        await connection.OpenAsync();
        foreach (var user in seeds)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO identity.users ("Id", "Email", "NormalizedEmail", "FullName", "Role", "PasswordHash", "IsLocked", "Revision", "CreatedAt")
                VALUES (@id, @email, @normalized, @name, @role, @hash, false, @revision, @created)
                """, connection);
            insert.Parameters.AddWithValue("id", user.Id);
            insert.Parameters.AddWithValue("email", user.Email);
            insert.Parameters.AddWithValue("normalized", user.NormalizedEmail);
            insert.Parameters.AddWithValue("name", user.FullName);
            insert.Parameters.AddWithValue("role", user.Role);
            insert.Parameters.AddWithValue("hash", user.PasswordHash);
            insert.Parameters.AddWithValue("revision", user.Revision);
            insert.Parameters.AddWithValue("created", user.CreatedAt);
            await insert.ExecuteNonQueryAsync();
        }
        var sessionId = Guid.NewGuid();
        var expiry = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO identity.refresh_sessions ("Id", "UserId", "TokenHash", "ExpiresAt", "RevokedAt")
            VALUES (@id, @user, @hash, @expiry, NULL)
            """, connection))
        {
            insert.Parameters.AddWithValue("id", sessionId);
            insert.Parameters.AddWithValue("user", seeds[0].Id);
            insert.Parameters.AddWithValue("hash", new string('a', 64));
            insert.Parameters.AddWithValue("expiry", expiry);
            await insert.ExecuteNonQueryAsync();
        }

        await db.Database.MigrateAsync();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal(seeds.Length, users.Count);
        foreach (var seed in seeds)
        {
            var actual = users[seed.Id];
            Assert.Equal(seed.Email, actual.Email);
            Assert.Equal(seed.NormalizedEmail, actual.NormalizedEmail);
            Assert.Equal(seed.FullName, actual.FullName);
            Assert.Equal(seed.Role, actual.Role);
            Assert.Equal(seed.PasswordHash, actual.PasswordHash);
            Assert.Equal(seed.Revision, actual.Revision);
            Assert.Equal(seed.CreatedAt, actual.CreatedAt);
            Assert.False(actual.IsLocked);
            Assert.Null(actual.Phone);
            Assert.Null(actual.AvatarFileId);
            Assert.Equal(0L, actual.ProfileVersion);
        }
        Assert.Equal("HV000001-20", users[seeds[0].Id].StudentCode);
        Assert.Equal("HV000002-24", users[seeds[1].Id].StudentCode);
        Assert.Equal("HV000003-26", users[seeds[2].Id].StudentCode);
        Assert.Null(users[seeds[3].Id].StudentCode);
        var session = await db.RefreshSessions.AsNoTracking().SingleAsync();
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(seeds[0].Id, session.UserId);
        Assert.Equal(new string('a', 64), session.TokenHash);
        Assert.Equal(expiry, session.ExpiresAt);
        Assert.Null(session.RevokedAt);

        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(users.Values.OrderBy(x => x.Id).Select(x => x.StudentCode),
            (await db.Users.AsNoTracking().ToListAsync()).OrderBy(x => x.Id).Select(x => x.StudentCode));
        var next = await scope.ServiceProvider.GetRequiredService<IStudentCodeGenerator>().AllocateAsync(new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), default);
        Assert.Equal("HV000004-26", next);
        Assert.DoesNotContain(next, users.Values.Select(x => x.StudentCode));
    }

    [PostgresFact]
    public async Task EmptyDatabase_RepeatedMigrationAndConcurrentAllocation_GenerateUniqueGlobalNumbers()
    {
        await using var services = Services();
        await using (var setup = services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
        }
        var createdAt = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        var codes = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            await using var scope = services.CreateAsyncScope();
            var code = await scope.ServiceProvider.GetRequiredService<IStudentCodeGenerator>().AllocateAsync(createdAt, default);
            var user = User.Create($"{Guid.NewGuid():N}@example.test", "Student", Roles.Student, "test-hash", createdAt);
            user.AssignStudentCode(code);
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return code;
        }));
        Assert.Equal(16, codes.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 16).Select(x => $"HV{x:D6}-26"), codes.Order());
        await using var verify = services.CreateAsyncScope();
        var databaseContext = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(16, await databaseContext.Users.CountAsync());
        await databaseContext.Database.MigrateAsync();
        Assert.Equal("HV000017-27", await verify.ServiceProvider.GetRequiredService<IStudentCodeGenerator>()
            .AllocateAsync(createdAt.AddYears(1), default));
    }

    [PostgresFact]
    public async Task SequenceCapacity_IsExplicitAndNeverCycles()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT setval('identity.student_code_sequence', 999998, true)");
        var generator = scope.ServiceProvider.GetRequiredService<IStudentCodeGenerator>();
        var now = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("HV999999-26", await generator.AllocateAsync(now, default));
        var failure = await Assert.ThrowsAsync<IdentityFailure>(() => generator.AllocateAsync(now, default));
        Assert.Equal(409, failure.Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<IdentityFailure>(() => generator.AllocateAsync(now, default))).Status);
    }

    [PostgresFact]
    public async Task DatabaseConstraints_RejectDuplicateWrongRoleWrongYearAndMissingStudentCode()
    {
        await using var services = Services();
        await using (var setup = services.CreateAsyncScope())
            await setup.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        var now = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        foreach (var item in new[]
        {
            (Role: Roles.Student, Code: (string?)null, State: PostgresErrorCodes.CheckViolation),
            (Role: Roles.Teacher, Code: (string?)"HV000001-26", State: PostgresErrorCodes.CheckViolation),
            (Role: Roles.Student, Code: (string?)"HV000001-25", State: PostgresErrorCodes.CheckViolation),
            (Role: Roles.Student, Code: (string?)"HV000000-26", State: PostgresErrorCodes.CheckViolation)
        })
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = User.Create($"{Guid.NewGuid():N}@example.test", "User", item.Role, "test-hash", now);
            db.Users.Add(user);
            db.Entry(user).Property(x => x.StudentCode).CurrentValue = item.Code;
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(item.State, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
        await using (var first = services.CreateAsyncScope())
        {
            var user = User.Create("first@example.test", "First", Roles.Student, "test-hash", now);
            user.AssignStudentCode("HV000001-26");
            var db = first.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        await using var duplicateScope = services.CreateAsyncScope();
        var duplicate = User.Create("second@example.test", "Second", Roles.Student, "test-hash", now);
        duplicate.AssignStudentCode("HV000001-26");
        var duplicateDb = duplicateScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        duplicateDb.Users.Add(duplicate);
        var duplicateFailure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateFailure.InnerException).SqlState);
    }
}
