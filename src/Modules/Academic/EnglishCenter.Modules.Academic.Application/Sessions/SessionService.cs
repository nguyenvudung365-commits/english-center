using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

public sealed class SessionService(ISessionStore store, IAcademicStore classes, ClassAccess access,
    IAccountAccess accounts, TimeProvider clock, IEventBus events, ILogger<SessionService> log)
{
    public async Task<SessionResponse> CreateAsync(CreateSessionRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var (session, item) = await ValidateScheduleAsync(request, ct);
        await store.CreateWithoutTeacherOverlapAsync(session, item.TeacherId, ct);
        var teacher = await classes.FindTeacherAsync(item.TeacherId, ct);
        return Response(session, item, teacher?.FullName ?? string.Empty);
    }

    public async Task<SessionResponse> ChangeAsync(Guid id, int expectedRevision, CreateSessionRequest? request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var existing = await store.FindAsync(id, ct) ?? throw new AcademicFailure(404, "Session not found.");
        if (request is not null && request.ClassId != existing.ClassId)
            throw Invalid(new() { [nameof(request.ClassId)] = ["The class of an existing session cannot change."] });
        Session? replacement = null;
        Class item;
        if (request is not null) (replacement, item) = await ValidateScheduleAsync(request, ct);
        else item = await classes.FindClassAsync(existing.ClassId, ct) ?? throw new AcademicFailure(404, "Class not found.");
        var changed = await store.ChangeAsync(id, item.Id, item.TeacherId, expectedRevision, replacement, ct);
        try
        {
            await events.PublishAsync(new SessionScheduleChangedIntegrationEvent(changed.Id, item.Id, item.TeacherId, changed.Revision, changed.IsCancelled),
                ct, throwOnHandlerFailure: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // shortcut: notifications are best effort after commit; use a durable outbox if delivery must be guaranteed.
            log.LogError(ex, "Session {SessionId} revision {Revision} saved but notifications failed", changed.Id, changed.Revision);
        }
        var teacher = await classes.FindTeacherAsync(item.TeacherId, ct);
        return Response(changed, item, teacher?.FullName ?? string.Empty);
    }

    internal async Task<(Session Session, Class Item)> ValidateScheduleAsync(CreateSessionRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ClassId is null || request.ClassId == Guid.Empty) errors[nameof(request.ClassId)] = ["Class is required."];
        if (request.StartsAtUtc is null || request.StartsAtUtc.Value.UtcDateTime == default)
            errors[nameof(request.StartsAtUtc)] = ["A valid start with an explicit ISO offset is required."];
        if (request.EndsAtUtc is null || request.EndsAtUtc.Value.UtcDateTime == default)
            errors[nameof(request.EndsAtUtc)] = ["A valid end with an explicit ISO offset is required."];
        else if (request.StartsAtUtc is not null && request.EndsAtUtc <= request.StartsAtUtc)
            errors[nameof(request.EndsAtUtc)] = ["End must be after start."];
        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Trim().Length > Session.UnitMaxLength)
            errors[nameof(request.Unit)] = ["Unit must contain 1–100 characters."];
        if (string.IsNullOrWhiteSpace(request.Lesson) || request.Lesson.Trim().Length > Session.LessonMaxLength)
            errors[nameof(request.Lesson)] = ["Lesson must contain 1–200 characters."];
        SessionSkills skills = 0;
        if (request.Skills is null || request.Skills.Length == 0)
            errors[nameof(request.Skills)] = ["Select one or more distinct named skills."];
        else
            foreach (var value in request.Skills)
            {
                var name = Enum.GetNames<SessionSkills>().FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
                if (name is null) { errors[nameof(request.Skills)] = ["Unknown skill."]; continue; }
                var skill = Enum.Parse<SessionSkills>(name);
                if ((skills & skill) != 0) errors[nameof(request.Skills)] = ["Skills must be distinct."];
                skills |= skill;
            }
        var typeName = Enum.GetNames<SessionType>().FirstOrDefault(x => string.Equals(x, request.SessionType, StringComparison.OrdinalIgnoreCase));
        var type = typeName is null ? default : Enum.Parse<SessionType>(typeName);
        if (typeName is null) errors[nameof(request.SessionType)] = ["Session type must be Regular, MockTest or FinalTest."];
        var slot = string.IsNullOrWhiteSpace(request.ExamSlot) ? null : request.ExamSlot.Trim();
        if (slot?.Length > Session.ExamSlotMaxLength || (type == SessionType.Regular && slot is not null))
            errors[nameof(request.ExamSlot)] = ["Exam slot is limited to 100 characters and test sessions."];
        if (errors.Count > 0) throw Invalid(errors);

        SessionWindow.ValidatePrecision(request.StartsAtUtc!.Value.UtcDateTime, nameof(request.StartsAtUtc));
        SessionWindow.ValidatePrecision(request.EndsAtUtc!.Value.UtcDateTime, nameof(request.EndsAtUtc));
        var item = await classes.FindClassAsync(request.ClassId!.Value, ct) ?? throw new AcademicFailure(404, "Class not found.");
        if (!await accounts.IsActiveInRoleAsync(item.TeacherId, Roles.Teacher, ct))
            throw Invalid(new() { ["TeacherId"] = ["The assigned teacher must be an active Teacher."] });
        var start = request.StartsAtUtc!.Value.UtcDateTime;
        var end = request.EndsAtUtc!.Value.UtcDateTime;
        SessionWindow.Validate(item, start, end);
        var session = Session.Create(item.Id, request.Unit!, request.Lesson!, skills, type, slot, start, end, clock.GetUtcNow().UtcDateTime);
        return (session, item);
    }

    public async Task<SessionResponse> GetAsync(Guid id, CancellationToken ct)
    {
        await access.RequireUserAsync(ct);
        var session = await store.FindAsync(id, ct) ?? throw new AcademicFailure(404, "Session not found.");
        await access.RequireReadAsync(session.ClassId, ct);
        var item = await classes.FindClassAsync(session.ClassId, ct) ?? throw new AcademicFailure(404, "Class not found.");
        var teacher = await classes.FindTeacherAsync(item.TeacherId, ct);
        return Response(session, item, teacher?.FullName ?? string.Empty);
    }

    public async Task<IReadOnlyList<SessionResponse>> RangeAsync(SessionRangeQuery query, CancellationToken ct)
    {
        await access.RequireUserAsync(ct);
        var errors = new Dictionary<string, string[]>();
        if (query.FromUtc is null || query.FromUtc.Value.UtcDateTime == default) errors[nameof(query.FromUtc)] = ["A valid range start is required."];
        if (query.ToUtc is null || query.ToUtc.Value.UtcDateTime == default) errors[nameof(query.ToUtc)] = ["A valid range end is required."];
        if (query.FromUtc is not null && query.ToUtc is not null &&
            (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > TimeSpan.FromDays(93)))
            errors[nameof(query.ToUtc)] = ["Range must be positive and no longer than 93 elapsed days."];
        if (query.ClassId == Guid.Empty) errors[nameof(query.ClassId)] = ["Class ID must not be empty."];
        if (errors.Count > 0) throw Invalid(errors);
        SessionWindow.ValidatePrecision(query.FromUtc!.Value.UtcDateTime, nameof(query.FromUtc));
        SessionWindow.ValidatePrecision(query.ToUtc!.Value.UtcDateTime, nameof(query.ToUtc));
        IReadOnlyList<Class> visible;
        if (query.ClassId is { } id)
        {
            await access.RequireReadAsync(id, ct);
            visible = [await classes.FindClassAsync(id, ct) ?? throw new AcademicFailure(404, "Class not found.")];
        }
        else visible = await access.VisibleClassesAsync(ct);
        var byId = visible.ToDictionary(x => x.Id);
        var sessions = await store.RangeAsync(query.FromUtc!.Value.UtcDateTime, query.ToUtc!.Value.UtcDateTime, byId.Keys.ToArray(), ct);
        var teachers = (await classes.TeachersAsync(ct)).ToDictionary(x => x.Id);
        return sessions.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Select(x =>
        {
            var item = byId[x.ClassId];
            return Response(x, item, teachers.GetValueOrDefault(item.TeacherId)?.FullName ?? string.Empty);
        }).ToList();
    }

    internal static SessionResponse Response(Session session, Class item, string teacherName) =>
        new(session.Id, item.Id, item.Code, item.Name, item.TeacherId, teacherName, session.Unit, session.Lesson,
            Enum.GetValues<SessionSkills>().Where(x => (session.Skills & x) != 0).Select(x => x.ToString()).ToArray(),
            session.SessionType.ToString(), session.ExamSlot, Utc(session.StartsAtUtc), Utc(session.EndsAtUtc), Utc(session.CreatedAtUtc), session.Revision, session.IsCancelled);

    // SQLite materializes DateTime with Unspecified kind; these columns always contain UTC.
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static AcademicFailure Invalid(Dictionary<string, string[]> errors) => new(400, "One or more validation errors occurred.", errors);
}
