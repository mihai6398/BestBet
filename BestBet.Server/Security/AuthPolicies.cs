using System.Security.Claims;
using BestBet.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BestBet.Security;

/// <summary>The names the authorization rules are known by, in one place so nothing drifts.</summary>
public static class AuthPolicies
{
    /// <summary>
    /// Signed in, nothing more. Only for the few endpoints a user with a temporary password still
    /// needs: who am I, change my password, sign me out.
    /// </summary>
    public const string SignedIn = "SignedIn";

    /// <summary>
    /// Signed in with a password of their own. This is the fallback policy, so every endpoint that
    /// does not say otherwise requires it — including any endpoint added later.
    /// </summary>
    public const string Active = "Active";

    /// <summary>An active user in the admin role.</summary>
    public const string Admin = "Admin";

    public const string AdminRole = "Admin";

    /// <summary>Present, with the value "true", while the user is still on a temporary password.</summary>
    public const string MustChangePasswordClaim = "bestbet:must_change_password";

    public static bool MustChangePassword(ClaimsPrincipal principal)
        => principal.HasClaim(MustChangePasswordClaim, "true");
}

/// <summary>
/// Puts the temporary-password flag into the login cookie, so the server can hold a user to it on
/// every request without a database read.
/// </summary>
/// <remarks>
/// Changing the password refreshes the sign-in, which rebuilds the claims through here and drops the
/// flag. An admin resetting a password updates the security stamp, which signs the user out within
/// the validation interval and makes them come back through here with the flag set.
/// </remarks>
public class AppUserClaimsFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(AuthPolicies.MustChangePasswordClaim, "true"));
        }

        return identity;
    }
}
