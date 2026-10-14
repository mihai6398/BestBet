using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// The store behind the live feed: the last reading pushed in from a page the user has open.
/// </summary>
public class LiveFeedTests
{
    [Fact]
    public void Nothing_has_been_read_until_something_is_pushed()
        => Assert.Null(new LiveFeedStore().Latest);

    [Fact]
    public void A_reading_comes_back_with_where_it_came_from()
    {
        var store = new LiveFeedStore();

        store.Publish("Peste 2.5 1.95", "https://www.sofascore.com/match");

        Assert.Equal("Peste 2.5 1.95", store.Latest!.Text);
        Assert.Equal("https://www.sofascore.com/match", store.Latest.Source);
    }

    [Fact]
    public void Only_the_newest_reading_is_kept()
    {
        // A match page is a snapshot, not a history: the one from three minutes ago is not evidence
        // of anything and keeping it would only create a way to price against the wrong minute.
        var store = new LiveFeedStore();

        store.Publish("minute 40", null);
        store.Publish("minute 55", null);

        Assert.Equal("minute 55", store.Latest!.Text);
    }

    [Fact]
    public void An_oversized_push_is_cut_rather_than_kept_whole()
    {
        // The endpoint accepts a cross-origin POST, so the size of what it will hold has to be the
        // store's decision and not the caller's.
        var store = new LiveFeedStore();

        store.Publish(new string('x', LiveFeedStore.MaxTextLength * 2), null);

        Assert.Equal(LiveFeedStore.MaxTextLength, store.Latest!.Text.Length);
    }

    [Fact]
    public void Clearing_stops_the_watch()
    {
        var store = new LiveFeedStore();
        store.Publish("minute 55", null);

        store.Clear();

        Assert.Null(store.Latest);
    }

    [Fact]
    public void Every_run_gets_its_own_token()
    {
        // The token is the real guard on the push endpoint: a browser sends a simple cross-origin
        // POST whatever the CORS policy says, so a page that was not handed the token is the only
        // thing standing between this and any site the user happens to open.
        var first = new LiveFeedStore().Token;
        var second = new LiveFeedStore().Token;

        Assert.NotEqual(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void A_reading_is_stamped_when_it_arrives()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var store = new LiveFeedStore();

        store.Publish("minute 55", null);

        Assert.InRange(store.Latest!.ReceivedUtc, before, DateTime.UtcNow.AddSeconds(1));
    }
}
