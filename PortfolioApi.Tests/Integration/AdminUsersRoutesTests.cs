using System.Net;
using System.Net.Http.Json;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>The admin routes for users: only an Admin may list users and make a Gast an Editor.</summary>
public class AdminUsersRoutesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private Task<IList<string>> RolesOfAsync(string userId) => factory.WithUserManagerAsync(async users =>
        await users.GetRolesAsync((await users.FindByIdAsync(userId))!));

    // ---------- GET /api/admin/users ----------

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(RoleNames.Guest, HttpStatusCode.Forbidden)]
    [InlineData(RoleNames.Editor, HttpStatusCode.Forbidden)]
    public async Task GetAll_NotAdmin_IsRefused(string? role, HttpStatusCode expected)
    {
        // Arrange
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.GetAsync("/api/admin/users");

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AsAdmin_Returns200WithUsersAndTheirRoles()
    {
        // Arrange
        var editor = await factory.CreateUserAsync(RoleNames.Editor);
        var client = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        var response = await client.GetAsync("/api/admin/users");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.ReadAsync<List<UserDto>>();
        var listed = Assert.Single(users, u => u.Id == editor.Id);
        Assert.Equal(editor.Email, listed.Email);
        Assert.Equal([RoleNames.Editor, RoleNames.Guest], listed.Roles);
    }

    // ---------- POST /api/admin/users/{id}/editor ----------

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(RoleNames.Guest, HttpStatusCode.Forbidden)]
    [InlineData(RoleNames.Editor, HttpStatusCode.Forbidden)]
    public async Task GrantEditor_NotAdmin_IsRefusedAndChangesNothing(string? role, HttpStatusCode expected)
    {
        // Arrange
        var guest = await factory.CreateUserAsync();
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.PostAsync($"/api/admin/users/{guest.Id}/editor", null);

        // Assert
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal([RoleNames.Guest], await RolesOfAsync(guest.Id));
    }

    [Fact]
    public async Task GrantEditor_AsAdminForAGuest_Returns200AndTheGuestIsEditorAfterSigningInAgain()
    {
        // Arrange: the guest is signed in before the change.
        var guest = await factory.CreateUserAsync();
        var guestClient = factory.CreateBrowserClient();
        await PortfolioApiFactory.SignInAsync(guestClient, guest);
        var adminClient = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        var response = await adminClient.PostAsync($"/api/admin/users/{guest.Id}/editor", null);

        // Assert: the role is stored ...
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([RoleNames.Editor, RoleNames.Guest], (await response.ReadAsync<UserDto>()).Roles);
        Assert.Contains(RoleNames.Editor, await RolesOfAsync(guest.Id));

        // ... the guest's old cookie no longer works ...
        Assert.Equal(HttpStatusCode.Unauthorized, (await guestClient.GetAsync("/api/auth/me")).StatusCode);

        // ... and after signing in again, the new Editor may write.
        await PortfolioApiFactory.SignInAsync(guestClient, guest);
        var write = await guestClient.PostAsJsonAsync("/api/blogposts",
            new { title = "First post as Editor", content = "x", publishedDate = "2026-11-27T12:00:00" });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
    }

    [Fact]
    public async Task GrantEditor_AsAdmin_DoesNotSignTheAdminOut()
    {
        // Arrange
        var guest = await factory.CreateUserAsync();
        var adminClient = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        await adminClient.PostAsync($"/api/admin/users/{guest.Id}/editor", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task GrantEditor_UnknownUser_Returns404()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        var response = await client.PostAsync("/api/admin/users/does-not-exist/editor", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task GrantEditor_UserIsAlreadyEditorOrAdmin_Returns409AndChangesNothing(string existingRole)
    {
        // Arrange
        var user = await factory.CreateUserAsync(existingRole);
        var rolesBefore = await RolesOfAsync(user.Id);
        var client = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        var response = await client.PostAsync($"/api/admin/users/{user.Id}/editor", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(rolesBefore, await RolesOfAsync(user.Id));
    }
}
