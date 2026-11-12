using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Storage;

public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly S3StorageOptions options;
    private readonly ILogger<S3ObjectStorage> logger;
    private readonly Lazy<AmazonS3Client> dataClient;
    private readonly Lazy<AmazonS3Client> signingClient;

    public S3ObjectStorage(IOptions<S3StorageOptions> options, ILogger<S3ObjectStorage> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        dataClient = new Lazy<AmazonS3Client>(() => CreateClient(this.options.Endpoint));
        signingClient = new Lazy<AmazonS3Client>(() => CreateClient(this.options.PublicEndpoint));
    }

    public Task<Uri> PresignPutAsync(string key, string contentType, DateTimeOffset expiresAt, CancellationToken ct) =>
        GuardAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (!key.StartsWith("staging/", StringComparison.Ordinal))
                throw new InvalidOperationException("Upload capabilities may only target staging objects.");
            var request = new GetPreSignedUrlRequest
            {
                BucketName = options.Bucket, Key = key, Verb = HttpVerb.PUT,
                Protocol = new Uri(options.PublicEndpoint).Scheme == "http" ? Protocol.HTTP : Protocol.HTTPS,
                ContentType = contentType, Expires = expiresAt.UtcDateTime
            };
            // Sign using the browser-reachable endpoint. A signed host must never be rewritten.
            var url = await signingClient.Value.GetPreSignedURLAsync(request);
            ct.ThrowIfCancellationRequested();
            return new Uri(url);
        }, ct);

    public Task<Uri> PresignGetAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken ct) =>
        GuardAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var request = new GetPreSignedUrlRequest
            {
                BucketName = options.Bucket, Key = key, Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
                Protocol = new Uri(options.PublicEndpoint).Scheme == "http" ? Protocol.HTTP : Protocol.HTTPS,
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = $"attachment; filename=\"download\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}",
                    CacheControl = "private, no-store"
                }
            };
            var url = await signingClient.Value.GetPreSignedURLAsync(request);
            ct.ThrowIfCancellationRequested();
            return new Uri(url);
        }, ct);

    public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => GuardAsync<ObjectInfo?>(async () =>
    {
        try
        {
            var response = await dataClient.Value.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.Bucket, Key = key
            }, ct);
            return new ObjectInfo(response.ContentLength, response.Headers.ContentType ?? string.Empty, response.ETag ?? string.Empty);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != "NoSuchBucket")
        {
            return null;
        }
    }, ct);

    public Task CopyAsync(string sourceKey, string destinationKey, string sourceETag, string contentType, CancellationToken ct) =>
        GuardAsync(async () =>
        {
            try
            {
                await dataClient.Value.CopyObjectAsync(new CopyObjectRequest
                {
                    SourceBucket = options.Bucket, SourceKey = sourceKey,
                    DestinationBucket = options.Bucket, DestinationKey = destinationKey,
                    ETagToMatch = sourceETag, MetadataDirective = S3MetadataDirective.REPLACE,
                    ContentType = contentType, ContentDisposition = "attachment", CacheControl = "private, no-store"
                }, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
            {
                throw new FileFailure(409, "Upload changed during confirmation. Retry confirmation.");
            }
        }, ct);

    public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => GuardAsync(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await dataClient.Value.GetObjectAsync(new GetObjectRequest
        {
            BucketName = options.Bucket, Key = key
        }, timeout.Token);
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await response.ResponseStream.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, maxBytes + 1L - content.Length)), timeout.Token)) > 0)
        {
            if (content.Length + count > maxBytes) throw new FileFailure(400, "File exceeds the maximum allowed size.");
            await content.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
        return content.ToArray();
    }, ct);

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            EnsureEnabled();
            await dataClient.Value.DeleteObjectAsync(new DeleteObjectRequest { BucketName = options.Bucket, Key = key }, ct);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "File candidate cleanup was cancelled for {Key}.", key);
            throw;
        }
        catch (Exception ex) { logger.LogWarning(ex, "File candidate cleanup failed for {Key}.", key); }
    }

    private AmazonS3Client CreateClient(string endpoint) => new(new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = endpoint, AuthenticationRegion = options.Region, ForcePathStyle = options.ForcePathStyle,
            Timeout = TimeSpan.FromSeconds(15), MaxErrorRetry = 1
        });

    private void EnsureEnabled()
    {
        if (!options.Enabled) throw Unavailable();
    }

    private async Task<T> GuardAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureEnabled();
        try { return await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (FileFailure) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "S3 file operation failed.");
            throw Unavailable();
        }
    }

    private static FileFailure Unavailable() => new(503, "File storage is temporarily unavailable.");

    public void Dispose()
    {
        if (dataClient.IsValueCreated) dataClient.Value.Dispose();
        if (signingClient.IsValueCreated) signingClient.Value.Dispose();
    }
}
