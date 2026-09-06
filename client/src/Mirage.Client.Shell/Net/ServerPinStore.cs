using Mirage.Shared.Security;

namespace Mirage.Client.Shell.Net;

/// <summary>The client's certificate pins, in the per-user config dir.</summary>
public static class ServerPinStore
{
    public const string FileName = "server-pins.json";

    public static ServerPins Store { get; } = new(AppPaths.Config(FileName));
}
