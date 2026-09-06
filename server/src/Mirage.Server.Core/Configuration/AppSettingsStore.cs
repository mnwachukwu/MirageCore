using Mirage.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Reads and writes the five operator-facing log settings in appsettings.json.
///
/// <para>Edits the parsed node graph and writes it back, rather than serializing a typed model of the
/// file. Modeling it would mean modeling all of Serilog's configuration schema, and the hand-authored
/// three-way Logger split is structure this has no business regenerating.</para>
///
/// <para>appsettings.json configures the APPLICATION. What configures the SERVER — port, language, rules,
/// remote access — is <see cref="ServerConfig"/> in serverconfig.json.</para>
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The relaxed encoder is not optional. The default escapes apostrophes to <c>'</c>, and
    /// Serilog's filter expressions are full of them — a save would still PARSE, while turning a
    /// hand-readable file into something nobody wants to open.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public const string FileName = "appsettings.json";

    /// <summary>The live file, in the per-user state dir for the same reason
    /// <see cref="ServerConfigStore"/> resolves its path that way: it holds settings an operator changed,
    /// and an update deletes the install folder.</summary>
    public static string DefaultPath => ServerPaths.Data(FileName);

    /// <summary>The defaults the package ships, used only to seed <see cref="DefaultPath"/> once.</summary>
    public static string ShippedPath => Path.Combine(AppContext.BaseDirectory, FileName);

    private const string OutgoingLogger = "Mirage.Server.Host.Net.TcpPacketDispatcher";
    private const string IncomingLogger = "Mirage.Server.Host.Net.ReceiveLoop";
    private const string ServerLogPath = "logs/server-.log";
    private const string NetworkLogPath = "logs/network-.log";

    /// <summary>Reads the settings. Anything that does not resolve is left out of
    /// <see cref="LogSettings.Available"/> rather than defaulted, so the caller can show it as unavailable
    /// instead of offering to overwrite a file it did not understand.</summary>
    public static (LogSettings Settings, string? Error) Load(string path)
    {
        if (!File.Exists(path))
            return (new LogSettings { Available = LogKnobs.None }, $"No {Path.GetFileName(path)} at {path}.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ParseOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new LogSettings { Available = LogKnobs.None }, $"Could not read {path} ({ex.Message}).");
        }
        if (root is null) return (new LogSettings { Available = LogKnobs.None }, $"{path} contained no configuration.");

        var settings = new LogSettings { Available = LogKnobs.None };
        var minimum = root["Serilog"]?["MinimumLevel"];

        if (minimum?["Default"]?.GetValue<string>() is { } level)
            settings = settings with { MinimumLevel = level, Available = settings.Available | LogKnobs.MinimumLevel };

        var overrides = minimum?["Override"];
        if (overrides?[OutgoingLogger]?.GetValue<string>() is { } outgoing)
            settings = settings with
            {
                LogOutgoingPackets = IsVerbose(outgoing),
                Available = settings.Available | LogKnobs.OutgoingPackets,
            };
        if (overrides?[IncomingLogger]?.GetValue<string>() is { } incoming)
            settings = settings with
            {
                LogIncomingPackets = IsVerbose(incoming),
                Available = settings.Available | LogKnobs.IncomingPackets,
            };

        if (FindFileSinkArgs(root, ServerLogPath)?["retainedFileCountLimit"]?.GetValue<int>() is { } serverDays)
            settings = settings with { ServerLogRetentionDays = serverDays, Available = settings.Available | LogKnobs.ServerRetention };
        if (FindFileSinkArgs(root, NetworkLogPath)?["retainedFileCountLimit"]?.GetValue<int>() is { } networkDays)
            settings = settings with { NetworkLogRetentionDays = networkDays, Available = settings.Available | LogKnobs.NetworkRetention };

        return (settings, settings.Available == LogKnobs.All
            ? null
            : $"Some settings are not where they are expected in {Path.GetFileName(path)}.");
    }

    /// <summary>Writes only the knobs <paramref name="settings"/> reports as available, leaving every other
    /// byte of structure alone. Returns null on success or a message on failure.</summary>
    public static string? Save(string path, LogSettings settings)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ParseOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"Could not read {path} ({ex.Message}).";
        }
        if (root is null) return $"{path} contained no configuration.";

        var minimum = root["Serilog"]?["MinimumLevel"];
        if (settings.Has(LogKnobs.MinimumLevel) && minimum is not null)
            minimum["Default"] = settings.MinimumLevel;

        var overrides = minimum?["Override"];
        if (settings.Has(LogKnobs.OutgoingPackets) && overrides is not null)
            overrides[OutgoingLogger] = settings.LogOutgoingPackets ? LogSettings.PacketsOn : LogSettings.PacketsOff;
        if (settings.Has(LogKnobs.IncomingPackets) && overrides is not null)
            overrides[IncomingLogger] = settings.LogIncomingPackets ? LogSettings.PacketsOn : LogSettings.PacketsOff;

        if (settings.Has(LogKnobs.ServerRetention) && FindFileSinkArgs(root, ServerLogPath) is { } serverArgs)
            serverArgs["retainedFileCountLimit"] = settings.ServerLogRetentionDays;
        if (settings.Has(LogKnobs.NetworkRetention) && FindFileSinkArgs(root, NetworkLogPath) is { } networkArgs)
            networkArgs["retainedFileCountLimit"] = settings.NetworkLogRetentionDays;

        try
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(WriteOptions));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Could not write {path} ({ex.Message}).";
        }
    }

    /// <summary>A packet logger set at Debug is on; anything coarser silences it, because that is the level
    /// it emits at.</summary>
    private static bool IsVerbose(string level) =>
        level.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
        level.Equals("Verbose", StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds a File sink's Args by the log file it writes, walking the whole graph.
    ///
    /// <para>Keyed on the PATH rather than on array indices. Both retention limits live in File sinks
    /// nested at different depths of the same WriteTo array, and an index would silently address the wrong
    /// sink the moment anyone reorders the file — where the path names the sink the operator means.</para></summary>
    private static JsonObject? FindFileSinkArgs(JsonNode node, string logPath)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["Name"]?.GetValue<string>() == "File" &&
                    obj["Args"] is JsonObject args &&
                    PathComparison.SameLocation(args["path"]?.GetValue<string>(), logPath))
                    return args;
                foreach (var (_, child) in obj)
                    if (child is not null && FindFileSinkArgs(child, logPath) is { } found) return found;
                return null;

            case JsonArray array:
                foreach (var child in array)
                    if (child is not null && FindFileSinkArgs(child, logPath) is { } found) return found;
                return null;

            default:
                return null;
        }
    }
}
