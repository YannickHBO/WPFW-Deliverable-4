using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Models;

namespace PortfolioApi.Controllers;

/// <summary>
/// Account routes, built on ASP.NET Core Identity: UserManager creates accounts and assigns roles,
/// SignInManager checks the password and writes the authentication cookie.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(UserManager<User> userManager, SignInManager<User> signInManager) : ControllerBase
{
    /// <summary>Creates an account with the role Gast. Never assigns Editor or Admin. Does not sign in.</summary>
    [HttpPost("register")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CurrentUserDto>> Register(RegisterDto dto)
    {
        var user = new User { UserName = dto.Email, Email = dto.Email };

        // Hashes the password and checks Identity's rules (password strength, unique e-mail).
        var created = await userManager.CreateAsync(user, dto.Password);
        if (!created.Succeeded) return IdentityErrors(created);

        var roleAdded = await userManager.AddToRoleAsync(user, RoleNames.Guest);
        if (!roleAdded.Succeeded)
        {
            // No account without a role: undo the registration instead of leaving it half done.
            await userManager.DeleteAsync(user);
            return IdentityErrors(roleAdded);
        }

        return StatusCode(StatusCodes.Status201Created, new CurrentUserDto(user.Id, user.Email, [RoleNames.Guest]));
    }

    /// <summary>Checks the password and, on success, returns the authentication cookie (Set-Cookie).</summary>
    [HttpPost("login")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginDto dto)
    {
        // isPersistent: false = a session cookie, gone when the browser closes.
        // lockoutOnFailure: true = after 5 wrong passwords the account is locked for 5 minutes.
        var result = await signInManager.PasswordSignInAsync(dto.Email, dto.Password, isPersistent: false, lockoutOnFailure: true);

        // One message for an unknown e-mail, a wrong password and a locked account,
        // so the response doesn't reveal which e-mail addresses have an account.
        if (!result.Succeeded)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password.");

        var user = await userManager.FindByEmailAsync(dto.Email);
        var roles = await userManager.GetRolesAsync(user!);
        return Ok(new CurrentUserDto(user!.Id, user.Email!, roles.ToList()));
    }

    /// <summary>Removes the authentication cookie. Always 204, also when nobody was signed in.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>Who is signed in, read from the cookie. The SPA calls this on start-up.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserDto> Me() =>
        Ok(new CurrentUserDto(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            User.FindFirstValue(ClaimTypes.Email) ?? User.Identity!.Name!,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList()));

    /// <summary>Turns Identity's errors into the same 400 ValidationProblem format as the DTO validation.</summary>
    private ActionResult IdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }
}
