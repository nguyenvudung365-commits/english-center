namespace EnglishCenter.Modules.Identity.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "EnglishCenter";
    public string Audience { get; set; } = "EnglishCenter";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
