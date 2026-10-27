using System.ComponentModel.DataAnnotations;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Identity.Domain.Users;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

public sealed class IdentityService(IIdentityStore store, IPasswordService passwords,
    ITokenService tokens, IEventBus events, TimeProvider clock)
{
    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        Validate(request);
        var user = await store.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        // Always perform a password verification, including for unknown accounts.
        var valid = passwords.Verify(user?.PasswordHash, request.Password);
        if (user is null || !valid) throw InvalidCredentials();
        if (user.IsLocked) throw new IdentityFailure(403, "Account is locked.");
        var response = Issue(user);
        await store.SaveAsync(ct);
        return response;
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        Validate(request);
        var session = await store.FindSessionAsync(tokens.HashRefreshToken(request.RefreshToken), ct);
        if (session is null || !session.IsActive(clock.GetUtcNow())) throw InvalidCredentials();
        var user = await store.FindUserAsync(session.UserId, ct);
        if (user is null || user.IsLocked) throw InvalidCredentials();
        session.Revoke(clock.GetUtcNow());
        var response = Issue(user);
        await store.SaveAsync(ct);
        return response;
    }

    public async Task LogoutAsync(Guid userId, RefreshRequest request, CancellationToken ct)
    {
        Validate(request);
        var session = await store.FindSessionAsync(tokens.HashRefreshToken(request.RefreshToken), ct);
        if (session is null || session.UserId != userId || !session.IsActive(clock.GetUtcNow()))
            throw InvalidCredentials();
        var user = await store.FindUserAsync(userId, ct) ?? throw InvalidCredentials();
        session.Revoke(clock.GetUtcNow());
        user.Touch();
        await store.SaveAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        Validate(request);
        var user = await store.FindUserAsync(userId, ct) ?? throw InvalidCredentials();
        if (user.IsLocked) throw new IdentityFailure(403, "Account is locked.");
        if (!passwords.Verify(user.PasswordHash, request.CurrentPassword))
            throw new IdentityFailure(400, "Current password is incorrect.");
        if (request.CurrentPassword == request.NewPassword)
            throw new IdentityFailure(400, "New password must differ from the current password.");
        user.ChangePassword(passwords.Hash(request.NewPassword));
        foreach (var session in await store.SessionsAsync(userId, ct)) session.Revoke(clock.GetUtcNow());
        await store.SaveAsync(ct);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct)
    {
        Validate(request);
        if (request.Role is not (Roles.Student or Roles.Teacher))
            throw new IdentityFailure(400, "Only Student or Teacher accounts can be created through this endpoint.");
        return await CreateAsync(request, ct);
    }

    public async Task BootstrapAdminAsync(string email, string name, string password, CancellationToken ct)
    {
        var request = new CreateUserRequest(email, name, password, Roles.Admin);
        Validate(request);
        var existing = await store.FindByEmailAsync(User.NormalizeEmail(email), ct);
        if (existing is not null)
        {
            if (existing.Role != Roles.Admin)
                throw new IdentityFailure(409, "Bootstrap email belongs to a non-admin account.");
            return;
        }
        await CreateAsync(request, ct);
    }

    private async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName)) throw new IdentityFailure(400, "Full name is required.");
        if (await store.FindByEmailAsync(User.NormalizeEmail(request.Email), ct) is not null)
            throw new IdentityFailure(409, "Email is already registered.");
        var user = User.Create(request.Email, request.FullName, request.Role, passwords.Hash(request.Password), clock.GetUtcNow());
        store.AddUser(user);
        await store.SaveAsync(ct);
        await events.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, user.Email, user.FullName, user.Role), ct);
        return new UserResponse(user.Id, user.Email, user.FullName, user.Role);
    }

    private TokenResponse Issue(User user)
    {
        var response = tokens.Issue(user, clock.GetUtcNow());
        user.Touch();
        store.AddSession(RefreshSession.Create(user.Id, tokens.HashRefreshToken(response.RefreshToken), response.RefreshTokenExpiresAt));
        return response;
    }

    private static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            throw new IdentityFailure(400, string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }

    private static IdentityFailure InvalidCredentials() => new(401, "Invalid credentials or refresh token.");

}
