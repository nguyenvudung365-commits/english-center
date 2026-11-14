using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishCenter.Academic.Tests;

public class UserRegisteredIntegrationEventHandlerTests
{
    private readonly FakeAcademicStore _store = new();

    private UserRegisteredIntegrationEventHandler Handler() =>
        new(_store, NullLogger<UserRegisteredIntegrationEventHandler>.Instance);

    [Fact]
    public async Task TeacherRegistered_AddsTeacherReadModel()
    {
        var evt = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "teacher@example.test", "Nguyen Van Giao", Roles.Teacher);
        await Handler().HandleAsync(evt);

        var teacher = Assert.Single(_store.Teachers);
        Assert.Equal(evt.UserId, teacher.Id);
        Assert.Equal("Nguyen Van Giao", teacher.FullName);
        Assert.Equal("teacher@example.test", teacher.Email);
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    public async Task OtherRoles_AreIgnored(string role)
    {
        await Handler().HandleAsync(new UserRegisteredIntegrationEvent(Guid.NewGuid(), "user@example.test", "User", role));
        Assert.Empty(_store.Teachers);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task SameVersionWithChangedPayload_KeepsOriginalTeacher()
    {
        var evt = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "teacher@example.test", "Nguyen Van Giao", Roles.Teacher);
        await Handler().HandleAsync(evt);
        await Handler().HandleAsync(evt with { FullName = "Nguyen Van Giao B" });

        var teacher = Assert.Single(_store.Teachers);
        Assert.Equal("Nguyen Van Giao", teacher.FullName);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task UnresolvedConflict_RetriesTwiceThenPropagates()
    {
        _store.ConflictOnSave = true;
        var failure = await Assert.ThrowsAsync<EnglishCenter.Modules.Academic.Application.AcademicFailure>(() =>
            Handler().HandleAsync(new UserRegisteredIntegrationEvent(Guid.NewGuid(), "t@example.test", "T", Roles.Teacher)));
        Assert.Equal(409, failure.Status);
        Assert.Equal(3, _store.SaveAttempts);
        Assert.Equal(2, _store.ReloadCount);
        Assert.Empty(_store.Teachers);
    }

    [Fact]
    public async Task TransientConflict_ReloadsThenPersistsSnapshot()
    {
        _store.ConflictsRemaining = 1;
        await Handler().HandleAsync(new UserRegisteredIntegrationEvent(Guid.NewGuid(), "t@example.test", "T", Roles.Teacher, 3));
        Assert.Equal(3L, Assert.Single(_store.Teachers).ProfileVersion);
        Assert.Equal(2, _store.SaveAttempts);
        Assert.Equal(1, _store.ReloadCount);
    }

    [Fact]
    public async Task ConcurrentNewerSnapshot_IsReloadedAndNeverOverwritten()
    {
        var id = Guid.NewGuid();
        _store.ConflictsRemaining = 1;
        _store.BeforeReloadTeacher = () => _store.Teachers.Add(Teacher.Create(id, "Newest", "newest@example.test", 5));
        await Handler().HandleAsync(new UserProfileUpdatedIntegrationEvent(id, "old@example.test", "Old", Roles.Teacher, 4));
        var teacher = Assert.Single(_store.Teachers);
        Assert.Equal(5L, teacher.ProfileVersion);
        Assert.Equal("Newest", teacher.FullName);
        Assert.Equal(1, _store.SaveAttempts);
        Assert.Equal(1, _store.ReloadCount);
    }

    [Fact]
    public async Task NewerProfile_AppliesOnce_AndRegistrationReplayCannotRegressIt()
    {
        var id = Guid.NewGuid();
        await Handler().HandleAsync(new UserRegisteredIntegrationEvent(id, "original@example.test", "Original", Roles.Teacher, 0));
        var updated = new UserProfileUpdatedIntegrationEvent(id, "new@example.test", "New", Roles.Teacher, 2);
        await Handler().HandleAsync(updated);
        await Handler().HandleAsync(updated);
        await Handler().HandleAsync(new UserRegisteredIntegrationEvent(id, "old@example.test", "Old", Roles.Teacher, 1));
        var teacher = Assert.Single(_store.Teachers);
        Assert.Equal("New", teacher.FullName);
        Assert.Equal("new@example.test", teacher.Email);
        Assert.Equal(2L, teacher.ProfileVersion);
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public void Teacher_RejectsMissingProfile()
    {
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.Empty, "T", "t@example.test"));
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.NewGuid(), " ", "t@example.test"));
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.NewGuid(), "T", ""));
    }
}
