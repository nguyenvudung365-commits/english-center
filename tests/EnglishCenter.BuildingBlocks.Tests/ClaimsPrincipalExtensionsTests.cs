using System.Security.Claims;
using EnglishCenter.BuildingBlocks.Security;
using Xunit;

namespace EnglishCenter.BuildingBlocks.Tests;

public sealed class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test", AuthClaims.Subject, AuthClaims.Role));

    [Fact]
    public void TryGetUserId_ValidGuidSub_ReturnsTrueAndId()
    {
        var id = Guid.NewGuid();
        Assert.True(Authenticated(new Claim(AuthClaims.Subject, id.ToString())).TryGetUserId(out var actual));
        Assert.Equal(id, actual);
    }

    [Fact]
    public void TryGetUserId_AuthenticatedPrincipalWithEmptyGuidSub_ReturnsFalseAndEmptyId()
    {
        var principal = Authenticated(new Claim(AuthClaims.Subject, Guid.Empty.ToString()));

        var result = principal.TryGetUserId(out var id);

        Assert.Equal(Guid.Empty, id);
        Assert.False(result);
    }

    [Fact]
    public void TryGetUserId_MissingSub_ReturnsFalse()
    {
        Assert.False(Authenticated(new Claim(AuthClaims.Role, Roles.Student)).TryGetUserId(out var id));
        Assert.Equal(Guid.Empty, id);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("12345")]
    public void TryGetUserId_NonGuidSub_ReturnsFalse(string sub)
    {
        Assert.False(Authenticated(new Claim(AuthClaims.Subject, sub)).TryGetUserId(out _));
    }

    [Fact]
    public void TryGetUserId_UnauthenticatedPrincipal_ReturnsFalseEvenWithValidSub()
    {
        var identity = new ClaimsIdentity([new Claim(AuthClaims.Subject, Guid.NewGuid().ToString())]); // no authenticationType
        Assert.False(new ClaimsPrincipal(identity).TryGetUserId(out _));
        Assert.False(new ClaimsPrincipal().TryGetUserId(out _));
    }

    [Fact]
    public void TryGetUserId_MicrosoftUriClaim_IsNotAccepted()
    {
        var claim = new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString());
        Assert.False(Authenticated(claim).TryGetUserId(out _));
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    public void GetRole_ReturnsRoleClaim(string role)
    {
        Assert.Equal(role, Authenticated(new Claim(AuthClaims.Role, role)).GetRole());
    }

    [Fact]
    public void GetRole_NoClaim_ReturnsNull_AndMicrosoftRoleUriIsIgnored()
    {
        Assert.Null(Authenticated().GetRole());
        Assert.Null(Authenticated(new Claim(ClaimTypes.Role, Roles.Admin)).GetRole());
    }

    [Theory]
    [InlineData(Roles.Staff, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Teacher, false)]
    [InlineData(Roles.Student, false)]
    [InlineData("Other", false)]
    public void IsStaffOrAdmin_MatchesRole(string role, bool expected)
    {
        Assert.Equal(expected, Authenticated(new Claim(AuthClaims.Role, role)).IsStaffOrAdmin());
    }

    [Fact]
    public void IsStaffOrAdmin_WithoutRole_IsFalse()
    {
        Assert.False(Authenticated().IsStaffOrAdmin());
    }

    [Fact]
    public void BuildingBlocks_DoesNotReferenceAspNetCore()
    {
        var refs = typeof(ICurrentUser).Assembly.GetReferencedAssemblies().Select(x => x.Name!);
        Assert.DoesNotContain(refs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
}
