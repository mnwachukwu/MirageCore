using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Advances animated HP/MP/SP display values on world-space entity bars.
/// Mirrors the snap/lerp rules from HudPanel: snap on first appearance or death, lerp otherwise.
/// Call once per frame from Update(), before RenderCommandBuilder.Build().
/// </summary>
public static class WorldBarAnimator
{
    private const float LerpSpeed = 5f;

    public static void Tick(ClientState state, float deltaSeconds)
    {
        float t = Math.Min(1f, LerpSpeed * deltaSeconds);
        long now = Environment.TickCount64;

        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var n = state.MapNpcs[i];
            if (n.Num == 0 || n.MaxHp <= 0) continue;
            TickNpc(n, t, now);
        }

        // Neighbor-map NPCs animate their bars too (else DispHp stays unset and bars won't show).
        // Skip cells with no loaded map — their NPC arrays are empty (no bars to advance).
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
                    if (n.Num == 0 || n.MaxHp <= 0) continue;
                    TickNpc(n, t, now);
                }
            }
        }

        // Visiting (chasing) NPCs animate their HP bar the same way.
        foreach (var tn in state.TraversalNpcs.Values)
        {
            if (tn.Num == 0 || tn.MaxHp <= 0) continue;
            TickNpc(tn, t, now);
        }

        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            var p = state.Players[i];
            if (string.IsNullOrEmpty(p.Name) || p.MaxHp <= 0) continue;
            TickPlayer(p, t, now);
        }
    }

    private static void TickNpc(ClientMapNpc n, float t, long now)
    {
        bool snap = n.DispHp < 0f;
        // Hold the HP bar while a deferred spell hit is in flight, so it drops in sync with the visible bolt.
        float targetHp = now < n.BarHoldUntilMs ? n.DispHp : Frac(n.Hp, n.MaxHp);
        float targetMp = Frac(n.Mp, n.MaxMp);
        float targetSp = Frac(n.Sp, n.MaxSp);
        if (snap)
        {
            n.DispHp = targetHp;
            n.DispMp = targetMp;
            n.DispSp = targetSp;
            return;
        }
        n.DispHp = LerpFrac(n.DispHp, targetHp, t);
        n.DispMp = LerpFrac(n.DispMp, targetMp, t);
        n.DispSp = LerpFrac(n.DispSp, targetSp, t);
    }

    private static void TickPlayer(PlayerRecord p, float t, long now)
    {
        bool snap = p.DispHp < 0f || p.SnapVitals;
        p.SnapVitals = false;
        // Hold the HP bar while a deferred spell hit is in flight, so it drops in sync with the visible bolt.
        float targetHp = now < p.BarHoldUntilMs ? p.DispHp : Frac(p.Hp, p.MaxHp);
        float targetMp = Frac(p.Mp, p.MaxMp);
        float targetSp = Frac(p.Sp, p.MaxSp);
        if (snap)
        {
            p.DispHp = targetHp;
            p.DispMp = targetMp;
            p.DispSp = targetSp;
            return;
        }
        p.DispHp = LerpFrac(p.DispHp, targetHp, t);
        p.DispMp = LerpFrac(p.DispMp, targetMp, t);
        p.DispSp = LerpFrac(p.DispSp, targetSp, t);
    }

    private static float LerpFrac(float disp, float target, float t)
    {
        if (target < 0f) return -1f;
        float next = disp + (target - disp) * t;
        // Snap when sub-pixel close so the bar doesn't show a ghost sliver at full/empty.
        return MathF.Abs(next - target) < 0.002f ? target : next;
    }

    private static float Frac(int cur, int max) =>
        max > 0 ? Math.Clamp((float)cur / max, 0f, 1f) : -1f;
}
