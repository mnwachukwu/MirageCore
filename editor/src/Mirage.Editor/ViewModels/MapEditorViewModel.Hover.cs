using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The hovered tile's exploded read-out: one preview cell per layer in each stack, both
/// planes' attributes, and whatever light or NPC-spawn pin the tile carries.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    public bool IsHovering => HoveredX >= 0 && SelectedMap is not null;

    private TileRecord? HoveredTileRecord =>
        InMapBounds(HoveredX, HoveredY) &&
        SelectedMap is not null
            ? SelectedMap.Record.Tile[HoveredX, HoveredY]
            : null;

    // Exploded preview of the hovered tile: every layer of each layer type, one column per type.
    public IReadOnlyList<HoveredLayerPreview> HoveredGroundLayers => BuildHoveredLayers(LayerType.Ground);
    public IReadOnlyList<HoveredLayerPreview> HoveredFringeLayers => BuildHoveredLayers(LayerType.Fringe);
    public IReadOnlyList<HoveredLayerPreview> HoveredCanopyLayers => BuildHoveredLayers(LayerType.Canopy);

    private IReadOnlyList<HoveredLayerPreview> BuildHoveredLayers(LayerType type)
    {
        var t = HoveredTileRecord;
        int count = MaxLayersOf(type);
        var list = new HoveredLayerPreview[count];
        for (int i = 0; i < count; i++)
        {
            int packed = CellAt(t, type, i);
            int sheet = LayerCell.Sheet(packed);
            int tileIndex = LayerCell.Tile(packed);
            Bitmap? bmp = sheet >= 0 && sheet < Tilesets.Count ? Tilesets[sheet] : null;
            // Sheet index shown beside the tile so the source sheet is identifiable; blank when empty.
            string sheetText = tileIndex > 0 ? sheet.ToString() : "";
            // Star-mark animated layers so a tile's anim state is visible at a glance in the hover preview.
            string label = LayerCell.Anim(packed) ? $"{i + 1}*" : $"{i + 1}";
            // A hidden layer is still ON the tile, so it is still listed — dimmed rather than dropped.
            // Omitting it is how somebody concludes a layer is empty and paints over what is there.
            list[i] = new HoveredLayerPreview(label, bmp, tileIndex, sheetText,
                IsHidden: !LayerVisibility.IsVisible(type, i));
        }
        return list;
    }

    public TileType HoveredAttrType => HoveredTileRecord?.Type ?? TileType.Walkable;
    public TileAttr HoveredGroundAttr => HoveredTileRecord?.ToGroundAttr() ?? TileAttr.Walkable;

    // The Fringe plane's attribute (FringeAttr) — the walkable bridge-top layer's own Blocked/Warp/Item/etc.,
    // including LayerRamp (always authored here, never on the ground's inline Type). Read alongside the ground
    // attribute so the hover preview shows BOTH logical planes, not just Ground.
    public TileType HoveredFringeAttrType => HoveredTileRecord?.FringeAttr?.Type ?? TileType.Walkable;
    public TileAttr HoveredFringeAttr => HoveredTileRecord?.FringeAttr?.ToAttr() ?? TileAttr.Walkable;

    public string HoveredGroundAttributeText => EditorStrings.Format(EditorStrings.MapEditor_AttrLabel,
        ("Layer", EditorVocabulary.NameOf(WorldLayer.Ground)),
        ("Value", FormatAttributeText(HoveredGroundAttr)));
    public string HoveredFringeAttributeText => EditorStrings.Format(EditorStrings.MapEditor_AttrLabel,
        ("Layer", EditorVocabulary.NameOf(WorldLayer.Fringe)),
        ("Value", FormatAttributeText(HoveredFringeAttr)));

    // Placed-light info for the hovered tile, shown in the Shift exploded-tile preview so a tile's whole
    // definition — layers, attribute, AND any light — is visible in one place.
    private PlacedLight? HoveredLight =>
        InMapBounds(HoveredX, HoveredY) && SelectedMap is not null
            ? LightAt(SelectedMap.Record, HoveredX, HoveredY, SelectedAttributeLayer)
            : null;
    public string HoveredLightText => HoveredLight is { } pl
        ? EditorStrings.Format(EditorStrings.MapEditor_LightText,
            ("Color", (pl.Light.Rgb & 0xFFFFFF).ToString("X6")),
            ("Radius", pl.Light.Radius),
            ("Intensity", (int)Math.Round(pl.Light.Intensity * 100)),
            ("Flicker", pl.Light.Flicker))
        : EditorStrings.Get(EditorStrings.MapEditor_LightText_None);
    public bool HoveredHasLight => HoveredLight is not null;
    // Rendered swatch of the hovered light's color (null when the tile has no light).
    public IBrush? HoveredLightBrush => HoveredLight is { } pl ? new SolidColorBrush(ColorHex.ToColor(pl.Light.Rgb)) : null;

    // NpcSpawn has no TileType (it writes a MapRecord.Npcs entry's pin, not tile.Type/FringeAttr — see
    // AttributeTool), so the hover preview surfaces it separately from the Ground/Fringe attribute lines.
    private int? HoveredNpcSpawnIndex =>
        InMapBounds(HoveredX, HoveredY) && SelectedMap is not null
            ? EntryPinnedAt(SelectedMap.Record, HoveredX, HoveredY, SelectedAttributeLayer)
            : null;
    public bool HoveredHasNpcSpawn => HoveredNpcSpawnIndex is not null;
    public string HoveredNpcSpawnText => HoveredNpcSpawnIndex is int i
        ? EditorStrings.Format(EditorStrings.MapEditor_AttrText_NpcSpawn,
            ("Name", EditorVocabulary.NameOf(AttributeTool.NpcSpawn)),
            ("Npc", NpcEntryForRow(i)?.Name ?? $"#{SelectedMap!.Record.Npcs[i].Npc}"))
        : "";

    // The attribute's NAME comes from EditorVocabulary (English in every language, matching the tool
    // dropdown and the map files); only the surrounding phrasing and the value labels are translated.
    private string FormatAttributeText(TileAttr a) => FormatAttributeText(a.Type, a);

    private string FormatAttributeText(TileType type, TileAttr a) => type switch
    {
        // Not a name but a statement that the tile carries no attribute, so this one stays localized.
        TileType.Walkable => EditorStrings.Get(EditorStrings.MapEditor_AttrText_None),
        // 🔴 What a wall stops is stated ALWAYS, including when the answer is nothing. The two flags are
        // invisible on the map — the fringe frame bounding the upper plane is Blocked with BOTH off and
        // reads identically to solid rock — and this is the exploded view, whose whole job is to be
        // explicit. Saying it only when it deviates from the default leaves the reader inferring silence.
        TileType.Blocked => EditorStrings.Format(EditorStrings.MapEditor_AttrText_BlockedStops,
            ("Name", EditorVocabulary.NameOf(type)),
            ("Stops", EditorStrings.Get((a.BlocksLight, a.BlocksSight) switch
            {
                (true, true) => EditorStrings.MapEditor_Blocked_LightAndSight,
                (true, false) => EditorStrings.MapEditor_Blocked_Light,
                (false, true) => EditorStrings.MapEditor_Blocked_Sight,
                _ => EditorStrings.MapEditor_Blocked_Nothing,
            }))),
        TileType.NpcAvoid => EditorVocabulary.NameOf(type),
        TileType.Warp => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Warp,
            ("Name", EditorVocabulary.NameOf(type)), ("Map", MapLabel(a.WarpMap)), ("X", a.WarpX), ("Y", a.WarpY)),
        TileType.Item => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Item,
            ("Name", EditorVocabulary.NameOf(type)), ("Item", ItemLabel(a.ItemNum)), ("Qty", a.ItemQuantity),
            ("Respawn", a.ItemRespawnSecs == 0
                ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_RespawnDefault)
                : EditorStrings.Format(EditorStrings.MapEditor_AttrText_RespawnSeconds, ("Seconds", a.ItemRespawnSecs)))),
        TileType.Key => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Key,
            ("Name", EditorVocabulary.NameOf(type)), ("Item", ItemLabel(a.KeyItemNum)),
            ("Action", a.KeyIsConsumed
                ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_KeyTake)
                : EditorStrings.Get(EditorStrings.MapEditor_AttrText_KeyKeep))),
        TileType.KeyOpen => EditorStrings.Format(EditorStrings.MapEditor_AttrText_KeyOpen,
            ("Name", EditorVocabulary.NameOf(type)), ("X", a.DoorX), ("Y", a.DoorY)),
        TileType.LayerRamp => EditorStrings.Format(EditorStrings.MapEditor_AttrText_LayerRamp,
            ("Name", EditorVocabulary.NameOf(type)), ("Direction", EditorVocabulary.NameOf(a.RampGroundSide))),
        _ => EditorVocabulary.NameOf(type),
    };

    private string ItemLabel(short id) => id <= 0
        ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_None)
        : (id < _data.LiveItemEntries.Length ? _data.LiveItemEntries[id].Name : $"#{id}");
    private string MapLabel(short id) => id <= 0
        ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_None)
        : (id < _data.LiveMapEntries.Length ? _data.LiveMapEntries[id].Name : $"#{id}");

    private void NotifyHoveredTile()
    {
        OnPropertyChanged(nameof(IsHovering));
        OnPropertyChanged(nameof(HoveredGroundLayers));
        OnPropertyChanged(nameof(HoveredFringeLayers));
        OnPropertyChanged(nameof(HoveredCanopyLayers));
        OnPropertyChanged(nameof(HoveredAttrType));
        OnPropertyChanged(nameof(HoveredGroundAttr));
        OnPropertyChanged(nameof(HoveredFringeAttrType));
        OnPropertyChanged(nameof(HoveredFringeAttr));
        OnPropertyChanged(nameof(HoveredGroundAttributeText));
        OnPropertyChanged(nameof(HoveredFringeAttributeText));
        OnPropertyChanged(nameof(HoveredHasNpcSpawn));
        OnPropertyChanged(nameof(HoveredNpcSpawnText));
        OnPropertyChanged(nameof(HoveredLightText));
        OnPropertyChanged(nameof(HoveredHasLight));
        OnPropertyChanged(nameof(HoveredLightBrush));
        OnPropertyChanged(nameof(HoveredHasInboundWarps));
        OnPropertyChanged(nameof(HoveredInboundWarpText));
    }
}
