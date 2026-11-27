namespace PortfolioApi.Models;

/// <summary>
/// A commit the sync has already handled. The sync never makes a second post for a commit in this
/// table, so a commit post that an Editor deleted stays deleted.
/// </summary>
public class SyncedCommit
{
    /// <summary>The full git commit SHA (40 characters); the primary key.</summary>
    public string Sha { get; set; } = string.Empty;

    /// <summary>When the sync first saw this commit (UTC).</summary>
    public DateTime SyncedAt { get; set; }
}
