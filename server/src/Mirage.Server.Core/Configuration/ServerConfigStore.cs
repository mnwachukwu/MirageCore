using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="ServerConfig"/> as JSON.
///
/// <para>Failures come back as a message rather than an exception: a bad config must not stop a server
/// booting, but it must not pass silently either — the failure mode is a server running rules its
/// operator thinks they changed.</para>
/// </summary>
public static class ServerConfigStore
{
    /// <summary>Mirrors <c>JsonPersistenceService.Options</c>, so a value moved between here and a game
    /// record reads identically.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        // Comments are tolerated on read but do NOT survive a write — the shell serializes the object
        // graph, which has nowhere to keep them.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string FileName = "serverconfig.json";

    /// <summary>The operator's own config. In the per-user state dir, not beside the executable: what an
    /// operator set has to outlive the version they set it in, and an update deletes the install folder —
    /// see <see cref="ServerPaths"/>. Resolved off an absolute root rather than the working directory so it
    /// lands in the same place however the process was launched, including as the shell's child.</summary>
    public static string DefaultPath => ServerPaths.Data(FileName);

    /// <summary>The defaults the package ships. Read once, to seed <see cref="DefaultPath"/> on a machine
    /// that has no config yet; never written.</summary>
    public static string ShippedPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Loads the config at <paramref name="path"/>.
    ///
    /// <para>A missing file is not an error: it means an operator who has never changed anything, and it
    /// yields <see cref="ServerConfig.Default"/>. Malformed content yields the defaults too, but with a
    /// message — the caller is expected to surface it.</para></summary>
    public static (ServerConfig Config, string? Error) Load(string path)
    {
        if (!File.Exists(path)) return (ServerConfig.Default, null);
        try
        {
            var loaded = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Options);
            // Deserializing the literal "null" is well-formed JSON that produces nothing usable.
            return loaded is null
                ? (ServerConfig.Default, $"{path} contained no configuration; using defaults.")
                : (loaded, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (ServerConfig.Default, $"Could not read {path} ({ex.Message}); using defaults.");
        }
    }

    /// <summary>Writes <paramref name="config"/> to <paramref name="path"/>, returning null on success or
    /// a message on failure. Writes through a temporary file so an interrupted save cannot leave a
    /// truncated config behind — the file it would corrupt is the one the next boot reads.</summary>
    public static string? Save(string path, ServerConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Could not write {path} ({ex.Message}).";
        }
    }
}
