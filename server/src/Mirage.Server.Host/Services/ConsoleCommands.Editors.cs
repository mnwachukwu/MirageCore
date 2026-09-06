using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;

namespace Mirage.Server.Host.Services;

/// <summary>
/// What the operator can see and do about editor sessions.
///
/// <para>An editor connection is not a player connection: it holds no character and never appears in
/// <c>/who</c>. It also outlives a change to the account behind it, so revoking someone's access leaves their
/// open session running on the access it already has — which is what <c>/kickeditor</c> is for.</para>
/// </summary>
public sealed partial class ConsoleCommands
{
    /// <summary>Every connected editor: slot, account, access, and what it is holding open.</summary>
    private void CmdEditors()
    {
        int count = 0;
        for (int i = 1; i <= Mirage.Shared.Constants.MaxEditorSessions; i++)
        {
            var s = _editors.GetSession(i);
            if (s is null || !s.IsConnected) continue;
            count++;
            string held = string.Join(", ", _editorLocks.HeldBy(i).Select(h => $"{h.Section}#{h.Num}"));
            string who = s.IsAuthenticated ? s.Login : "(signing in)";
            System.Console.WriteLine(
                $"  [{i,2}] {who,-20}  {s.AdminLevel,-9}  {(held.Length > 0 ? "editing " + held : "idle")}");
        }
        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_EditorsTotal, ("Count", count)));
    }

    /// <summary>Ends an editor session by slot or by account name.
    ///
    /// <para>Its locks go with it, exactly as they would if the socket had dropped — so cutting someone off
    /// never leaves the records they were holding shut against everybody else.</para></summary>
    private void CmdKickEditor(string args)
    {
        string target = args.Trim();
        if (target.Length == 0)
        {
            System.Console.WriteLine(ServerStrings.Get(ServerStrings.Console_KickEditorUsage));
            return;
        }

        EditorSession? found = null;
        if (int.TryParse(target, out int slot))
        {
            var s = _editors.GetSession(slot);
            if (s is { IsConnected: true }) found = s;
        }
        else
        {
            for (int i = 1; i <= Mirage.Shared.Constants.MaxEditorSessions; i++)
            {
                var s = _editors.GetSession(i);
                if (s is { IsConnected: true } &&
                    string.Equals(s.Login, target, StringComparison.OrdinalIgnoreCase)) { found = s; break; }
            }
        }

        if (found is null)
        {
            System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_EditorNotFound, ("Target", target)));
            return;
        }

        string who = found.IsAuthenticated ? found.Login : $"slot {found.Index}";
        _editorHandler.OnEditorDisconnected(found.Index);
        _dispatcher.GracefulDisconnectEditor(found.Index);
        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_EditorKicked, ("Target", who)));
        _logger.LogInformation("Operator disconnected editor session {Slot} ({Login}).", found.Index, who);
    }
}
