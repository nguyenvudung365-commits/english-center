using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Attendance;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Modules.Academic.Application.Attendance;

public sealed class AttendanceService(IAttendanceStore store, ISessionStore sessions, IAcademicStore classes,
    ClassAccess access, ICurrentUser current, IEnrollmentAccess enrollment, IEnrollmentRoster roster,
    IStudentDisplayDirectory displays, IEventBus events, TimeProvider clock, ILogger<AttendanceService> logger)
{
    public async Task<AttendanceRow> RecordAsync(Guid sessionId, Guid studentId, RecordAttendanceRequest request, CancellationToken ct)
    {
        var actor = await access.RequireUserAsync(ct);
        if (current.Role != Roles.Teacher) throw Forbidden("Only the assigned Teacher can record attendance.");
        Id(sessionId); Id(studentId);
        var status = ParseStatus(request.Status, false)!.Value;
        if (request.ExpectedRevision is < 1) throw new AcademicFailure(400, "Expected revision must be null or a positive integer.");
        var rawNow = clock.GetUtcNow().UtcDateTime;
        var session = await sessions.FindAsync(sessionId, ct) ?? throw Missing("Session");
        var item = await classes.FindClassAsync(session.ClassId, ct) ?? throw Missing("Class");
        await Recheck(session, item.TeacherId, ct);
        var result = await store.WriteAsync(sessionId, studentId, status, request.ExpectedRevision, actor, rawNow, Recheck, ct);
        if (result.Source is { } source)
        {
            try { await PublishAsync(source, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogError(ex, "Attendance {AttendanceId} revision {Revision} committed; dispatch failed. Source retained for replay.", source.AttendanceId, source.Revision);
            }
        }
        return Row(result.Row);

        async Task Recheck(Session liveSession, Guid teacherId, CancellationToken token)
        {
            if (await access.RequireUserAsync(token) != actor || current.Role != Roles.Teacher || teacherId != actor)
                throw Forbidden("Only the live assigned Teacher can record attendance.");
            if (!InWindow(liveSession, rawNow)) throw Forbidden("The attendance write window is closed.");
            // Cross-module authority is the last live check, not a cross-schema transaction lock.
            if (!await enrollment.IsActiveAsync(liveSession.ClassId, studentId, token))
                throw Forbidden("The target must be a live enrolled Student.");
        }
    }

    public async Task<RosterPage> RosterAsync(Guid sessionId, PageQuery page, CancellationToken ct)
    {
        var actor = await access.RequireUserAsync(ct);
        if (current.Role == Roles.Student) throw Forbidden("Students cannot read a peer roster.");
        Id(sessionId); page.Validate();
        var rawNow = clock.GetUtcNow().UtcDateTime;
        var session = await sessions.FindAsync(sessionId, ct) ?? throw Missing("Session");
        await access.RequireReadAsync(session.ClassId, ct);
        var rows = (await store.ForSessionAsync(sessionId, ct)).ToDictionary(x => x.StudentId);
        var candidates = (await roster.GetActiveStudentIdsAsync(session.ClassId, ct)).Union(rows.Keys).OrderBy(x => x).ToArray();
        var names = (await displays.FindActiveAsync(candidates, ct)).ToDictionary(x => x.StudentId);
        var items = new List<RosterItem>();
        foreach (var id in candidates)
        {
            var active = await enrollment.IsActiveAsync(session.ClassId, id, ct);
            if (!active && !rows.ContainsKey(id)) continue;
            var display = names.GetValueOrDefault(id);
            items.Add(new(id, display?.FullName, display?.StudentCode, active, rows.TryGetValue(id, out var row) ? Row(row) : null));
        }
        var item = await classes.FindClassAsync(session.ClassId, ct) ?? throw Missing("Class");
        var offset = (long)(page.Page - 1) * page.PageSize;
        var selected = offset >= items.Count ? [] : items.Skip((int)offset).Take(page.PageSize).ToArray();
        return new(session.Id, session.ClassId, Utc(AttendanceEntity.Microseconds(rawNow)),
            current.Role == Roles.Teacher && actor == item.TeacherId && InWindow(session, rawNow), Utc(session.EndsAtUtc.AddHours(24)),
            selected, page.Page, page.PageSize, items.Count);
    }

    public async Task<Page<AttendanceRow>> ListAsync(AttendanceQuery query, CancellationToken ct)
    {
        var filter = await ScopeAsync(new(query.ClassId, query.SessionId, query.StudentId, query.Status), ct);
        var page = new PageQuery(query.Page, query.PageSize); page.Validate();
        var result = await store.ListAsync(filter, page, ct);
        return new(result.Items.Select(Row).ToArray(), result.PageNumber, result.PageSize, result.Total);
    }

    public async Task<Summary> SummaryAsync(SummaryQuery query, CancellationToken ct) =>
        await store.SummaryAsync(await ScopeAsync(query, ct), ct);

    private async Task<AttendanceFilter> ScopeAsync(SummaryQuery query, CancellationToken ct)
    {
        var actor = await access.RequireUserAsync(ct);
        if (query.ClassId is { } classId) Id(classId);
        if (query.SessionId is { } sessionId) Id(sessionId);
        if (query.StudentId is { } studentId) Id(studentId);
        var status = ParseStatus(query.Status, true);
        var student = query.StudentId;
        if (current.Role == Roles.Student)
        {
            if (student is not null && student != actor) throw Forbidden("Only your own attendance is visible.");
            student = actor;
        }
        else if (current.Role == Roles.Teacher && query.ClassId is null)
            throw new AcademicFailure(400, "Teacher attendance queries require classId.");
        if (query.ClassId is { } classFilter)
        {
            if (current.Role == Roles.Student)
            {
                if (await classes.FindClassAsync(classFilter, ct) is null) throw Missing("Class");
            }
            else await access.RequireReadAsync(classFilter, ct);
        }
        if (query.SessionId is { } sessionFilter)
        {
            var session = await sessions.FindAsync(sessionFilter, ct) ?? throw Missing("Session");
            if (query.ClassId is not null && query.ClassId != session.ClassId)
                throw new AcademicFailure(400, "Class and session filters must match.");
            if (current.Role != Roles.Student) await access.RequireReadAsync(session.ClassId, ct);
        }
        return new(query.ClassId, query.SessionId, student, status);
    }

    public async Task<Page<AttendanceRevisionResponse>> HistoryAsync(Guid id, PageQuery page, CancellationToken ct)
    {
        await RequireRowAsync(id, ct); page.Validate();
        var result = await store.HistoryAsync(id, page, ct);
        return new(result.Items.Select(x => new AttendanceRevisionResponse(x.AttendanceId, x.Revision, x.PreviousStatus?.ToString(),
            x.Status.ToString(), x.ChangedBy, Utc(x.ChangedAtUtc))).ToArray(), result.PageNumber, result.PageSize, result.Total);
    }

    private async Task RequireRowAsync(Guid id, CancellationToken ct)
    {
        var actor = await access.RequireUserAsync(ct); Id(id);
        var row = await store.FindAsync(id, ct) ?? throw Missing("Attendance");
        if (current.Role == Roles.Student)
        {
            if (row.StudentId != actor) throw Forbidden("Only your own attendance is visible.");
        }
        else await access.RequireReadAsync(row.ClassId, ct);
    }

    public async Task<ReplayDispatchResponse> ReplayAsync(Guid id, ReplayAttendanceRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct); Id(id);
        if (request.Revision is null or < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new AcademicFailure(400, "A positive revision and a reason of 1–500 characters are required.");
        if (await store.FindAsync(id, ct) is null) throw Missing("Attendance");
        var source = await store.SourceAsync(id, request.Revision.Value, ct) ?? throw Missing("Attendance revision");
        var attempt = AttendanceReplayAttempt.Create(id, source.Revision, current.UserId!.Value, clock.GetUtcNow().UtcDateTime, request.Reason);
        await store.AddReplayAsync(attempt, ct);
        try { await PublishAsync(source, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            attempt.Complete(false, clock.GetUtcNow().UtcDateTime);
            await store.CompleteReplayAsync(attempt, ct);
            logger.LogError(ex, "Attendance replay {AttemptId} failed for {AttendanceId} revision {Revision}.", attempt.Id, id, source.Revision);
            throw new AcademicFailure(503, "Replay dispatch failed. The failed attempt is retained in the audit.");
        }
        attempt.Complete(true, clock.GetUtcNow().UtcDateTime);
        await store.CompleteReplayAsync(attempt, ct);
        return new(attempt.Id, id, source.Revision, "Dispatched");
    }

    public async Task<Page<ReplayAttempt>> ReplaysAsync(Guid id, PageQuery page, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct); Id(id); page.Validate();
        if (await store.FindAsync(id, ct) is null) throw Missing("Attendance");
        var result = await store.ReplaysAsync(id, page, ct);
        return new(result.Items.Select(x => new ReplayAttempt(x.Id, x.AttendanceId, x.Revision, x.RequestedBy, Utc(x.RequestedAtUtc),
            x.Reason, x.DispatchStatus.ToString(), x.CompletedAtUtc is { } completed ? Utc(completed) : null)).ToArray(), result.PageNumber, result.PageSize, result.Total);
    }

    private Task PublishAsync(AttendanceRevision source, CancellationToken ct) => events.PublishAsync(
        new AttendanceRevisionRecordedIntegrationEvent(source.AttendanceId, source.SessionId, source.ClassId, source.StudentId,
            source.Status.ToString(), source.Revision, source.ChangedBy, DateTime.SpecifyKind(source.ChangedAtUtc, DateTimeKind.Utc)), ct, throwOnHandlerFailure: true);
    private static bool InWindow(Session session, DateTime rawNow) => !session.IsCancelled && rawNow >= session.StartsAtUtc && rawNow <= session.EndsAtUtc.AddHours(24);
    private static AttendanceStatus? ParseStatus(string? value, bool optional)
    {
        if (value is null && optional) return null;
        if (value is null || !Enum.GetNames<AttendanceStatus>().Contains(value, StringComparer.Ordinal))
            throw new AcademicFailure(400, "An exact named attendance status is required.");
        return Enum.Parse<AttendanceStatus>(value);
    }
    private static void Id(Guid id) { if (id == Guid.Empty) throw new AcademicFailure(400, "A nonempty identifier is required."); }
    private static AcademicFailure Forbidden(string message) => new(403, message);
    private static AcademicFailure Missing(string name) => new(404, $"{name} not found.");
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static AttendanceRow Row(AttendanceEntity x) => new(x.Id, x.SessionId, x.ClassId, x.StudentId, x.Status.ToString(), x.Revision,
        x.RecordedBy, Utc(x.RecordedAtUtc), x.UpdatedBy, Utc(x.UpdatedAtUtc));
}
