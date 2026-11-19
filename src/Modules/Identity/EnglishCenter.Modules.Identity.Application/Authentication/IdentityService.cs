using System.ComponentModel.DataAnnotations;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

public sealed class IdentityService(IIdentityStore store, IPasswordService passwords,
    ITokenService tokens, IEventBus events, TimeProvider clock, ICurrentUser? currentUser = null,
    IStudentCodeGenerator? studentCodes = null, IAvatarFileAccess? avatarFiles = null)
{
    public async Task<(TokenResponse Tokens, Guid SessionId)> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        Validate(request);
        var user = await store.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        // Always perform a password verification, including for unknown accounts.
        var valid = passwords.Verify(user?.PasswordHash, request.Password);
        if (user is null || !valid) throw InvalidCredentials();
        if (user.IsLocked) throw new IdentityFailure(403, "Account is locked.");
        var response = Issue(user, out var session);
        await store.SaveAsync(ct);
        return (response, session.Id);
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        Validate(request);
        var session = await store.FindSessionAsync(tokens.HashRefreshToken(request.RefreshToken), ct);
        if (session is null || !session.IsActive(clock.GetUtcNow())) throw InvalidCredentials();
        var user = await store.FindUserAsync(session.UserId, ct);
        if (user is null || user.IsLocked) throw InvalidCredentials();
        session.Revoke(clock.GetUtcNow());
        var response = Issue(user, out _);
        await store.SaveAsync(ct);
        return response;
    }

    public async Task LogoutAsync(Guid userId, RefreshRequest request, CancellationToken ct)
    {
        Validate(request);
        await RevokeSessionAsync(userId, request.RefreshToken, null, ct);
    }

    public async Task<(TokenResponse Tokens, Guid SessionId)> RefreshBrowserAsync(string cookie, Guid? expectedSession, CancellationToken ct)
    {
        var session = await store.FindSessionAsync(tokens.HashRefreshToken(cookie), ct);
        if (session is null || !session.IsActive(clock.GetUtcNow())) throw InvalidCredentials();
        if (expectedSession is not null && session.Id != expectedSession) throw InvalidCredentials();
        var user = await store.FindUserAsync(session.UserId, ct);
        if (user is null || user.IsLocked) throw InvalidCredentials();
        var response = tokens.Issue(user, clock.GetUtcNow());
        // Keep the browser session identity stable while rotating its single-use credential.
        session.Rotate(tokens.HashRefreshToken(response.RefreshToken), response.RefreshTokenExpiresAt);
        user.Touch();
        await store.SaveAsync(ct);
        return (response, session.Id);
    }

    // userId is null for cookie-only logout, where the refresh cookie is the sole credential.
    public async Task RevokeSessionAsync(Guid? userId, string refreshToken, Guid? expectedSession, CancellationToken ct)
    {
        var session = await store.FindSessionAsync(tokens.HashRefreshToken(refreshToken), ct);
        if (session is null || (userId is not null && session.UserId != userId) ||
            (expectedSession is not null && session.Id != expectedSession))
            throw InvalidCredentials();
        if (!session.IsActive(clock.GetUtcNow()))
        {
            if (expectedSession is not null) return; // Confirm logout after password change or expiry.
            throw InvalidCredentials();
        }
        var user = await store.FindUserAsync(session.UserId, ct) ?? throw InvalidCredentials();
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
        var manager = await RequireActiveUserAsync(ct);
        if (manager.Role is not (Roles.Admin or Roles.Staff))
            throw new IdentityFailure(403, "Only Admin or Staff can create accounts.");
        request = request with { Email = request.Email?.Trim()!, FullName = request.FullName?.Trim()!, Phone = NormalizePhone(request.Phone) };
        Validate(request);
        if (request.Role is not (Roles.Student or Roles.Teacher))
            throw new IdentityFailure(400, "Only Student or Teacher accounts can be created through this endpoint.");
        return await CreateAsync(request, ct);
    }

    public async Task<ProfileResponse> GetProfileAsync(CancellationToken ct) =>
        await ProfileAsync(await RequireActiveUserAsync(ct), ct);

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(ct);
        request = request with { Email = request.Email?.Trim()!, FullName = request.FullName?.Trim()!, Phone = NormalizePhone(request.Phone) };
        Validate(request);
        if (request.ExpectedRevision == Guid.Empty)
            throw FieldError(nameof(request.ExpectedRevision), "A valid expected revision is required.");
        if (request.ExpectedRevision != user.Revision)
            throw new IdentityFailure(409, "Profile changed. Reload before saving.");
        var existing = await store.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (existing is not null && existing.Id != user.Id)
            throw new IdentityFailure(409, "Email is already registered.");

        AvatarDownload? avatar = null;
        if (request.AvatarFileId is { } fileId)
        {
            if (fileId == Guid.Empty) throw FieldError(nameof(request.AvatarFileId), "A confirmed owned avatar is required.");
            avatar = avatarFiles is null ? null : await avatarFiles.GetConfirmedAsync(user.Id, fileId, ct);
            if (avatar is null) throw FieldError(nameof(request.AvatarFileId), "A confirmed owned avatar is required.");
        }

        var oldVersion = user.ProfileVersion;
        user.UpdateProfile(request.FullName, request.Email, request.Phone, request.AvatarFileId);
        await store.SaveAsync(ct);
        await PublishProfileAsync(user, oldVersion, ct);
        return Response(user, avatar);
    }

    public async Task<IReadOnlyList<ManagedTeacher>> TeachersAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return (await store.UsersAsync(Roles.Teacher, ct)).OrderBy(x => x.FullName).ThenBy(x => x.Id).Select(Teacher).ToArray();
    }

    public async Task<ManagedTeacher> UpdateTeacherAsync(Guid id, UpdateTeacherRequest request, CancellationToken ct)
    {
        var user = await RequireTeacherAsync(id, request.ExpectedRevision, ct);
        request = request with { FullName = request.FullName?.Trim()!, Email = request.Email?.Trim()!, Phone = NormalizePhone(request.Phone) };
        Validate(request);
        var existing = await store.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (existing is not null && existing.Id != user.Id) throw new IdentityFailure(409, "Email is already registered.");
        var version = user.ProfileVersion;
        user.UpdateProfile(request.FullName, request.Email, request.Phone, user.AvatarFileId);
        await store.SaveAsync(ct);
        await PublishProfileAsync(user, version, ct);
        return Teacher(user);
    }

    public async Task<ManagedTeacher> SetTeacherLockAsync(Guid id, TeacherLockRequest request, CancellationToken ct)
    {
        var user = await RequireTeacherAsync(id, request.ExpectedRevision, ct);
        Validate(request);
        if (user.IsLocked == request.IsLocked!.Value) return Teacher(user);
        user.SetLocked(request.IsLocked.Value);
        if (user.IsLocked)
            foreach (var session in await store.SessionsAsync(id, ct)) session.Revoke(clock.GetUtcNow());
        await store.SaveAsync(ct);
        return Teacher(user);
    }

    private async Task RequireAdminAsync(CancellationToken ct)
    {
        if ((await RequireActiveUserAsync(ct)).Role != Roles.Admin)
            throw new IdentityFailure(403, "Only Admin can manage teachers.");
    }

    private async Task<User> RequireTeacherAsync(Guid id, Guid? revision, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var user = await store.FindUserAsync(id, ct);
        if (user is null || user.Role != Roles.Teacher) throw new IdentityFailure(404, "Teacher account not found.");
        if (revision is null || revision == Guid.Empty) throw FieldError("ExpectedRevision", "A valid expected revision is required.");
        if (user.Revision != revision) throw new IdentityFailure(409, "Account changed. Reload before saving.");
        return user;
    }

    private static ManagedTeacher Teacher(User user) => new(user.Id, user.FullName, user.Email, user.Phone, user.IsLocked, user.Revision);

    private async Task PublishProfileAsync(User user, long oldVersion, CancellationToken ct)
    {
        // Publication is not atomic with persistence; republish repairs a failed delivery.
        if (user.Role == Roles.Teacher && user.ProfileVersion != oldVersion)
        {
            try
            {
                await events.PublishAsync(new UserProfileUpdatedIntegrationEvent(user.Id, user.Email, user.FullName,
                    user.Role, user.ProfileVersion), ct, throwOnHandlerFailure: true);
            }
            catch (AggregateException)
            {
                throw new IdentityFailure(503,
                    "Profile was saved, but teacher synchronization failed. Reload your profile and ask an administrator to republish.");
            }
        }
    }

    private async Task<User> RequireActiveUserAsync(CancellationToken ct)
    {
        if (currentUser is not { IsAuthenticated: true, UserId: { } id } || id == Guid.Empty)
            throw new IdentityFailure(401, "A valid authenticated subject is required.");
        var user = await store.FindUserAsync(id, ct) ?? throw new IdentityFailure(401, "Account no longer exists.");
        if (user.IsLocked || user.Role != currentUser.Role)
            throw new IdentityFailure(403, "Account is locked or its role has changed.");
        return user;
    }

    private async Task<ProfileResponse> ProfileAsync(User user, CancellationToken ct)
    {
        var avatar = user.AvatarFileId is { } fileId && avatarFiles is not null
            ? await avatarFiles.GetConfirmedAsync(user.Id, fileId, ct) : null;
        return Response(user, avatar);
    }

    private static ProfileResponse Response(User user, AvatarDownload? avatar) => new(user.Id, user.Role,
        user.FullName, user.Email, user.Phone, user.Role == Roles.Student ? user.StudentCode : null,
        user.AvatarFileId, avatar?.Url, avatar?.ExpiresAt, user.Revision);

    private static string? NormalizePhone(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    private static IdentityFailure FieldError(string field, string message) => new(400,
        "One or more validation errors occurred.", new Dictionary<string, string[]> { [field] = [message] });

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
        user.UpdateProfile(request.FullName, request.Email, request.Phone, null);
        if (user.Role == Roles.Student)
        {
            if (studentCodes is null) throw new InvalidOperationException("Student code generator is not configured.");
            user.AssignStudentCode(await studentCodes.AllocateAsync(user.CreatedAt, ct));
        }
        store.AddUser(user);
        await store.SaveAsync(ct);
        await events.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, user.Email, user.FullName, user.Role, user.ProfileVersion), ct);
        return new UserResponse(user.Id, user.Email, user.FullName, user.Role, user.Phone, user.StudentCode);
    }

    private TokenResponse Issue(User user, out RefreshSession session)
    {
        var response = tokens.Issue(user, clock.GetUtcNow());
        user.Touch();
        session = RefreshSession.Create(user.Id, tokens.HashRefreshToken(response.RefreshToken), response.RefreshTokenExpiresAt);
        store.AddSession(session);
        return response;
    }

    private static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
        {
            var byField = errors
                .SelectMany(e => (e.MemberNames.Any() ? e.MemberNames : [string.Empty])
                    .Select(m => (Field: m, Message: e.ErrorMessage ?? "Invalid value.")))
                .GroupBy(x => x.Field)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Message).Distinct().ToArray());
            throw new IdentityFailure(400, "One or more validation errors occurred.", byField);
        }
    }

    private static IdentityFailure InvalidCredentials() => new(401, "Invalid credentials or refresh token.");

}
