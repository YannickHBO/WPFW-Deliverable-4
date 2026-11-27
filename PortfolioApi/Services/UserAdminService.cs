using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Data;
using PortfolioApi.Dtos;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

public class UserAdminService(
    PortfolioDbContext db,
    UserManager<User> userManager,
    ILogger<UserAdminService> logger) : IUserAdminService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync()
    {
        // One query for all users and their role names, instead of a GetRolesAsync call per user.
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.Email,
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .OrderBy(name => name)
                    .ToList()
            })
            .ToListAsync();

        return users.Select(u => new UserDto(u.Id, u.Email!, u.Roles)).ToList();
    }

    public async Task<GrantEditorResult> GrantEditorAsync(string userId, string adminId)
    {
        // With EnableRetryOnFailure (Program.cs), EF Core only allows a transaction inside its
        // execution strategy: if the database briefly drops out, the strategy runs this whole block
        // again. Every attempt therefore starts from scratch.
        var strategy = db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            // Forget anything a failed earlier attempt left behind in the DbContext.
            db.ChangeTracker.Clear();

            var user = await userManager.FindByIdAsync(userId);
            if (user is null) return new GrantEditorResult(GrantEditorStatus.UserNotFound);

            // Only a plain Gast can be promoted. An Editor or Admin is left alone, so this route can
            // never change anything for an account that already has write rights.
            var roles = await userManager.GetRolesAsync(user);
            var isPlainGuest = roles.Contains(RoleNames.Guest)
                && !roles.Contains(RoleNames.Editor)
                && !roles.Contains(RoleNames.Admin);
            if (!isPlainGuest) return new GrantEditorResult(GrantEditorStatus.NotAGuest);

            // The role and the new SecurityStamp go together: either both are saved or neither.
            // UserManager uses the same scoped DbContext, so its saves run inside this transaction.
            await using var transaction = await db.Database.BeginTransactionAsync();

            ThrowIfFailed(await userManager.AddToRoleAsync(user, RoleNames.Editor), "add the role Editor");

            // AddToRoleAsync does not change the SecurityStamp by itself. Renewing it makes the
            // user's current cookie invalid (401), so they sign in again and get the new role.
            ThrowIfFailed(await userManager.UpdateSecurityStampAsync(user), "renew the security stamp");

            await transaction.CommitAsync();

            var newRoles = await userManager.GetRolesAsync(user);
            return new GrantEditorResult(GrantEditorStatus.Granted,
                new UserDto(user.Id, user.Email!, newRoles.Order().ToList()));
        });

        if (result.Status == GrantEditorStatus.Granted)
            logger.LogInformation("Admin {AdminId} gave user {UserId} the role {Role}.", adminId, userId, RoleNames.Editor);

        return result;
    }

    /// <summary>
    /// An Identity call that fails here is unexpected (the user and role were just checked), so it is
    /// an error, not a validation result. Throwing leaves the transaction uncommitted: it rolls back.
    /// </summary>
    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
    }
}
