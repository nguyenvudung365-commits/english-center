namespace EnglishCenter.Modules.MediaNotification.Application.Files;

internal static class AvatarPolicy
{
    public const int MaxSizeBytes = 2 * 1024 * 1024;

    public static bool IsValid(string fileName, string contentType, long sizeBytes) =>
        sizeBytes is > 0 and <= MaxSizeBytes && Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" => string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".png" => string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
