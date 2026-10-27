using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;

// Dựng read model academic.teachers để tạo lớp/buổi học kiểm tra được giáo viên mà không đọc schema identity.
public sealed class UserRegisteredIntegrationEventHandler
    : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
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

    public async Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent.Role != Roles.Teacher) return;

        // Idempotent: nhận trùng event thì cập nhật bản sao, không tạo dòng thứ hai.
        var teacher = await _store.FindTeacherAsync(integrationEvent.UserId, cancellationToken);
        if (teacher is null)
        {
            _store.AddTeacher(Teacher.Create(integrationEvent.UserId, integrationEvent.FullName, integrationEvent.Email));
        }
        else
        {
            teacher.UpdateProfile(integrationEvent.FullName, integrationEvent.Email);
        }

        try
        {
            await _store.SaveAsync(cancellationToken);
        }
        catch (AcademicFailure failure) when (failure.Status == 409)
        {
            _logger.LogInformation("Giáo viên {UserId} đã có trong academic.teachers, bỏ qua event trùng", integrationEvent.UserId);
            return;
        }

        _logger.LogInformation("Đồng bộ giáo viên {UserId} vào academic.teachers", integrationEvent.UserId);
    }
}
