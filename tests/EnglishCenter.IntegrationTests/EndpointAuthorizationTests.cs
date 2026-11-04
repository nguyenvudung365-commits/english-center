using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EnglishCenter.IntegrationTests;

/// <summary>Tiêu chí E1: mọi action phải khai báo quyền tường minh; ngoại lệ cố định là các ping controller.</summary>
public sealed class EndpointAuthorizationTests
{
    // Danh sách ngoại lệ cố định (kiểm tra theo thực tế mã nguồn: mỗi module có đúng một ping controller).
    private static readonly string[] AllowedUnprotected =
    [
        "EnglishCenter.Modules.Identity.Api.Controllers.IdentityPingController",
        "EnglishCenter.Modules.Academic.Api.Controllers.AcademicPingController",
        "EnglishCenter.Modules.Enrollment.Api.Controllers.EnrollmentPingController",
        "EnglishCenter.Modules.Finance.Api.Controllers.FinancePingController",
        "EnglishCenter.Modules.MediaNotification.Api.Controllers.MediaNotificationPingController"
    ];

    private static readonly Assembly[] ApiAssemblies =
    [
        EnglishCenter.Modules.Identity.Api.AssemblyReference.Assembly,
        EnglishCenter.Modules.Academic.Api.AssemblyReference.Assembly,
        EnglishCenter.Modules.Enrollment.Api.AssemblyReference.Assembly,
        EnglishCenter.Modules.Finance.Api.AssemblyReference.Assembly,
        EnglishCenter.Modules.MediaNotification.Api.AssemblyReference.Assembly
    ];

    private static IEnumerable<Type> Controllers() => ApiAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    private static bool HasDeclaration(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(a => a is IAuthorizeData || a is IAllowAnonymous);

    [Fact]
    public void ControllersAreDiscovered_InEveryModule()
    {
        var controllers = Controllers().ToList();
        Assert.NotEmpty(controllers);
        foreach (var module in new[] { "Identity", "Academic", "Enrollment", "Finance", "MediaNotification" })
            Assert.Contains(controllers, c => c.Namespace!.StartsWith($"EnglishCenter.Modules.{module}.Api", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryAction_DeclaresAuthorizeOrAllowAnonymous_ExceptFixedPingList()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers().Where(c => !AllowedUnprotected.Contains(c.FullName)))
            foreach (var action in Actions(controller))
                if (!HasDeclaration(controller) && !HasDeclaration(action))
                    violations.Add($"{controller.FullName}.{action.Name}");
        Assert.True(violations.Count == 0, "Action thiếu [Authorize]/[AllowAnonymous]: " + string.Join(", ", violations));
    }

    [Fact]
    public void ExceptionList_MatchesReality_OnlyPingControllersAreUnprotected()
    {
        var unprotected = Controllers()
            .Where(c => !HasDeclaration(c) && Actions(c).Any(a => !HasDeclaration(a)))
            .Select(c => c.FullName!)
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(AllowedUnprotected.OrderBy(x => x).ToArray(), unprotected);
    }

    [Fact]
    public void PingControllers_AreReadOnly_GetOnly()
    {
        foreach (var controller in Controllers().Where(c => AllowedUnprotected.Contains(c.FullName)))
            foreach (var action in Actions(controller))
                Assert.All(action.GetCustomAttributes<HttpMethodAttribute>(), a => Assert.Equal(["GET"], a.HttpMethods));
    }

    [Fact]
    public void Controllers_ExposeNoActionsWithoutHttpVerb()
    {
        var bad = Controllers().SelectMany(c => Actions(c).Where(a => !a.GetCustomAttributes<HttpMethodAttribute>(true).Any())
            .Select(a => $"{c.FullName}.{a.Name}")).ToList();
        Assert.True(bad.Count == 0, "Action công khai không có HTTP verb: " + string.Join(", ", bad));
    }

    [Theory]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.AuthController", "Login", true)]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.AuthController", "Refresh", true)]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.AuthController", "Logout", false)]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.AuthController", "ChangePassword", false)]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.UsersController", "Create", false)]
    [InlineData("EnglishCenter.Modules.Identity.Api.Controllers.UsersController", "Republish", false)]
    public void IdentityMatrix_AnonymousOnlyForLoginAndRefresh(string controllerName, string actionName, bool anonymous)
    {
        var controller = Controllers().Single(c => c.FullName == controllerName);
        var action = controller.GetMethod(actionName)!;
        Assert.Equal(anonymous, action.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    [Fact]
    public void Republish_IsAdminOnly()
    {
        var action = Controllers().Single(c => c.Name == "UsersController").GetMethod("Republish")!;
        var authorize = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin", authorize.Roles);
    }
}
