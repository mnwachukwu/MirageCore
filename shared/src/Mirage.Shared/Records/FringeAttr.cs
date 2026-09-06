namespace Mirage.Shared.Records;

/// <summary>
/// The gameplay attribute of a tile's walkable FRINGE layer (the top of a bridge). Its PRESENCE on a
/// <see cref="TileRecord"/> means "a walkable fringe layer exists here" — distinct from the tile merely
/// carrying <see cref="TileRecord.Fringe"/> decor art (a treetop has Fringe[] art but no FringeAttr).
/// Mirrors the tile's inline ground attribute for the fringe layer; both are read uniformly through
/// LayerLogic.AttrFor, which resolves either into a <see cref="TileAttr"/>.
///
/// <para>Immutable, like the tile that holds it: change one with <c>fa with { Type = ... }</c>. A tile is
/// a value, so a mutable object hanging off it would be a hole in that — two tiles sharing one instance
/// would change together.</para>
///
/// <para>A reference type rather than a value, because most tiles have no fringe plane at all: a nullable
/// struct would sit in every tile of every map to serve the few that are bridges.</para>
///
/// <para>Field-for-field identical to <see cref="TileRecord"/>'s inline attribute and to
/// <see cref="TileAttr"/> — see the latter for what each field means and why they are named rather than
/// numbered. Kept a separate type because storage and the resolved value have different lifetimes: this
/// one is authored and persisted, that one is a snapshot handed to a movement check.</para>
/// </summary>
public sealed record FringeAttr
{
    public TileType Type { get; init; }

    // Warp — see TileAttr for the meaning of each field.
    public short WarpMap { get; init; }
    public ushort WarpX { get; init; }
    public ushort WarpY { get; init; }
    public WorldLayer WarpLayer { get; init; }

    // Item
    public short ItemNum { get; init; }
    public short ItemQuantity { get; init; }
    public short ItemRespawnSecs { get; init; }

    // Key (a locked door)
    public short KeyItemNum { get; init; }
    public bool KeyIsConsumed { get; init; }

    // KeyOpen (a plate that opens a door elsewhere)
    public ushort DoorX { get; init; }
    public ushort DoorY { get; init; }
    public WorldLayer DoorLayer { get; init; }

    // Blocked — what the wall stops. Both default TRUE: a wall stops everything unless it says otherwise,
    // which is also what a map authored without these fields means.
    public bool BlocksLight { get; init; } = true;
    public bool BlocksSight { get; init; } = true;

    // LayerRamp — the side you mount from. The one field the fringe plane uses that the ground never does.
    public Direction RampGroundSide { get; init; }

    /// <summary>A copy with every field this <see cref="Type"/> does not use cleared, so a retyped plane
    /// cannot keep the previous kind's numbers — the same rule items and spells follow on save.</summary>
    public FringeAttr Normalized() => TileAttrRules.Normalize(this);

    /// <summary>Stored fringe state from a resolved attribute, normalized on the way in. The editor's
    /// paint path writes through here.</summary>
    public static FringeAttr From(TileAttr a) => (new FringeAttr
    {
        Type = a.Type,
        WarpMap = a.WarpMap, WarpX = a.WarpX, WarpY = a.WarpY, WarpLayer = a.WarpLayer,
        ItemNum = a.ItemNum, ItemQuantity = a.ItemQuantity, ItemRespawnSecs = a.ItemRespawnSecs,
        KeyItemNum = a.KeyItemNum, KeyIsConsumed = a.KeyIsConsumed,
        DoorX = a.DoorX, DoorY = a.DoorY, DoorLayer = a.DoorLayer,
        RampGroundSide = a.RampGroundSide,
        BlocksLight = a.BlocksLight, BlocksSight = a.BlocksSight,
    }).Normalized();

    /// <summary>The resolved value a movement or interaction check reads.</summary>
    public TileAttr ToAttr() => new()
    {
        Type = Type,
        WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
        ItemNum = ItemNum, ItemQuantity = ItemQuantity, ItemRespawnSecs = ItemRespawnSecs,
        KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
        DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
        RampGroundSide = RampGroundSide,
        BlocksLight = BlocksLight, BlocksSight = BlocksSight,
    };
}
