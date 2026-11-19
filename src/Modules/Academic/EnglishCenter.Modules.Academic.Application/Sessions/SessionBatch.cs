using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

public sealed record BatchSessionSlot(DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc);

// One class and one set of lesson fields; every slot becomes a session validated by the single-session rules.
public sealed record CreateSessionBatchRequest(Guid? ClassId, string? Unit, string? Lesson, string[]? Skills,
    string? SessionType, string? ExamSlot, BatchSessionSlot?[]? Slots);

public sealed record SessionBatchPreviewItem(int Index, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, string Status, string? Message);

public sealed record SessionBatchPreview(bool CanCreate, IReadOnlyList<SessionBatchPreviewItem> Items);

public interface ISessionBatchStore
{
    /// <summary>Indexes (into <paramref name="windows"/>) that overlap a live session of the teacher. Read-only.</summary>
    Task<IReadOnlyList<int>> TeacherOverlapIndexesAsync(Guid teacherId, IReadOnlyList<(DateTime Start, DateTime End)> windows, CancellationToken ct);

    /// <summary>All-or-nothing insert under the same teacher/class locks as single-session creation.</summary>
    Task CreateManyWithoutTeacherOverlapAsync(IReadOnlyList<Session> sessions, Guid teacherId, CancellationToken ct);
}

public sealed class SessionBatchService(SessionService single, ISessionBatchStore store, IAcademicStore classes, ClassAccess access)
{
    public const int MaxSlots = 62;
    private const string Ok = "Ok", Invalid = "Invalid", BatchOverlap = "BatchOverlap", TeacherOverlap = "TeacherOverlap";

    private sealed class Row(int index, BatchSessionSlot slot)
    {
        internal int Index { get; } = index;
        internal BatchSessionSlot Slot { get; } = slot;
        internal Session? Session { get; set; }
        internal string Status { get; set; } = Ok;
        internal string? Message { get; set; }
        internal IReadOnlyDictionary<string, string[]>? Errors { get; set; }
    }

    public async Task<SessionBatchPreview> PreviewAsync(CreateSessionBatchRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var (rows, item) = await PlanAsync(request, ct);
        var open = rows.Where(x => x.Status == Ok).ToList();
        if (item is not null && open.Count > 0)
        {
            var hits = await store.TeacherOverlapIndexesAsync(item.TeacherId, open.Select(x => (x.Session!.StartsAtUtc, x.Session.EndsAtUtc)).ToList(), ct);
            foreach (var hit in hits) Flag(open[hit], TeacherOverlap, "Teacher already has an overlapping session.");
        }
        return new(rows.All(x => x.Status == Ok), rows.Select(x => new SessionBatchPreviewItem(x.Index, x.Slot.StartsAtUtc, x.Slot.EndsAtUtc, x.Status, x.Message)).ToList());
    }

    public async Task<IReadOnlyList<SessionResponse>> CreateAsync(CreateSessionBatchRequest request, CancellationToken ct)
    {
        await access.RequireManagerAsync(ct);
        var (rows, item) = await PlanAsync(request, ct);
        if (rows.Any(x => x.Status == Invalid))
        {
            var errors = new Dictionary<string, string[]>();
            foreach (var row in rows.Where(x => x.Status == Invalid))
                foreach (var (key, messages) in row.Errors ?? new Dictionary<string, string[]> { ["Slots"] = [row.Message!] })
                {
                    // Start/end belong to a slot; every other field is shared, so report it once.
                    var name = key is nameof(BatchSessionSlot.StartsAtUtc) or nameof(BatchSessionSlot.EndsAtUtc) ? $"Slots[{row.Index}].{key}" : key;
                    errors[name] = errors.TryGetValue(name, out var known) ? known.Union(messages).ToArray() : messages;
                }
            throw new AcademicFailure(400, "One or more validation errors occurred.", errors);
        }
        if (rows.Any(x => x.Status == BatchOverlap))
            throw new AcademicFailure(409, "Selected sessions overlap each other.",
                rows.Where(x => x.Status == BatchOverlap).ToDictionary(x => $"Slots[{x.Index}]", x => new[] { x.Message! }));
        var sessions = rows.Select(x => x.Session!).ToList();
        await store.CreateManyWithoutTeacherOverlapAsync(sessions, item!.TeacherId, ct);
        var teacher = await classes.FindTeacherAsync(item.TeacherId, ct);
        return sessions.Select(x => SessionService.Response(x, item, teacher?.FullName ?? string.Empty)).ToList();
    }

    private async Task<(List<Row> Rows, Class? Item)> PlanAsync(CreateSessionBatchRequest request, CancellationToken ct)
    {
        if (request.Slots is null || request.Slots.Length is 0 or > MaxSlots)
            throw new AcademicFailure(400, "One or more validation errors occurred.",
                new Dictionary<string, string[]> { ["Slots"] = [$"Select 1–{MaxSlots} dates."] });
        var rows = new List<Row>();
        Class? item = null;
        for (var i = 0; i < request.Slots.Length; i++)
        {
            var slot = request.Slots[i] ?? new BatchSessionSlot(null, null);
            var row = new Row(i, slot);
            rows.Add(row);
            try
            {
                (row.Session, item) = await single.ValidateScheduleAsync(new(request.ClassId, slot.StartsAtUtc, slot.EndsAtUtc,
                    request.Unit, request.Lesson, request.Skills, request.SessionType, request.ExamSlot), ct);
            }
            catch (AcademicFailure failure) when (failure.Status == 400)
            {
                row.Status = Invalid;
                row.Errors = failure.Errors;
                row.Message = string.Join(" ", failure.Errors?.Values.SelectMany(x => x) ?? [failure.Message]);
            }
        }
        var valid = rows.Where(x => x.Status == Ok).ToList();
        for (var a = 0; a < valid.Count; a++)
            for (var b = a + 1; b < valid.Count; b++)
                if (valid[a].Session!.StartsAtUtc < valid[b].Session!.EndsAtUtc && valid[b].Session!.StartsAtUtc < valid[a].Session!.EndsAtUtc)
                {
                    Flag(valid[a], BatchOverlap, $"Overlaps selected slot {valid[b].Index + 1}.");
                    Flag(valid[b], BatchOverlap, $"Overlaps selected slot {valid[a].Index + 1}.");
                }
        return (rows, item);
    }

    private static void Flag(Row row, string status, string message)
    {
        row.Status = status;
        row.Message ??= message;
    }
}
