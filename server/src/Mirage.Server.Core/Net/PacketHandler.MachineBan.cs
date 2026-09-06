using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.Net;

/// <summary>
/// The machine-ban gate: what happens when a login or a registration arrives from a machine an operator
/// banned.
///
/// <para>Shared by both doors deliberately. Blocking a LOGIN alone would leave the feature useless —
/// registering again is exactly what an account ban already fails to stop, so the registration door is the
/// one that matters.</para>
/// </summary>
public sealed partial class PacketHandler
{
    /// <summary>
    /// Salts the key a client sent, records it on the session, and decides whether the connection may
    /// continue. Returns false when the caller should stop; it has already alerted and disconnected.
    /// </summary>
    /// <remarks>
    /// An empty key always passes. A client that could not read its machine's identifier, or a build old
    /// enough not to send one, is not evidence of anything — and treating blank as a value would give
    /// every such machine ONE shared identity and ban all of them the first time any of them was banned.
    /// </remarks>
    private async Task<bool> ApplyMachineKeyAsync(int index, string clientKey, string accountName)
    {
        string hashed = await _persistence.HashMachineKeyAsync(clientKey);
        _pm[index].MachineKey = hashed;
        if (hashed.Length == 0) return true;

        var ban = await _persistence.FindHardwareBanAsync(hashed);
        if (ban is null) return true;

        if (_config.HardwareBans.Mode == HardwareBanMode.Block)
        {
            _logger.LogWarning(
                "Refused {Name} — the machine is banned (originally as {BannedLogin}: {Reason}).",
                accountName, ban.Login, ban.Reason);
            AlertAndDisconnect(index, ServerStrings.Auth_Banned, ("GameName", _config.GameName));
            return false;
        }

        // Signal mode: they get in, and the people who can act on it are told. The log line matters as
        // much as the in-game notice — an operator reading it tomorrow is the likelier reader, and under
        // this mode the log is the only lasting record that the match happened at all.
        _logger.LogWarning(
            "{Name} signed in from a machine banned as {BannedLogin} ({Reason}). Allowed: hardware bans are set to report, not block.",
            accountName, ban.Login, ban.Reason);
        NotifyStaffOfMachineBanHit(accountName, ban.Login);
        return true;
    }

    /// <summary>Tells every Monitor and above online that a banned machine just came through. Silent when
    /// nobody is on to hear it — the log line above is the durable record.
    /// <para> Walks the roster, so it belongs on the game thread with the rest of this handler.</para></summary>
    private void NotifyStaffOfMachineBanHit(string accountName, string bannedLogin)
    {
        foreach (int slot in _pm.Online)
        {
            if (!_pm[slot].IsPlaying) continue;
            if (_pm[slot].Char.Access < AdminLevel.Monitor) continue;
            _dispatcher.SendLocalizedChatTo(slot, ServerStrings.AdminCommand_MachineBanHit,
                new ChatMetadata(GameColor.Yellow, ChatChannel.Notice),
                ("Name", accountName), ("Banned", bannedLogin));
        }
    }
}
