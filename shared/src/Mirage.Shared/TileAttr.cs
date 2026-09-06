namespace Mirage.Shared;

/// <summary>
/// A tile's gameplay attribute on one layer: what the tile IS, plus the fields that kind of tile uses.
///
/// <para>What <see cref="LayerLogic.AttrFor"/> returns — the resolved, read-only answer to "what governs
/// an entity standing here, on this layer". Distinct from <see cref="Records.FringeAttr"/> and the
/// tile's own inline attribute, which carry the same values but are STORAGE with their own persistence
/// contracts. All three share this field set exactly; a value read here means what it means there.</para>
///
/// <para>Every slot is a named, typed field rather than a positional <c>Data1/2/3</c> decoded per
/// <see cref="TileType"/>, so no call site does bit math or compares against a magic number to work out
/// what a value means.</para>
/// </summary>
public readonly record struct TileAttr
{
    public TileType Type { get; init; }

    // ── Warp: where stepping onto this tile sends you ───────────────────────────────────────────
    public short WarpMap { get; init; }
    public ushort WarpX { get; init; }
    public ushort WarpY { get; init; }
    /// <summary>Which plane the warp delivers onto — a warp can put you up on a bridge deck.</summary>
    public WorldLayer WarpLayer { get; init; }

    // ── Item: what lies on this tile ────────────────────────────────────────────────────────────
    public short ItemNum { get; init; }
    /// <summary>Stack size for a Currency item; 1 for anything else.</summary>
    public short ItemQuantity { get; init; }
    /// <summary>Seconds before it returns after being taken. 0 = use
    /// <see cref="Constants.DefaultItemRespawnSeconds"/>.</summary>
    public short ItemRespawnSecs { get; init; }

    // ── Key: a locked door ──────────────────────────────────────────────────────────────────────
    /// <summary>The item that opens it, by item number.</summary>
    public short KeyItemNum { get; init; }
    /// <summary>Whether opening it consumes the key.</summary>
    public bool KeyIsConsumed { get; init; }

    // ── KeyOpen: a pressure plate that opens a door elsewhere ───────────────────────────────────
    public ushort DoorX { get; init; }
    public ushort DoorY { get; init; }
    /// <summary>Which plane the door sits on, so a ground plate can open a door up on the deck.</summary>
    public WorldLayer DoorLayer { get; init; }

    // ── Blocked: what the wall stops ────────────────────────────────────────────────────────────
    /// <summary>Whether light stops here. A solid wall does; a railing, a window or a shallow ledge
    /// does not, and the lamp behind it still lights the ground in front.</summary>
    public bool BlocksLight { get; init; }
    /// <summary>Whether sight stops here — spell line-of-sight, and everything that mirrors it. Separate
    /// from <see cref="BlocksLight"/>: a stained window stops neither, a curtain stops sight alone.</summary>
    public bool BlocksSight { get; init; }

    // ── LayerRamp: the connector between the two planes ─────────────────────────────────────────
    /// <summary>The ground side — the direction you mount the ramp from.</summary>
    public Direction RampGroundSide { get; init; }

    /// <summary>The fringe plane is uniform and open by default, so a tile with no fringe attribute
    /// reads as this rather than as "no attribute".</summary>
    public static readonly TileAttr Walkable = new() { Type = TileType.Walkable };

    /// <summary>What a ramp reads as from the ground layer: a solid understructure, not a hole.</summary>
    public static readonly TileAttr Blocked =
        new() { Type = TileType.Blocked, BlocksLight = true, BlocksSight = true };
}
