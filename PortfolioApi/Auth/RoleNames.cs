namespace PortfolioApi.Auth;

/// <summary>
/// The role names, in one place so a typo can't create a second, different role.
/// Gast: every new account (public registration). Editor and Admin: only assigned by hand
/// (scripts/assign-role.sql) or by an Admin, never by registration.
/// </summary>
public static class RoleNames
{
    public const string Guest = "Gast";
    public const string Editor = "Editor";
    public const string Admin = "Admin";
}
