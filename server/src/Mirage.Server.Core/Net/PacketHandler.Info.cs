using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>Read-only lookups a player asks for about themselves or someone else: who is online, a
/// player's info card, and playtime.</summary>
public sealed partial class PacketHandler
{
    //  Info handlers
    // ===========================================================================

    private void HandleWhosOnline(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _joinLeave.SendWhosOnline(index);
    }

    private void HandlePlayerInfoRequest(int index, PlayerInfoRequestPacket pkt)
    {
        if (!_pm[index].IsPlaying) return;

        int n = _pm.FindPlayerByName(pkt.Target);
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }

        string login = _pm[n].Login.Trim();
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_PlayerInfo,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Account", login), ("Name", _pm[n].Char.Name.Trim()));
        // Playtime line — the target's current character + account total, shown to any requester.
        long nowUtc = NowUtc;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_Played,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Char", PlaytimeFormat.HoursMinutes(_pm[n].CharPlaytimeSeconds(nowUtc))),
            ("Total", PlaytimeFormat.HoursMinutes(_pm[n].AccountPlaytimeSeconds(nowUtc))));

    }

    // /played — the requester's own playtime (current character + account total).
    private void HandlePlayedRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var sp = _pm[index];
        long nowUtc = NowUtc;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_Played,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Char", PlaytimeFormat.HoursMinutes(sp.CharPlaytimeSeconds(nowUtc))),
            ("Total", PlaytimeFormat.HoursMinutes(sp.AccountPlaytimeSeconds(nowUtc))));
    }

}
