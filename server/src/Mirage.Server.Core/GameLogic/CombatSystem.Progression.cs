using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>What a fight moves on the character sheet: level-ups on EXP gained, and the EXP
/// loss plus random stat drain a death imposes.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ── Level up ──────────────────────────────────────────────────────────────

    /// <summary>Fired (once) after a player gains one or more levels — lets the quest layer re-push eligibility
    /// (a new level can newly satisfy a quest's accept requirements). Wired at startup; null in unit tests (no-op).</summary>
    public Action<int>? PlayerLeveledUp;

    public void CheckPlayerLevelUp(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;
        bool leveled = false;
        while (p.Level < Constants.MaxLevel && p.Exp >= ExpFormulas.ExpFloorForLevel(p.Level + 1))
        {
            leveled = true;
            p.Level++;
            p.Points += Constants.PointsPerLevel;
            _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_LevelUpBroadcast,
                new ChatMetadata(GameColor.Brown, ChatChannel.System),
                ("PlayerName", p.TrimmedName));
            SendMsg(index, ServerStrings.CombatSystem_LevelUp, GameColor.BrightBlue, ChatChannel.System, ("Level", p.Level), ("Points", p.Points));
            if (p.Level == 10)
            {
                SendMsg(index, ServerStrings.CombatSystem_Level10Congrats, GameColor.Yellow, ChatChannel.System);
                SendMsg(index, ServerStrings.CombatSystem_Level10DeathPenalty, GameColor.BrightRed, ChatChannel.System);
                SendMsg(index, ServerStrings.CombatSystem_Level10PvpEnabled, GameColor.BrightRed, ChatChannel.System);
            }
            var cls = _world.Classes[p.Class];
            StatFormulas.RefreshPlayerMaxVitals(p, cls, _world.WeatherOn(p.Map));
            _dispatcher.SendTo(index, PacketBuilder.SendHp(index, p.Hp, p.MaxHp));
            SendToMap(_world, p.Map, PacketBuilder.SendMp(index, p.Mp, p.MaxMp));
            SendToMap(_world, p.Map, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));
            _dispatcher.SendTo(index, PacketBuilder.SendStats(p));
            _pm.MarkDirty(index);   // persist the gained level + stat points this tick
        }
        if (p.Exp > ExpFormulas.MaxTotalExp) p.Exp = ExpFormulas.MaxTotalExp;
        if (leveled) PlayerLeveledUp?.Invoke(index);
    }

    // Returns the EXP actually taken, which the PvP paths hand to the killers — so 0 means "nothing to
    // transfer" and the call sites test for it. The floor below takes at least 1, which is why the switch
    // is an early return rather than a zeroed amount.
    internal long ApplyExpLoss(int index, long amount)
    {
        if (!Config.DeathPenalty.ExpLoss) return 0;
        var p = _pm[index].Char;
        long actual = Math.Max(amount, 1L);
        p.Exp = Math.Max(p.Exp - actual, 0L);
        int levelBefore = p.Level;
        while (p.Level > 1 && p.Exp < ExpFormulas.ExpFloorForLevel(p.Level))
        {
            p.Level--;
            int toRemove = Constants.PointsPerLevel;
            int fromUnspent = Math.Min(p.Points, toRemove);
            p.Points -= fromUnspent;
            toRemove -= fromUnspent;
            // Build the loss-description fragments in the recipient's locale so the {LossDesc}
            // substitution into the outer per-recipient template stays in the same language.
            var lossParts = new List<string>(2);
            if (fromUnspent > 0)
            {
                lossParts.Add(ServerStrings.ForPlayer(index,
                    fromUnspent == 1 ? ServerStrings.CombatSystem_LevelDownUnspentSingle : ServerStrings.CombatSystem_LevelDownUnspentPlural,
                    ("Count", fromUnspent)));
            }

            if (toRemove > 0)
            {
                string statDrain = DrainRandomStats(index, p, toRemove);
                if (statDrain.Length > 0) lossParts.Add(statDrain);
            }
            string lossDesc = lossParts.Count > 0
                ? string.Join(" and ", lossParts)
                : ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_LevelDownFallback);
            _dispatcher.SendLocalizedChatToAll(ServerStrings.CombatSystem_LevelDownBroadcast,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System),
                ("PlayerName", p.TrimmedName));
            SendMsg(index, ServerStrings.CombatSystem_LevelDown, GameColor.BrightRed, ChatChannel.System, ("Level", p.Level), ("LossDesc", lossDesc));
            var cls = _world.Classes[p.Class];
            StatFormulas.RefreshPlayerMaxVitals(p, cls, _world.WeatherOn(p.Map));
            p.Hp = Math.Min(p.Hp, p.MaxHp);
            p.Mp = Math.Min(p.Mp, p.MaxMp);
            p.Sp = Math.Min(p.Sp, p.MaxSp);
            _dispatcher.SendTo(index, PacketBuilder.SendHp(index, p.Hp, p.MaxHp));
            SendToMap(_world, p.Map, PacketBuilder.SendMp(index, p.Mp, p.MaxMp));
            SendToMap(_world, p.Map, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));
            _dispatcher.SendTo(index, PacketBuilder.SendStats(p));
        }
        // A delevel drains random stats, which can drop the player below the STR/DEF requirement of
        // gear they're wearing. Strip any now-unwearable pieces (they stay in the bag) so a player
        // can't spec into gear, die down a level, then re-spec while keeping it equipped. Spells need
        // no equivalent sweep — SpellSystem.CastSpell re-checks the INT requirement live on every cast.
        if (p.Level != levelBefore) _items.RevalidateEquipmentRequirements(index);
        return actual;
    }

    // Returns the comma-joined "1 Str, 2 Def" loss summary in the recipient's locale, since the
    // result is embedded as a substitution into the outer per-recipient level-down template.
    private string DrainRandomStats(int index, PlayerRecord p, int count)
    {
        int[] drainable = new int[4];
        int[] removed = new int[4];
        for (int i = 0; i < count; i++)
        {
            int n = 0;
            if (p.Str > 0) drainable[n++] = 0;
            if (p.Def > 0) drainable[n++] = 1;
            if (p.Int > 0) drainable[n++] = 2;
            if (p.Spd > 0) drainable[n++] = 3;
            if (n == 0) break;
            int chosen = drainable[Rng.Next(n)];
            removed[chosen]++;
            switch (chosen)
            {
                case 0:
                    p.Str--;
                    break;
                case 1:
                    p.Def--;
                    break;
                case 2:
                    p.Int--;
                    break;
                case 3:
                    p.Spd--;
                    break;
            }
        }
        var parts = new List<string>(4);
        if (removed[0] > 0) parts.Add(ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_LevelDownStatStr, ("Count", removed[0])));
        if (removed[1] > 0) parts.Add(ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_LevelDownStatDef, ("Count", removed[1])));
        if (removed[2] > 0) parts.Add(ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_LevelDownStatInt, ("Count", removed[2])));
        if (removed[3] > 0) parts.Add(ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_LevelDownStatSpd, ("Count", removed[3])));
        return string.Join(", ", parts);
    }
}
