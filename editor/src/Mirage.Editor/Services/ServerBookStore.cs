using Mirage.Shared.Security;

namespace Mirage.Editor.Services;

/// <summary>The servers this editor knows about, in its per-user config dir.</summary>
internal static class ServerBookStore
{
    public const string FileName = "known-servers.json";

    public static ServerBook Book { get; } = new(Path.Combine(EditorPaths.Config, FileName));
}
