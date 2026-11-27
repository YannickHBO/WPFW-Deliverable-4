using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.Auth;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Unit;

/// <summary>
/// UserAdminService on its own. The route tests already show the happy path via HTTP; this class also
/// tests what HTTP can't reach: that the role is rolled back when renewing the security stamp fails.
/// </summary>
public sealed class UserAdminServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>A real UserManager on the test database, built by hand instead of by the DI container.</summary>
    private static UserManager<User> CreateUserManager(PortfolioDbContext db, bool stampUpdateFails = false)
    {
        var store = new UserStore<User>(db);
        var options = Options.Create(new IdentityOptions());
        return stampUpdateFails
            ? new FailingStampUserManager(store, options)
            : new UserManager<User>(store, options, new PasswordHasher<User>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
                NullLogger<UserManager<User>>.Instance);
    }

    private async Task<string> ArrangeUserAsync(params string[] roles)
    {
        await using var db = _database.CreateContext();
        var users = CreateUserManager(db);
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new User { UserName = email, Email = email };
        await users.CreateAsync(user);
        await users.AddToRolesAsync(user, roles);
        return user.Id;
    }

    private async Task<IList<string>> RolesOfAsync(string userId)
    {
        await using var db = _database.CreateContext();
        var users = CreateUserManager(db);
        return await users.GetRolesAsync((await users.FindByIdAsync(userId))!);
    }

    private UserAdminService CreateService(PortfolioDbContext db, bool stampUpdateFails = false) =>
        new(db, CreateUserManager(db, stampUpdateFails), NullLogger<UserAdminService>.Instance);

    [Fact]
    public async Task GrantEditorAsync_Guest_AddsEditorAndRenewsTheSecurityStamp()
    {
        // Arrange
        var userId = await ArrangeUserAsync(RoleNames.Guest);
        var stampBefore = (await _database.CreateContext().Users.SingleAsync(u => u.Id == userId)).SecurityStamp;

        // Act
        var result = await CreateService(_database.CreateContext()).GrantEditorAsync(userId, "admin-id");

        // Assert
        Assert.Equal(GrantEditorStatus.Granted, result.Status);
        Assert.Equal([RoleNames.Editor, RoleNames.Guest], result.User!.Roles);
        Assert.Contains(RoleNames.Editor, await RolesOfAsync(userId));
        var stampAfter = (await _database.CreateContext().Users.SingleAsync(u => u.Id == userId)).SecurityStamp;
        Assert.NotEqual(stampBefore, stampAfter);
    }

    [Fact]
    public async Task GrantEditorAsync_StampUpdateFails_RollsTheRoleBack()
    {
        // Arrange
        var userId = await ArrangeUserAsync(RoleNames.Guest);

        // Act
        var grant = () => CreateService(_database.CreateContext(), stampUpdateFails: true).GrantEditorAsync(userId, "admin-id");

        // Assert: an error, and no Editor role without a new stamp.
        await Assert.ThrowsAsync<InvalidOperationException>(grant);
        Assert.Equal([RoleNames.Guest], await RolesOfAsync(userId));
    }

    [Fact]
    public async Task GrantEditorAsync_UserWithoutTheRoleGast_ReturnsNotAGuest()
    {
        // Arrange: an account without any role (should not exist, but the route must not promote it).
        var userId = await ArrangeUserAsync();

        // Act
        var result = await CreateService(_database.CreateContext()).GrantEditorAsync(userId, "admin-id");

        // Assert
        Assert.Equal(GrantEditorStatus.NotAGuest, result.Status);
        Assert.Empty(await RolesOfAsync(userId));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsUsersOrderedByEmailWithSortedRoles()
    {
        // Arrange
        var first = await ArrangeUserAsync(RoleNames.Guest, RoleNames.Editor);
        var second = await ArrangeUserAsync(RoleNames.Guest);

        // Act
        var users = await CreateService(_database.CreateContext()).GetAllAsync();

        // Assert
        Assert.Equal(users.OrderBy(u => u.Email, StringComparer.Ordinal).Select(u => u.Id), users.Select(u => u.Id));
        Assert.Equal([RoleNames.Editor, RoleNames.Guest], users.Single(u => u.Id == first).Roles);
        Assert.Equal([RoleNames.Guest], users.Single(u => u.Id == second).Roles);
    }

    /// <summary>Hand-made fake: a real UserManager, except that renewing the security stamp fails.</summary>
    private sealed class FailingStampUserManager(IUserStore<User> store, IOptions<IdentityOptions> options)
        : UserManager<User>(store, options, new PasswordHasher<User>(), [], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance)
    {
        public override Task<IdentityResult> UpdateSecurityStampAsync(User user) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Stamp update failed (fake)." }));
    }
}
