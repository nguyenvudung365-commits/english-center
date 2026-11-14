using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class MediaFileValidationTests
{
    private const string PresignUrl = "/api/media-notification/files/presign";
    private static readonly string ValidSubject = Guid.NewGuid().ToString();
    private const long MaxSizeBytes = 20 * 1024 * 1024;

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(MaxSizeBytes + 1)]
    [InlineData(null)]
    public async Task Presign_InvalidSize_Returns400BeforeDatabaseOrStorage(long? sizeBytes)
    {
        await using var factory = new ValidationFactory();
        using var client = factory.CreateClient();
        var body = ValidBody();
        body["sizeBytes"] = sizeBytes;

        using var response = await client.PostAsJsonAsync(PresignUrl, body);

        await AssertProblemDetails(response, HttpStatusCode.BadRequest, "SizeBytes");
        Assert.Equal(0, factory.Dependencies.Calls);
    }

    [Theory]
    [InlineData("fileName", "lesson.exe", "FileName")]
    [InlineData("contentType", "image/png", "ContentType")]
    [InlineData("purpose", "avatar", "Purpose")]
    [InlineData("purpose", "", "Purpose")]
    [InlineData("fileName", "../lesson.pdf", "FileName")]
    [InlineData("fileName", "C:\\lesson.pdf", "FileName")]
    [InlineData("fileName", "folder/lesson.pdf", "FileName")]
    [InlineData("fileName", "lesson\n.pdf", "FileName")]
    [InlineData("fileName", "lesson\u0000.pdf", "FileName")]
    [InlineData("fileName", "", "FileName")]
    [InlineData("fileName", "   ", "FileName")]
    [InlineData("fileName", null, "FileName")]
    [InlineData("contentType", null, "ContentType")]
    [InlineData("purpose", null, "Purpose")]
    public async Task Presign_InvalidField_Returns400BeforeDatabaseOrStorage(
        string property, string? value, string errorKey)
    {
        await using var factory = new ValidationFactory();
        using var client = factory.CreateClient();
        var body = ValidBody();
        body[property] = value;

        using var response = await client.PostAsJsonAsync(PresignUrl, body);

        await AssertProblemDetails(response, HttpStatusCode.BadRequest, errorKey);
        Assert.Equal(0, factory.Dependencies.Calls);
    }

    [Theory]
    [InlineData("fileName", "FileName")]
    [InlineData("contentType", "ContentType")]
    [InlineData("sizeBytes", "SizeBytes")]
    [InlineData("purpose", "Purpose")]
    public async Task Presign_MissingField_Returns400BeforeDatabaseOrStorage(string property, string errorKey)
    {
        await using var factory = new ValidationFactory();
        using var client = factory.CreateClient();
        var body = ValidBody();
        body.Remove(property);

        using var response = await client.PostAsJsonAsync(PresignUrl, body);

        await AssertProblemDetails(response, HttpStatusCode.BadRequest, errorKey);
        Assert.Equal(0, factory.Dependencies.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Presign_AuthenticatedWithInvalidSubject_Returns401BeforeDatabaseOrStorage(string? subject)
    {
        // The authentication ticket succeeds even for a missing/malformed subject. The endpoint
        // must obtain the owner from the real HTTP-backed ICurrentUser and reject that identity.
        await using var factory = new ValidationFactory(subject);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(PresignUrl, ValidBody());

        await AssertProblemDetails(response, HttpStatusCode.Unauthorized);
        Assert.Equal(0, factory.Dependencies.Calls);
    }

    [Fact]
    public async Task Presign_Anonymous_Returns401BeforeDatabaseOrStorage()
    {
        await using var factory = new ValidationFactory(authenticated: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(PresignUrl, ValidBody());

        await AssertProblemDetails(response, HttpStatusCode.Unauthorized);
        Assert.Equal(0, factory.Dependencies.Calls);
    }

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["fileName"] = "lesson.pdf",
        ["contentType"] = "application/pdf",
        ["sizeBytes"] = 100L,
        ["purpose"] = "submission"
    };

    private static async Task AssertProblemDetails(
        HttpResponseMessage response, HttpStatusCode expected, string? errorKey = null)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));

        // Business validation may return plain ProblemDetails. If field errors are supplied,
        // check that they identify the rejected field rather than a generic/unrelated error.
        if (errorKey is not null && problem.TryGetProperty("errors", out var errors))
        {
            Assert.Equal(JsonValueKind.Object, errors.ValueKind);
            var field = Assert.Single(errors.EnumerateObject(),
                x => string.Equals(x.Name, errorKey, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(JsonValueKind.Array, field.Value.ValueKind);
            Assert.NotEmpty(field.Value.EnumerateArray());
        }
    }

    [Fact]
    public async Task Fixture_ValidSubject_ReachesPersistenceWithHttpCurrentUserOwner()
    {
        var expectedOwner = Guid.Parse(ValidSubject);
        Assert.NotEqual(Guid.Empty, expectedOwner);
        await using var factory = new ValidationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(PresignUrl, ValidBody());

        // The fail-fast fake intentionally aborts at persistence: this is not a successful-upload
        // test. Reaching it with the expected owner verifies the real HTTP-backed current user.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, factory.Dependencies.Calls);
        Assert.Equal(expectedOwner, factory.Dependencies.AttemptedOwnerId);
    }

    private sealed class ValidationFactory : WebApplicationFactory<Program>
    {
        private readonly string? _subject;
        private readonly bool _authenticated;

        public ValidationFactory(bool authenticated = true) : this(ValidSubject, authenticated) { }

        public ValidationFactory(string? subject, bool authenticated = true)
        {
            _subject = subject;
            _authenticated = authenticated;
        }

        public NoIoDependencies Dependencies { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Identity:InitializeDatabase", "false");
            builder.UseSetting("Academic:InitializeDatabase", "false");
            builder.UseSetting("MediaNotification:InitializeDatabase", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStore>();
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<IFileStore>(Dependencies);
                services.AddSingleton<IObjectStorage>(Dependencies);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = ValidationAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ValidationAuthenticationHandler.SchemeName;
                }).AddScheme<ValidationAuthenticationOptions, ValidationAuthenticationHandler>(
                    ValidationAuthenticationHandler.SchemeName, options =>
                    {
                        options.Subject = _subject;
                        options.Authenticated = _authenticated;
                    });
            });
        }
    }

    private sealed class ValidationAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string? Subject { get; set; }
        public bool Authenticated { get; set; }
    }

    private sealed class ValidationAuthenticationHandler(
        IOptionsMonitor<ValidationAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<ValidationAuthenticationOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MediaValidationTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Options.Authenticated) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(AuthClaims.Role, Roles.Student) };
            if (Options.Subject is not null) claims.Add(new Claim(AuthClaims.Subject, Options.Subject));
            var identity = new ClaimsIdentity(claims, SchemeName, AuthClaims.Subject, AuthClaims.Role);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class NoIoDependencies : IFileStore, IObjectStorage
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Guid? AttemptedOwnerId { get; private set; }

        private InvalidOperationException UnexpectedIo()
        {
            Interlocked.Increment(ref _calls);
            return new InvalidOperationException("Rejected requests must not access the database or object storage.");
        }

        public Task AddAsync(StoredFile file, CancellationToken ct)
        {
            AttemptedOwnerId = file.OwnerId;
            throw UnexpectedIo();
        }
        public Task<StoredFile?> FindOwnedAsync(Guid fileId, Guid ownerId, CancellationToken ct) => throw UnexpectedIo();
        public Task<bool> TryCompleteAsync(Guid fileId, Guid ownerId, Guid revision, string fileKey,
            DateTimeOffset uploadedAt, CancellationToken ct) => throw UnexpectedIo();
        public Task<bool> IsReferencedAsync(string fileKey, CancellationToken ct) => throw UnexpectedIo();
        public Task<Uri> PresignPutAsync(string key, string contentType, DateTimeOffset expiresAt,
            CancellationToken ct) => throw UnexpectedIo();
        public Task<Uri> PresignGetAsync(string key, string fileName, DateTimeOffset expiresAt,
            CancellationToken ct) => throw UnexpectedIo();
        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw UnexpectedIo();
        public Task CopyAsync(string sourceKey, string destinationKey, string sourceETag, string contentType,
            CancellationToken ct) => throw UnexpectedIo();
        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => throw UnexpectedIo();
        public Task DeleteAsync(string key, CancellationToken ct) => throw UnexpectedIo();
    }
}
