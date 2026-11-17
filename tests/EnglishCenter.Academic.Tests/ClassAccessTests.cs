using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using Xunit;
using static EnglishCenter.Academic.Tests.SessionDomainTests;

namespace EnglishCenter.Academic.Tests;

public sealed class ClassAccessTests
{
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("null-id", 401)]
    [InlineData("empty-id", 401)]
    [InlineData("null-role", 403)]
    [InlineData("wrong-case", 403)]
    [InlineData("unknown-role", 403)]
    [InlineData("deleted", 403)]
    [InlineData("role-changed", 403)]
    public async Task Guard_RejectsInvalidSubjectOrNonLiveRole(string scenario, int status)
    {
        var policy = new FakeClassPolicy(new());
        switch (scenario)
        {
            case "anonymous": policy.IsAuthenticated = false; break;
            case "null-id": policy.UserId = null; break;
            case "empty-id": policy.UserId = Guid.Empty; break;
            case "null-role": policy.Role = null; break;
            case "wrong-case": policy.As("admin"); break;
            case "unknown-role": policy.As("Owner"); break;
            case "deleted": policy.Accounts.Clear(); break;
            case "role-changed": policy.Accounts[policy.UserId!.Value] = Roles.Student; break;
        }
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireReadAsync(Guid.NewGuid(), default));
        Assert.Equal(status, failure.Status);
    }

    [Theory]
    [InlineData(Roles.Admin, 2)]
    [InlineData(Roles.Staff, 2)]
    [InlineData(Roles.Teacher, 1)]
    [InlineData(Roles.Student, 1)]
    public async Task Reads_UseOwnershipOrCurrentEnrollment_AndKeepMissing404Distinct(string role, int count)
    {
        var store = new FakeAcademicStore();
        var policy = new FakeClassPolicy(store); policy.As(role);
        var own = NewClass(policy.UserId!.Value); var other = NewClass(Guid.NewGuid(), "B");
        store.Classes.AddRange([own, other]);
        policy.Enrollments.Add((own.Id, policy.UserId.Value));
        Assert.Equal(count, (await policy.Access.VisibleClassesAsync(default)).Count);
        await policy.Access.RequireReadAsync(own.Id, default);
        var missing = await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireReadAsync(Guid.NewGuid(), default));
        Assert.Equal(404, missing.Status);
        if (count == 1)
            Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireReadAsync(other.Id, default))).Status);
        if (role == Roles.Student)
        {
            policy.Enrollments.Clear();
            Assert.Empty(await policy.Access.VisibleClassesAsync(default));
            Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireReadAsync(own.Id, default))).Status);
        }
        policy.Accounts.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.VisibleClassesAsync(default))).Status);
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Staff, true)]
    [InlineData(Roles.Teacher, false)]
    [InlineData(Roles.Student, false)]
    public async Task ManagerGuard_RequiresCurrentLiveManager(string role, bool allowed)
    {
        var policy = new FakeClassPolicy(new()); policy.As(role);
        if (allowed) await policy.Access.RequireManagerAsync(default);
        else Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireManagerAsync(default))).Status);
    }

    [Fact]
    public async Task StudentGuard_RequiresSameStudent_AndForwardsCancellation()
    {
        var policy = new FakeClassPolicy(new()); policy.As(Roles.Student);
        await policy.Access.RequireStudentAsync(policy.UserId!.Value, default);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => policy.Access.RequireStudentAsync(Guid.NewGuid(), default))).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.Access.RequireUserAsync(cancellation.Token));
    }
}
