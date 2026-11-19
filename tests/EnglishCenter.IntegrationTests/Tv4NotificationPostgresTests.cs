using EnglishCenter.Modules.MediaNotification.Domain.Feed;
using EnglishCenter.Modules.MediaNotification.Domain.Feedback;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Chỉ chạy khi có máy chủ PostgreSQL riêng (cùng biến môi trường với các test PostgreSQL khác).
public sealed class Tv4NotificationPostgresTests : IAsyncLifetime
{
    private string _server = string.Empty;
    private string _database = string.Empty;
    private string DbConnection => new NpgsqlConnectionStringBuilder(_server) { Database = _database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        _server = value;
        _database = "ec_tv4_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database.Length == 0) return;
        using var own = new NpgsqlConnection(DbConnection);
        NpgsqlConnection.ClearPool(own);
        await using var connection = new NpgsqlConnection(_server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private MediaNotificationDbContext Context() => new(new DbContextOptionsBuilder<MediaNotificationDbContext>().UseNpgsql(DbConnection,
        x => x.MigrationsHistoryTable("__EFMigrationsHistory", MediaNotificationSchema.Name)).Options);

    [PostgresFact]
    public async Task Migration_EnforcesNotificationKey_LikeKey_AndRatingRange()
    {
        await using (var setup = Context()) await setup.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        var (user, source) = (Guid.NewGuid(), Guid.NewGuid());
        Notification Make(int version) => Notification.Create(user, "SubmissionGraded", source, version, "t", "Assignment", Guid.NewGuid(), now);

        await using (var db = Context())
        {
            db.Notifications.AddRange(Make(1), Make(2));
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            db.Notifications.Add(Make(1));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }

        var post = ClassPost.Create(Guid.NewGuid(), user, "Student", "hi", now);
        await using (var db = Context())
        {
            db.Posts.Add(post);
            db.Likes.Add(PostLike.Create(post.Id, user, now));
            await db.SaveChangesAsync();
        }
        await using (var db = Context())
        {
            db.Likes.Add(PostLike.Create(post.Id, user, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = Context())
        {
            db.Feedbacks.Add(StudentFeedback.Create(user, "t", "c", 6, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
