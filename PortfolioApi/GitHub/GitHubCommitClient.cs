using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace PortfolioApi.GitHub;

/// <summary>
/// GET https://api.github.com/repos/{owner}/{repo}/commits?sha=main&amp;per_page=100.
/// The HttpClient (base address, User-Agent, timeout) is configured in Program.cs.
/// </summary>
public class GitHubCommitClient(HttpClient http, IOptions<GitHubOptions> options) : IGitHubCommitClient
{
    public async Task<GitHubCommitsResponse> GetCommitsAsync(string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"repos/{GitHubRepository.Owner}/{GitHubRepository.Name}/commits?sha={GitHubRepository.Branch}&per_page=100");

        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var token = options.Value.Token;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new GitHubCommitsResponse(NotModified: true, [], etag);

        // 403/429 (rate limit), 404, 5xx: an HttpRequestException, handled by the sync.
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<CommitItem>>(cancellationToken) ?? [];
        var commits = items
            .Select(item => new GitHubCommit(
                item.Sha,
                item.Commit.Message,
                item.Commit.Author.Date,
                IsMerge: item.Parents.Count > 1))
            .ToList();

        var newETag = response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;
        return new GitHubCommitsResponse(NotModified: false, commits, newETag);
    }

    // Only the parts of GitHub's JSON that the sync uses. Property names match GitHub's (case-insensitive).
    private record CommitItem(string Sha, CommitDetail Commit, List<ParentRef> Parents);
    private record CommitDetail(string Message, CommitAuthor Author);
    private record CommitAuthor(DateTimeOffset Date);
    private record ParentRef(string Sha);
}
