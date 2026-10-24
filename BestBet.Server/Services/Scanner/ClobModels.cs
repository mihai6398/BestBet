using System.Text.Json.Serialization;

namespace BestBet.Services.Scanner;

// The CLOB's answers. Prices and sizes come as strings ("0.63", "1416.33") and are read as numbers
// with AllowReadingFromString.

/// <summary>One token's order book, as <c>GET /book</c> and <c>POST /books</c> return it.</summary>
/// <remarks>
/// The levels arrive bids ascending and asks descending, so the best price is the LAST element of
/// each list. Nothing here relies on that order: <see cref="OrderBookView"/> sorts them itself.
/// </remarks>
public sealed class ClobBook
{
    /// <summary>The market's condition id.</summary>
    [JsonPropertyName("market")]
    public string? Market { get; set; }

    /// <summary>The token this book is for.</summary>
    [JsonPropertyName("asset_id")]
    public string? AssetId { get; set; }

    /// <summary>Milliseconds since the epoch, as a string.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("bids")]
    public List<ClobLevel>? Bids { get; set; }

    [JsonPropertyName("asks")]
    public List<ClobLevel>? Asks { get; set; }

    [JsonPropertyName("tick_size")]
    public double? TickSize { get; set; }

    [JsonPropertyName("min_order_size")]
    public double? MinOrderSize { get; set; }

    [JsonPropertyName("last_trade_price")]
    public double? LastTradePrice { get; set; }

    public DateTime? TimestampUtc =>
        long.TryParse(Timestamp, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
}

public sealed class ClobLevel
{
    [JsonPropertyName("price")]
    public double Price { get; set; }

    /// <summary>In shares; one share pays $1 if the outcome happens.</summary>
    [JsonPropertyName("size")]
    public double Size { get; set; }
}

/// <summary><c>GET /prices-history</c>: <c>{"history": [{"t": 1792195247, "p": 0.61}]}</c>.</summary>
public sealed class ClobPriceHistory
{
    [JsonPropertyName("history")]
    public List<ClobHistoryPoint>? History { get; set; }
}

public sealed class ClobHistoryPoint
{
    /// <summary>Seconds since the epoch.</summary>
    [JsonPropertyName("t")]
    public long T { get; set; }

    [JsonPropertyName("p")]
    public double P { get; set; }
}
