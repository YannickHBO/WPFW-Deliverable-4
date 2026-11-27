using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.Dtos;

/// <summary>
/// Request body for POST /api/auth/register. The e-mail address is also the user name.
/// Password rules (length, digit, upper/lower case, symbol) are checked by Identity itself;
/// the maximum length only stops absurdly long input from being hashed.
/// </summary>
public record RegisterDto(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);

/// <summary>Request body for POST /api/auth/login.</summary>
public record LoginDto(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);

/// <summary>Who is signed in, for the SPA: shown after register/login and by GET /api/auth/me.</summary>
public record CurrentUserDto(string Id, string Email, IReadOnlyList<string> Roles);
