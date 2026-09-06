using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>
/// One blood pool: a size×size tile RECTANGLE (top-left at X,Y in 0-based map-local coords) with a shared
/// stain amount.  Pools are the unit of blood — a size-1 hit drops a 1×1 pool, a big NPC drops a
/// footprint-sized one — and they overlap freely.  <see cref="Amount"/> (capped at
/// <see cref="Constants.BloodMaxTileAmount"/>) drives the decal's size/opacity; <see cref="Peak"/> is the amount
/// at the last deposit, so freshness = Amount/Peak is the client's opacity.
/// </summary>
public sealed class BloodPool
{
    public int X;
    public int Y;
    public int Size;
    // Two-layer world: which logical layer this pool is on.  Pools only merge with same-layer pools, and
    // fringe-layer blood draws over ground-layer blood (a bridge deck bleeds on top, the ground beneath separately).
    public WorldLayer Layer;
    public float Amount;
    public float Peak;

    public int Right => X + Size - 1;
    public int Bottom => Y + Size - 1;
}

/// <summary>
/// Per-map server-side blood state: a LIST of overlapping <see cref="BloodPool"/> rectangles (no per-tile grid).
/// A deposit adds/feeds/merges pools (<see cref="GameLogic.BloodSystem"/>) and sets <see cref="Dirty"/>; the
/// game-loop tick decays every pool and, when Dirty, broadcasts the map's WHOLE current pool list (full-list
/// replace — so a merged-away pool simply drops out, no per-pool removal wire).  Decay never sets Dirty: each
/// client replays the same linear decay locally, so a purely-fading map costs zero bandwidth.
/// <para>Invariant (maintained by BloodSystem): no pool is ever fully contained inside another — overlapping
/// pools only ever partially overlap.</para>
/// </summary>
public sealed class BloodField
{
    public List<BloodPool> Pools { get; } = new();
    public bool Dirty;   // a deposit/merge changed the list since the last broadcast
}
