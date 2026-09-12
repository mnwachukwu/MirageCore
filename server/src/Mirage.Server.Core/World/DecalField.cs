using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>
/// One stain on the ground: a size×size tile RECTANGLE (top-left at X,Y in 0-based map-local coords) carrying
/// a color and an amount.
///
/// <para><b>Core knows a stain spreads, darkens and dries; it does not know what spilled.</b> Blood, oil,
/// scorch, paint and snowmelt are one mechanism, and the world says which it is once through
/// <see cref="Records.WorldManifest.DecalColor"/> rather than the engine deciding.</para>
///
/// <para><see cref="Amount"/> (capped at <see cref="Constants.DecalMaxAmount"/>) drives the decal's size;
/// <see cref="Peak"/> is the amount at the last deposit, so freshness = Amount/Peak is the client's opacity —
/// a fresh spill on an almost-dry stain darkens it back to full and then fades again.</para>
/// </summary>
public sealed class Decal
{
    public int X;
    public int Y;
    public int Size;

    /// <summary>Two-layer world: which logical layer this stain is on. Stains only merge with same-layer
    /// stains, and a fringe-layer one draws over a ground-layer one — a bridge deck stains on top, the
    /// ground beneath separately.</summary>
    public WorldLayer Layer;

    public float Amount;
    public float Peak;

    public int Right => X + Size - 1;
    public int Bottom => Y + Size - 1;
}

/// <summary>
/// Per-map stain state: a LIST of overlapping <see cref="Decal"/> rectangles, no per-tile grid.
///
/// <para>A deposit adds, feeds or merges stains (<see cref="GameLogic.DecalSystem"/>) and sets
/// <see cref="Dirty"/>; the tick decays every stain and, when dirty, broadcasts the map's WHOLE current list.
/// A full-list replace means a merged-away stain simply drops out, with no per-stain removal wire.</para>
///
/// <para><b>Decay never sets Dirty.</b> Each client replays the same linear decay locally, so a map that is
/// only drying costs zero bandwidth — which is most maps, most of the time.</para>
///
/// <para>Invariant, maintained by <see cref="GameLogic.DecalSystem"/>: no two stains share a rectangle and
/// a layer — a deposit onto one feeds it instead.</para>
/// </summary>
public sealed class DecalField
{
    public List<Decal> Decals { get; } = new();

    /// <summary>A deposit or merge changed the list since the last broadcast.</summary>
    public bool Dirty;
}
