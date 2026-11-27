using PortfolioApi.Dtos;

namespace PortfolioApi.Services;

/// <summary>User management for Admins: list the accounts and promote a Gast to Editor.</summary>
public interface IUserAdminService
{
    /// <summary>All accounts with their roles, ordered by e-mail address.</summary>
    Task<IReadOnlyList<UserDto>> GetAllAsync();

    /// <summary>
    /// Gives the user the role Editor, but only when they are a plain Gast (not Editor or Admin yet).
    /// Also renews their SecurityStamp, so they have to sign in again.
    /// </summary>
    /// <param name="userId">The account to promote.</param>
    /// <param name="adminId">The Admin doing it, for the log.</param>
    Task<GrantEditorResult> GrantEditorAsync(string userId, string adminId);
}

/// <summary>What happened when an Admin tried to make someone Editor.</summary>
public enum GrantEditorStatus
{
    Granted,
    UserNotFound,
    NotAGuest
}

/// <param name="Status">The outcome.</param>
/// <param name="User">The account with its roles after the change; only set when Status is Granted.</param>
public record GrantEditorResult(GrantEditorStatus Status, UserDto? User = null);
