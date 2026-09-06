using Mirage.Shared.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One map tile — a VALUE, held directly in <see cref="MapRecord.Tile"/>.
///
/// <para>The whole tile lives in the map's array: the three art stacks are inline (see
/// <see cref="GroundStack"/>), so a map of any size is one allocation rather than four per tile, and the
/// tiles a loop walks are contiguous in memory instead of scattered pointers.</para>
///
/// <para><b>Nothing here has a setter.</b> Every field is init-only, so a tile is changed by producing a
/// new one — <c>map.Tile[x, y] = map.Tile[x, y] with { Type = TileType.Blocked }</c> — and storing it back.
/// That is deliberate: a mutable value type read into a local (<c>var t = map.Tile[x, y]; t.Type = ...</c>)
/// would silently modify the copy and leave the map untouched, which compiles perfectly and does nothing.
/// With no setters, every one of those is a compile error instead.</para>
///
/// <para>Three visual stacks: <see cref="Ground"/> draws below all entities, <see cref="Fringe"/> between
/// the ground- and fringe-layer entity passes (the walkable top of a bridge where a fringe layer exists),
/// and <see cref="Canopy"/> on top of everything. Index 0 is the bottom; the editor labels them 1..N; a
/// value of <see cref="LayerCell.Empty"/> means unused.</para>
///
/// <para>Gameplay attributes are per LOGICAL layer: the inline <see cref="Type"/> and the fields below
/// govern the ground layer, while <see cref="FringeAttr"/> (non-null iff a walkable fringe layer exists
/// here) governs the fringe layer. Read both uniformly via LayerLogic.AttrFor.</para>
///
/// <para>The attribute fields are field-for-field identical to <see cref="Records.FringeAttr"/> and
/// <see cref="TileAttr"/>; see <see cref="TileAttr"/> for what each one means, and
/// <see cref="TileAttrRules"/> for which apply to which <see cref="TileType"/>.</para>
///
/// <para>Serialized by <see cref="TileRecordConverter"/>.</para>
/// </summary>
[JsonConverter(typeof(TileRecordConverter))]
public record struct TileRecord
{
    private readonly GroundStack _ground;
    private readonly FringeStack _fringe;
    private readonly CanopyStack _canopy;

    public TileRecord() { }

    // Copies `from`, then replaces one stack's art. Private because the only way in is the With* methods:
    // a readonly field is writable in a constructor and nowhere else, which is what keeps the art immutable
    // without making it awkward to author.
    private TileRecord(in TileRecord from, LayerType type, ReadOnlySpan<int> art)
    {
        this = from;
        switch (type)
        {
            case LayerType.Ground:
                for (int i = 0; i < Constants.MaxGroundLayers; i++) _ground[i] = i < art.Length ? art[i] : LayerCell.Empty;
                break;
            case LayerType.Fringe:
                for (int i = 0; i < Constants.MaxFringeLayers; i++) _fringe[i] = i < art.Length ? art[i] : LayerCell.Empty;
                break;
            default:
                for (int i = 0; i < Constants.MaxCanopyLayers; i++) _canopy[i] = i < art.Length ? art[i] : LayerCell.Empty;
                break;
        }
    }

    // Copies `from`, then replaces one cell of one stack.
    private TileRecord(in TileRecord from, LayerType type, int index, int cell)
    {
        this = from;
        switch (type)
        {
            case LayerType.Ground when (uint)index < Constants.MaxGroundLayers: _ground[index] = cell; break;
            case LayerType.Fringe when (uint)index < Constants.MaxFringeLayers: _fringe[index] = cell; break;
            case LayerType.Canopy when (uint)index < Constants.MaxCanopyLayers: _canopy[index] = cell; break;
        }
    }

    // ── Art ───────────────────────────────────────────────────────────────────

    // [UnscopedRef]: the span points into the tile itself. Reading it off a tile that lives in a map array
    // is exactly the intent; reading it off a short-lived local is the caller's business, as with any span.

    /// <summary>The ground art stack. Read-only: change it with <see cref="WithArt"/> or
    /// <see cref="WithCell"/>.</summary>
    [UnscopedRef] public readonly ReadOnlySpan<int> Ground => _ground;

    /// <inheritdoc cref="Ground"/>
    [UnscopedRef] public readonly ReadOnlySpan<int> Fringe => _fringe;

    /// <inheritdoc cref="Ground"/>
    [UnscopedRef] public readonly ReadOnlySpan<int> Canopy => _canopy;

    /// <summary>One layer stack by type, so a caller that already has a <see cref="LayerType"/> does not
    /// have to switch on it.</summary>
    [UnscopedRef] public readonly ReadOnlySpan<int> Art(LayerType type) => type switch
    {
        LayerType.Ground => Ground,
        LayerType.Fringe => Fringe,
        _ => Canopy,
    };

    /// <summary>How deep a given stack is.</summary>
    public static int Depth(LayerType type) => type switch
    {
        LayerType.Ground => Constants.MaxGroundLayers,
        LayerType.Fringe => Constants.MaxFringeLayers,
        _ => Constants.MaxCanopyLayers,
    };

    /// <summary>A copy with one stack's art replaced whole. Extra cells are dropped and missing ones are
    /// left empty, so the stack is always exactly its own depth.</summary>
    public readonly TileRecord WithArt(LayerType type, ReadOnlySpan<int> art) => new(in this, type, art);

    /// <summary>A copy with one cell of one stack replaced. An index outside the stack changes nothing.</summary>
    public readonly TileRecord WithCell(LayerType type, int index, int cell) => new(in this, type, index, cell);

    /// <summary>A copy with every stack cleared.</summary>
    public readonly TileRecord WithNoArt() =>
        WithArt(LayerType.Ground, []).WithArt(LayerType.Fringe, []).WithArt(LayerType.Canopy, []);

    /// <summary>True when no stack holds any art.</summary>
    public readonly bool HasNoArt
    {
        get
        {
            foreach (int cell in Ground) if (!LayerCell.IsEmpty(cell)) return false;
            foreach (int cell in Fringe) if (!LayerCell.IsEmpty(cell)) return false;
            foreach (int cell in Canopy) if (!LayerCell.IsEmpty(cell)) return false;
            return true;
        }
    }

    // ── The ground layer's gameplay attribute ─────────────────────────────────

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

    // LayerRamp. Authored on the FRINGE attribute in practice — a ramp is a fringe-plane surface — but
    // carried here too so the two attribute sets stay identical and nothing has to special-case which
    // plane it was read from.
    public Direction RampGroundSide { get; init; }

    /// <summary>The tile's fringe plane, or null when it has none. A reference, and immutable: change it
    /// with <c>tile with { FringeAttr = fa with { ... } }</c>.</summary>
    public FringeAttr? FringeAttr { get; init; }

    // ── Attribute conversion ──────────────────────────────────────────────────

    /// <summary>A copy whose ground attribute is <paramref name="a"/>, normalized as it lands so the tile
    /// never keeps a previous type's fields. The editor's paint path writes through here.</summary>
    public readonly TileRecord WithGroundAttr(TileAttr a) => (this with
    {
        Type = a.Type,
        WarpMap = a.WarpMap, WarpX = a.WarpX, WarpY = a.WarpY, WarpLayer = a.WarpLayer,
        ItemNum = a.ItemNum, ItemQuantity = a.ItemQuantity, ItemRespawnSecs = a.ItemRespawnSecs,
        KeyItemNum = a.KeyItemNum, KeyIsConsumed = a.KeyIsConsumed,
        DoorX = a.DoorX, DoorY = a.DoorY, DoorLayer = a.DoorLayer,
        RampGroundSide = a.RampGroundSide,
        BlocksLight = a.BlocksLight, BlocksSight = a.BlocksSight,
    }).Normalized();

    /// <summary>A copy with every ground-attribute field this <see cref="Type"/> does not use zeroed. Does
    /// NOT touch <see cref="FringeAttr"/>, which normalizes itself.</summary>
    public readonly TileRecord Normalized() => TileAttrRules.Normalize(this);

    /// <summary>The ground layer's resolved attribute.</summary>
    public readonly TileAttr ToGroundAttr() => new()
    {
        Type = Type,
        WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
        ItemNum = ItemNum, ItemQuantity = ItemQuantity, ItemRespawnSecs = ItemRespawnSecs,
        KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
        DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
        RampGroundSide = RampGroundSide,
        BlocksLight = BlocksLight, BlocksSight = BlocksSight,
    };

    // ── Equality ──────────────────────────────────────────────────────────────
    // Written out rather than synthesized. An inline-array field has ONE field of its own, so the
    // compiler's memberwise comparison would look at the first layer of each stack and call it a day —
    // two tiles differing only above layer 1 would read as equal, which the sparse map wire relies on
    // being false.

    public readonly bool Equals(TileRecord other) =>
        Type == other.Type
        && WarpMap == other.WarpMap && WarpX == other.WarpX && WarpY == other.WarpY && WarpLayer == other.WarpLayer
        && ItemNum == other.ItemNum && ItemQuantity == other.ItemQuantity && ItemRespawnSecs == other.ItemRespawnSecs
        && KeyItemNum == other.KeyItemNum && KeyIsConsumed == other.KeyIsConsumed
        && DoorX == other.DoorX && DoorY == other.DoorY && DoorLayer == other.DoorLayer
        && BlocksLight == other.BlocksLight && BlocksSight == other.BlocksSight
        && RampGroundSide == other.RampGroundSide
        && Equals(FringeAttr, other.FringeAttr)
        && Ground.SequenceEqual(other.Ground)
        && Fringe.SequenceEqual(other.Fringe)
        && Canopy.SequenceEqual(other.Canopy);

    public readonly override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Type);
        h.Add(WarpMap); h.Add(WarpX); h.Add(WarpY); h.Add(WarpLayer);
        h.Add(ItemNum); h.Add(ItemQuantity); h.Add(ItemRespawnSecs);
        h.Add(KeyItemNum); h.Add(KeyIsConsumed);
        h.Add(DoorX); h.Add(DoorY); h.Add(DoorLayer);
        h.Add(BlocksLight); h.Add(BlocksSight);
        h.Add(RampGroundSide);
        h.Add(FringeAttr);
        foreach (int cell in Ground) h.Add(cell);
        foreach (int cell in Fringe) h.Add(cell);
        foreach (int cell in Canopy) h.Add(cell);
        return h.ToHashCode();
    }
}
