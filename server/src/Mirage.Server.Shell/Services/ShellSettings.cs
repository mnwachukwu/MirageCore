using Mirage.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Server.Shell.Services;

/// <summary>Which server this window drives.</summary>
public enum ConnectionMode
{
    /// <summary>The server shipped beside this shell, run as a child process.</summary>
    Local,
    /// <summary>A server on another machine, reached over its management port.</summary>
    Remote,
}

/// <summary>
/// This window's own preferences — where it points and how it got there.
///
/// <para>Deliberately NOT in serverconfig.json. That file describes a server; this describes an operator's
/// client, and one shell can point at several servers over its life while each of those servers has one
/// config. It lives in the per-user config directory for the same reason.</para>
/// </summary>
public sealed record ShellSettings
{
    /// <summary>The management port a shell reaches for when nothing has been configured. One above the
    /// game port, so the two are adjacent and neither is a number anyone has to remember.</summary>
    public const int DefaultManagementPort = Constants.GamePort + 1;

    public ConnectionMode Mode { get; init; } = ConnectionMode.Local;
    public string RemoteHost { get; init; } = "";
    public int RemotePort { get; init; }

    /// <summary>The token for the remote server, kept so an operator is not retyping a secret every time
    /// they attach. Stored as written, in the per-user config directory.</summary>
    public string RemoteToken { get; init; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Per-user, not beside the executable: an install can be shared, and one operator's remote
    /// target is not another's.</summary>
    public static string DefaultPath =>
        new UserPaths(Constants.GameName + " Server Shell").Config("shell.json");

    /// <summary>Reads the settings, falling back to a fresh set. A shell that cannot read its own
    /// preferences still has to open.</summary>
    public static ShellSettings Load(string path)
    {
        if (!File.Exists(path)) return new ShellSettings();
        try
        {
            return JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(path), Options) ?? new ShellSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ShellSettings();
        }
    }

    /// <summary>Writes the settings. Failure is silent: nothing an operator is doing should stop because
    /// a preference did not persist.</summary>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
}
