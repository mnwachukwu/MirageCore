using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Advances the tile animation frame counter and clears expired attack animations.
/// Drives the map-animation toggle and clears the attack pose when its timer expires.
/// </summary>
public static class AnimationProcessor
{
    private const long AttackClearMs = 1000; // the attack pose clears this long after the swing

    public static void Process(ClientState state, long nowMs)
    {
        // Tile animation: advance one frame per MapAnimIntervalMs (each frame dwells that long).
        if (nowMs - state.MapAnimTimer >= Constants.MapAnimIntervalMs)
        {
            state.MapAnimFrame++;
            state.MapAnimTimer = nowMs;
        }

        // Attack timers use Environment.TickCount64 (wall clock), matching ClientPacketHandler.
        long tickNow = Environment.TickCount64;

        // Clear expired player attack animations and spell cast lock
        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            var p = state.Players[i];
            if (p.Attacking && tickNow - p.AttackTimer >= AttackClearMs)
                p.Attacking = false;
        }

        // Clear expired NPC attack animations — center map…
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var n = state.MapNpcs[i];
            if (n.Attacking && tickNow - n.AttackTimer >= AttackClearMs)
                n.Attacking = false;
        }

        // …and the neighbor maps, so their NPCs' attack frames clear the same way.
        // Skip cells with no loaded map — their NPC arrays are empty.
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (c == 1 && r == 1) continue;
                if (state.NeighborMaps[c, r] is null) continue;
                var cell = state.NeighborNpcs[c, r];
                for (int i = 1; i <= Constants.MaxMapNpcs; i++)
                {
                    var n = cell[i];
                    if (n.Attacking && tickNow - n.AttackTimer >= AttackClearMs)
                        n.Attacking = false;
                }
            }
        }

        // …and visiting (chasing) NPCs, so their swing frame clears too.
        foreach (var t in state.TraversalNpcs.Values)
        {
            if (t.Attacking && tickNow - t.AttackTimer >= AttackClearMs)
                t.Attacking = false;
        }
    }
}
