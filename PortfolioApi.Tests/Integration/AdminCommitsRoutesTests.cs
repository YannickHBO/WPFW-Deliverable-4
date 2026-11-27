using System.Net;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Services;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>
/// POST /api/admin/sync-commits, with a fake GitHub (<see cref="FakeGitHubCommitClient"/>): the real
/// GitHub is never called. Every test pushes its own commits, so the tests don't depend on each other.
/// </summary>
public class AdminCommitsRoutesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 11, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(RoleNames.Guest, HttpStatusCode.Forbidden)]
    [InlineData(RoleNames.Editor, HttpStatusCode.Forbidden)]
    public async Task SyncCommits_NotAdmin_IsRefusedAndGitHubIsNotCalled(string? role, HttpStatusCode expected)
    {
        // Arrange
        var client = await factory.CreateClientAsync(role);
        var callsBefore = factory.GitHub.Calls;

        // Act
        var response = await client.PostAsync("/api/admin/sync-commits", null);

        // Assert
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(callsBefore, factory.GitHub.Calls);
    }

    [Fact]
    public async Task SyncCommits_AsAdmin_AddsTheNewCommitAsBlogPostAndSkipsTheMerge()
    {
        // Arrange
        factory.GitHub.FailWith = null;
        var commit = FakeGitHubCommitClient.Commit("Tests toegevoegd.", Noon);
        var merge = FakeGitHubCommitClient.Commit("Merge pull request #3", Noon.AddMinutes(1), isMerge: true);
        factory.GitHub.Push(commit, merge);
        var client = await factory.CreateClientAsync(RoleNames.Admin);

        // Act
        var response = await client.PostAsync("/api/admin/sync-commits", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadAsync<CommitSyncResult>();
        Assert.Equal(CommitSyncStatus.Synced, result.Status);
        Assert.Equal(1, result.Added);

        var posts = await (await client.GetAsync("/api/blogposts")).ReadAsync<List<BlogPostDto>>();
        var post = Assert.Single(posts, p => p.CommitSha == commit.Sha);
        Assert.Equal("Tests toegevoegd.", post.Content);
        Assert.EndsWith($"/commit/{commit.Sha}", post.CommitUrl);
        Assert.DoesNotContain(posts, p => p.CommitSha == merge.Sha);
    }

    [Fact]
    public async Task SyncCommits_TwiceWithoutNewCommits_SecondTimeReturnsNotModified()
    {
        // Arrange
        factory.GitHub.FailWith = null;
        factory.GitHub.Push(FakeGitHubCommitClient.Commit("Iets gewijzigd.", Noon));
        var client = await factory.CreateClientAsync(RoleNames.Admin);
        await client.PostAsync("/api/admin/sync-commits", null);

        // Act
        var response = await client.PostAsync("/api/admin/sync-commits", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CommitSyncStatus.NotModified, (await response.ReadAsync<CommitSyncResult>()).Status);
        Assert.Equal(factory.GitHub.CurrentETag, factory.GitHub.LastETagReceived);
    }

    [Fact]
    public async Task SyncCommits_GitHubUnreachable_Returns502AndKeepsTheBlogAsItWas()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Admin);
        var countBefore = await factory.WithDbAsync(db => db.BlogPosts.CountAsync());
        factory.GitHub.Push(FakeGitHubCommitClient.Commit("Komt nu niet binnen.", Noon));
        factory.GitHub.FailWith = new HttpRequestException("GitHub is down (fake).");

        try
        {
            // Act
            var response = await client.PostAsync("/api/admin/sync-commits", null);

            // Assert
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(countBefore, await factory.WithDbAsync(db => db.BlogPosts.CountAsync()));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/blogposts")).StatusCode);
        }
        finally
        {
            factory.GitHub.FailWith = null;
        }
    }
}
