using Mirage.Shared.Records;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Serialization;

/// <summary>
/// Writes a world's manifest as only what it says that the stock answers do not.
///
/// <para>Every setting in the file has a default, and a folder with no file at all runs on all of them —
/// so a key repeating its default states nothing. A world that only has a name is a file with only a name
/// in it, and what an operator has actually chosen is the whole content rather than three lines buried in
/// forty.</para>

///
/// <para>Reading is the mirror: an absent key is the default, which is the same answer an absent FILE
/// gives.</para>
/// </summary>
public sealed class WorldManifestConverter : JsonConverter<WorldManifest>
{
    public override WorldManifest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var result = new WorldManifest();

        foreach (var p in root.EnumerateObject())
        {
            if (p.NameEquals("name"))
            {
                result = result with { Name = p.Value.GetString() ?? "" };
            }
            else if (p.NameEquals("records"))
            {
                var limits = p.Value.Deserialize<RecordLimits>(options);
                if (limits is not null) result = result with { Records = limits };
            }
            else if (p.NameEquals("defaultMapSize"))
            {
                result = result with { DefaultMapSize = p.Value.Deserialize<MapSize>(options) };
            }
            else if (p.NameEquals("startingItems"))
            {
                var authored = p.Value.Deserialize<List<StartingItem>>(options);
                if (authored is not null) result = result with { StartingItems = authored };
            }
            else if (p.NameEquals("decalColor"))
            {
                if (ReadColor(p.Value) is { } rgb) result = result with { DecalColor = rgb };
            }
            else if (p.NameEquals("appearances"))
            {
                var offered = p.Value.Deserialize<List<CharacterAppearance>>(options);
                // An empty list is the same statement as an absent key, and the init accessor answers both
                // with the stock set — so a world cannot accidentally offer nothing at character creation.
                if (offered is { Count: > 0 }) result = result with { Appearances = offered };
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, WorldManifest value, JsonSerializerOptions options)
    {
        var stock = new WorldManifest();
        writer.WriteStartObject();

        if (value.IsNamed)
        {
            writer.WriteString("name", value.Name);
        }

        if (value.DefaultMapSize != stock.DefaultMapSize)
        {
            writer.WritePropertyName("defaultMapSize");
            JsonSerializer.Serialize(writer, value.DefaultMapSize, options);
        }

        if (value.Records != stock.Records)
        {
            writer.WritePropertyName("records");
            JsonSerializer.Serialize(writer, value.Records, options);
        }

        // Compared by CONTENT, not by reference: the stock set is one entry, and a world that authored a
        // roster identical to it is saying nothing this file has to carry.
        if (!value.Appearances.SequenceEqual(stock.Appearances))
        {
            writer.WritePropertyName("appearances");
            JsonSerializer.Serialize(writer, value.Appearances, options);
        }

        if (value.DecalColor != stock.DecalColor)
        {
            writer.WriteString("decalColor", $"#{value.DecalColor:X6}");
        }

        if (value.StartingItems.Count > 0)
        {
            writer.WritePropertyName("startingItems");
            JsonSerializer.Serialize(writer, value.StartingItems, options);
        }

        writer.WriteEndObject();
    }

    // Written as "#RRGGBB" because this file is hand-edited and 5375496 is not a color anybody recognizes.
    // A bare number still reads, so a file written by some other tool is not rejected over its notation.
    private static uint? ReadColor(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return e.TryGetUInt32(out uint n) ? n & 0xFFFFFFu : null;
        if (e.ValueKind != JsonValueKind.String) return null;

        string t = (e.GetString() ?? "").Trim().TrimStart('#');
        return uint.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                             System.Globalization.CultureInfo.InvariantCulture, out uint v) ? v & 0xFFFFFFu : null;
    }
}
