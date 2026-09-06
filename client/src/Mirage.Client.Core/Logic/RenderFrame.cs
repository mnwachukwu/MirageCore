using Mirage.Shared;

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

/// <summary>Draw a blood ground decal at the given screen position (tile origin).  <paramref name="Amount"/>
/// (raw, 0..BloodMaxTileAmount) drives the pool-blob SIZE and droplet COUNT.  <paramref name="Freshness"/> (0..1)
/// drives OPACITY — any hit redarkens it to full, then it fades with age.  <paramref name="Seed"/> is a stable
/// per-(map,tile) hash picking the blob variant, rotation, and jitter so the tile's look never shimmers.
/// <paramref name="Size"/> is the footprint size class (1/2/3) of the NPC that bled here, so a large NPC's stain
/// draws as ONE decal scaled to its whole body (Size*32 px, centered on the footprint), not separate tile pools.</summary>
public readonly record struct BloodDrawCmd(float ScreenX, float ScreenY, float Amount, float Freshness, int Seed, int Size = 1, WorldLayer Layer = WorldLayer.Ground);

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

/// <summary>Per-viewer control of a territory-contest capture point — drives the flag/circle color
/// — blue when the viewer's own guild controls it, red for an enemy guild, gray for
/// neutral/contested.</summary>
public enum ContestControl { Neutral, Own, Enemy }

/// <summary>A territory-contest capture point rendered in the world layer for a war participant:
/// a triangular flag + the tinted capture zone + the point's name label, colored per-viewer by
/// <see cref="Control"/>. <see cref="ScreenX"/>/<see cref="ScreenY"/> is the point's tile origin (matching
/// <see cref="SpriteDrawCmd"/>); the zone's extent comes from
/// <see cref="Constants.TerritoryCapturePointRadius"/>, which the shell walks a tile at a time.</summary>
public readonly record struct ContestPointCmd(
    float ScreenX, float ScreenY, ContestControl Control, string Label,
    WorldLayer Layer = WorldLayer.Ground,
    /// <summary>True when the point itself is outside the viewport. Its flag, zone and light are not drawn
    /// at all — only the label survives, pinned to the edge the point lies past, so it reads as a bearing on
    /// an objective rather than a thing standing in the room.</summary>
    bool OffScreen = false);

// FlickerStyle now lives in Mirage.Shared (shared by LightSpec + records); resolved via `using Mirage.Shared`.

/// <summary>A light emitter at the given screen position (tile origin, matching <see cref="SpriteDrawCmd"/>;
/// the shell offsets by half a tile to center the halo). Emitted with a wider cull than sprites so an entity
/// just off-screen still casts its halo into the viewport. <see cref="Intensity"/> (0..1) scales the halo
/// down where a safe-zone map light already covers the emitter — 1 in open wilderness, fading to 0 in a lit
/// town. <see cref="Rgb"/> is the packed 0xRRGGBB core color (Core stays MonoGame-free; the shell unpacks and
/// tints the white halo textures). <see cref="Radius"/> is the outer reach in px (inner core derived from it).
/// <see cref="Flicker"/> picks the core animation, seeded by the STABLE <see cref="Id"/> (per entity/effect) so
/// a light's flicker phase never jumps when the Lights list reorders.</summary>
public readonly record struct LightSourceCmd(
    float ScreenX, float ScreenY, float Intensity,
    uint Rgb, float Radius, FlickerStyle Flicker, int Id, float EffectiveDarkness = 0f,
    WorldLayer Layer = WorldLayer.Ground,
    /// <summary>Screen position of the top-left of the tile <see cref="Reach"/> was traced from, NOT wherever
    /// the halo itself is being drawn. The two differ for anything mid-step or wider than a tile, and the
    /// mask has to follow the trace.</summary>
    float TileScreenX = 0f, float TileScreenY = 0f,
    /// <summary>How far the reach masks extend from their tile, in tiles.</summary>
    int ReachRadius = 0,
    /// <summary>Where this light reaches over its own square, row-major at
    /// <c>LightOcclusion.MaskTexels(ReachRadius)</c> a side — finer than a tile, so the falloff at a wall
    /// can stop clear of it. Null means everything in range: a light with nothing to hide behind, or a
    /// frame built without occlusion.</summary>
    byte[]? Reach = null,
    /// <summary>The same, traced from the tile a mid-step emitter is moving INTO, with
    /// <see cref="ReachBlend"/> saying how far between the two it is. Reach is answered per tile, so without
    /// this the whole shadow pattern changes in one jump each time an emitter crosses a border; blending the
    /// two makes it continuous. Null whenever the emitter is standing still, which is what keeps the second
    /// trace something only moving things pay for.</summary>
    byte[]? ReachInto = null,
    float IntoScreenX = 0f, float IntoScreenY = 0f,
    /// <summary>0 on the tile just left, 1 on the tile being entered.</summary>
    float ReachBlend = 0f);

/// <summary>A map-wide area light for a safe-zone map cell. <see cref="ScreenX"/>/<see cref="ScreenY"/>
/// is the cell's top-left in screen space and <see cref="PxW"/>/<see cref="PxH"/> the cell's own size in
/// pixels, which is the map's size and not the viewport's. Rendered as a soft-edged box (non-flickering)
/// so safe zones stay lit at night with a little spill into the surrounding wilderness.</summary>
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
/// <para><see cref="GuildRankWord"/> (0 = none, else a <c>GuildRank</c>) appends the localized rank word,
/// and <see cref="GuildStanding"/> (0 = none, else the 1-based seasonal standing) appends " (N)". Both
/// are assembled and localized in the Shell layer, which owns the string table — only the numbers travel
/// on the command. <see cref="AtWar"/> draws a crossed-swords marker to the left, flagging a guild the
/// viewer's guild is at war with.</para>
/// </summary>
public readonly record struct TextDrawCmd(float ScreenX, float ScreenY, string Text, int ColorIndex,
    bool AlignBottom = false, int RgbOverride = -1, int LineOffset = 0, int GuildRankWord = 0, bool AtWar = false,
    int GuildStanding = 0, WorldLayer Layer = WorldLayer.Ground);

/// <summary>
/// Draw the tab-target indicator arrow above or below an entity's name.
/// NameY/NameAlignBottom mirror the paired TextDrawCmd so the shell can
/// compute the final pixel position after measuring the font line height.
/// OutOfRange grays the arrow when the target lies beyond the local player's
/// Pythagorean-clamped centered range (see WorldCoordHelper.IsInSpellRange).
/// NoLineOfSight grays the arrow when a Blocked tile or closed Key door sits
/// on the straight tile-line between caster and target — same "can't cast"
/// signal as OutOfRange, just a different reason.
/// </summary>
public readonly record struct TargetArrowCmd(float CenterX, float NameY, bool NameAlignBottom,
                                              bool OutOfRange = false, bool NoLineOfSight = false);

/// <summary>
/// Draw an entity's HP/MP/SP bars — plus, as the bottom row of the same group (one shared outline), the
/// swing/cast COOLDOWN bar. CenterX is the horizontal center; TopY is the topmost (HP) bar's Y.
/// Vital fractions are clamped 0..1; -1f means "omit that bar" (e.g. when max is 0).
/// <see cref="CdFrac"/> is the remaining fraction of the action cooldown (1 = just acted, 0 = ready); < 0
/// omits the row entirely.
/// </summary>
public readonly record struct BarDrawCmd(
    float CenterX, float TopY,
    float HpFrac, float MpFrac, float SpFrac,
    float CdFrac,
    bool ShowCombatBorder,
    bool IsTarget = false,
    bool OutOfRange = false,
    int Size = 1,
    WorldLayer Layer = WorldLayer.Ground);

/// <summary>
/// Draw a chat bubble: rounded rect with shadow + colored border + white text, centered horizontally
/// on CenterX. <see cref="AnchorY"/> pins the bottom edge of the panel (default) or the top edge when
/// <see cref="AnchorBelow"/> is true — used when the entity's name has been flipped below the sprite,
/// so the bubble drops underneath instead of stacking above. BorderColorIndex is a GameColor index.
/// Alpha multiplies every layer for fade-out.
/// </summary>
public readonly record struct ChatBubbleDrawCmd(
    float CenterX, float AnchorY,
    string Text, int BorderColorIndex, float Alpha,
    bool AnchorBelow = false);

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
    /// <summary>Blood-pool ground decals, drawn below entities and above the base ground tiles.</summary>
    public List<BloodDrawCmd> Blood { get; } = new();
    public List<SpriteDrawCmd> Npcs { get; } = new();
    public List<SpriteDrawCmd> Players { get; } = new();
    /// <summary>Dead-player corpse markers (red X), drawn in the entity layer in place of their sprites.</summary>
    public List<CorpseDrawCmd> Corpses { get; } = new();
    /// <summary>Corpse name labels — drawn in the WORLD layer with the red X (below items/NPCs/players), not
    /// with the floating <see cref="Names"/> overlay, so nothing walks "over" a corpse's name.</summary>
    public List<TextDrawCmd> CorpseNames { get; } = new();
    /// <summary>Territory-contest capture points (participant-only) — flag + radius circle + name, drawn in the
    /// world layer so they scroll with the map and living entities draw over them.</summary>
    public List<ContestPointCmd> ContestPoints { get; } = new();
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
        Blood.Clear();
        Npcs.Clear();
        Players.Clear();
        Corpses.Clear();
        CorpseNames.Clear();
        ContestPoints.Clear();
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
