namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public const string SectionName = "MediaNotification:Storage";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string PublicEndpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;

    public bool IsValid() => !Enabled ||
        (ValidEndpoint(Endpoint) && ValidEndpoint(PublicEndpoint) &&
         !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey) &&
         !string.IsNullOrWhiteSpace(Region) && Bucket.Length is >= 3 and <= 63 &&
         Bucket.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.') &&
         char.IsAsciiLetterOrDigit(Bucket[0]) && char.IsAsciiLetterOrDigit(Bucket[^1]) &&
         !Bucket.Contains("..", StringComparison.Ordinal));

    private static bool ValidEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";
}
