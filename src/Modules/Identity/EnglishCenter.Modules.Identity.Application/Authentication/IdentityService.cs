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
        var response = tokens.Issue(user, session.Id, clock.GetUtcNow());
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
        await RequireManagerAsync(ct);
        if (request.Role is not (Roles.Student or Roles.Teacher))
            throw new IdentityFailure(400, "Only Student or Teacher accounts can be created through this endpoint.");
        return await CreateManagedAsync(request, ct);
    }

    public async Task<UserResponse> ProvisionStaffAsync(ProvisionStaffRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return await CreateManagedAsync(new CreateUserRequest(request.Email, request.FullName, request.Password, Roles.Staff, request.Phone), ct);
    }

    private Task<UserResponse> CreateManagedAsync(CreateUserRequest request, CancellationToken ct)
    {
        request = request with { Email = request.Email?.Trim()!, FullName = request.FullName?.Trim()!, Phone = VietnamMobile.Normalize(request.Phone) };
        Validate(request);
        return CreateAsync(request, ct);
    }

    public async Task<ProfileResponse> GetProfileAsync(CancellationToken ct) =>
        await ProfileAsync(await RequireActiveUserAsync(ct), ct);

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(ct);
        request = request with { Email = request.Email?.Trim()!, FullName = request.FullName?.Trim()!, Phone = VietnamMobile.Normalize(request.Phone, user.Phone) };
        Validate(request);
        if (request.ExpectedRevision == Guid.Empty)
            throw FieldError(nameof(request.ExpectedRevision), "A valid expected revision is required.");
        if (request.ExpectedRevision != user.Revision)
            throw new IdentityFailure(409, "Profile changed. Reload before saving.");
        await EnsureEmailAvailableAsync(user, request.Email, ct);

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
        await RequireAdminAsync(ct);
        var user = await FindManagedAccountAsync(id, Roles.Teacher, ct);
        RequireRevision(user, request.ExpectedRevision);
        request = request with { FullName = request.FullName?.Trim()!, Email = request.Email?.Trim()!, Phone = VietnamMobile.Normalize(request.Phone, user.Phone) };
        Validate(request);
        await EnsureEmailAvailableAsync(user, request.Email, ct);
        var version = user.ProfileVersion;
        user.UpdateProfile(request.FullName, request.Email, request.Phone, user.AvatarFileId);
        await store.SaveAsync(ct);
        await PublishProfileAsync(user, version, ct);
        return Teacher(user);
    }

    public async Task<ManagedTeacher> SetTeacherLockAsync(Guid id, TeacherLockRequest request, CancellationToken ct)
    {
        var user = await SetAccountLockAsync(id, Roles.Teacher, new AccountLockRequest(request.IsLocked, request.ExpectedRevision), ct);
        return Teacher(user);
    }

    public async Task<ManagedAccountPage> ListStudentsAsync(string? q, int page, int pageSize, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        return await ListManagedAccountsAsync(Roles.Student, q, page, pageSize, ct);
    }

    public async Task<ManagedAccountPage> ListStaffAsync(string? q, int page, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return await ListManagedAccountsAsync(Roles.Staff, q, page, pageSize, ct);
    }

    private Task<ManagedAccountPage> ListManagedAccountsAsync(string role, string? q, int page, int pageSize, CancellationToken ct)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length > 200) throw FieldError("q", "Search term cannot exceed 200 characters.");
        if (pageSize is < 1 or > 100) throw FieldError("pageSize", "Page size must be between 1 and 100.");
        if (page is < 1 or > 100000) throw FieldError("page", "Page must be between 1 and 100000.");
        return store.ListManagedAccountsAsync(role, query, VietnamMobile.CanonicalNumber(query)?[3..], page, pageSize, ct);
    }

    public async Task<ManagedAccount> GetStudentAsync(Guid id, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        return Managed(await FindManagedAccountAsync(id, Roles.Student, ct));
    }

    public async Task<ManagedAccount> UpdateStudentContactAsync(Guid id, UpdateStudentContactRequest request, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        var user = await FindManagedAccountAsync(id, Roles.Student, ct);
        RequireRevision(user, request.ExpectedRevision);
        request = request with { FullName = request.FullName?.Trim()!, Email = request.Email?.Trim()!, Phone = VietnamMobile.Normalize(request.Phone, user.Phone) };
        Validate(request);
        await EnsureEmailAvailableAsync(user, request.Email, ct);
        user.UpdateProfile(request.FullName, request.Email, request.Phone, user.AvatarFileId);
        await store.SaveAsync(ct);
        return Managed(user);
    }

    public async Task<ManagedAccount> SetStudentLockAsync(Guid id, AccountLockRequest request, CancellationToken ct) =>
        Managed(await SetAccountLockAsync(id, Roles.Student, request, ct));

    public async Task<ManagedAccount> SetStaffLockAsync(Guid id, AccountLockRequest request, CancellationToken ct) =>
        Managed(await SetAccountLockAsync(id, Roles.Staff, request, ct));

    private async Task<User> SetAccountLockAsync(Guid id, string role, AccountLockRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var user = await FindManagedAccountAsync(id, role, ct);
        RequireRevision(user, request.ExpectedRevision);
        Validate(request);
        if (user.IsLocked == request.IsLocked!.Value) return user;
        user.SetLocked(request.IsLocked.Value);
        if (user.IsLocked)
            foreach (var session in await store.SessionsAsync(id, ct)) session.Revoke(clock.GetUtcNow());
        await store.SaveAsync(ct);
        return user;
    }

    private async Task RequireAdminAsync(CancellationToken ct)
    {
        if ((await RequireActiveUserAsync(ct)).Role != Roles.Admin)
            throw new IdentityFailure(403, "Only Admin can perform this operation.");
    }

    private async Task RequireManagerAsync(CancellationToken ct)
    {
        if ((await RequireActiveUserAsync(ct)).Role is not (Roles.Admin or Roles.Staff))
            throw new IdentityFailure(403, "Only Admin or Staff can perform this operation.");
    }

    private async Task<User> FindManagedAccountAsync(Guid id, string role, CancellationToken ct)
    {
        var user = await store.FindUserAsync(id, ct);
        if (user is null || user.Role != role) throw new IdentityFailure(404, "Account not found.");
        return user;
    }

    private static void RequireRevision(User user, Guid? revision)
    {
        if (revision is null || revision == Guid.Empty) throw FieldError("ExpectedRevision", "A valid expected revision is required.");
        if (user.Revision != revision) throw new IdentityFailure(409, "Account changed. Reload before saving.");
    }

    private async Task EnsureEmailAvailableAsync(User user, string email, CancellationToken ct)
    {
        var existing = await store.FindByEmailAsync(User.NormalizeEmail(email), ct);
        if (existing is not null && existing.Id != user.Id) throw new IdentityFailure(409, "Email is already registered.");
    }

    private static ManagedTeacher Teacher(User user) => new(user.Id, user.FullName, user.Email, user.Phone, user.IsLocked, user.Revision);
    private static ManagedAccount Managed(User user) => new(user.Id, user.FullName, user.Email, user.Phone, user.StudentCode, user.IsLocked, user.Revision);

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
        var sessionId = Guid.NewGuid();
        var response = tokens.Issue(user, sessionId, clock.GetUtcNow());
        user.Touch();
        session = RefreshSession.Create(sessionId, user.Id, tokens.HashRefreshToken(response.RefreshToken), response.RefreshTokenExpiresAt);
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
