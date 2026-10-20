using Microsoft.AspNetCore.Identity;

namespace BestBet.Data.Entities;

/// <summary>
/// A person allowed into the app. Passwords, lockout and the security stamp are ASP.NET Identity's;
/// this only adds what the app itself needs to know.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>
    /// Set when an admin created the account or reset its password. The temporary password works
    /// exactly once in the sense that matters: nothing else is reachable until it has been replaced
    /// by one the admin never saw.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
