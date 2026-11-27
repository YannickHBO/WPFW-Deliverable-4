using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApi.Services;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Unit;

/// <summary>
/// The commit sync with a fake GitHub and a fixed clock: which commits become posts, their number,
/// text and time, and what happens when GitHub can't be reached. Every test gets a fresh database.
/// </summary>
public sealed class CommitSyncServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeGitHubCommitClient _gitHub = new();
    private readonly CommitSyncState _state = new();

    public void Dispose() => _database.Dispose();

    /// <summary>A new service on a new DbContext, like one request or one background run.</summary>
    private CommitSyncService CreateService() => new(
        _database.CreateContext(), _gitHub, _state, new FixedTimeProvider(Now), NullLogger<CommitSyncService>.Instance);

    [Fact]
    public async Task SyncAsync_NewCommits_AddsPostsOldestFirstAfterTheHighestPostNumber()
    {
        // Arrange: the seed data goes up to "Blog Post 20".
        var older = FakeGitHubCommitClient.Commit("Eerste.", Now.AddHours(-2));
        var newer = FakeGitHubCommitClient.Commit("Tweede.", Now.AddHours(-1));
        _gitHub.Push(newer, older);

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(new CommitSyncResult(CommitSyncStatus.Synced, Added: 2), result);
        await using var db = _database.CreateContext();
        Assert.Equal("Blog Post 21", (await db.BlogPosts.SingleAsync(b => b.CommitSha == older.Sha)).Title);
        Assert.Equal("Blog Post 22", (await db.BlogPosts.SingleAsync(b => b.CommitSha == newer.Sha)).Title);
    }

    [Fact]
    public async Task SyncAsync_CommitMessage_BecomesTheTrimmedContent()
    {
        // Arrange
        var commit = FakeGitHubCommitClient.Commit("Titel van de commit.\n\nMeer uitleg.\n", Now);
        _gitHub.Push(commit);

        // Act
        await CreateService().SyncAsync();

        // Assert
        var post = await _database.CreateContext().BlogPosts.SingleAsync(b => b.CommitSha == commit.Sha);
        Assert.Equal("Titel van de commit.\n\nMeer uitleg.", post.Content);
    }

    [Theory]
    [InlineData("2026-11-07T13:11:50Z", "2026-11-07T14:11:50")] // Winter time: UTC+1
    [InlineData("2026-07-01T10:00:00Z", "2026-07-01T12:00:00")] // Summer time: UTC+2
    public async Task SyncAsync_AuthorDate_IsStoredInDutchTime(string authoredUtc, string expectedLocal)
    {
        // Arrange
        var commit = FakeGitHubCommitClient.Commit("Tijd.", DateTimeOffset.Parse(authoredUtc));
        _gitHub.Push(commit);

        // Act
        await CreateService().SyncAsync();

        // Assert
        var post = await _database.CreateContext().BlogPosts.SingleAsync(b => b.CommitSha == commit.Sha);
        Assert.Equal(DateTime.Parse(expectedLocal), post.PublishedDate);
    }

    [Fact]
    public async Task SyncAsync_MergeCommit_IsSkipped()
    {
        // Arrange
        var merge = FakeGitHubCommitClient.Commit("Merge pull request #2", Now, isMerge: true);
        _gitHub.Push(merge);

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(0, result.Added);
        Assert.False(await _database.CreateContext().BlogPosts.AnyAsync(b => b.CommitSha == merge.Sha));
    }

    [Fact]
    public async Task SyncAsync_CommitThatAlreadyHasAPost_IsOnlyMarkedAsHandled()
    {
        // Arrange: the commit of seed post 13.
        const string seededSha = "f7353fb131697e83aec75eb39167cd3490fb436d";
        _gitHub.Push(new PortfolioApi.GitHub.GitHubCommit(seededSha, "Portfolio API ...", Now, IsMerge: false));

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(0, result.Added);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.BlogPosts.CountAsync(b => b.CommitSha == seededSha));
        Assert.True(await db.SyncedCommits.AnyAsync(c => c.Sha == seededSha));
    }

    [Fact]
    public async Task SyncAsync_CommitPostDeletedByAnEditor_IsNotAddedAgain()
    {
        // Arrange: synced once, then the post is deleted, then GitHub sends the full list again.
        var commit = FakeGitHubCommitClient.Commit("Weg ermee.", Now);
        _gitHub.Push(commit);
        await CreateService().SyncAsync();
        await using (var db = _database.CreateContext())
        {
            db.BlogPosts.Remove(await db.BlogPosts.SingleAsync(b => b.CommitSha == commit.Sha));
            await db.SaveChangesAsync();
        }
        _state.ETag = null;

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(new CommitSyncResult(CommitSyncStatus.Synced, Added: 0), result);
        Assert.False(await _database.CreateContext().BlogPosts.AnyAsync(b => b.CommitSha == commit.Sha));
    }

    [Fact]
    public async Task SyncAsync_RemembersWhenEachCommitWasHandled()
    {
        // Arrange
        var commit = FakeGitHubCommitClient.Commit("Klok.", Now.AddDays(-1));
        _gitHub.Push(commit);

        // Act
        await CreateService().SyncAsync();

        // Assert
        var handled = await _database.CreateContext().SyncedCommits.SingleAsync(c => c.Sha == commit.Sha);
        Assert.Equal(Now.UtcDateTime, handled.SyncedAt);
    }

    [Fact]
    public async Task SyncAsync_SecondTimeWithoutChanges_SendsTheETagAndReturnsNotModified()
    {
        // Arrange
        _gitHub.Push(FakeGitHubCommitClient.Commit("Eén keer.", Now));
        await CreateService().SyncAsync();

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(CommitSyncStatus.NotModified, result.Status);
        Assert.Equal(_gitHub.CurrentETag, _gitHub.LastETagReceived);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]   // No connection, 403 rate limit, 5xx
    [InlineData(typeof(TaskCanceledException))]  // Time-out
    public async Task SyncAsync_GitHubUnreachable_ReturnsGitHubUnavailableAndChangesNothing(Type exceptionType)
    {
        // Arrange
        _gitHub.Push(FakeGitHubCommitClient.Commit("Komt niet binnen.", Now));
        _gitHub.FailWith = (Exception)Activator.CreateInstance(exceptionType)!;
        var countBefore = await _database.CreateContext().BlogPosts.CountAsync();

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(CommitSyncStatus.GitHubUnavailable, result.Status);
        Assert.Equal(countBefore, await _database.CreateContext().BlogPosts.CountAsync());
        Assert.Null(_state.ETag);
    }

    [Fact]
    public async Task SyncAsync_AfterGitHubWasUnreachable_CatchesUpNextTime()
    {
        // Arrange
        var commit = FakeGitHubCommitClient.Commit("Later alsnog.", Now);
        _gitHub.Push(commit);
        _gitHub.FailWith = new HttpRequestException();
        await CreateService().SyncAsync();
        _gitHub.FailWith = null;

        // Act
        var result = await CreateService().SyncAsync();

        // Assert
        Assert.Equal(new CommitSyncResult(CommitSyncStatus.Synced, Added: 1), result);
    }

    [Fact]
    public async Task SyncAsync_SavingFails_DoesNotRememberTheETag()
    {
        // Arrange: a database that refuses to save.
        _gitHub.Push(FakeGitHubCommitClient.Commit("Gaat mis bij opslaan.", Now));
        var service = new CommitSyncService(new FailingSaveDbContext(_database), _gitHub, _state,
            new FixedTimeProvider(Now), NullLogger<CommitSyncService>.Instance);

        // Act
        var sync = () => service.SyncAsync();

        // Assert: if the ETag were remembered, GitHub would answer "304 Not Modified" next time and
        // this commit would never become a post.
        await Assert.ThrowsAsync<DbUpdateException>(sync);
        Assert.Null(_state.ETag);
    }

    /// <summary>Hand-made fake: a DbContext on the test database whose SaveChanges always fails.</summary>
    private sealed class FailingSaveDbContext(SqliteTestDatabase database) : PortfolioApi.Data.PortfolioDbContext(database.Options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Saving failed (fake).");
    }
}
