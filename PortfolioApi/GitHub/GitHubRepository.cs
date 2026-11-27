namespace PortfolioApi.GitHub;

/// <summary>
/// The public repository of this portfolio, whose commits become blog posts. Fixed on purpose:
/// the blog shows the history of this site, not of a repository that can be configured.
/// </summary>
public static class GitHubRepository
{
    public const string Owner = "YannickHBO";
    public const string Name = "WPFW-Deliverable-4";

    /// <summary>Only commits on this branch become blog posts (so work merged into it, not unmerged branches).</summary>
    public const string Branch = "main";

    /// <summary>The commit's page on GitHub, e.g. for the "commit 4c44f35" link on the blog.</summary>
    public static string CommitUrl(string sha) => $"https://github.com/{Owner}/{Name}/commit/{sha}";
}
