using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Client-side blood-pool decay.  The server is authoritative and sends deposit events (<c>BloodUpdatePacket</c>,
/// a full-list replace of a map's pools); between events each client replays the SAME linear decay locally, so a
/// fading pool costs zero network and stays smooth per-frame.  Uses the shared
/// <see cref="Constants.BloodDissipationPerSec"/> so client and server agree, and drops a pool the moment it
/// dries — exactly as the server does — so both sides converge without a removal wire.  Mirrors
/// <see cref="AnimationProcessor"/>'s static-processor shape; called from <c>GameplayScreen.Update</c>.
/// </summary>
public static class BloodProcessor
{
    public static void Process(ClientState state, float dtSec)
    {
        if (dtSec <= 0f || state.BloodByMap.Count == 0) return;
        float d = Constants.BloodDissipationPerSec * dtSec;

        List<int>? emptyMaps = null;
        foreach (var (mapNum, pools) in state.BloodByMap)
        {
            for (int i = pools.Count - 1; i >= 0; i--)
            {
                var p = pools[i];
                float oldA = p.Amount;
                float newA = MathF.Max(0f, oldA - d);
                if (newA <= Constants.BloodVisibleEpsilon)
                {
                    pools.RemoveAt(i);
                    continue;
                }  // dried out — drop it
                // Decay Amount (drives SIZE) and fade Freshness (OPACITY) PROPORTIONALLY, so both reach the floor
                // together — a pool fades and shrinks in lockstep, never lingering invisible-but-present.
                p.Freshness *= newA / oldA;
                p.Amount = newA;
            }
            if (pools.Count == 0) (emptyMaps ??= new()).Add(mapNum);
        }
        if (emptyMaps is not null)
            foreach (int m in emptyMaps) state.BloodByMap.Remove(m);
    }
}
