using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Domain.Invoices;
using EnglishCenter.Modules.Finance.Application.Invoices;
using EnglishCenter.Modules.Finance.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed partial class Tv3Week3Tests : IAsyncLifetime
{
    private const string Password = "Test-password-123!";
    private const string Url = "/api/enrollment/enrollments";
    private readonly Week3Factory _factory = new();
    private HttpClient _client = null!;
    private Guid _student, _other, _class, _course, _teacher;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public async Task InitializeAsync()
    {
        await _factory.CreateDatabaseAsync();
        _client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await Initialize(sp.GetRequiredService<IdentityDbContext>());
            await Initialize(sp.GetRequiredService<AcademicDbContext>());
            await Initialize(sp.GetRequiredService<EnrollmentDbContext>());
            await Initialize(sp.GetRequiredService<FinanceDbContext>());
            var identity = sp.GetRequiredService<IdentityDbContext>();
            var passwords = sp.GetRequiredService<IPasswordService>();
            foreach (var role in new[] { Roles.Staff, Roles.Admin })
                identity.Users.Add(User.Create($"{role}@week3.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow));
            await identity.SaveChangesAsync();
        }
        await Login("Staff");
        _student = await CreateUser("Student", Roles.Student);
        _other = await CreateUser("Other", Roles.Student);
        _teacher = await CreateUser("Teacher", Roles.Teacher);
        _course = (await _client.GetFromJsonAsync<List<CourseResponse>>("/api/academic/courses"))!.First().Id;
        _class = await CreateClass();
    }

    private Task Initialize(DbContext db) => _factory.Postgres ? db.Database.MigrateAsync() : db.Database.EnsureCreatedAsync();
    public async Task DisposeAsync() { _client.Dispose(); await _factory.DisposeAsync(); }

    [Theory]
    [InlineData("Staff", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Teacher", HttpStatusCode.Forbidden)]
    [InlineData("Student", HttpStatusCode.Forbidden)]
    public async Task Enrollment_RequiresManager(string name, HttpStatusCode expected)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enroll()).StatusCode);
        await Login(name);
        Assert.Equal(expected, (await Enroll()).StatusCode);
    }

    [Fact]
    public async Task RealApis_EnrollPersistInvoiceAndOwnClass_ReplayDoesNotDuplicate()
    {
        var response = await Enroll();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        Assert.Equal(4_500_000m, value.TuitionFee);
        Assert.Equal(value, await _client.GetFromJsonAsync<EnrollmentResponse>(response.Headers.Location));
        var invoice = await GetInvoice(value.Id);
        Assert.Equal(value.StudentId, invoice.StudentId);
        Assert.Equal(_class, invoice.ClassId);
        Assert.Equal(value.TuitionFee, invoice.Amount);
        Assert.Equal("Unpaid", invoice.Status);
        Assert.Equal(DateOnly.FromDateTime(value.EnrolledAtUtc.AddHours(7)).AddDays(7), invoice.DueDate);
        Assert.Single(_factory.Invoices);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Url}/{value.Id}/replay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Url}/{value.Id}/replay", null)).StatusCode);
        Assert.Single(_factory.Invoices);
        using (var scope = _factory.Services.CreateScope())
        {
            Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
            Assert.Single(await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().ClassStudents.ToListAsync());
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Classes.SingleAsync()).CurrentStudents);
            Assert.Equal(value.Id, (await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Enrollments.SingleAsync()).Id);
            Assert.True(await scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>().IsActiveAsync(_class, _student, default));
        }
        await Login("Student");
        Assert.Equal(invoice, await GetInvoice(value.Id));
        Assert.Equal(_class, Assert.Single((await _client.GetFromJsonAsync<List<StudentClassResponse>>("/api/academic/classes/mine"))!).ClassId);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsync($"{Url}/{value.Id}/replay", null)).StatusCode);
        await Login("Other");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{value.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/finance/invoices/by-enrollment/{value.Id}")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<List<StudentClassResponse>>("/api/academic/classes/mine"))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationOnly_AuthorizesAssignments_AndRevocationAppliesWithoutEvents(bool cancelFinancial)
    {
        await Login("Teacher");
        var now = DateTimeOffset.UtcNow;
        var created = await _client.PostAsJsonAsync("/api/academic/assignments", new
        {
            classId = _class, title = "Registration-backed access",
            opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var assignment = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/academic/assignments/{assignment}";
        await Login("Student");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(path)).StatusCode);

        var registration = new Registration(_student, _class, 4_500_000m, DateTime.UtcNow, 3, 5, 2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.ClrType.Name == "ClassEnrollment");
            db.Enrollments.Add(registration);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>();
            Assert.True(await access.IsActiveAsync(_class, _student, default));
            Assert.False(await access.IsActiveAsync(_class, _other, default));
            Assert.False(await access.IsActiveAsync(Guid.NewGuid(), _student, default));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().ClassStudents.ToListAsync());
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/academic/assignments?classId={_class}"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path + "/submissions/mine")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            var stored = await db.Enrollments.SingleAsync();
            Assert.Equal(registration.Id, stored.Id);
            if (cancelFinancial) db.Entry(stored).Property(x => x.Status).CurrentValue = "Cancelled";
            else db.Entry(stored).Property(x => x.IsActive).CurrentValue = false;
            await db.SaveChangesAsync();
        }
        using (var scope = _factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>().IsActiveAsync(_class, _student, default));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync($"/api/academic/assignments?classId={_class}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(path + "/submissions/mine")).StatusCode);
        Assert.Empty(_factory.EnrollmentEvents);
        Assert.Empty(_factory.Invoices);
    }

    [Fact]
    public async Task Memberships_GrantAndRevokeCourseworkOnly_PreserveFinancialRegistrationCapacityInvoiceAndEvents()
    {
        _class = await CreateClass(1);
        var enrolled = await Enroll();
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        var registration = (await enrolled.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        var invoice = await GetInvoice(registration.Id);
        const string memberships = "/api/enrollment/memberships";
        var listed = Assert.Single((await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"{memberships}?classId={_class}"))!);
        Assert.All(new[] { "classId", "id", "isActive", "studentId" }, name => Assert.True(listed.TryGetProperty(name, out _)));
        Assert.Equal(registration.Id, listed.GetProperty("id").GetGuid());
        Assert.Equal(_class, listed.GetProperty("classId").GetGuid());
        Assert.Equal(_student, listed.GetProperty("studentId").GetGuid());
        Assert.True(listed.GetProperty("isActive").GetBoolean());

        async Task ReuseMembership()
        {
            var response = await _client.PostAsJsonAsync(memberships, new { classId = _class, studentId = _student });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var member = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(registration.Id, member.GetProperty("id").GetGuid());
            Assert.True(member.GetProperty("isActive").GetBoolean());
        }

        async Task AssertCanonicalState(bool active)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            var stored = Assert.Single(await db.Enrollments.ToListAsync());
            Assert.Equal(registration.Id, stored.Id);
            Assert.Equal("Active", stored.Status);
            Assert.Equal(active, stored.IsActive);
            Assert.Equal((3, 5, 2), (stored.MaxAbsences, stored.MaxLates, stored.MaxSkippedAssignments));
            Assert.Equal(4_500_000m, stored.TuitionFee);
            Assert.Equal(1, (await db.Classes.SingleAsync(x => x.ClassId == _class)).CurrentStudents);
            Assert.Equal(active, await scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>().IsActiveAsync(_class, _student, default));
            Assert.Equal(invoice.Id, (await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.SingleAsync()).Id);
            Assert.Equal(registration.Id, Assert.Single(_factory.EnrollmentEvents).EnrollmentId);
            Assert.Single(_factory.Invoices);
        }

        await ReuseMembership();
        await AssertCanonicalState(true);
        Assert.Equal(HttpStatusCode.Conflict, (await Enroll()).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync(memberships, new { classId = _class, studentId = _other })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{memberships}/{registration.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{memberships}/{registration.Id}")).StatusCode);
        await AssertCanonicalState(false);
        await ReuseMembership();
        await AssertCanonicalState(true);
        Assert.Equal(invoice, await GetInvoice(registration.Id));
        Assert.Single(_factory.Invoices);
    }

    [Fact]
    public async Task Memberships_WithoutFinancialRegistration_RejectGrantWithoutCreatingRegistrationInvoiceOrEvents()
    {
        const string memberships = "/api/enrollment/memberships";
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync(memberships,
            new { classId = _class, studentId = _student })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync(memberships, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(Url,
            new { classId = _class, studentId = _student })).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"{memberships}?classId={_class}"))!);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Empty(await db.Enrollments.ToListAsync());
        Assert.Equal(0, (await db.Classes.SingleAsync()).CurrentStudents);
        Assert.False(await scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>().IsActiveAsync(_class, _student, default));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
        Assert.Empty(_factory.EnrollmentEvents);
        Assert.Empty(_factory.Invoices);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task Memberships_CancelledFinancialRegistration_RejectsCourseworkReactivationWithoutSideEffects(string method)
    {
        var enrolled = await Enroll();
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        var registration = (await enrolled.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        var invoice = await GetInvoice(registration.Id);
        var enrolledEvent = Assert.Single(_factory.EnrollmentEvents);
        var invoiceEvent = Assert.Single(_factory.Invoices);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            var stored = await db.Enrollments.SingleAsync();
            db.Entry(stored).Property(x => x.Status).CurrentValue = "Cancelled";
            db.Entry(stored).Property(x => x.IsActive).CurrentValue = false;
            await db.SaveChangesAsync();
            Assert.Equal(1, (await db.Classes.SingleAsync()).CurrentStudents);
        }

        const string memberships = "/api/enrollment/memberships";
        var response = method == "POST"
            ? await _client.PostAsJsonAsync(memberships, new { classId = _class, studentId = _student })
            : await _client.PatchAsJsonAsync($"{memberships}/{registration.Id}/status", new { isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            var stored = Assert.Single(await db.Enrollments.ToListAsync());
            Assert.Equal(registration.Id, stored.Id);
            Assert.Equal("Cancelled", stored.Status);
            Assert.False(stored.IsActive);
            Assert.Equal((3, 5, 2), (stored.MaxAbsences, stored.MaxLates, stored.MaxSkippedAssignments));
            Assert.Equal(registration.TuitionFee, stored.TuitionFee);
            Assert.Equal(1, (await db.Classes.SingleAsync()).CurrentStudents);
            Assert.False(await scope.ServiceProvider.GetRequiredService<IEnrollmentAccess>().IsActiveAsync(_class, _student, default));
            Assert.Equal(invoice.Id, (await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.SingleAsync()).Id);
        }
        Assert.Equal(invoice, await GetInvoice(registration.Id));
        Assert.Equal(enrolledEvent, Assert.Single(_factory.EnrollmentEvents));
        Assert.Equal(invoiceEvent, Assert.Single(_factory.Invoices));
    }

    [Fact]
    public async Task ClientCannotOverrideClassTuition()
    {
        var response = await _client.PostAsJsonAsync(Url, new
        { StudentId = _student, ClassId = _class, MaxAbsences = 0, MaxLates = 0, MaxSkippedAssignments = 0, TuitionFee = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        Assert.Equal(4_500_000, (await GetInvoice(value.Id)).Amount);
    }

    [Fact]
    public async Task DuplicateAndFullClass_Return409_WithoutChangingCounts()
    {
        _class = await CreateClass(1);
        Assert.Equal(HttpStatusCode.Created, (await Enroll()).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enroll()).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enroll(_other)).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Equal(1, await db.Enrollments.CountAsync());
        Assert.Equal(1, (await db.Classes.SingleAsync(x => x.ClassId == _class)).CurrentStudents);
    }

    [Fact]
    public async Task EndDate_IsInclusive_AndTomorrowIsEnded()
    {
        _class = await CreateClass(end: Today.AddDays(-1));
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll()).StatusCode);
        _class = await CreateClass(end: Today);
        Assert.Equal(HttpStatusCode.Created, (await Enroll()).StatusCode);
    }

    [Fact]
    public async Task MissingSnapshotsAndInvalidLimits_Return400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(Guid.NewGuid())).StatusCode);
        _class = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll()).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(Url, new EnrollRequest(_student, _class, null, 0, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(Url, new EnrollRequest(_student, _class, -1, 0, 0))).StatusCode);
    }

    [Fact]
    public async Task ClassCreatedReplay_PreservesOccupiedSeats_AndConflictsAreRejected()
    {
        var response = (await (await Enroll()).Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        var x = await db.Classes.AsNoTracking().SingleAsync(c => c.ClassId == _class);
        var handler = ActivatorUtilities.CreateInstance<EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers.ClassCreatedHandler>(scope.ServiceProvider);
        var e = new EnglishCenter.Modules.Academic.Contracts.IntegrationEvents.ClassCreatedIntegrationEvent(x.ClassId, x.ClassCode,
            x.StartDate, x.EndDate, x.MaxStudents, x.TuitionFee, "Main");
        await handler.HandleAsync(e);
        await handler.HandleAsync(e with { EventId = Guid.NewGuid() });
        Assert.Equal(1, (await db.Classes.AsNoTracking().SingleAsync(c => c.ClassId == _class)).CurrentStudents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(e with { TuitionFee = 1 }));
        Assert.Equal(response.Id, (await db.Enrollments.SingleAsync()).Id);
    }

    [Theory]
    [InlineData("Staff", HttpStatusCode.Accepted, HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.Accepted, HttpStatusCode.OK)]
    [InlineData("Teacher", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData("Student", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task AcademicClassReplayAndBackfill_RequireManager(string name, HttpStatusCode replay, HttpStatusCode backfill)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReplayClass()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/academic/classes/republish", null)).StatusCode);
        await Login(name);
        Assert.Equal(replay, (await ReplayClass()).StatusCode);
        Assert.Equal(backfill, (await _client.PostAsync("/api/academic/classes/republish", null)).StatusCode);
    }

    [Fact]
    public async Task AcademicClassReplay_UnknownClass_Returns404()
    {
        var response = await _client.PostAsync($"/api/academic/classes/{Guid.NewGuid()}/replay", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Classes.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyAcademicClass_BackfillEnablesEnrollment_AndReplayPreservesOccupiedSeats(bool bulk)
    {
        _class = await SeedLegacyClass();
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll()).StatusCode);
        using (var scope = _factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Classes.AnyAsync(x => x.ClassId == _class));

        async Task Replay()
        {
            if (!bulk) Assert.Equal(HttpStatusCode.Accepted, (await ReplayClass()).StatusCode);
            else
            {
                var response = await _client.PostAsync("/api/academic/classes/republish", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(2, (await response.Content.ReadFromJsonAsync<RepublishClassesResponse>())!.Published);
            }
        }

        await Replay();
        await Replay();
        var enrolled = await Enroll();
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        var registration = (await enrolled.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        Assert.Equal(_class, (await GetInvoice(registration.Id)).ClassId);
        await Replay();
        await Replay();
        Assert.Equal(HttpStatusCode.Conflict, (await Enroll(_other)).StatusCode);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        var snapshot = await db.Classes.AsNoTracking().SingleAsync(x => x.ClassId == _class);
        Assert.Equal(2, await db.Classes.CountAsync());
        Assert.Equal(1, snapshot.CurrentStudents);
        Assert.Equal(1, snapshot.MaxStudents);
        Assert.Equal(4_500_000m, snapshot.TuitionFee);
        Assert.Equal(registration.Id, (await db.Enrollments.SingleAsync()).Id);
        var academic = verify.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(2, await academic.Classes.CountAsync());
        Assert.Equal(_class, (await academic.ClassStudents.SingleAsync()).ClassId);
        Assert.Single(await verify.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
    }

    [PgFact]
    public async Task PostgreSql_ConcurrentClassReplays_BackfillOnce_AndPreserveOccupiedSeats()
    {
        _class = await SeedLegacyClass();
        async Task ReplayConcurrently()
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, 8).Select(async _ => { await start.Task; return await ReplayClass(); }).ToArray();
            start.SetResult();
            Assert.All(await Task.WhenAll(requests), x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode));
        }
        await ReplayConcurrently();
        Assert.Equal(HttpStatusCode.Created, (await Enroll()).StatusCode);
        await ReplayConcurrently();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Equal(2, await db.Classes.CountAsync());
        Assert.Equal(1, (await db.Classes.AsNoTracking().SingleAsync(x => x.ClassId == _class)).CurrentStudents);
        Assert.Single(await db.Enrollments.ToListAsync());
    }

    [Fact]
    public async Task EnrollmentSaveFailure_RollsBackReservedSeat_AndDoesNotPublish()
    {
        _factory.EnrollmentSave.Fail = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Enroll()).StatusCode);
        Assert.False(_factory.EnrollmentSave.Fail);
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Empty(await db.Enrollments.ToListAsync());
        Assert.Equal(0, (await db.Classes.SingleAsync()).CurrentStudents);
        Assert.Empty(_factory.Invoices);
        Assert.Empty(_factory.EnrollmentEvents);
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
    }

    [Fact]
    public async Task FinanceFailure_LeavesEnrollmentRecoverableByReplay()
    {
        _factory.FinanceSave.Fail = true;
        var response = await Enroll();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/finance/invoices/by-enrollment/{value.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Url}/{value.Id}/replay", null)).StatusCode);
        Assert.Equal(value.TuitionFee, (await GetInvoice(value.Id)).Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999_999_999_999)]
    public async Task InvoiceReplay_SameOrNewEventId_PersistsOnce_AndConflictDoesNotOverwrite(long amount)
    {
        var e = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), _student, _class, amount, DateTime.UtcNow);
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers.StudentEnrolledIntegrationEventHandler>(scope.ServiceProvider);
        await handler.HandleAsync(e);
        await handler.HandleAsync(e);
        await handler.HandleAsync(e with { EventId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(e with { StudentId = _other }));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
        Assert.Equal(amount, (await GetInvoice(e.EnrollmentId)).Amount);
        Assert.Single(_factory.Invoices);
    }

    [PgFact]
    public async Task PostgreSql_LastSeat_OnlyOneConcurrentRequestSucceeds()
    {
        _class = await CreateClass(1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Run(Guid student) { await start.Task; return await Enroll(student); }
        var first = Run(_student); var second = Run(_other);
        start.SetResult();
        var statuses = (await Task.WhenAll(first, second)).Select(x => x.StatusCode).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Enrollments.CountAsync());
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.CountAsync());
    }

    [PgFact]
    public async Task PostgreSql_ConcurrentSameStudent_DoesNotLeakReservedSeats()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 8).Select(async _ => { await start.Task; return await Enroll(); }).ToArray();
        start.SetResult();
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Single(await db.Enrollments.ToListAsync());
        Assert.Equal(1, (await db.Classes.SingleAsync()).CurrentStudents);
    }

    [PgFact]
    public async Task PostgreSql_ConcurrentInvoiceEvents_CreateOneInvoice()
    {
        var e = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), _student, _class, 1_500_000, DateTime.UtcNow);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, 12).Select(async _ =>
        {
            await start.Task;
            using var scope = _factory.Services.CreateScope();
            await ActivatorUtilities.CreateInstance<EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers.StudentEnrolledIntegrationEventHandler>(scope.ServiceProvider).HandleAsync(e with { EventId = Guid.NewGuid() });
        }).ToArray();
        start.SetResult(); await Task.WhenAll(workers);
        using var verify = _factory.Services.CreateScope();
        Assert.Single(await verify.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
        Assert.Single(_factory.Invoices);
    }

    private EnrollRequest Request(Guid? student = null) => new(student ?? _student, _class, 3, 5, 2);
    private Task<HttpResponseMessage> ReplayClass() => _client.PostAsync($"/api/academic/classes/{_class}/replay", null);
    private async Task<Guid> SeedLegacyClass()
    {
        // Lớp đã tồn tại trước khi Enrollment nhận sự kiện: chỉ lưu Academic, không phát ClassCreated.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var value = EnglishCenter.Modules.Academic.Domain.Classes.Class.Create("LEGACY-" + Guid.NewGuid().ToString("N"),
            "Legacy class", _course, EnglishCenter.Modules.Academic.Domain.Classes.ClassType.Main,
            Today.AddDays(-30), Today.AddDays(30), 1, 4_500_000, _teacher, DateTimeOffset.UtcNow.AddDays(-30));
        db.Classes.Add(value);
        await db.SaveChangesAsync();
        return value.Id;
    }
    private Task<HttpResponseMessage> Enroll(Guid? student = null) => _client.PostAsJsonAsync(Url, Request(student));
    private async Task<InvoiceResponse> GetInvoice(Guid enrollment) =>
        (await _client.GetFromJsonAsync<InvoiceResponse>($"/api/finance/invoices/by-enrollment/{enrollment}"))!;
    private async Task Login(string name)
    {
        var r = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest($"{name}@week3.test", Password));
        r.EnsureSuccessStatusCode();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await r.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
    }
    private async Task<Guid> CreateUser(string name, string role)
    {
        var r = await _client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest($"{name}@week3.test", name, Password, role));
        Assert.True(r.IsSuccessStatusCode, $"Create {role}: {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<UserResponse>())!.Id;
    }
    private async Task<Guid> CreateClass(int capacity = 20, DateOnly? end = null)
    {
        var r = await _client.PostAsJsonAsync("/api/academic/classes", new CreateClassRequest("TV3-" + Guid.NewGuid().ToString("N"),
            "TV3 week 3", _course, "Main", Today.AddDays(-30), end ?? Today.AddDays(30), capacity, 4_500_000, _teacher));
        r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<ClassResponse>())!.Id;
    }

    private sealed class PgFactAttribute : FactAttribute
    {
        public PgFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TV3_POSTGRES"))) Skip = "Set TV3_POSTGRES to a dedicated PostgreSQL test server."; }
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail) { Fail = false; throw new InvalidOperationException("Injected database failure."); }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TestStudentCodes : IStudentCodeGenerator
    {
        private int sequence;
        public Task<string> AllocateAsync(DateTimeOffset createdAt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult($"HV{Interlocked.Increment(ref sequence):D6}-{createdAt.UtcDateTime:yy}");
        }
    }

    private sealed class Week3Factory : WebApplicationFactory<Program>, IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>,
        IIntegrationEventHandler<StudentEnrolledIntegrationEvent>, IIntegrationEventHandler<WarningIssuedIntegrationEvent>,
        IIntegrationEventHandler<InvoicePaidIntegrationEvent>
    {
        private readonly string? _server = Environment.GetEnvironmentVariable("TV3_POSTGRES");
        private readonly string _database = "tv3_test_" + Guid.NewGuid().ToString("N");
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "tv3-week3-" + Guid.NewGuid().ToString("N"));
        public bool Postgres => !string.IsNullOrWhiteSpace(_server);
        public TestClock Clock { get; } = new();
        public FailSave EnrollmentSave { get; } = new();
        public FailSave FinanceSave { get; } = new();
        public System.Collections.Concurrent.ConcurrentBag<InvoiceCreatedIntegrationEvent> Invoices { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<StudentEnrolledIntegrationEvent> EnrollmentEvents { get; } = [];
        public Task HandleAsync(InvoiceCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
        { Invoices.Add(e); return Task.CompletedTask; }
        public Task HandleAsync(StudentEnrolledIntegrationEvent e, CancellationToken cancellationToken = default)
        { EnrollmentEvents.Add(e); return Task.CompletedTask; }
        public System.Collections.Concurrent.ConcurrentBag<WarningIssuedIntegrationEvent> WarningEvents { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<InvoicePaidIntegrationEvent> PaymentEvents { get; } = [];
        public Task HandleAsync(WarningIssuedIntegrationEvent e, CancellationToken cancellationToken = default)
        { WarningEvents.Add(e); return Task.CompletedTask; }
        public Task HandleAsync(InvoicePaidIntegrationEvent e, CancellationToken cancellationToken = default)
        { PaymentEvents.Add(e); return Task.CompletedTask; }

        public async Task CreateDatabaseAsync()
        {
            Directory.CreateDirectory(_temp);
            if (!Postgres) return;
            await using var connection = new NpgsqlConnection(_server);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Identity:InitializeDatabase"] = "false", ["Academic:InitializeDatabase"] = "false",
                ["Enrollment:InitializeDatabase"] = "false", ["Finance:InitializeDatabase"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                Replace<IdentityDbContext>(services, "identity"); Replace<AcademicDbContext>(services, "academic");
                Replace<EnrollmentDbContext>(services, "enrollment", EnrollmentSave); Replace<FinanceDbContext>(services, "finance", FinanceSave);
                if (!Postgres)
                {
                    services.RemoveAll<IStudentCodeGenerator>();
                    services.AddSingleton<IStudentCodeGenerator, TestStudentCodes>();
                }
                services.AddSingleton<IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>>(this);
                services.AddSingleton<IIntegrationEventHandler<StudentEnrolledIntegrationEvent>>(this);
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IIntegrationEventHandler<WarningIssuedIntegrationEvent>>(this);
                services.AddSingleton<IIntegrationEventHandler<InvoicePaidIntegrationEvent>>(this);
            });
        }
        private void Replace<T>(IServiceCollection services, string schema, IInterceptor? interceptor = null) where T : DbContext
        {
            services.RemoveAll<DbContextOptions<T>>(); services.RemoveAll<IDbContextOptionsConfiguration<T>>();
            services.AddDbContext<T>(o =>
            {
                if (Postgres)
                {
                    var connection = new NpgsqlConnectionStringBuilder(_server!) { Database = _database };
                    o.UseNpgsql(connection.ConnectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", schema));
                }
                else o.UseSqlite($"Data Source={Path.Combine(_temp, schema + ".db")}");
                if (interceptor is not null) o.AddInterceptors(interceptor);
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Postgres)
            {
                NpgsqlConnection.ClearAllPools();
                await using var connection = new NpgsqlConnection(_server);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_temp, true);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset? UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow ?? base.GetUtcNow();
    }
}
