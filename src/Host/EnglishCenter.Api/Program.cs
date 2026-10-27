using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Api;
using EnglishCenter.Modules.Enrollment.Api;
using EnglishCenter.Modules.Finance.Api;
using EnglishCenter.Modules.Identity.Api;
using EnglishCenter.Modules.MediaNotification.Api;

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
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
