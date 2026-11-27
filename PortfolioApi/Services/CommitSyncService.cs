using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.GitHub;
using PortfolioApi.Models;

namespace PortfolioApi.Services;

/// <summary>
/// What survives between syncs: the lock that stops two syncs from running at once (the background
/// service and the admin route) and GitHub's ETag of the last answer. Registered as a singleton.
/// </summary>
public class CommitSyncState
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public string? ETag { get; set; }
}

public class CommitSyncService(
    PortfolioDbContext db,
    IGitHubCommitClient gitHub,
    CommitSyncState state,
    TimeProvider clock,
    ILogger<CommitSyncService> logger) : ICommitSyncService
{
    private const string TitlePrefix = "Blog Post ";

    /// <summary>The blog shows commit times in Dutch time, like the seed data and <c>git log</c> on my machine.</summary>
    private static readonly TimeZoneInfo DutchTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public async Task<CommitSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        // One sync at a time: two at once would both see the same new commit and add it twice.
        await state.Lock.WaitAsync(cancellationToken);
        try
        {
            GitHubCommitsResponse response;
            try
            {
                response = await gitHub.GetCommitsAsync(state.ETag, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // GitHub down, rate limited or no internet: the blog keeps showing what is in the database.
                logger.LogWarning(ex, "Commit sync skipped: GitHub could not be reached.");
                return new CommitSyncResult(CommitSyncStatus.GitHubUnavailable);
            }

            if (response.NotModified) return new CommitSyncResult(CommitSyncStatus.NotModified);

            var added = await AddNewCommitsAsync(response.Commits, cancellationToken);

            // Only remember the ETag once the commits are saved. If saving failed, the next sync must
            // get the full list again instead of a "304 Not Modified" that would skip these commits.
            state.ETag = response.ETag;

            if (added > 0) logger.LogInformation("Commit sync added {Count} blog post(s).", added);
            return new CommitSyncResult(CommitSyncStatus.Synced, added);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    private async Task<int> AddNewCommitsAsync(IReadOnlyList<GitHubCommit> commits, CancellationToken cancellationToken)
    {
        var handled = (await db.SyncedCommits.Select(c => c.Sha).ToListAsync(cancellationToken)).ToHashSet();
        var withPost = (await db.BlogPosts
                .Where(b => b.CommitSha != null)
                .Select(b => b.CommitSha!)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        // Oldest first, so the post numbers follow the order of the commits.
        var newCommits = commits
            .Where(c => !c.IsMerge && !handled.Contains(c.Sha))
            .OrderBy(c => c.AuthoredAt)
            .ToList();
        if (newCommits.Count == 0) return 0;

        var nextNumber = await HighestPostNumberAsync(cancellationToken) + 1;
        var now = clock.GetUtcNow().UtcDateTime;
        var added = 0;

        foreach (var commit in newCommits)
        {
            // A post that already exists for this commit (the seed data) is only marked as handled.
            if (!withPost.Contains(commit.Sha))
            {
                db.BlogPosts.Add(new BlogPost
                {
                    Title = TitlePrefix + nextNumber++,
                    Content = commit.Message.Trim(),
                    PublishedDate = TimeZoneInfo.ConvertTime(commit.AuthoredAt, DutchTime).DateTime,
                    CommitSha = commit.Sha
                });
                added++;
            }

            // Remembered for good, so a commit post that an Editor deleted is not added again.
            db.SyncedCommits.Add(new SyncedCommit { Sha = commit.Sha, SyncedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>The highest N of all posts titled "Blog Post N" (hand-written ones too), or 0.</summary>
    private async Task<int> HighestPostNumberAsync(CancellationToken cancellationToken)
    {
        var titles = await db.BlogPosts
            .Where(b => b.Title.StartsWith(TitlePrefix))
            .Select(b => b.Title)
            .ToListAsync(cancellationToken);

        return titles
            .Select(title => int.TryParse(title[TitlePrefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
    }
}
