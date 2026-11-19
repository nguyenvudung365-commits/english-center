using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Academic.Tests;

internal sealed class FakeAcademicStore : IAcademicStore
{
    private readonly List<Class> _pendingClasses = [];
    private readonly List<Teacher> _pendingTeachers = [];

    public List<Class> Classes { get; } = [];
    public List<Course> Courses { get; } = [];
    public List<Teacher> Teachers { get; } = [];
    public bool ConflictOnSave { get; set; }
    public int ConflictsRemaining { get; set; }
    public int SaveAttempts { get; private set; }
    public int ReloadCount { get; private set; }
    public Action? BeforeReloadTeacher { get; set; }
    public int SaveCount { get; private set; }

    public Task<bool> ClassCodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        Task.FromResult(Classes.Any(x => x.Code == normalizedCode));
    public Task<Class?> FindClassAsync(Guid id, CancellationToken ct) => Task.FromResult(Classes.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<Class>> ClassesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Class>>(Classes);
    public Task<Course?> FindCourseAsync(Guid id, CancellationToken ct) => Task.FromResult(Courses.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<Course>> CoursesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Course>>(Courses);
    public Task<Teacher?> FindTeacherAsync(Guid id, CancellationToken ct) => Task.FromResult(Teachers.SingleOrDefault(x => x.Id == id));
    public Task<Teacher?> ReloadTeacherAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ReloadCount++;
        _pendingTeachers.Clear();
        BeforeReloadTeacher?.Invoke();
        return Task.FromResult(Teachers.SingleOrDefault(x => x.Id == id));
    }
    public Task<IReadOnlyList<Teacher>> TeachersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Teacher>>(Teachers);
    public void AddClass(Class academicClass) => _pendingClasses.Add(academicClass);
    public Task SaveNewClassAsync(Class academicClass, CancellationToken ct) { AddClass(academicClass); return SaveAsync(ct); }
    public void AddCourse(Course course) => Courses.Add(course);
    public void AddTeacher(Teacher teacher) => _pendingTeachers.Add(teacher);

    public Task SaveAsync(CancellationToken ct)
    {
        SaveAttempts++;
        if (ConflictOnSave || ConflictsRemaining > 0)
        {
            if (ConflictsRemaining > 0) ConflictsRemaining--;
            _pendingClasses.Clear();
            _pendingTeachers.Clear();
            throw new AcademicFailure(409, "Academic data already exists.");
        }
        Classes.AddRange(_pendingClasses);
        Teachers.AddRange(_pendingTeachers);
        _pendingClasses.Clear();
        _pendingTeachers.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingEventBus : IEventBus
{
    public List<IIntegrationEvent> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default,
        bool throwOnHandlerFailure = false)
        where TEvent : IIntegrationEvent
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeClassPolicy : ICurrentUser, IAccountAccess, IEnrollmentAccess, IClassDirectory
{
    private readonly FakeAcademicStore store;
    public FakeClassPolicy(FakeAcademicStore store)
    {
        this.store = store;
        Accounts[UserId!.Value] = Roles.Admin;
    }

    public bool IsAuthenticated { get; set; } = true;
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? Role { get; set; } = Roles.Admin;
    public Dictionary<Guid, string> Accounts { get; } = [];
    public HashSet<(Guid ClassId, Guid StudentId)> Enrollments { get; } = [];
    public List<(Guid UserId, string? Role)> AccountChecks { get; } = [];
    public bool IsInRole(string role) => Role == role;
    public ClassAccess Access => new(this, this, this, this, store);
    public void As(string role, Guid? id = null)
    {
        UserId = id ?? Guid.NewGuid(); Role = role;
        Accounts[UserId.Value] = role;
    }
    public Task<bool> IsActiveInRoleAsync(Guid userId, string? expectedRole, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AccountChecks.Add((userId, expectedRole));
        return Task.FromResult(Accounts.TryGetValue(userId, out var role) && role == expectedRole);
    }
    public Task<bool> IsActiveAsync(Guid classId, Guid studentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Enrollments.Contains((classId, studentId)));
    }
    public Task<IReadOnlyList<Guid>> TransferredClassIdsAsync(Guid studentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<ClassIdentity?> FindClassAsync(Guid classId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var item = store.Classes.SingleOrDefault(x => x.Id == classId);
        return Task.FromResult(item is null ? null : new ClassIdentity(item.Id, item.TeacherId));
    }
}
