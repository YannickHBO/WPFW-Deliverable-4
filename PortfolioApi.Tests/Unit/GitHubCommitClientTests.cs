using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PortfolioApi.GitHub;

namespace PortfolioApi.Tests.Unit;

/// <summary>
/// The real GitHub client, but with a hand-made fake HTTP handler instead of the network: what it asks
/// GitHub, and how it reads GitHub's answer (JSON, ETag, 304, errors).
/// </summary>
public class GitHubCommitClientTests
{
    // Shortened copy of a real GitHub answer: two commits, newest first; the newest is a merge.
    private const string GitHubJson = """
        [
          {
            "sha": "0a0f2c2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "commit": { "message": "Merge pull request #2", "author": { "name": "x", "date": "2026-11-27T13:20:00Z" } },
            "parents": [ { "sha": "d286429" }, { "sha": "89d1cc9" } ]
          },
          {
            "sha": "89d1cc9aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "commit": { "message": "Commits op main worden blogposts.", "author": { "name": "x", "date": "2026-11-27T13:10:00Z" } },
            "parents": [ { "sha": "ff38939" } ]
          }
        ]
        """;

    private static (GitHubCommitClient Client, FakeHandler Handler) CreateClient(
        HttpResponseMessage response, string? token = null)
    {
        var handler = new FakeHandler(response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        return (new GitHubCommitClient(http, Options.Create(new GitHubOptions { Token = token })), handler);
    }

    private static HttpResponseMessage Ok(string json, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    [Fact]
    public async Task GetCommitsAsync_AsksForTheNewest100CommitsOnMain()
    {
        // Arrange
        var (client, handler) = CreateClient(Ok("[]"));

        // Act
        await client.GetCommitsAsync(etag: null, CancellationToken.None);

        // Assert
        Assert.Equal("https://api.github.com/repos/YannickHBO/WPFW-Deliverable-4/commits?sha=main&per_page=100",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetCommitsAsync_GitHubAnswer_IsReadIntoCommits()
    {
        // Arrange
        var (client, _) = CreateClient(Ok(GitHubJson, etag: "\"abc\""));

        // Act
        var response = await client.GetCommitsAsync(etag: null, CancellationToken.None);

        // Assert
        Assert.False(response.NotModified);
        Assert.Equal("\"abc\"", response.ETag);
        Assert.Equal(2, response.Commits.Count);
        var merge = response.Commits[0];
        var commit = response.Commits[1];
        Assert.True(merge.IsMerge);
        Assert.False(commit.IsMerge);
        Assert.Equal("89d1cc9aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", commit.Sha);
        Assert.Equal("Commits op main worden blogposts.", commit.Message);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 13, 10, 0, TimeSpan.Zero), commit.AuthoredAt);
    }

    [Fact]
    public async Task GetCommitsAsync_WithETagAndNothingChanged_SendsIfNoneMatchAndReturnsNotModified()
    {
        // Arrange
        var (client, handler) = CreateClient(new HttpResponseMessage(HttpStatusCode.NotModified));

        // Act
        var response = await client.GetCommitsAsync("\"abc\"", CancellationToken.None);

        // Assert
        Assert.Equal("\"abc\"", handler.LastRequest!.Headers.GetValues("If-None-Match").Single());
        Assert.True(response.NotModified);
        Assert.Empty(response.Commits);
        Assert.Equal("\"abc\"", response.ETag);
    }

    [Fact]
    public async Task GetCommitsAsync_WithoutToken_SendsNoAuthorizationHeader()
    {
        // Arrange
        var (client, handler) = CreateClient(Ok("[]"));

        // Act
        await client.GetCommitsAsync(etag: null, CancellationToken.None);

        // Assert
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task GetCommitsAsync_WithToken_SendsItAsBearerToken()
    {
        // Arrange
        var (client, handler) = CreateClient(Ok("[]"), token: "test-token");

        // Act
        await client.GetCommitsAsync(etag: null, CancellationToken.None);

        // Assert
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]           // Rate limit reached
    [InlineData(HttpStatusCode.NotFound)]            // Repository renamed or private
    [InlineData(HttpStatusCode.InternalServerError)] // GitHub itself has a problem
    public async Task GetCommitsAsync_ErrorStatus_ThrowsHttpRequestException(HttpStatusCode status)
    {
        // Arrange
        var (client, _) = CreateClient(new HttpResponseMessage(status));

        // Act
        var call = () => client.GetCommitsAsync(etag: null, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(call);
    }

    /// <summary>Hand-made fake: answers every request with the given response and remembers the request.</summary>
    private sealed class FakeHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
