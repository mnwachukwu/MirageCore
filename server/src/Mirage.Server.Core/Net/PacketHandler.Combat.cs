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

/// <summary>The attack keypress, target selection, and the search/interact probe that decides what
/// the player is pointing at.</summary>
public sealed partial class PacketHandler
{
    //  Combat handler
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleAttack(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _combat.HandleAttack(index);
    }

    // The client's target scrolled out of its viewport — clear it so casts can't continue.
    private void HandleDropTarget(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _pm[index].Target = 0;
        _pm[index].TargetType = 0;
        _pm[index].TargetMap = 0;
        _pm[index].TargetSpawnSlot = 0;
    }

    private void HandleSearch(int index, SearchPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!_world.Maps[_pm[index].Char.Map].Contains(p.X, p.Y)) return;
        if (p.ProposedType is not (0 or 1 or 2 or 3 or 255))
        {
            HackingAttempt(index, "Invalid ProposedType");
            return;
        }

        // The tile is the click scan's anchor for the item listing only; target acquisition is
        // resolved by identity from the client's proposal, not by scanning entities at the tile.
        int tileMapNum = _pm[index].Char.Map;
        if (p.MapNum > 0 && p.MapNum <= _world.Limits.Maps && _world.IsObserving(index, p.MapNum))
            tileMapNum = p.MapNum;
        int myLevel = _pm[index].Char.Level;

        // Search lines (level-gap readout, item descriptions, "target now") are all personal system
        // notices to the searcher, never broadcasts. Key-based so each line localizes via ForPlayer.
        void Say(string key, int color, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, ChatChannel.System), args);

        bool targetResolved = false;
        bool targetFailed = false;

        switch (p.ProposedType)
        {
            case 0: // Player
            {
                // _pm.IsValidSlot, not the protocol check: ProposedId is client-supplied and indexes a
                // player two lines down, so it has to be bounded by THIS server's array.
                if (!_pm.IsValidSlot(p.ProposedId) || p.ProposedId == index)
                {
                    targetFailed = true;
                    break;
                }
                var sp = _pm[p.ProposedId];
                if (!sp.IsPlaying)
                {
                    targetFailed = true;
                    break;
                }
                if (!_world.IsObserving(index, sp.Char.Map))
                {
                    targetFailed = true;
                    break;
                }

                int diff = sp.Char.Level - myLevel;
                if (diff >= 5) Say(ServerStrings.SearchSystem_WouldntStandChance, GameColor.DarkGray);
                else if (diff > 0) Say(ServerStrings.SearchSystem_TheyHaveAdvantage, GameColor.DarkGray);
                else if (diff == 0) Say(ServerStrings.SearchSystem_EvenFight, GameColor.DarkGray);
                else if (diff > -5) Say(ServerStrings.SearchSystem_YouHaveAdvantage, GameColor.DarkGray);
                else Say(ServerStrings.SearchSystem_TheyWouldntChance, GameColor.DarkGray);

                _pm[index].Target = p.ProposedId;
                _pm[index].TargetType = 0;
                _pm[index].TargetMap = 0;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetNow, GameColor.Yellow, ("TargetName", sp.Char.TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 0, Target = p.ProposedId });
                targetResolved = true;
                break;
            }
            case 1: // Native-slot NPC
            {
                if (!SlotValidation.IsValidMapNum(p.ProposedMap, _world.Limits.Maps) || !SlotValidation.IsValidNpcSlot(p.ProposedId))
                {
                    targetFailed = true;
                    break;
                }
                if (!_world.IsObserving(index, p.ProposedMap))
                {
                    targetFailed = true;
                    break;
                }
                var mn = _world.MapNpcs[p.ProposedMap, p.ProposedId];
                if (mn.Num <= 0)
                {
                    targetFailed = true;
                    break;
                }

                var npc = _world.Npcs[mn.Num];
                if (npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked or NpcBehavior.Guard)
                {
                    var me = _pm[index].Char;
                    // On-target strength flavor: the NPC's virtual level vs the player's ACTUAL level.  Pure
                    // information — EXP itself is player-relative (ExpFormulas.ExpForKill), not level-gap-tiered.
                    int levelDiff = StatFormulas.NpcLevel(npc) - me.Level;
                    if (levelDiff >= 5) Say(ServerStrings.SearchSystem_WouldntStandChance, GameColor.BrightRed);
                    else if (levelDiff > 0) Say(ServerStrings.SearchSystem_TheyHaveAdvantage, GameColor.Yellow);
                    else if (levelDiff == 0) Say(ServerStrings.SearchSystem_EvenFight, GameColor.White);
                    else if (levelDiff > -5) Say(ServerStrings.SearchSystem_YouHaveAdvantageNpc, GameColor.Yellow);
                    else Say(ServerStrings.SearchSystem_NpcWouldntChance, GameColor.BrightBlue);
                }

                _pm[index].Target = p.ProposedId;
                _pm[index].TargetType = 1;
                _pm[index].TargetMap = p.ProposedMap;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetNowNpc, GameColor.Yellow, ("NpcName", npc.TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 1, Target = p.ProposedId, TargetMap = p.ProposedMap });
                targetResolved = true;
                break;
            }
            case 2: // Self
            {
                if (p.ProposedId != index)
                {
                    targetFailed = true;
                    break;
                }
                _pm[index].Target = index;
                _pm[index].TargetType = 2;
                _pm[index].TargetMap = 0;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetSelf, GameColor.Yellow);
                _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 2 });
                targetResolved = true;
                break;
            }
            case 3: // Traversal NPC (addressed by (SpawnMap, SpawnSlot) identity)
            {
                if (!SlotValidation.IsValidMapNum(p.ProposedMap, _world.Limits.Maps) || !SlotValidation.IsValidNpcSlot(p.ProposedId))
                {
                    targetFailed = true;
                    break;
                }

                // A guest roams between maps as it chases; scan the player's observable region
                // for one matching the proposal's permanent identity.  Mirrors SpellSystem.FindTraversalTarget.
                Span<int> observed = stackalloc int[9];
                int observedCount = _world.ObservedMapsInto(_pm[index].Char.Map, observed);
                TraversalNpcRecord? found = null;
                int foundMapNum = 0;
                for (int oi = 0; oi < observedCount && found is null; oi++)
                {
                    int m = observed[oi];
                    var guests = _world.MapTraversalNpcs[m];
                    for (int gi = 0; gi < guests.Count; gi++)
                    {
                        var t = guests[gi];
                        if (t.Num <= 0) continue;
                        if (t.SpawnMapNum != p.ProposedMap || t.SpawnSlot != p.ProposedId) continue;
                        found = t;
                        foundMapNum = m;
                        break;
                    }
                }
                if (found is null)
                {
                    targetFailed = true;
                    break;
                }

                var npc = _world.Npcs[found.Num];
                var me = _pm[index].Char;
                int levelDiff = StatFormulas.NpcLevel(npc) - me.Level;
                if (levelDiff >= 5) Say(ServerStrings.SearchSystem_WouldntStandChance, GameColor.BrightRed);
                else if (levelDiff > 0) Say(ServerStrings.SearchSystem_TheyHaveAdvantage, GameColor.Yellow);
                else if (levelDiff == 0) Say(ServerStrings.SearchSystem_EvenFight, GameColor.White);
                else if (levelDiff > -5) Say(ServerStrings.SearchSystem_YouHaveAdvantageNpc, GameColor.Yellow);
                else Say(ServerStrings.SearchSystem_NpcWouldntChance, GameColor.BrightBlue);

                _pm[index].TargetType = 3;
                _pm[index].Target = 0;
                _pm[index].TargetMap = foundMapNum;
                _pm[index].TargetSpawnMap = p.ProposedMap;
                _pm[index].TargetSpawnSlot = p.ProposedId;
                Say(ServerStrings.SearchSystem_TargetNowNpc, GameColor.Yellow, ("NpcName", npc.TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket
                {
                    TargetType = 3,
                    TargetMap = foundMapNum,
                    SpawnMap = p.ProposedMap,
                    SpawnSlot = p.ProposedId,
                });
                targetResolved = true;
                break;
            }
        }

        if (!targetResolved)
        {
            // Empty click (255) OR proposal failed validation — drop server-side target either way.
            _pm[index].Target = 0;
            _pm[index].TargetType = 0;
            _pm[index].TargetMap = 0;
            _pm[index].TargetSpawnMap = 0;
            _pm[index].TargetSpawnSlot = 0;
            // On a stale proposal, tell the client to drop its mis-acquired guess.
            if (targetFailed)
                _dispatcher.SendTo(index, new ClearTargetPacket());
        }

        // Item listing: reports every item on the clicked tile, top-down by drop order (LIFO via DropSeq).
        var list = _world.MapItems[tileMapNum];
        var hits = new List<MapItemRecord>();
        for (int i = 0; i < list.Count; i++)
        {
            var mi = list[i];
            if (mi.Num <= 0 || mi.X != p.X || mi.Y != p.Y) continue;
            hits.Add(mi);
        }
        hits.Sort((a, b) => b.DropSeq.CompareTo(a.DropSeq));
        foreach (var mi in hits)
        {
            var seenItem = _world.Items[mi.Num];
            string seenName = seenItem.TrimmedName;
            switch (seenItem.Type)
            {
                case ItemType.Currency:
                    Say(ServerStrings.SearchSystem_SeeCurrency, GameColor.Yellow, ("Amount", mi.Quantity), ("Name", seenName));
                    break;
                case ItemType.Weapon:
                case ItemType.Armor:
                case ItemType.Helmet:
                case ItemType.Shield:
                    Say(ServerStrings.SearchSystem_SeeEquipment, GameColor.Yellow, ("Name", seenName), ("Dur", mi.Dur), ("Max", seenItem.Durability));
                    break;
                default:
                    Say(ServerStrings.SearchSystem_SeeItem, GameColor.Yellow, ("Name", seenName));
                    break;
            }
        }
    }
}
