using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Client.Core.Logic;

// ── Per-layer draw commands ────────────────────────────────────────────────────

// Screen positions are FLOAT: the world layer is drawn into a supersampled target, so sub-pixel
// positions survive (rasterized at supersample granularity) for smooth scrolling, and the player —
// always exactly at the camera center — lands on an exact pixel so it never wobbles.

/// <summary>Draw one tile graphic at the given screen position. <paramref name="TileIndex"/> is the
/// 1-based index within tileset <paramref name="Sheet"/> (see <c>LayerCell</c> / <c>TileAtlas</c>).</summary>
public readonly record struct TileDrawCmd(float ScreenX, float ScreenY, int TileIndex, int Sheet);

/// <summary>Draw one item sprite from the item atlas at the given screen position.  <paramref name="Layer"/>
/// is the logical layer the item sits on, so it draws with the matching over/under entity pass.</summary>
public readonly record struct ItemDrawCmd(
    float ScreenX, float ScreenY, short Pic, WorldLayer Layer = WorldLayer.Ground, short Sheet = 0);

/// <summary>Draw a ground stain at the given screen position (tile origin).  <paramref name="Amount"/>
/// (raw, 0..DecalMaxAmount) drives the blob SIZE and droplet COUNT.  <paramref name="Freshness"/> (0..1)
/// drives OPACITY — any hit redarkens it to full, then it fades with age.  <paramref name="Seed"/> is a stable
/// per-(map,tile) hash picking the blob variant, rotation, and jitter so the tile's look never shimmers.
/// <paramref name="Size"/> is the footprint size class (1/2/3) of the NPC that bled here, so a large NPC's stain
/// draws as ONE decal scaled to its whole body (Size*32 px, centered on the footprint), not separate tile pools.</summary>
public readonly record struct DecalDrawCmd(float ScreenX, float ScreenY, float Amount, float Freshness,
                                          int Seed, int Size = 1, WorldLayer Layer = WorldLayer.Ground);

/// <summary>Draw one thing a game has marked on the ground: a pennant on the tile, optionally a ring around
/// it, a label over it, and a meter under the label.
///
/// <para>Everything past the tile is optional. <paramref name="Label"/> empty writes nothing,
/// <paramref name="Radius"/> at nothing draws no ring, and <paramref name="Ceiling"/> at nothing draws no
/// meter — so the same command covers a bare pin and a contested point with all three.</para></summary>
public readonly record struct MarkerDrawCmd(float ScreenX, float ScreenY, int Rgb, string Label,
                                            int Radius, long Value, long Ceiling,
                                            WorldLayer Layer = WorldLayer.Ground);

/// <summary>Draw one character sprite (player or NPC) at the given screen position.
/// <paramref name="AnimFrame"/> is 0 = idle/stand, 1 = walk, 2 = attack.  <paramref name="Size"/> is the
/// footprint size class 1/2/3: the sprite is drawn Size*32 px square from a Size-matched atlas, anchored at
/// (ScreenX,ScreenY) so it covers its SxS-tile footprint. Players and ordinary NPCs are 1.</summary>
public readonly record struct SpriteDrawCmd(
    float ScreenX,
    float ScreenY,
    int SpriteRow,
    int AnimFrame,
    Direction Dir,
    int Size = 1,
    WorldLayer Layer = WorldLayer.Ground,
    // Which sprite sheet SpriteRow is a row of. The size picks the folder, the sheet picks the file.
    int Sheet = 0);

/// <summary>A dead player's corpse marker: a tile-sized (32x32) red X drawn at the tile origin
/// (<see cref="ScreenX"/>/<see cref="ScreenY"/>) in place of the live sprite.</summary>
public readonly record struct CorpseDrawCmd(float ScreenX, float ScreenY, WorldLayer Layer = WorldLayer.Ground);

// FlickerStyle now lives in Mirage.Shared (shared by LightSpec + records); resolved via `using Mirage.Shared`.

/// <summary>A light emitter at the given screen position (tile origin, matching <see cref="SpriteDrawCmd"/>;
/// the shell offsets by half a tile to center the halo). Emitted with a wider cull than sprites so an entity
/// just off-screen still casts its halo into the viewport. <see cref="Intensity"/> (0..1) scales the halo
/// down where a safe-zone map light already covers the emitter — 1 in open wilderness, fading to 0 in a lit
/// town. <see cref="Rgb"/> is the packed 0xRRGGBB core color (Core stays MonoGame-free; the shell unpacks and
/// tints the white halo textures). <see cref="Radius"/> is the outer reach in px (inner core derived from it).
/// <see cref="Flicker"/> picks the core animation, seeded by the STABLE <see cref="Id"/> (per entity/effect) so
/// a light's flicker phase never jumps when the Lights list reorders.</summary>
/// <param name="TileScreenX">Screen position of the top-left of the tile <paramref name="Reach"/> was traced
/// from, NOT wherever the halo itself is being drawn. The two differ for anything mid-step or wider than a
/// tile, and the mask has to follow the trace.</param>
/// <param name="TileScreenY">The other half of that tile's position.</param>
/// <param name="ReachRadius">How far the reach masks extend from their tile, in tiles.</param>
/// <param name="Reach">Where this light reaches over its own square, row-major at
/// <c>LightOcclusion.MaskTexels(ReachRadius)</c> a side — finer than a tile, so the falloff at a wall
/// can stop clear of it. Null means everything in range: a light with nothing to hide behind, or a
/// frame built without occlusion.</param>
/// <param name="ReachInto">The same, traced from the tile a mid-step emitter is moving INTO, with
/// <paramref name="ReachBlend"/> saying how far between the two it is. Reach is answered per tile, so without
/// this the whole shadow pattern changes in one jump each time an emitter crosses a border; blending the
/// two makes it continuous. Null whenever the emitter is standing still, which keeps the second
/// trace something only moving things pay for.</param>
/// <param name="ReachBlend">0 on the tile just left, 1 on the tile being entered.</param>
public readonly record struct LightSourceCmd(
    float ScreenX, float ScreenY, float Intensity,
    uint Rgb, float Radius, FlickerStyle Flicker, int Id, float EffectiveDarkness = 0f,
    WorldLayer Layer = WorldLayer.Ground,
    float TileScreenX = 0f, float TileScreenY = 0f,
    int ReachRadius = 0,
    byte[]? Reach = null,
    byte[]? ReachInto = null,
    float IntoScreenX = 0f, float IntoScreenY = 0f,
    float ReachBlend = 0f);

/// <summary>A map-wide area light for an always-lit map cell. <see cref="ScreenX"/>/<see cref="ScreenY"/>
/// is the cell's top-left in screen space and <see cref="PxW"/>/<see cref="PxH"/> the cell's own size in
/// pixels, which is the map's size and not the viewport's. Rendered as a soft-edged box (non-flickering)
/// so a lit map stays lit at night with a little spill into the surrounding dark.</summary>
public readonly record struct MapLightCmd(float ScreenX, float ScreenY, int PxW, int PxH);

/// <summary>A bright additive glow core for magical FX (spell balls, sparkles, embers). Drawn at the
/// post-composite "glow seam" so it punches through night darkness — unlike world-RT content, which the
/// night multiply dims. <see cref="ScreenX"/>/<see cref="ScreenY"/> is the glow CENTER; <see cref="Rgb"/>
/// the packed 0xRRGGBB color; <see cref="Radius"/> the glow reach in px.</summary>
public readonly record struct GlowCmd(float ScreenX, float ScreenY, uint Rgb, float Radius);

/// <summary>
/// Draw a text string centered horizontally on ScreenX. <see cref="AlignBottom"/> switches ScreenY from
/// the top of the text to its bottom, with the renderer subtracting the measured font height.
///
/// <para><see cref="RgbOverride"/> is a packed 0xRRGGBB color superseding <see cref="ColorIndex"/> when
/// >= 0, for the guild overhead name, whose color is a free RGB rather than a palette index.
/// <see cref="LineOffset"/> shifts the text up by that many name-line-heights using the real font metrics
/// at draw time, so a caller can stack a line without knowing the font from the logic layer.</para>
///
/// <para><see cref="GuildRankWord"/> (0 = none, else a <c>GuildRank</c>) appends the localized rank word.
/// It is assembled and localized in the Shell layer, which owns the string table — only the number travels
/// on the command.</para>
/// </summary>
public readonly record struct TextDrawCmd(float ScreenX, float ScreenY, string Text, int ColorIndex,
    bool AlignBottom = false, int RgbOverride = -1, int LineOffset = 0, int GuildRankWord = 0,
    WorldLayer Layer = WorldLayer.Ground);

/// <summary>
/// Draw the tab-target indicator arrow above or below an entity's name.
/// NameY/NameAlignBottom mirror the paired TextDrawCmd so the shell can
/// compute the final pixel position after measuring the font line height.
/// OutOfRange grays the arrow when the target lies beyond the local player's
/// Pythagorean-clamped centered range (see WorldCoordHelper.IsInInteractRange).
/// NoLineOfSight grays the arrow when a Blocked tile or closed Key door sits
/// on the straight tile-line between caster and target — same "can't cast"
/// signal as OutOfRange, just a different reason.
/// </summary>
public readonly record struct TargetArrowCmd(float CenterX, float NameY, bool NameAlignBottom,
                                              bool OutOfRange = false, bool NoLineOfSight = false);

/// <summary>
/// One row of an overhead bar group: how full, and the color the loaded game declared it in.
///
/// <para>The color travels rather than being looked up, because the draw layer has no idea what this row
/// measures. A constant named for a vital would be one game's answer compiled into every game.</para>
/// </summary>
public readonly record struct BarRow(float Frac, int Rgb)
{
    /// <summary>A row that is not there: the body carries no value for it, or the game declared fewer
    /// than three bars.</summary>
    public static readonly BarRow None = new(OverheadBar.Absent, 0);

    public bool Shown => Frac >= 0f;
}

/// <summary>
/// Draw an entity's overhead bars — up to <see cref="OverheadBarSet.Max"/> rows the loaded game declared,
/// plus, as the bottom row of the same group (one shared outline), the swing/cast COOLDOWN bar. CenterX
/// is the horizontal center; TopY is the topmost row's Y.
/// <para>Rows come in the game's declared order, and a row the body has no value for is
/// <see cref="BarRow.None"/> — the rows below it move up, so a group is always as tall as what it
/// actually draws.</para>
/// <see cref="CdFrac"/> is the remaining fraction of the action cooldown (1 = just acted, 0 = ready); &lt; 0
/// omits the row entirely. It is the engine's own row, not a declared one, so it keeps its own field and
/// its own color.
/// </summary>
public readonly record struct BarDrawCmd(
    float CenterX, float TopY,
    BarRow Row0, BarRow Row1, BarRow Row2,
    float CdFrac,
    bool ShowCombatBorder,
    bool IsTarget = false,
    bool OutOfRange = false,
    int Size = 1,
    WorldLayer Layer = WorldLayer.Ground)
{
    /// <summary>The declared row at <paramref name="index"/>, for a draw loop that walks the three
    /// positions rather than naming them.</summary>
    public BarRow RowAt(int index) => index switch { 0 => Row0, 1 => Row1, _ => Row2 };

    /// <summary>How many rows this group actually draws, the cooldown included.</summary>
    public int ShownRows =>
        (Row0.Shown ? 1 : 0) + (Row1.Shown ? 1 : 0) + (Row2.Shown ? 1 : 0) + (CdFrac >= 0 ? 1 : 0);
}

/// <summary>
/// Draw a chat bubble: rounded rect with shadow + colored border + white text, centered horizontally
/// on CenterX. <see cref="AnchorY"/> pins the bottom edge of the panel (default) or the top edge when
/// <see cref="AnchorBelow"/> is true — used when the entity's name has been flipped below the sprite,
/// so the bubble drops underneath instead of stacking above. Alpha multiplies every layer for fade-out.
///
/// <para>The border is a GameColor index, or a packed <c>0xRRGGBB</c> in <see cref="BorderRgb"/> where
/// the color is not one of the palette's: a creature speaks in the color its name is drawn in, and that
/// comes from the tints a game declared.</para>
/// </summary>
public readonly record struct ChatBubbleDrawCmd(
    float CenterX, float AnchorY,
    string Text, int BorderColorIndex, float Alpha,
    bool AnchorBelow = false, int BorderRgb = -1);

// ── Render frame ──────────────────────────────────────────────────────────────

/// <summary>
/// One complete frame's worth of draw commands, split by render layer.  Two-layer ("bridge") world draw order:
/// Below[] (ground tiles) → ground-layer entities → Above[] (fringe tiles = the bridge surface) → fringe-layer
/// entities → Canopy[] (over everything) → particles → names/bars.  The per-entity commands carry a
/// <see cref="WorldLayer"/> so the world-draw filters each list into the ground pass and the fringe pass.
/// <see cref="Below"/>/<see cref="Above"/>/<see cref="Canopy"/> are layer-major (one list per layer index) so
/// each layer batches together.  The lists are allocated once and cleared (not reallocated) each frame.
/// </summary>
public sealed class RenderFrame
{
    /// <summary>Ground layer stack, drawn below entities. <c>Below[k]</c> = ground layer index k.</summary>
    public List<TileDrawCmd>[] Below { get; }
    /// <summary>Fringe layer stack — drawn on the FRINGE plane between the ground- and fringe-layer entity passes
    /// (the bridge surface where a fringe layer exists, and pervasive over-player décor elsewhere). Lit by the
    /// fringe light map under the two-light-map split. <c>Above[k]</c> = fringe layer index k.</summary>
    public List<TileDrawCmd>[] Above { get; }
    /// <summary>Canopy layer stack, drawn OVER everything (after the fringe-layer entity pass) — treetops /
    /// roofs / foliage above both logical layers. <c>Canopy[k]</c> = canopy layer index k.</summary>
    public List<TileDrawCmd>[] Canopy { get; }
    public List<ItemDrawCmd> Items { get; } = new();
    /// <summary>Ground stains, drawn below entities and above the base ground tiles.</summary>
    public List<DecalDrawCmd> Decals { get; } = new();
    public List<SpriteDrawCmd> Npcs { get; } = new();
    public List<SpriteDrawCmd> Players { get; } = new();
    /// <summary>Dead-player corpse markers (red X), drawn in the entity layer in place of their sprites.</summary>
    public List<CorpseDrawCmd> Corpses { get; } = new();
    /// <summary>Corpse name labels — drawn in the WORLD layer with the red X (below items/NPCs/players), not
    /// with the floating <see cref="Names"/> overlay, so nothing walks "over" a corpse's name.</summary>
    public List<TextDrawCmd> CorpseNames { get; } = new();
    /// <summary>What a game has marked on the ground — a pennant, a ring, a label, a meter. Drawn in the
    /// world layer so marks scroll with the map and living entities draw over them.</summary>
    public List<MarkerDrawCmd> Markers { get; } = new();
    /// <summary>Light emitters (players + NPCs) within the halo-reach of the viewport. Wider cull than
    /// <see cref="Npcs"/>/<see cref="Players"/> so off-screen entities still light the view edge.</summary>
    public List<LightSourceCmd> Lights { get; } = new();
    /// <summary>Safe-zone map cells visible this frame — each gets a map-wide non-flickering area light.</summary>
    public List<MapLightCmd> AlwaysLitMapLights { get; } = new();
    /// <summary>Map cells with AlwaysDark set — stamped as NightAmbient in the light RT regardless of time of day.</summary>
    public List<MapLightCmd> AlwaysDarkMapLights { get; } = new();
    /// <summary>Indoor map cells (non-AlwaysDark) — stamped as White in the light RT so they stay lit at night.</summary>
    public List<MapLightCmd> IndoorsMapLights { get; } = new();
    /// <summary>Bright additive FX glow cores, drawn at the post-composite glow seam so they read at night.</summary>
    public List<GlowCmd> Glows { get; } = new();

    public List<TextDrawCmd> Names { get; } = new();
    public List<BarDrawCmd> Bars { get; } = new();
    public List<TargetArrowCmd> TargetArrows { get; } = new();
    public List<ChatBubbleDrawCmd> ChatBubbles { get; } = new();

    public RenderFrame()
    {
        Below = new List<TileDrawCmd>[Constants.MaxGroundLayers];
        Above = new List<TileDrawCmd>[Constants.MaxFringeLayers];
        Canopy = new List<TileDrawCmd>[Constants.MaxCanopyLayers];
        for (int i = 0; i < Below.Length; i++) Below[i] = new();
        for (int i = 0; i < Above.Length; i++) Above[i] = new();
        for (int i = 0; i < Canopy.Length; i++) Canopy[i] = new();
    }

    public void Clear()
    {
        foreach (var layer in Below) layer.Clear();
        foreach (var layer in Above) layer.Clear();
        foreach (var layer in Canopy) layer.Clear();
        Items.Clear();
        Decals.Clear();
        Npcs.Clear();
        Players.Clear();
        Corpses.Clear();
        CorpseNames.Clear();
        Markers.Clear();
        Lights.Clear();
        AlwaysLitMapLights.Clear();
        AlwaysDarkMapLights.Clear();
        IndoorsMapLights.Clear();
        Glows.Clear();
        Names.Clear();
        Bars.Clear();
        TargetArrows.Clear();
        ChatBubbles.Clear();
    }
}
