using Mirage.Shared.Security;

namespace Mirage.Client.Shell.Net;

/// <summary>The servers this client knows about, in the per-user config dir.</summary>
public static class ServerBookStore
{
    public const string FileName = "known-servers.json";

    public static ServerBook Book { get; } = new(AppPaths.Config(FileName));
}
