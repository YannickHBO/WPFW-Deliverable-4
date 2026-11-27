using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortfolioApi.Auth;
using PortfolioApi.Services;

namespace PortfolioApi.Controllers;

/// <summary>
/// "Sync now" for the commit blog, next to the automatic background sync. Admins only, like all of
/// api/admin: without sign-in 401, without the role Admin 403.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = Policies.IsAdmin)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminCommitsController(ICommitSyncService commitSyncService) : ControllerBase
{
    /// <summary>Fetches the commits from GitHub now and adds the new ones as blog posts.</summary>
    [HttpPost("sync-commits")]
    [ProducesResponseType<CommitSyncResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CommitSyncResult>> SyncCommits()
    {
        var result = await commitSyncService.SyncAsync(HttpContext.RequestAborted);

        // 502 Bad Gateway: the API itself is fine, but the service it depends on (GitHub) is not.
        return result.Status == CommitSyncStatus.GitHubUnavailable
            ? Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub could not be reached. The existing blog posts are unchanged; try again later.")
            : Ok(result);
    }
}
