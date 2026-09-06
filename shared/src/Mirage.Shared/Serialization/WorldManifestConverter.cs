using Mirage.Shared.Records;
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

        writer.WriteEndObject();
    }
}
