using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using PortfolioApi.Auth;
using PortfolioApi.Controllers;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>
/// Safety net for the authorization rules themselves. The route tests prove the rules work for the
/// routes that exist today; these tests also catch a write route added later without a policy.
/// </summary>
public class AuthorizationRulesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    /// <summary>Routes that must stay reachable without signing in, even though they are POSTs.</summary>
    private static readonly string[] PublicWriteActions =
    [
        $"{nameof(AuthController)}.{nameof(AuthController.Register)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Logout)}"
    ];

    private static IEnumerable<MethodInfo> ActionsOf(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static bool IsRead(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().All(a => a.HttpMethods.All(m => m == "GET"));

    /// <summary>The policy on the action itself, or else on its controller (null = none).</summary>
    private static string? PolicyOf(MethodInfo action) =>
        (action.GetCustomAttribute<AuthorizeAttribute>() ?? action.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>())?.Policy;

    [Fact]
    public void EveryWriteRouteOfEveryController_RequiresAPolicy_ExceptRegisterLoginLogout()
    {
        // Arrange
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        // Act
        var unprotected = controllers
            .SelectMany(ActionsOf)
            .Where(action => !IsRead(action) && PolicyOf(action) is null)
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToList();

        // Assert
        Assert.Equal(PublicWriteActions.Order(), unprotected.Order());
    }

    [Theory]
    [InlineData(typeof(ProjectsController))]
    [InlineData(typeof(BlogPostsController))]
    public void ContentControllers_WritesNeedCanEditContent_ReadsArePublic(Type controller)
    {
        // Arrange
        var actions = ActionsOf(controller).ToList();

        // Act
        var writePolicies = actions.Where(a => !IsRead(a)).Select(PolicyOf).ToList();
        var readPolicies = actions.Where(IsRead).Select(PolicyOf).ToList();

        // Assert
        Assert.Equal(3, writePolicies.Count); // POST, PUT, DELETE
        Assert.All(writePolicies, policy => Assert.Equal(Policies.CanEditContent, policy));
        Assert.Equal(2, readPolicies.Count);  // GET all, GET by id
        Assert.All(readPolicies, Assert.Null);
    }

    [Theory]
    [InlineData(Policies.CanEditContent, new[] { RoleNames.Editor, RoleNames.Admin })]
    [InlineData(Policies.IsAdmin, new[] { RoleNames.Admin })]
    public async Task Policy_AllowsExactlyTheseRoles(string policyName, string[] expectedRoles)
    {
        // Arrange
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        // Act
        var policy = await policies.GetPolicyAsync(policyName);

        // Assert: Admin is named explicitly; Gast never gives write rights.
        var roles = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>()).AllowedRoles;
        Assert.Equal(expectedRoles.Order(), roles.Order());
    }
}
