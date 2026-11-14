using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;

// Dựng read model academic.teachers để tạo lớp/buổi học kiểm tra được giáo viên mà không đọc schema identity.
public sealed class UserRegisteredIntegrationEventHandler
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>, IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    private readonly IAcademicStore _store;
    private readonly ILogger<UserRegisteredIntegrationEventHandler> _logger;

    public UserRegisteredIntegrationEventHandler(
        IAcademicStore store,
        ILogger<UserRegisteredIntegrationEventHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) =>
        SyncAsync(integrationEvent.UserId, integrationEvent.FullName, integrationEvent.Email,
            integrationEvent.Role, integrationEvent.ProfileVersion, cancellationToken);

    public Task HandleAsync(UserProfileUpdatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) =>
        SyncAsync(integrationEvent.UserId, integrationEvent.FullName, integrationEvent.Email,
            integrationEvent.Role, integrationEvent.ProfileVersion, cancellationToken);

    private async Task SyncAsync(Guid userId, string fullName, string email, string role, long version, CancellationToken ct)
    {
        if (role != Roles.Teacher) return;
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        for (var attempt = 0; ; attempt++)
        {
            var teacher = attempt == 0 ? await _store.FindTeacherAsync(userId, ct) : await _store.ReloadTeacherAsync(userId, ct);
            if (teacher is null) _store.AddTeacher(Teacher.Create(userId, fullName, email, version));
            else if (!teacher.ApplySnapshot(fullName, email, version)) return;
            try
            {
                await _store.SaveAsync(ct);
                _logger.LogInformation("Synced teacher {UserId} profile version {Version}.", userId, version);
                return;
            }
            catch (AcademicFailure failure) when (failure.Status == 409 && attempt < 2)
            {
                // Reload the tracked snapshot before retrying; never overwrite a newer version.
            }
        }
    }
}
