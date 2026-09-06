using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    };
}
