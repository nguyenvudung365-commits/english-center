using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

/// <summary>Phát lại UserRegisteredIntegrationEvent cho tài khoản đã có. Handler phía nhận phải idempotent theo UserId.</summary>
public sealed class UserSyncService(IIdentityStore store, IEventBus events, ILogger<UserSyncService> logger)
{
    private static readonly string[] AllRoles = [Roles.Student, Roles.Teacher, Roles.Staff, Roles.Admin];

    public async Task<RepublishUsersResponse> RepublishAsync(RepublishUsersRequest request, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim();
        if (role is not null && !AllRoles.Contains(role))
            throw new IdentityFailure(400, "One or more validation errors occurred.",
                new Dictionary<string, string[]> { [nameof(request.Role)] = ["Role is not valid."] });
        var users = await store.UsersAsync(role, ct);
        foreach (var user in users)
            await events.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, user.Email, user.FullName, user.Role, user.ProfileVersion), ct);
        logger.LogInformation("Republished {Count} user registration events (role filter: {Role}).", users.Count, role ?? "all");
        return new RepublishUsersResponse(users.Count, role);
    }
}
