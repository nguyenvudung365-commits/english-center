using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class MediaFileAuthorizationTests
{
    [Fact]
    public async Task Presign_WithoutAuthentication_Returns401ProblemDetails()
    {
        await using var factory = new MediaFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/media-notification/files/presign", new
        {
            fileName = "lesson.pdf",
            contentType = "application/pdf",
            sizeBytes = 100,
            purpose = "submission"
        });

        await AssertUnauthorizedProblemDetails(response);
    }

    [Fact]
    public async Task Confirm_WithoutAuthentication_Returns401ProblemDetails()
    {
        await using var factory = new MediaFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/media-notification/files/{Guid.NewGuid()}/confirm", null);

        await AssertUnauthorizedProblemDetails(response);
    }

    [Fact]
    public async Task Download_WithoutAuthentication_Returns401ProblemDetails()
    {
        await using var factory = new MediaFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/media-notification/files/{Guid.NewGuid()}/download");

        await AssertUnauthorizedProblemDetails(response);
    }

    private static async Task AssertUnauthorizedProblemDetails(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class MediaFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Identity:InitializeDatabase", "false");
            builder.UseSetting("Academic:InitializeDatabase", "false");
        }
    }
}
