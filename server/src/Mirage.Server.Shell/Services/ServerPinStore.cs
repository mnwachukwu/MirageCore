using Mirage.Shared;
using Mirage.Shared.Security;

namespace Mirage.Server.Shell.Services;

/// <summary>The shell's certificate pins, beside its shell.json.</summary>
public static class ServerPinStore
{
    public const string FileName = "server-pins.json";

    public static ServerPins Store { get; } =
        new(new UserPaths(Constants.GameName + " Server Shell").Config(FileName));
}
