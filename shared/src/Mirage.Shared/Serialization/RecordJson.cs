using Mirage.Shared.Extensibility;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Mirage.Shared.Serialization;

/// <summary>The one <see cref="JsonSerializerOptions"/> every reader and writer of a game record uses —
/// the server's persistence layer, the editor, the tests, and the out-of-tree content generators.
///
/// <para>Enums are written as NAMES. A reader without <see cref="JsonStringEnumConverter"/> throws on the
/// first record and, where the caller swallows it, surfaces as an empty collection rather than an error.
/// Tile arrays carry their own converter by attribute and need no registration here.</para></summary>
public static class RecordJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,   // the editor writes map JSON in PascalCase; the server writes camelCase
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyAttributeBags } },
    };

    /// <summary>Leaves an empty <see cref="AttributeBag"/> out of the file entirely.
    ///
    /// <para>Every body and most records carry a bag and almost none of them hold anything, so without
    /// this every file in a world gains an <c>"attributes": {}</c> line stating that nothing was
    /// authored. A world folder is read and hand-edited by people, and a key that is always there and
    /// always empty is the kind of noise that teaches a reader to skip a section.</para>
    ///
    /// <para>Done as a type-info modifier rather than an attribute on each property so it holds for every
    /// bag there will ever be, including one on a record a game adds.</para></summary>
    private static void SkipEmptyAttributeBags(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (property.PropertyType != typeof(AttributeBag)) continue;
            property.ShouldSerialize = static (_, value) => value is AttributeBag bag && !bag.IsEmpty;
        }
    }
}
