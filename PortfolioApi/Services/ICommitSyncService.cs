namespace PortfolioApi.Services;

/// <summary>Turns new commits on GitHub into blog posts.</summary>
public interface ICommitSyncService
{
    /// <summary>
    /// Fetches the commits from GitHub and adds a blog post for every commit that hasn't been handled
    /// yet. Never throws when GitHub is unreachable: then nothing changes and the status says so.
    /// </summary>
    Task<CommitSyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

public enum CommitSyncStatus
{
    /// <summary>GitHub answered; <see cref="CommitSyncResult.Added"/> posts were added (can be 0).</summary>
    Synced,

    /// <summary>GitHub answered "nothing changed since last time" (ETag).</summary>
    NotModified,

    /// <summary>GitHub could not be reached or gave an error; the existing posts are unchanged.</summary>
    GitHubUnavailable
}

/// <param name="Status">How the sync went.</param>
/// <param name="Added">The number of new blog posts.</param>
public record CommitSyncResult(CommitSyncStatus Status, int Added = 0);
