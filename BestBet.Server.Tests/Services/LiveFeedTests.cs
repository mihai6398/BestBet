using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// The store behind the live feed: the last reading pushed in from a page each user has open.
/// </summary>
public class LiveFeedTests
{
    private static readonly Guid Alice = TestUser.Alice.Id;
    private static readonly Guid Bob = TestUser.Bob.Id;

    [Fact]
    public void Nothing_has_been_read_until_something_is_pushed()
        => Assert.Null(new LiveFeedStore().Latest(Alice));

    [Fact]
    public void A_reading_comes_back_with_where_it_came_from()
    {
        var store = new LiveFeedStore();

        store.Publish(store.TokenFor(Alice), "Peste 2.5 1.95", "https://www.sofascore.com/match");

        Assert.Equal("Peste 2.5 1.95", store.Latest(Alice)!.Text);
        Assert.Equal("https://www.sofascore.com/match", store.Latest(Alice)!.Source);
    }

    [Fact]
    public void Only_the_newest_reading_is_kept()
    {
        // A match page is a snapshot, not a history: the one from three minutes ago is not evidence
        // of anything and keeping it would only create a way to price against the wrong minute.
        var store = new LiveFeedStore();
        var token = store.TokenFor(Alice);

        store.Publish(token, "minute 40", null);
        store.Publish(token, "minute 55", null);

        Assert.Equal("minute 55", store.Latest(Alice)!.Text);
    }

    [Fact]
    public void An_oversized_push_is_cut_rather_than_kept_whole()
    {
        // The endpoint accepts a cross-origin POST, so the size of what it will hold has to be the
        // store's decision and not the caller's.
        var store = new LiveFeedStore();

        store.Publish(store.TokenFor(Alice), new string('x', LiveFeedStore.MaxTextLength * 2), null);

        Assert.Equal(LiveFeedStore.MaxTextLength, store.Latest(Alice)!.Text.Length);
    }

    [Fact]
    public void Clearing_stops_the_watch()
    {
        var store = new LiveFeedStore();
        store.Publish(store.TokenFor(Alice), "minute 55", null);

        store.Clear(Alice);

        Assert.Null(store.Latest(Alice));
    }

    [Fact]
    public void Every_user_gets_their_own_token_and_keeps_it()
    {
        // The token is the real guard on the push endpoint: a browser sends a simple cross-origin
        // POST whatever the CORS policy says, so a page that was not handed the token is the only
        // thing standing between this and any site the user happens to open.
        var store = new LiveFeedStore();

        var alice = store.TokenFor(Alice);
        var bob = store.TokenFor(Bob);

        Assert.NotEqual(alice, bob);
        Assert.Equal(32, alice.Length);
        Assert.Equal(alice, store.TokenFor(Alice));
    }

    [Fact]
    public void A_reading_goes_only_to_the_user_whose_token_sent_it()
    {
        var store = new LiveFeedStore();
        store.TokenFor(Bob);

        store.Publish(store.TokenFor(Alice), "minute 55", null);

        Assert.NotNull(store.Latest(Alice));
        Assert.Null(store.Latest(Bob));
    }

    [Fact]
    public void A_token_nobody_was_given_stores_nothing()
    {
        var store = new LiveFeedStore();
        store.TokenFor(Alice);

        var accepted = store.Publish(Guid.NewGuid().ToString("N"), "minute 55", null);

        Assert.False(accepted);
        Assert.Null(store.Latest(Alice));
    }

    [Fact]
    public void A_reading_is_stamped_when_it_arrives()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var store = new LiveFeedStore();

        store.Publish(store.TokenFor(Alice), "minute 55", null);

        Assert.InRange(store.Latest(Alice)!.ReceivedUtc, before, DateTime.UtcNow.AddSeconds(1));
    }
}
