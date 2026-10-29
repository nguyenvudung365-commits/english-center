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
    public async Task SameEventTwice_KeepsSingleTeacher()
    {
        var evt = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "teacher@example.test", "Nguyen Van Giao", Roles.Teacher);
        await Handler().HandleAsync(evt);
        await Handler().HandleAsync(evt with { FullName = "Nguyen Van Giao B" });

        var teacher = Assert.Single(_store.Teachers);
        Assert.Equal("Nguyen Van Giao B", teacher.FullName);
    }

    [Fact]
    public async Task ConcurrentInsertConflict_IsSwallowed()
    {
        _store.ConflictOnSave = true;
        await Handler().HandleAsync(new UserRegisteredIntegrationEvent(Guid.NewGuid(), "t@example.test", "T", Roles.Teacher));
        Assert.Empty(_store.Teachers);
    }

    [Fact]
    public void Teacher_RejectsMissingProfile()
    {
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.Empty, "T", "t@example.test"));
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.NewGuid(), " ", "t@example.test"));
        Assert.Throws<ArgumentException>(() => Teacher.Create(Guid.NewGuid(), "T", ""));
    }
}
