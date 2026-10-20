using BestBet.Services;

namespace BestBet.Server.Tests;

/// <summary>A fixed signed-in user, for service tests that run without HTTP.</summary>
internal sealed class TestUser(Guid id) : ICurrentUser
{
    public Guid Id { get; } = id;

    public static TestUser Alice { get; } = new(Guid.Parse("a11ce000-0000-0000-0000-000000000001"));

    public static TestUser Bob { get; } = new(Guid.Parse("b0b00000-0000-0000-0000-000000000002"));
}
