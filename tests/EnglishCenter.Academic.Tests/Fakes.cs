using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;

namespace EnglishCenter.Academic.Tests;

internal sealed class FakeAcademicStore : IAcademicStore
{
    private readonly List<Class> _pendingClasses = [];
    private readonly List<Teacher> _pendingTeachers = [];

    public List<Class> Classes { get; } = [];
    public List<Course> Courses { get; } = [];
    public List<Teacher> Teachers { get; } = [];
    public bool ConflictOnSave { get; set; }
    public int SaveCount { get; private set; }

    public Task<bool> ClassCodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        Task.FromResult(Classes.Any(x => x.Code == normalizedCode));
    public Task<Class?> FindClassAsync(Guid id, CancellationToken ct) => Task.FromResult(Classes.SingleOrDefault(x => x.Id == id));
    public Task<Course?> FindCourseAsync(Guid id, CancellationToken ct) => Task.FromResult(Courses.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<Course>> CoursesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Course>>(Courses);
    public Task<Teacher?> FindTeacherAsync(Guid id, CancellationToken ct) => Task.FromResult(Teachers.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<Teacher>> TeachersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Teacher>>(Teachers);
    public void AddClass(Class academicClass) => _pendingClasses.Add(academicClass);
    public void AddTeacher(Teacher teacher) => _pendingTeachers.Add(teacher);

    public Task SaveAsync(CancellationToken ct)
    {
        if (ConflictOnSave)
        {
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

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
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
