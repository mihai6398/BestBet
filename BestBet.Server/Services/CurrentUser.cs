using System.Security.Claims;

namespace BestBet.Services;

/// <summary>
/// Who is asking. Every read and write of user data goes through this, never through anything the
/// request body or query string says.
/// </summary>
public interface ICurrentUser
{
    Guid Id { get; }
}

/// <summary>The signed-in user of the current request, read from the authentication cookie's claims.</summary>
/// <remarks>
/// Throws rather than returning an empty id when there is no user. The authorization fallback policy
/// stops anonymous requests before they reach anything that asks, so reaching here without a user is
/// a wiring bug — and a query run with an empty id would silently return nothing instead of failing.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("No signed-in user on this request.");
        }
    }
}
