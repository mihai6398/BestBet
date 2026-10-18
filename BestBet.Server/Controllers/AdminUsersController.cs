using BestBet.Contracts;
using BestBet.Data.Entities;
using BestBet.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Server.Controllers;

/// <summary>
/// The admin's door for everyone else: there is no public sign-up, so an account exists because an
/// admin made it here.
/// </summary>
/// <remarks>
/// New accounts and reset passwords are temporary — the user has to choose their own before anything
/// else answers them, so the admin never knows a password that is actually in use. Disabling and
/// resetting both rotate the security stamp, which ends that user's open sessions within a minute.
/// </remarks>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AuthPolicies.Admin)]
public class AdminUsersController(UserManager<AppUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> List(CancellationToken cancellationToken)
    {
        var all = await users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var admins = (await users.GetUsersInRoleAsync(AuthPolicies.AdminRole)).Select(u => u.Id).ToHashSet();

        return Ok(all.Select(u => Summarise(u, admins.Contains(u.Id))).ToList());
    }

    [HttpPost]
    [ProducesResponseType<UserSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            MustChangePassword = true,
            CreatedUtc = DateTime.UtcNow,
        };

        var result = await users.CreateAsync(user, request.TemporaryPassword ?? string.Empty);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, Summarise(user, isAdmin: false))
            : Invalid(AuthController.Describe(result));
    }

    /// <summary>Turns an account off until it is turned back on. Its open sessions end within a minute.</summary>
    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        if (IsSelf(id))
        {
            return Invalid("You cannot disable your own account.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return NoContent();
    }

    /// <summary>
    /// Gives the user a new temporary password, for when they have lost theirs. They have to replace it
    /// at their next sign-in, and any session they still have open ends within a minute.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        if (IsSelf(id))
        {
            return Invalid("Change your own password from your account page instead.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        // Checked before anything changes, so a password the policy refuses never leaves the account
        // without one.
        var password = request.TemporaryPassword ?? string.Empty;
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, password);
            if (!check.Succeeded)
            {
                return Invalid(AuthController.Describe(check));
            }
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        user.MustChangePassword = true;
        await users.ResetAccessFailedCountAsync(user);
        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    private bool IsSelf(Guid id) => users.GetUserId(User) == id.ToString();

    private static UserSummary Summarise(AppUser user, bool isAdmin)
    {
        var lockedUntil = user.LockoutEnd;
        var disabled = lockedUntil is { } until && until >= DateTimeOffset.MaxValue.AddYears(-1);

        return new UserSummary(
            user.Id,
            user.Email ?? string.Empty,
            isAdmin,
            disabled,
            !disabled && lockedUntil is { } end && end > DateTimeOffset.UtcNow,
            user.MustChangePassword,
            user.CreatedUtc);
    }

    private BadRequestObjectResult Invalid(string detail) => BadRequest(new ProblemDetails
    {
        Title = "The inputs do not make sense",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest,
    });
}
