namespace PortfolioApi.GitHub;

/// <summary>
/// Reads the commits of <see cref="GitHubRepository"/> from the GitHub REST API. An interface, so the
/// tests can use a fake with fixed commits and never call GitHub.
/// </summary>
public interface IGitHubCommitClient
{
    /// <summary>
    /// The newest 100 commits on the branch, newest first. With the ETag of an earlier answer,
    /// GitHub answers "304 Not Modified" when nothing changed (that costs no rate limit).
    /// </summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached or gave an error status.</exception>
    /// <exception cref="TaskCanceledException">GitHub did not answer in time.</exception>
    Task<GitHubCommitsResponse> GetCommitsAsync(string? etag, CancellationToken cancellationToken);
}

/// <param name="NotModified">True when GitHub answered 304: nothing changed since <c>etag</c>.</param>
/// <param name="Commits">The commits, newest first; empty when NotModified.</param>
/// <param name="ETag">The ETag of this answer, to send along next time.</param>
public record GitHubCommitsResponse(bool NotModified, IReadOnlyList<GitHubCommit> Commits, string? ETag);

/// <param name="Sha">The full commit SHA (40 characters).</param>
/// <param name="Message">The whole commit message.</param>
/// <param name="AuthoredAt">When the commit was written (the author date, like <c>git log</c> shows).</param>
/// <param name="IsMerge">True for a merge commit (more than one parent): those don't become posts.</param>
public record GitHubCommit(string Sha, string Message, DateTimeOffset AuthoredAt, bool IsMerge);
