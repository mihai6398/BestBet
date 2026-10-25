using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BestBet.Services.Scanner;

/// <summary>How both Polymarket APIs are read: camelCase names, and numbers that may come as strings.</summary>
public static class PolymarketJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientDouble(), new LenientNullableDouble() },
    };

    /// <summary>
    /// A number, a number in a string, or nothing. The CLOB sends <c>"last_trade_price": ""</c> for a
    /// token that has never traded, and one empty string must not cost the other books in the batch.
    /// </summary>
    private static double? Read(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                var text = reader.GetString();
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : null;
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    private sealed class LenientNullableDouble : JsonConverter<double?>
    {
        public override bool HandleNull => true;

        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => PolymarketJson.Read(ref reader);

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value is { } number)
            {
                writer.WriteNumberValue(number);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed class LenientDouble : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => PolymarketJson.Read(ref reader) ?? 0.0;

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
