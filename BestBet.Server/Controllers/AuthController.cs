using BestBet.Contracts;
using BestBet.Data.Entities;
using BestBet.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BestBet.Server.Controllers;

/// <summary>
/// Signing in and out. The session is an HttpOnly, Secure, SameSite=Strict cookie that script on the
/// page cannot read, so there is no token anywhere for an injected script to steal.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// A real hash of a password nobody knows, checked when the email matches no account. Hashing is
    /// the slow part of a login, so skipping it for unknown emails would let the response time say
    /// which addresses have accounts.
    /// </summary>
    private static readonly Lazy<string> DecoyHash =
        new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString("N")));

    /// <summary>
    /// Signs in with email and password.
    /// </summary>
    /// <remarks>
    /// Every failure gets the same 401 and the same words — wrong email, wrong password, locked out,
    /// disabled — so the endpoint never confirms that an address has an account. Five wrong passwords
    /// lock the account for fifteen minutes, and the rate limiter caps attempts per address on top,
    /// so guessing across many accounts is slowed as well as guessing one.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var user = email.Length == 0 ? null : await users.FindByEmailAsync(email);
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DecoyHash.Value, password);
            return Failed();
        }

        var result = await signIn.PasswordSignInAsync(user, password, request.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return Ok(await MeFor(user));
        }

        if (result.IsLockedOut)
        {
            // A locked account is refused before its password is checked; doing the work anyway keeps
            // the timing from saying it is locked.
            users.PasswordHasher.VerifyHashedPassword(user, DecoyHash.Value, password);
            logger.LogWarning("Sign-in refused for a locked or disabled account {UserId}", user.Id);
        }
        else
        {
            logger.LogInformation("Wrong password for account {UserId}", user.Id);
        }

        return Failed();
    }

    /// <summary>Ends the session. Anonymous on purpose, so an expired session can still be cleared.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>Who is signed in. 401 when nobody is.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.SignedIn)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(await MeFor(user));
    }

    /// <summary>Replaces the password. The only thing a user on a temporary password is allowed to do.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = AuthPolicies.SignedIn)]
    [EnableRateLimiting(RateLimits.Login)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            return Invalid("The new password has to be different from the current one.");
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);
        if (!result.Succeeded)
        {
            return Invalid(Describe(result));
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);

        // The password change rotated the security stamp; re-issuing the cookie keeps this session
        // alive with the new stamp and without the temporary-password flag, while every other
        // session of this user is signed out at its next check.
        await signIn.RefreshSignInAsync(user);
        return Ok(await MeFor(user));
    }

    /// <summary>
    /// Open sign-up. Off unless <c>Auth:AllowRegistration</c> is true, and then it answers as if it did
    /// not exist — the app is private, and the admin creates the accounts.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!configuration.GetValue<bool>("Auth:AllowRegistration"))
        {
            return NotFound();
        }

        var email = request.Email?.Trim() ?? string.Empty;
        var user = new AppUser { UserName = email, Email = email, CreatedUtc = DateTime.UtcNow };
        var result = await users.CreateAsync(user, request.Password ?? string.Empty);
        if (!result.Succeeded)
        {
            return Invalid(Describe(result));
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return StatusCode(StatusCodes.Status201Created, await MeFor(user));
    }

    private async Task<MeResponse> MeFor(AppUser user)
        => new(user.Email ?? string.Empty, await users.IsInRoleAsync(user, AuthPolicies.AdminRole), user.MustChangePassword);

    private UnauthorizedObjectResult Failed() => Unauthorized(new ProblemDetails
    {
        Title = "Sign-in failed",
        Detail = "Wrong email or password, or the account is locked for a while after too many attempts.",
        Status = StatusCodes.Status401Unauthorized,
    });

    private BadRequestObjectResult Invalid(string detail) => BadRequest(new ProblemDetails
    {
        Title = "The inputs do not make sense",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest,
    });

    internal static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
