using System.Text.RegularExpressions;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

internal static partial class VietnamMobile
{
    // Current allocated two-digit mobile prefixes after the domestic 0 / country code.
    [GeneratedRegex(@"\A(?:0|\+84)[ .-]*((?:3[ .-]*[2-9]|5[ .-]*[25689]|7[ .-]*[06789]|8[ .-]*[1-9]|9[ .-]*[0-9])(?:[ .-]*[0-9]){7})\z")]
    private static partial Regex MobilePattern();

    public static string? Normalize(string? phone, string? previous = null)
    {
        var value = phone?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value == previous) return previous;
        if (CanonicalNumber(value) is { } canonical) return canonical;
        throw new IdentityFailure(400, "One or more validation errors occurred.", new Dictionary<string, string[]>
        {
            ["Phone"] = ["Enter a valid Vietnamese mobile number, e.g. 090 123 4567 or +84 90 123 4567."]
        });
    }

    public static string? CanonicalNumber(string value)
    {
        var match = value.Length <= 32 ? MobilePattern().Match(value) : Match.Empty;
        return match.Success ? "+84" + string.Concat(match.Groups[1].Value.Where(char.IsAsciiDigit)) : null;
    }
}
