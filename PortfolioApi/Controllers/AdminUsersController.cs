using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Services;

namespace PortfolioApi.Controllers;

/// <summary>
/// User management. Every route here is for Admins only: the policy is on the controller, so a route
/// added later is protected too. Without sign-in: 401; signed in without the role Admin: 403.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Produces("application/json")]
[Authorize(Policy = Policies.IsAdmin)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminUsersController(IUserAdminService userAdminService) : ControllerBase
{
    /// <summary>All accounts with their roles, to find the one to promote.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll() =>
        Ok(await userAdminService.GetAllAsync());

    /// <summary>Makes a Gast an Editor. The user has to sign in again before the new role works.</summary>
    [HttpPost("{id}/editor")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> GrantEditor(string id)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await userAdminService.GrantEditorAsync(id, adminId);

        return result.Status switch
        {
            GrantEditorStatus.Granted => Ok(result.User),
            GrantEditorStatus.UserNotFound => NotFound(),
            _ => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Only a user with just the role Gast can be made Editor.")
        };
    }
}
