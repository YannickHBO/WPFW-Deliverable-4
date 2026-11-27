namespace PortfolioApi.GitHub;

/// <summary>Settings from the "GitHub" section of appsettings.json (and user-secrets for the token).</summary>
public class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>Run the background sync (at start-up and then every interval). Off in the tests.</summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>Minutes between two background syncs.</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Optional, read-only GitHub token for a higher rate limit (5,000 instead of 60 requests per hour).
    /// Not needed for this public repository. Never in appsettings.json: only in user-secrets
    /// (dotnet user-secrets set "GitHub:Token" "...") or the host's configuration.
    /// </summary>
    public string? Token { get; set; }
}
