using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Localization;
using System.Security.Cryptography;

namespace Mirage.Server.Host.Services;

/// <summary>
/// <c>/management</c> — remote operator access, set from the console.
///
/// <para>🔴 This exists because the deployment that most needs remote administration is the one that
/// could not turn it on. A headless server had no command for the port or the token, so an operator's
/// only route was to stop the process, hand-edit serverconfig.json, invent a shared secret themselves,
/// and start again. The management shell could set it; the CLI could not, which is backwards.</para>
///
/// <para>Every change is applied to the RUNNING listener and written to serverconfig.json, in that
/// order. Applying first means the operator learns immediately if the port will not bind, and a config
/// that was saved but refused would otherwise be a setting that looks right and does nothing.</para>
/// </summary>
public sealed partial class ConsoleCommands
{
    /// <summary>Bytes behind a generated token. 32 gives a 43-character base64url secret, which is past
    /// anything worth guessing and still short enough to paste over a phone.</summary>
    private const int TokenBytes = 32;

    private void CmdManagement(string args)
    {
        string[] parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string rest = parts.Length > 1 ? parts[1] : "";

        switch (verb)
        {
            case "": ShowManagement(); return;
            case "port": SetManagementPort(rest); return;
            case "token": SetManagementToken(rest); return;
            case "off": SetManagement(_config.Management with { Port = 0 }); return;
            default:
                Write(ServerStrings.Console_ManagementUsage);
                return;
        }
    }

    /// <summary>What is configured, what is actually bound, and who is attached.
    ///
    /// <para>Configured and bound are reported separately on purpose: a port already in use leaves the
    /// two disagreeing, and "the config says 4001" is not an answer to "why can nothing attach".</para></summary>
    private void ShowManagement()
    {
        var listener = _management();
        var cfg = _config.Management;

        Write(ServerStrings.Console_ManagementPort, ("Port", cfg.Port == 0 ? "off" : cfg.Port.ToString()));
        Write(ServerStrings.Console_ManagementToken,
            ("State", cfg.Token.Length == 0
                ? ServerStrings.Get(ServerStrings.Console_ManagementTokenUnset)
                : ServerStrings.Format(ServerStrings.Console_ManagementTokenSet, ("Length", cfg.Token.Length))));
        Write(ServerStrings.Console_ManagementListening,
            ("State", listener.IsListening
                ? ServerStrings.Format(ServerStrings.Console_ManagementBound, ("Port", listener.BoundPort))
                : ServerStrings.Get(ServerStrings.Console_ManagementNotBound)));
        Write(ServerStrings.Console_ManagementOperators, ("Count", listener.AttachedOperators));

        // The one actionable line. A port with no token is the configuration that looks half-done and is
        // refused outright, so say which half is missing rather than leaving it to be discovered.
        if (cfg.Port > 0 && cfg.Token.Length == 0) Write(ServerStrings.Console_ManagementNeedsToken);
    }

    private void SetManagementPort(string arg)
    {
        if (!int.TryParse(arg, out int port) || port is < 0 or > 65535)
        {
            Write(ServerStrings.Console_ManagementPortUsage);
            return;
        }
        SetManagement(_config.Management with { Port = port });
    }

    /// <summary>Set the shared secret, or generate one when none is given.
    ///
    /// <para>Generating is the default path deliberately. Asking an operator to invent a secret produces
    /// a weak one, and this is the only thing standing between a stranger and a remote console.</para></summary>
    private void SetManagementToken(string arg)
    {
        bool generated = arg.Length == 0;
        string token = generated
            ? Base64Url(RandomNumberGenerator.GetBytes(TokenBytes))
            : arg;

        // Printed ONCE, and only when generated. A token the operator typed is already in their scrollback;
        // one this made up is unrecoverable if it is not shown, because only the hash of intent is kept —
        // the file is the sole copy.
        if (generated) Write(ServerStrings.Console_ManagementTokenGenerated, ("Token", token));
        SetManagement(_config.Management with { Token = token });
    }

    /// <summary>Apply to the running listener, then persist. Both outcomes are reported: a save that
    /// failed after a successful rebind is a server that will lose the setting on restart, which the
    /// operator has to know now rather than next boot.</summary>
    private void SetManagement(ManagementConfig next)
    {
        string? refused = _management().Reconfigure(next);

        // The record is immutable and shared, so the live copy is replaced rather than edited.
        _config.Management = next;

        string? saveError = ServerConfigStore.Save(_configPath.Path, _config);

        if (refused is not null) Write(ServerStrings.Console_ManagementRefused, ("Reason", refused));
        else if (next.Port == 0) Write(ServerStrings.Console_ManagementOff);
        else Write(ServerStrings.Console_ManagementListening,
            ("State", ServerStrings.Format(ServerStrings.Console_ManagementBound, ("Port", next.Port))));

        if (saveError is not null) Write(ServerStrings.Console_ManagementSaveFailed, ("Error", saveError));

        _logger.LogInformation("Console set management to port {Port}, token {State}.",
            next.Port, next.Token.Length == 0 ? "unset" : "set");
    }

    /// <summary>base64url — no padding, and none of the characters that need escaping in a shell, a URL
    /// or a JSON string. A token gets pasted through all three.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
