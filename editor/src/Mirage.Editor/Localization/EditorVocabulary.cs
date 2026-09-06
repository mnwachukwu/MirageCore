using Mirage.Editor.ViewModels;
using Mirage.Shared;

namespace Mirage.Editor.Localization;

/// <summary>
/// The editor's structural vocabulary — layer types, tile attributes, and the small tool enums —
/// held in English for every language.
///
/// <para>These name CODE, not content. A tile's attribute is <c>TileType.NpcAvoid</c> on the wire,
/// in the JSON map files, and in every discussion of the engine; translating the label a map author
/// clicks would give the same concept a different name in each language while the thing it refers to
/// keeps only one. That is why this is deliberately NOT in <see cref="EditorStrings"/>: prose ABOUT
/// an attribute stays translated, but its name does not.</para>
///
/// <para>The names live here rather than coming from <c>ToString()</c> because the enum identifiers
/// read poorly as UI — "NpcAvoid" and "LayerRamp" against "NPC Avoid" and "Layer Ramp".
/// <c>EditorVocabularyTests</c> asserts every member of every covered enum has an entry, so a new
/// one cannot silently fall through to the identifier.</para>
/// </summary>
public static class EditorVocabulary
{
    // Shared between the AttributeTool (what the author paints with) and TileType (what gets stored)
    // spellings of the same concept, so the two can never drift into different words.
    private const string Blocked = "Blocked";
    private const string Warp = "Warp";
    private const string ItemSpawn = "Item Spawn";
    private const string NpcAvoid = "NPC Avoid";
    private const string Key = "Key";
    private const string KeyOpen = "KeyOpen";
    private const string NpcSpawn = "NPC Spawn";
    private const string LayerRamp = "Layer Ramp";

    private const string Ground = "Ground";
    private const string Fringe = "Fringe";
    private const string Canopy = "Canopy";

    /// <summary>The attribute-painting tools, as shown in the tool dropdown.</summary>
    public static string NameOf(AttributeTool tool) => tool switch
    {
        AttributeTool.Blocked => Blocked,
        AttributeTool.Warp => Warp,
        AttributeTool.Item => ItemSpawn,
        AttributeTool.NpcAvoid => NpcAvoid,
        AttributeTool.Key => Key,
        AttributeTool.KeyOpen => KeyOpen,
        AttributeTool.NpcSpawn => NpcSpawn,
        AttributeTool.LayerRamp => LayerRamp,
        _ => tool.ToString(),
    };

    /// <summary>The stored tile attribute. <see cref="TileType.Walkable"/> is absent on purpose: it
    /// means "no attribute here", which is a phrase rather than a name, so it stays localized as
    /// <see cref="EditorStrings.MapEditor_AttrText_None"/>.</summary>
    public static string NameOf(TileType type) => type switch
    {
        TileType.Blocked => Blocked,
        TileType.Warp => Warp,
        TileType.Item => ItemSpawn,
        TileType.NpcAvoid => NpcAvoid,
        TileType.Key => Key,
        TileType.KeyOpen => KeyOpen,
        TileType.LayerRamp => LayerRamp,
        _ => type.ToString(),
    };

    /// <summary>The three visual layer stacks a tile is painted on.</summary>
    public static string NameOf(LayerType layer) => layer switch
    {
        LayerType.Ground => Ground,
        LayerType.Fringe => Fringe,
        LayerType.Canopy => Canopy,
        _ => layer.ToString(),
    };

    /// <summary>The two gameplay planes an entity can occupy — the target of attribute placement.</summary>
    public static string NameOf(WorldLayer layer) => layer switch
    {
        WorldLayer.Ground => Ground,
        WorldLayer.Fringe => Fringe,
        _ => layer.ToString(),
    };

    public static string NameOf(Direction dir) => dir switch
    {
        Direction.Up => "Up",
        Direction.Down => "Down",
        Direction.Left => "Left",
        Direction.Right => "Right",
        _ => dir.ToString(),
    };

    public static string NameOf(AnimStyle style) => style switch
    {
        AnimStyle.Cycle => "Cycle",
        AnimStyle.Pendulum => "Pendulum",
        _ => style.ToString(),
    };

    public static string NameOf(FlickerStyle style) => style switch
    {
        FlickerStyle.None => "None",
        FlickerStyle.Flame => "Flame",
        FlickerStyle.Pulse => "Pulse",
        _ => style.ToString(),
    };

    /// <summary>Dispatches an untyped value to the right overload — the entry point for the XAML
    /// converter, which sees a boxed enum out of a <c>ComboBox</c>'s items. Anything not part of the
    /// vocabulary falls through to its own <c>ToString()</c>, so binding an unrelated type is a
    /// visual oddity rather than a crash.</summary>
    public static string NameOfValue(object? value) => value switch
    {
        AttributeTool t => NameOf(t),
        TileType t => NameOf(t),
        LayerType l => NameOf(l),
        WorldLayer l => NameOf(l),
        Direction d => NameOf(d),
        AnimStyle s => NameOf(s),
        FlickerStyle s => NameOf(s),
        _ => value?.ToString() ?? "",
    };
}
