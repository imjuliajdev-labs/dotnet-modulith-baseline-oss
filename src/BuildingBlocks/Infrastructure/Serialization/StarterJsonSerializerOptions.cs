using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace BuildingBlocks.Infrastructure.Serialization;

public static class StarterJsonSerializerOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Converters.OfType<InstantJsonConverter>().Any())
        {
            return;
        }

        options.Converters.Add(new InstantJsonConverter());
    }

    private sealed class InstantJsonConverter : JsonConverter<Instant>
    {
        private static readonly InstantPattern Pattern = InstantPattern.ExtendedIso;

        public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a JSON string for {nameof(Instant)} values.");
            }

            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"Expected a non-empty JSON string for {nameof(Instant)} values.");
            }

            var parseResult = Pattern.Parse(value);
            if (!parseResult.Success)
            {
                throw new JsonException($"Could not parse '{value}' as an ISO-8601 instant.");
            }

            return parseResult.Value;
        }

        public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(Pattern.Format(value));
        }
    }
}
