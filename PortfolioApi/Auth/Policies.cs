namespace PortfolioApi.Auth;

/// <summary>Authorization policy names. The rules themselves are registered in Program.cs.</summary>
public static class Policies
{
    /// <summary>
    /// Create, update and delete projects and blog posts. Requires the role Editor or Admin:
    /// both are named explicitly, because Admin does not inherit Editor's rights automatically.
    /// </summary>
    public const string CanEditContent = "CanEditContent";
}
