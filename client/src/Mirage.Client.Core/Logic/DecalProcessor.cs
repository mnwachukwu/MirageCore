using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Client-side stain drying.
///
/// <para>The server is authoritative and sends a whole-list replace whenever a map's stains CHANGE
/// (<c>DecalUpdatePacket</c>). Between those, each client replays the SAME linear fade locally from the same
/// <see cref="Constants.DecalDryingPerSec"/>, so a drying map costs zero network and stays smooth per-frame.
/// A stain is dropped the moment it dries, exactly as the server drops it, so both sides converge with no
/// removal wire.</para>
///
/// <para>Mirrors <see cref="AnimationProcessor"/>'s static-processor shape; called once per frame.</para>
/// </summary>
public static class DecalProcessor
{
    public static void Process(ClientState state, float dtSec)
    {
        if (dtSec <= 0f || state.DecalsByMap.Count == 0) return;
        float dry = Constants.DecalDryingPerSec * dtSec;

        List<int>? emptyMaps = null;
        foreach (var (mapNum, decals) in state.DecalsByMap)
        {
            for (int i = decals.Count - 1; i >= 0; i--)
            {
                var d = decals[i];
                float before = d.Amount;
                float after = MathF.Max(0f, before - dry);
                if (after <= Constants.DecalVisibleEpsilon)
                {
                    decals.RemoveAt(i);
                    continue;
                }

                // Amount (SIZE) and Freshness (OPACITY) fade PROPORTIONALLY, so both reach the floor together
                // — a stain shrinks and pales in lockstep, never lingering present but invisible.
                d.Freshness *= after / before;
                d.Amount = after;
            }

            if (decals.Count == 0) (emptyMaps ??= new()).Add(mapNum);
        }

        if (emptyMaps is not null)
            foreach (int m in emptyMaps) state.DecalsByMap.Remove(m);
    }
}
