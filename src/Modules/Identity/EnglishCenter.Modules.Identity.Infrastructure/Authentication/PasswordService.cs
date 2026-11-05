using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Identity;

namespace EnglishCenter.Modules.Identity.Infrastructure.Authentication;

internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    private readonly object _user = new();
    private readonly string _dummy;

    public PasswordService() => _dummy = _hasher.HashPassword(_user, Guid.NewGuid().ToString());
    public string Hash(string password) => _hasher.HashPassword(_user, password);
    public bool Verify(string? hash, string password) =>
        _hasher.VerifyHashedPassword(_user, hash ?? _dummy, password) != PasswordVerificationResult.Failed;
}
