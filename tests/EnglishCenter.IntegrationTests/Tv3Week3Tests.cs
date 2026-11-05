using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
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

public sealed class Tv3Week3Tests : IAsyncLifetime
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

    [Fact]
    public async Task EnrollmentSaveFailure_RollsBackReservedSeat_AndDoesNotPublish()
    {
        _factory.EnrollmentSave.Fail = true;
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnrollAsync(Request(), default));
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Empty(await db.Enrollments.ToListAsync());
        Assert.Equal(0, (await db.Classes.SingleAsync()).CurrentStudents);
        Assert.Empty(_factory.Invoices);
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
        r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<UserResponse>())!.Id;
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

    private sealed class Week3Factory : WebApplicationFactory<Program>, IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>
    {
        private readonly string? _server = Environment.GetEnvironmentVariable("TV3_POSTGRES");
        private readonly string _database = "tv3_test_" + Guid.NewGuid().ToString("N");
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "tv3-week3-" + Guid.NewGuid().ToString("N"));
        public bool Postgres => !string.IsNullOrWhiteSpace(_server);
        public FailSave EnrollmentSave { get; } = new();
        public FailSave FinanceSave { get; } = new();
        public System.Collections.Concurrent.ConcurrentBag<InvoiceCreatedIntegrationEvent> Invoices { get; } = [];
        public Task HandleAsync(InvoiceCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
        { Invoices.Add(e); return Task.CompletedTask; }

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
                services.AddSingleton<IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>>(this);
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
}
