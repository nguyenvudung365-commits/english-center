using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EnglishCenter.Modules.Identity.Infrastructure.Authentication;

internal sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    public TokenResponse Issue(User user, DateTimeOffset now)
    {
        var settings = options.Value;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(AuthClaims.Subject, user.Id.ToString()), new Claim(AuthClaims.Role, user.Role),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(jwt), expires,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), now.AddDays(settings.RefreshTokenDays));
    }

    public string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
