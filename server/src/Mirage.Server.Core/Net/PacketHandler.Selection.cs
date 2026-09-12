using Mirage.Server.Core.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>
/// What the player clicked, and what they now have selected.
///
/// <para>A click carries the tile plus the body the client believes was under the cursor, and the
/// server resolves that proposal by IDENTITY rather than by scanning the tile — a body that moved
/// between the click and the packet is a stale proposal, not a different target. A proposal that does
/// not resolve clears the selection on both ends rather than leaving the two disagreeing.</para>
///
/// <para>Selecting a body means nothing on its own. What may then be done with it is the game's.</para>
/// </summary>
public sealed partial class PacketHandler
{
    private void HandleDropTarget(int index)
    {
        if (!_pm[index].IsPlaying) return;
        ClearSelection(index);
    }

    private void ClearSelection(int index)
    {
        _pm[index].Target = 0;
        _pm[index].TargetType = 0;
        _pm[index].TargetMap = 0;
        _pm[index].TargetSpawnMap = 0;
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

        // The tile anchors the item listing only; the selection is resolved from the proposal.
        int tileMapNum = _pm[index].Char.Map;
        if (p.MapNum > 0 && p.MapNum <= _world.Limits.Maps && _world.IsObserving(index, p.MapNum))
        {
            tileMapNum = p.MapNum;
        }

        // Every line here is a personal notice to the clicker, never a broadcast. Key-based so each
        // one localizes into that player's own language.
        void Say(string key, int color, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, ChatChannel.System), args);

        bool resolved = false;
        bool failed = false;

        switch (p.ProposedType)
        {
            case 0:   // another player
            {
                // _pm.IsValidSlot rather than the protocol bound: the id is client-supplied and indexes
                // this server's array.
                if (!_pm.IsValidSlot(p.ProposedId) || p.ProposedId == index) { failed = true; break; }

                var sp = _pm[p.ProposedId];
                if (!sp.IsPlaying || !_world.IsObserving(index, sp.Char.Map)) { failed = true; break; }

                _pm[index].Target = p.ProposedId;
                _pm[index].TargetType = 0;
                _pm[index].TargetMap = 0;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetNow, GameColor.Yellow, ("TargetName", sp.Char.TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 0, Target = p.ProposedId });
                resolved = true;
                break;
            }

            case 1:   // an NPC standing in its own slot
            {
                if (!SlotValidation.IsValidMapNum(p.ProposedMap, _world.Limits.Maps)
                    || !SlotValidation.IsValidNpcSlot(p.ProposedId)
                    || !_world.IsObserving(index, p.ProposedMap))
                {
                    failed = true;
                    break;
                }

                var mn = _world.MapNpcs[p.ProposedMap, p.ProposedId];
                if (mn.Num <= 0) { failed = true; break; }

                _pm[index].Target = p.ProposedId;
                _pm[index].TargetType = 1;
                _pm[index].TargetMap = p.ProposedMap;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetNowNpc, GameColor.Yellow,
                    ("NpcName", _world.Npcs[mn.Num].TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket
                {
                    TargetType = 1,
                    Target = p.ProposedId,
                    TargetMap = p.ProposedMap,
                });
                resolved = true;
                break;
            }

            case 2:   // yourself
            {
                if (p.ProposedId != index) { failed = true; break; }

                _pm[index].Target = index;
                _pm[index].TargetType = 2;
                _pm[index].TargetMap = 0;
                _pm[index].TargetSpawnMap = 0;
                _pm[index].TargetSpawnSlot = 0;
                Say(ServerStrings.SearchSystem_TargetSelf, GameColor.Yellow);
                _dispatcher.SendTo(index, new SetTargetPacket { TargetType = 2 });
                resolved = true;
                break;
            }

            case 3:   // a visiting NPC, named by the identity it spawned under
            {
                if (!SlotValidation.IsValidMapNum(p.ProposedMap, _world.Limits.Maps)
                    || !SlotValidation.IsValidNpcSlot(p.ProposedId))
                {
                    failed = true;
                    break;
                }

                // A visitor roams as it moves, so the observable region is scanned for the one whose
                // permanent identity matches — its current slot says nothing about which body it is.
                Span<int> observed = stackalloc int[9];
                int observedCount = _world.ObservedMapsInto(_pm[index].Char.Map, observed);
                TraversalNpcRecord? found = null;
                int foundMapNum = 0;
                for (int oi = 0; oi < observedCount && found is null; oi++)
                {
                    var guests = _world.MapTraversalNpcs[observed[oi]];
                    for (int gi = 0; gi < guests.Count; gi++)
                    {
                        var t = guests[gi];
                        if (t.Num <= 0) continue;
                        if (t.SpawnMapNum != p.ProposedMap || t.SpawnSlot != p.ProposedId) continue;
                        found = t;
                        foundMapNum = observed[oi];
                        break;
                    }
                }

                if (found is null) { failed = true; break; }

                _pm[index].TargetType = 3;
                _pm[index].Target = 0;
                _pm[index].TargetMap = foundMapNum;
                _pm[index].TargetSpawnMap = p.ProposedMap;
                _pm[index].TargetSpawnSlot = p.ProposedId;
                Say(ServerStrings.SearchSystem_TargetNowNpc, GameColor.Yellow,
                    ("NpcName", _world.Npcs[found.Num].TrimmedName));
                _dispatcher.SendTo(index, new SetTargetPacket
                {
                    TargetType = 3,
                    TargetMap = foundMapNum,
                    SpawnMap = p.ProposedMap,
                    SpawnSlot = p.ProposedId,
                });
                resolved = true;
                break;
            }
        }

        if (!resolved)
        {
            // An empty click, or a proposal that did not survive validation: either way nothing is
            // selected now.
            ClearSelection(index);

            // Only a FAILED proposal needs telling — the client believes it selected something.
            if (failed) _dispatcher.SendTo(index, new ClearTargetPacket());
        }

        // What is lying on the clicked tile, newest first.
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
            var seen = _world.Items[mi.Num];
            if (seen.Type == ItemType.Currency)
            {
                Say(ServerStrings.SearchSystem_SeeCurrency, GameColor.Yellow,
                    ("Amount", mi.Quantity), ("Name", seen.TrimmedName));
            }
            else
            {
                Say(ServerStrings.SearchSystem_SeeItem, GameColor.Yellow, ("Name", seen.TrimmedName));
            }
        }
    }
}
