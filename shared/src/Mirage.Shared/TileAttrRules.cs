using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Which tile fields apply to which <see cref="TileType"/>, stated once. The editor asks it what to
/// show, <c>Normalize</c> asks it what to clear, and the serializer asks it what to write — so a field
/// hidden in the form is exactly a field absent from the file.
///
/// <para>This is the half that makes named fields honest rather than merely readable. Repaint a Warp
/// tile as a Key and its destination map would otherwise sit on the record forever: invisible in the
/// editor, still in the file, and live again the moment anything set it back to Warp. Items and spells
/// carry the same rule for the same reason.</para>
/// </summary>
public static class TileAttrRules
{
    public static bool UsesWarp(TileType type) => type is TileType.Warp;
    public static bool UsesItem(TileType type) => type is TileType.Item;
    public static bool UsesKey(TileType type) => type is TileType.Key;
    public static bool UsesDoor(TileType type) => type is TileType.KeyOpen;
    public static bool UsesRamp(TileType type) => type is TileType.LayerRamp;
    /// <summary>Blocked carries what the wall stops. Every other kind of tile stops nothing, so the two
    /// fields are only meaningful here.</summary>
    public static bool UsesBlocked(TileType type) => type is TileType.Blocked;

    /// <summary>Whether this kind of tile carries any authored field at all — Walkable and NpcAvoid are
    /// pure attributes with nothing to configure.</summary>
    public static bool UsesAnyField(TileType type) =>
        UsesWarp(type) || UsesItem(type) || UsesKey(type) || UsesDoor(type) || UsesRamp(type)
        || UsesBlocked(type);

    /// <summary>A copy of <paramref name="t"/> with every ground-attribute field its <see cref="TileType"/>
    /// does not use cleared, so a retyped tile cannot keep the previous kind's numbers. Leaves the art and
    /// the fringe plane alone.</summary>
    public static TileRecord Normalize(TileRecord t) => t with
    {
        WarpMap = UsesWarp(t.Type) ? t.WarpMap : (short)0,
        WarpX = UsesWarp(t.Type) ? t.WarpX : (ushort)0,
        WarpY = UsesWarp(t.Type) ? t.WarpY : (ushort)0,
        WarpLayer = UsesWarp(t.Type) ? t.WarpLayer : default,
        ItemNum = UsesItem(t.Type) ? t.ItemNum : (short)0,
        ItemQuantity = UsesItem(t.Type) ? t.ItemQuantity : (short)0,
        ItemRespawnSecs = UsesItem(t.Type) ? t.ItemRespawnSecs : (short)0,
        KeyItemNum = UsesKey(t.Type) ? t.KeyItemNum : (short)0,
        KeyIsConsumed = UsesKey(t.Type) && t.KeyIsConsumed,
        DoorX = UsesDoor(t.Type) ? t.DoorX : (ushort)0,
        DoorY = UsesDoor(t.Type) ? t.DoorY : (ushort)0,
        DoorLayer = UsesDoor(t.Type) ? t.DoorLayer : default,
        RampGroundSide = UsesRamp(t.Type) ? t.RampGroundSide : default,
        // Reset to stopping everything, so a tile that is not a wall carries no permission a wall would honour.
        BlocksLight = !UsesBlocked(t.Type) || t.BlocksLight,
        BlocksSight = !UsesBlocked(t.Type) || t.BlocksSight,
    };

    /// <inheritdoc cref="Normalize(TileRecord)"/>
    public static FringeAttr Normalize(FringeAttr a) => a with
    {
        WarpMap = UsesWarp(a.Type) ? a.WarpMap : (short)0,
        WarpX = UsesWarp(a.Type) ? a.WarpX : (ushort)0,
        WarpY = UsesWarp(a.Type) ? a.WarpY : (ushort)0,
        WarpLayer = UsesWarp(a.Type) ? a.WarpLayer : default,
        ItemNum = UsesItem(a.Type) ? a.ItemNum : (short)0,
        ItemQuantity = UsesItem(a.Type) ? a.ItemQuantity : (short)0,
        ItemRespawnSecs = UsesItem(a.Type) ? a.ItemRespawnSecs : (short)0,
        KeyItemNum = UsesKey(a.Type) ? a.KeyItemNum : (short)0,
        KeyIsConsumed = UsesKey(a.Type) && a.KeyIsConsumed,
        DoorX = UsesDoor(a.Type) ? a.DoorX : (ushort)0,
        DoorY = UsesDoor(a.Type) ? a.DoorY : (ushort)0,
        DoorLayer = UsesDoor(a.Type) ? a.DoorLayer : default,
        RampGroundSide = UsesRamp(a.Type) ? a.RampGroundSide : default,
        BlocksLight = !UsesBlocked(a.Type) || a.BlocksLight,
        BlocksSight = !UsesBlocked(a.Type) || a.BlocksSight,
    };
}
