using PortfolioApi.GitHub;

namespace PortfolioApi.Tests.Support;

/// <summary>
/// Hand-made fake of the GitHub client (see the mocking slides): the tests decide which commits
/// "GitHub" has, whether it is reachable, and the tests never call the real GitHub.
/// It behaves like GitHub with ETags: the same ETag back means "304 Not Modified".
/// </summary>
public class FakeGitHubCommitClient : IGitHubCommitClient
{
    private readonly List<GitHubCommit> _commits = [];
    private int _version = 1;

    /// <summary>Set to make the next calls fail like an unreachable GitHub.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>The ETag the sync sent with its last call (null = none).</summary>
    public string? LastETagReceived { get; private set; }

    public int Calls { get; private set; }

    public string CurrentETag => $"\"v{_version}\"";

    /// <summary>"Pushes" commits to the fake repository; like on GitHub, this gives a new ETag.</summary>
    public void Push(params GitHubCommit[] commits)
    {
        _commits.AddRange(commits);
        _version++;
    }

    public Task<GitHubCommitsResponse> GetCommitsAsync(string? etag, CancellationToken cancellationToken)
    {
        Calls++;
        LastETagReceived = etag;

        if (FailWith is not null) throw FailWith;

        if (etag == CurrentETag)
            return Task.FromResult(new GitHubCommitsResponse(NotModified: true, [], etag));

        // Newest first, like the GitHub API.
        var newestFirst = _commits.OrderByDescending(c => c.AuthoredAt).ToList();
        return Task.FromResult(new GitHubCommitsResponse(NotModified: false, newestFirst, CurrentETag));
    }

    /// <summary>A commit with a unique, valid-looking SHA (40 hex characters).</summary>
    public static GitHubCommit Commit(string message, DateTimeOffset authoredAt, bool isMerge = false) =>
        new(Guid.NewGuid().ToString("N") + "00000000", message, authoredAt, isMerge);
}
