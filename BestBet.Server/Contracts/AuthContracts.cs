namespace BestBet.Contracts;

/// <param name="RememberMe">Keep the session across browser restarts. Off by default.</param>
public record LoginRequest(string Email, string Password, bool RememberMe = false);

/// <summary>Who is signed in, as much as the UI needs to know.</summary>
/// <param name="MustChangePassword">Still on a temporary password; nothing but changing it works.</param>
public record MeResponse(string Email, bool IsAdmin, bool MustChangePassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record RegisterRequest(string Email, string Password);

/// <summary>Body of <c>POST /api/admin/users</c>.</summary>
/// <param name="TemporaryPassword">Works once, in the sense that matters: the new user has to replace
/// it before anything else in the app answers them.</param>
public record CreateUserRequest(string Email, string TemporaryPassword);

public record ResetPasswordRequest(string TemporaryPassword);

/// <param name="IsDisabled">Turned off by an admin, until turned back on.</param>
/// <param name="IsLockedOut">Locked for a few minutes after too many wrong passwords.</param>
public record UserSummary(
    Guid Id,
    string Email,
    bool IsAdmin,
    bool IsDisabled,
    bool IsLockedOut,
    bool MustChangePassword,
    DateTime CreatedUtc);
