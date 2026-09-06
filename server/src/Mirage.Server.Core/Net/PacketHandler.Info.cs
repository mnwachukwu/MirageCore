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

/// <summary>Read-only lookups a player asks for about themselves or someone else: who is online, a player's info card, playtime, and the stat sheet.</summary>
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

        var tp = _pm[n].Char;
        string login = _pm[n].Login.Trim();
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_PlayerInfo,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Account", login), ("Name", tp.Name.Trim()));
        // Playtime line — the target's current character + account total, shown to any requester.
        long nowUtc = NowUtc;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_Played,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Char", PlaytimeFormat.HoursMinutes(_pm[n].CharPlaytimeSeconds(nowUtc))),
            ("Total", PlaytimeFormat.HoursMinutes(_pm[n].AccountPlaytimeSeconds(nowUtc))));

        if (_pm[index].Char.Access <= AdminLevel.Monitor) return;

        long tnl = ExpFormulas.TnlForLevel(tp.Level);
        long withinLevel = tp.Exp - ExpFormulas.ExpFloorForLevel(tp.Level);
        string critChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(tp.Str, tp.Level));
        string blockChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(tp.Def, tp.Level));
        string spellCritChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(tp.Int, tp.Level));

        void Say(string key, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice), args);
        Say(ServerStrings.AdminCommand_StatsHeader, ("Name", tp.Name.Trim()));
        Say(ServerStrings.AdminCommand_StatsLevel, ("Level", tp.Level), ("Current", withinLevel), ("Tnl", tnl), ("Total", tp.Exp));
        Say(ServerStrings.AdminCommand_StatsVitals, ("Hp", tp.Hp), ("MaxHp", tp.MaxHp), ("Mp", tp.Mp), ("MaxMp", tp.MaxMp), ("Sp", tp.Sp), ("MaxSp", tp.MaxSp));
        Say(ServerStrings.AdminCommand_StatsAttributes, ("Str", tp.Str), ("Def", tp.Def), ("Int", tp.Int), ("Spd", tp.Spd));
        Say(ServerStrings.AdminCommand_StatsChances, ("PCrit", critChance), ("Block", blockChance), ("MCrit", spellCritChance));
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

    private void HandleGetStats(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        long tnl = ExpFormulas.TnlForLevel(p.Level);
        long withinLevel = p.Exp - ExpFormulas.ExpFloorForLevel(p.Level);
        string critChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(p.Str, p.Level));
        string blockChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(p.Def, p.Level));
        string spellCritChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(p.Int, p.Level));

        void Say(string key, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(GameColor.White, ChatChannel.System), args);
        Say(ServerStrings.AdminCommand_StatsHeader, ("Name", p.Name.Trim()));
        Say(ServerStrings.AdminCommand_StatsLevel, ("Level", p.Level), ("Current", withinLevel), ("Tnl", tnl), ("Total", p.Exp));
        Say(ServerStrings.AdminCommand_StatsVitals, ("Hp", p.Hp), ("MaxHp", p.MaxHp), ("Mp", p.Mp), ("MaxMp", p.MaxMp), ("Sp", p.Sp), ("MaxSp", p.MaxSp));
        Say(ServerStrings.AdminCommand_StatsAttributes, ("Str", p.Str), ("Def", p.Def), ("Int", p.Int), ("Spd", p.Spd));
        Say(ServerStrings.AdminCommand_StatsChances, ("PCrit", critChance), ("Block", blockChance), ("MCrit", spellCritChance));
    }
}
