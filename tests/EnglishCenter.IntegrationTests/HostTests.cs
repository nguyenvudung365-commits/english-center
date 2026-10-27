using System.Net;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public class HostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HostTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/api/identity/ping", "Identity", "identity")]
    [InlineData("/api/academic/ping", "Academic", "academic")]
    [InlineData("/api/enrollment/ping", "Enrollment", "enrollment")]
    [InlineData("/api/finance/ping", "Finance", "finance")]
    [InlineData("/api/media-notification/ping", "MediaNotification", "media_notification")]
    public async Task F2_Ping_Returns200WithModuleAndSchema(string url, string module, string schema)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(module, doc.RootElement.GetProperty("module").GetString());
        Assert.Equal(schema, doc.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task F3_Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist/ping");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void F4_FinanceHandlerRegisteredForStudentEnrolled()
    {
        _ = _factory.Server;
        using var scope = _factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<StudentEnrolledIntegrationEvent>>().ToList();
        Assert.Contains(handlers, h => h.GetType().Assembly.GetName().Name == "EnglishCenter.Modules.Finance.Application");
    }

    [Fact]
    public async Task F5_StudentEnrolled_CreatesRecord_AndIsIdempotent()
    {
        _ = _factory.Server;
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var recorder = _factory.Services.GetRequiredService<IInvoiceCreationRecorder>();
        var evt = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1_500_000m, DateTime.UtcNow);

        await bus.PublishAsync(evt);
        Assert.Contains(evt.EnrollmentId, recorder.RecordedEnrollmentIds);
        Assert.Equal(1, recorder.RecordedEnrollmentIds.Count(id => id == evt.EnrollmentId));

        await bus.PublishAsync(evt);
        Assert.Equal(1, recorder.RecordedEnrollmentIds.Count(id => id == evt.EnrollmentId));
    }

    [Fact]
    public async Task F5b_DifferentEnrollments_CreateSeparateRecords()
    {
        _ = _factory.Server;
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var recorder = _factory.Services.GetRequiredService<IInvoiceCreationRecorder>();
        var a = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, DateTime.UtcNow);
        var b = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, DateTime.UtcNow);
        await bus.PublishAsync(a);
        await bus.PublishAsync(b);
        Assert.Contains(a.EnrollmentId, recorder.RecordedEnrollmentIds);
        Assert.Contains(b.EnrollmentId, recorder.RecordedEnrollmentIds);
    }

    [Fact]
    public void D2_EachModuleDbContext_UsesOwnSchema()
    {
        _ = _factory.Server;
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        Assert.Equal("identity", sp.GetRequiredService<IdentityDbContext>().Model.GetDefaultSchema());
        Assert.Equal("academic", sp.GetRequiredService<AcademicDbContext>().Model.GetDefaultSchema());
        Assert.Equal("enrollment", sp.GetRequiredService<EnrollmentDbContext>().Model.GetDefaultSchema());
        Assert.Equal("finance", sp.GetRequiredService<FinanceDbContext>().Model.GetDefaultSchema());
        Assert.Equal("media_notification", sp.GetRequiredService<MediaNotificationDbContext>().Model.GetDefaultSchema());
    }

    [Fact]
    public void D3_SchemasAreDistinct()
    {
        var schemas = new[] { IdentitySchema.Name, AcademicSchema.Name, EnrollmentSchema.Name, FinanceSchema.Name, MediaNotificationSchema.Name };
        Assert.Equal(5, schemas.Distinct().Count());
    }

    [Fact]
    public void D2b_DbContextBuiltStandalone_WithNpgsql_HasSchema()
    {
        var o = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql("Host=localhost;Database=x").Options;
        using var ctx = new FinanceDbContext(o);
        Assert.Equal("finance", ctx.Model.GetDefaultSchema());
    }
}
