using System.Net;
using System.Net.Http.Json;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Models;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>
/// Registration and sign-in with the real Identity configuration: UserManager, password rules,
/// the authentication cookie, lockout and the security stamp check.
/// </summary>
public class AuthRoutesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private Task<IList<string>> RolesOfAsync(string email) => factory.WithUserManagerAsync(async users =>
        await users.GetRolesAsync((await users.FindByEmailAsync(email))!));

    // ---------- POST /api/auth/register ----------

    [Fact]
    public async Task Register_ValidEmailAndPassword_Returns201AndGivesOnlyTheRoleGast()
    {
        // Arrange
        var client = factory.CreateBrowserClient();
        var email = NewEmail();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = PortfolioApiFactory.TestPassword });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadAsync<UserDto>();
        Assert.Equal(email, user.Email);
        Assert.Equal([RoleNames.Guest], user.Roles);
        Assert.Equal([RoleNames.Guest], await RolesOfAsync(email)); // Also in the database.
    }

    [Fact]
    public async Task Register_WithRolesInTheBody_StillOnlyGivesGast()
    {
        // Arrange: an attempt to register straight as Admin (overposting).
        var client = factory.CreateBrowserClient();
        var email = NewEmail();
        var body = new { email, password = PortfolioApiFactory.TestPassword, roles = new[] { RoleNames.Admin, RoleNames.Editor } };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([RoleNames.Guest], await RolesOfAsync(email));
    }

    [Fact]
    public async Task Register_DoesNotSignIn()
    {
        // Arrange
        var client = factory.CreateBrowserClient();
        await client.PostAsJsonAsync("/api/auth/register", new { email = NewEmail(), password = PortfolioApiFactory.TestPassword });

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_ExistingEmail_Returns400()
    {
        // Arrange
        var existing = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = existing.Email, password = PortfolioApiFactory.TestPassword });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DuplicateEmail", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Sh1!", "PasswordTooShort")]
    [InlineData("nouppercase1!", "PasswordRequiresUpper")]
    [InlineData("NoDigitHere!", "PasswordRequiresDigit")]
    [InlineData("NoSymbol123", "PasswordRequiresNonAlphanumeric")]
    public async Task Register_WeakPassword_Returns400WithIdentityRule(string password, string expectedError)
    {
        // Arrange
        var client = factory.CreateBrowserClient();
        var email = NewEmail();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedError, await response.Content.ReadAsStringAsync());
        Assert.Null(await factory.WithUserManagerAsync(users => users.FindByEmailAsync(email)));
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = PortfolioApiFactory.TestPassword });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Email", await response.Content.ReadAsStringAsync());
    }

    // ---------- POST /api/auth/login ----------

    [Fact]
    public async Task Login_CorrectPassword_Returns200AndASecureHttpOnlyCookie()
    {
        // Arrange: a client that doesn't swallow the cookie, so the Set-Cookie header can be read.
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient(handleCookies: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([RoleNames.Guest], (await response.ReadAsync<UserDto>()).Roles);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("PortfolioAuth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithoutCookie()
    {
        // Arrange
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient(handleCookies: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "Wrong-Password1" });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401WithSameMessageAsWrongPassword()
    {
        // Arrange
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new { email = NewEmail(), password = "Wrong-Password1" });
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "Wrong-Password1" });

        // Assert: the response doesn't reveal whether the e-mail address has an account.
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(TitleOf(await unknownEmail.Content.ReadAsStringAsync()), TitleOf(await wrongPassword.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Login_AfterThreeWrongPasswords_IsLockedOutEvenWithTheRightPassword()
    {
        // Arrange
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();
        for (var attempt = 0; attempt < 3; attempt++)
            await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "Wrong-Password1" });

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await factory.WithUserManagerAsync(async users => await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id))!)));
    }

    [Fact]
    public async Task Login_TwoWrongPasswordsThenTheRightOne_SignsInAndResetsTheCounter()
    {
        // Arrange
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();
        for (var attempt = 0; attempt < 2; attempt++)
            await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "Wrong-Password1" });

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await factory.WithUserManagerAsync(async users => await users.GetAccessFailedCountAsync((await users.FindByIdAsync(user.Id))!)));
    }

    [Fact]
    public async Task Login_InvalidBody_Returns400()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/auth/me ----------

    [Theory]
    [InlineData(RoleNames.Guest, new[] { RoleNames.Guest })]
    [InlineData(RoleNames.Editor, new[] { RoleNames.Editor, RoleNames.Guest })]
    [InlineData(RoleNames.Admin, new[] { RoleNames.Admin, RoleNames.Guest })]
    public async Task Me_SignedIn_Returns200WithTheRolesFromTheCookie(string role, string[] expectedRoles)
    {
        // Arrange
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedRoles.Order(), (await response.ReadAsync<UserDto>()).Roles.Order());
    }

    [Fact]
    public async Task Me_Anonymous_Returns401NotARedirect()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Me_AfterSecurityStampChange_Returns401SoTheUserMustSignInAgain()
    {
        // Arrange: signed in, then the stamp changes (as on every role change).
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();
        await PortfolioApiFactory.SignInAsync(client, user);
        await factory.WithUserManagerAsync(async users => await users.UpdateSecurityStampAsync((await users.FindByIdAsync(user.Id))!));

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterRoleChange_GivesACookieWithTheNewRole()
    {
        // Arrange: Gast becomes Editor the way the SQL script does it (role + new stamp).
        var user = await factory.CreateUserAsync();
        var client = factory.CreateBrowserClient();
        await PortfolioApiFactory.SignInAsync(client, user);
        await factory.WithUserManagerAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id))!;
            await users.AddToRoleAsync(stored, RoleNames.Editor);
            return await users.UpdateSecurityStampAsync(stored);
        });
        var oldCookie = await client.GetAsync("/api/auth/me");

        // Act
        await PortfolioApiFactory.SignInAsync(client, user);
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(RoleNames.Editor, (await response.ReadAsync<UserDto>()).Roles);
    }

    // ---------- POST /api/auth/logout ----------

    [Fact]
    public async Task Logout_SignedIn_Returns204AndTheCookieNoLongerWorks()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Guest);

        // Act
        var response = await client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_Anonymous_Returns204()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string? TitleOf(string problemJson) =>
        System.Text.Json.JsonDocument.Parse(problemJson).RootElement.GetProperty("title").GetString();
}
