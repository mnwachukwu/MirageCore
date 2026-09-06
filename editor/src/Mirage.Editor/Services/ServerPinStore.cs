using Mirage.Shared.Security;

namespace Mirage.Editor.Services;

/// <summary>The editor's certificate pins, in its per-user config dir.</summary>
internal static class ServerPinStore
{
    public const string FileName = "server-pins.json";

    public static ServerPins Store { get; } = new(Path.Combine(EditorPaths.Config, FileName));
}
