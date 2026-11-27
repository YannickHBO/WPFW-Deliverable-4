using Microsoft.AspNetCore.Identity;

namespace PortfolioApi.Models;

/// <summary>
/// A user account, stored by ASP.NET Core Identity in the AspNetUsers table.
/// IdentityUser already has everything this API needs: Id (a string GUID), UserName, Email,
/// PasswordHash (never the password itself) and SecurityStamp (changes on a role change, which
/// signs the user out). This class has no extra fields yet; it exists so the account can be
/// extended later without changing every UserManager&lt;IdentityUser&gt; in the code.
/// </summary>
public class User : IdentityUser
{
}
