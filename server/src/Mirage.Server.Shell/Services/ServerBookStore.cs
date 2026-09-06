using Mirage.Shared;
using Mirage.Shared.Security;

namespace Mirage.Server.Shell.Services;

/// <summary>The servers this shell knows about, beside its shell.json. Addressed by MANAGEMENT port —
/// the shell attaches to a console socket, not the game one.</summary>
public static class ServerBookStore
{
    public const string FileName = "known-servers.json";

    public static ServerBook Book { get; } =
        new(new UserPaths(Constants.GameName + " Server Shell").Config(FileName),
            ShellSettings.DefaultManagementPort);
}
