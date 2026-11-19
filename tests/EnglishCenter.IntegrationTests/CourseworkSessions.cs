using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.IntegrationTests;

// AC-05: bài tập phải gắn một buổi học của lớp trước khi công bố. Tạo nhanh một buổi (ngày mai) cho test.
internal static class CourseworkSessions
{
    public static async Task<Guid> SeedAsync(IServiceProvider services, Guid classId, string unit = "Unit 1", string lesson = "Lesson 1")
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(1);
        var session = Session.Create(classId, unit, lesson, SessionSkills.Speaking, SessionType.Regular, null, start, start.AddHours(2),
            DateTime.UtcNow);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
