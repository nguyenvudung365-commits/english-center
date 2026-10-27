using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Api;
using EnglishCenter.Modules.Enrollment.Api;
using EnglishCenter.Modules.Finance.Api;
using EnglishCenter.Modules.Identity.Api;
using EnglishCenter.Modules.MediaNotification.Api;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddInMemoryEventBus();

builder.Services.AddIdentityModule(configuration);
builder.Services.AddAcademicModule(configuration);
builder.Services.AddEnrollmentModule(configuration);
builder.Services.AddFinanceModule(configuration);
builder.Services.AddMediaNotificationModule(configuration);

builder.Services
    .AddControllers()
    .AddApplicationPart(EnglishCenter.Modules.Identity.Api.AssemblyReference.Assembly)
    .AddApplicationPart(EnglishCenter.Modules.Academic.Api.AssemblyReference.Assembly)
    .AddApplicationPart(EnglishCenter.Modules.Enrollment.Api.AssemblyReference.Assembly)
    .AddApplicationPart(EnglishCenter.Modules.Finance.Api.AssemblyReference.Assembly)
    .AddApplicationPart(EnglishCenter.Modules.MediaNotification.Api.AssemblyReference.Assembly);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("identity-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
