using Mirage.Server.Core.Players;

namespace Mirage.Server.Core.World;

/// <summary>
/// Keeps every player's selected entity pointing at the body they picked, as that body moves between
/// maps or leaves the world.
///
/// <para><b>Why a selection needs maintenance at all.</b> The seamless world vacates and reuses both
/// native slots and visitor identities. A selection left holding a slot number therefore re-binds
/// itself, silently, to whatever body next occupies that slot — so a selection that is not migrated
/// when its subject moves, and not cleared when its subject goes, ends up naming a stranger.</para>
///
/// <para>Nothing here decides what a selection is FOR. A game may use it for an interaction, a trade, a
/// context menu, or nothing at all.</para>
/// </summary>
public sealed class SelectionTracking(PlayerManager players)
{
    private readonly PlayerManager _players = players;

    /// <summary>Moves any selection of the native slot (<paramref name="fromMap"/>,
    /// <paramref name="npcSlot"/>) onto that body's stable spawn identity, because it is now standing on
    /// <paramref name="toMap"/> as a visitor with no slot of its own.</summary>
    public void FollowNpcAcrossSeam(int fromMap, int npcSlot, int toMap)
    {
        for (int i = 1; i <= _players.Slots; i++)
        {
            var sp = _players[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType != 1 || sp.Target != npcSlot || sp.TargetMap != fromMap) continue;

            sp.TargetType = 3;
            sp.Target = 0;
            sp.TargetSpawnMap = fromMap;
            sp.TargetSpawnSlot = npcSlot;
            sp.TargetMap = toMap;
        }
    }

    /// <summary>Clears every selection of a native NPC slot, for a body that has left it.</summary>
    public void ClearSelectionsOfNpcSlot(int mapNum, int npcSlot)
    {
        for (int i = 1; i <= _players.Slots; i++)
        {
            var sp = _players[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType != 1 || sp.Target != npcSlot || sp.TargetMap != mapNum) continue;

            sp.Target = 0;
            sp.TargetType = 0;
            sp.TargetMap = 0;
        }
    }

    /// <summary>Clears every selection of a visitor identity, for a body that has gone home or left the
    /// world.</summary>
    public void ClearSelectionsOfVisitor(int spawnMap, int spawnSlot)
    {
        for (int i = 1; i <= _players.Slots; i++)
        {
            var sp = _players[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType != 3 || sp.TargetSpawnMap != spawnMap || sp.TargetSpawnSlot != spawnSlot) continue;

            sp.Target = 0;
            sp.TargetType = 0;
            sp.TargetMap = 0;
            sp.TargetSpawnSlot = 0;
        }
    }
}
