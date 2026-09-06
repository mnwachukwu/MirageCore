using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Pure function: converts <see cref="ClientState"/> into a <see cref="RenderFrame"/>.
/// No I/O, no side effects.  Shell iterates the lists and blits textures.
///
/// Render layer order: Ground layer stack (Below) → Items → NPCs → Players → Fringe layer stack (Above) → Names.
/// </summary>
public static class RenderCommandBuilder
{
    // Attack sprite shows for the first 500ms; idle (frame 0) shows 500–1000ms.
    private const long AttackFrameMs = 500;
    private const long AttackLockMs = 1000;
    private const int BarGroupH = 9;    // max label group height: 3 bars × 3 px (used only for BelowSpriteThreshold)
    private const int BarH = 3;         // height of each individual bar in pixels
    private const int LabelGap = 2;     // px gap between: sprite↔bars and bars↔name
    // Overhead guild-name color when a guild hasn't picked one yet (packed 0xRRGGBB, a neutral light gray).
    private const int GuildNameDefaultRgb = 0xC0C0C0;
    // Set to true to also render MP/SP bars on NPCs.
    private const bool NpcShowMpSp = false;
    // Set to true to also render MP/SP bars on players (overhead world bars only; HUD is unaffected).
    private const bool PlayerShowMpSp = false;
    // Cooldown-row visibility, a player option set once per frame by Build (safe as static: Build is the single
    // per-frame entry point and rendering is single-threaded). _showCooldownBar gates the LOCAL player's own
    // cooldown row; _showOtherCooldownBars gates every other player + every NPC.
    private static bool _showCooldownBar = true;
    private static bool _showOtherCooldownBars = false;
    // Per-tile corpse-name stack counter, reused across frames (cleared in EmitPlayers) so the render
    // hot path allocates nothing — safe as static for the same reason as the cooldown flags above.
    private static readonly Dictionary<(int, int, int), int> _corpseStack = new();
    // Flip labels to below-sprite when sprite top is within this many px of the screen top.
    // Estimate: gap + bars + gap + ~16px font ≈ 29px.
    public const int BelowSpriteThreshold = LabelGap + BarGroupH + LabelGap + 16; // = 29

    public static RenderFrame Build(ClientState state, RenderFrame frame, Camera camera,
        bool alwaysShowBars = true,
        TargetRef hoveredEntity = default,
        TargetRef targetEntity = default,
        bool showNpcNames = true,
        bool showOtherPlayerNames = true,
        bool showPlayerName = true,
        int myIndex = 0,
        float nameLineH = 16f,
        bool showCooldownBar = true,
        bool showOtherCooldownBars = false)
    {
        _showCooldownBar = showCooldownBar;
        _showOtherCooldownBars = showOtherCooldownBars;
        frame.Clear();
        TrimReachCache(state);
        long tickNow = Environment.TickCount64;
        // Lighting overrides first so frame.AlwaysDarkMapLights is populated for EffectiveDarkness lookups.
        EmitMapDarkOverrides(state, frame, camera);
        EmitTileGround(state, frame, camera);
        EmitBloodDecals(state, frame, camera);
        EmitItems(state, frame, camera);
        EmitNpcs(state, frame, camera, tickNow, alwaysShowBars, hoveredEntity, targetEntity, showNpcNames, nameLineH);
        EmitPlayers(state, frame, camera, tickNow, alwaysShowBars, hoveredEntity, targetEntity, showOtherPlayerNames, showPlayerName, myIndex, nameLineH);
        EmitContest(state, frame, camera);
        EmitMapPlacedLights(state, frame, camera);
        EmitTileFringe(state, frame, camera);
        EmitTileCanopy(state, frame, camera);
        EmitAlwaysLitMapLights(state, frame, camera);
        return frame;
    }

    // Center map occupies grid cell (1,1), so its world-tile origin is one map in on each axis — measured
    // in the CENTER map's own size, which is the whole neighbourhood's (a map only links to its own size).
    private static int CenterWorldOffX(ClientState state) => state.MapTilesX;
    private static int CenterWorldOffY(ClientState state) => state.MapTilesY;

    // ── Tiles (Ground, Mask, Anim, Fringe) ────────────────────────────────────

    // Visible world-tile rectangle for the current camera.
    // Includes the partial tiles straddling each edge; the world pass is scissor-clipped
    // to the 512×384 map viewport (see GameplayScreen.Draw), so those partials render up
    // to the boundary and slide in smoothly without overhanging the game space.
    private static TileBounds VisibleTileBounds(Camera camera) => new(
        FirstWX: (int)Math.Floor(camera.CameraX / Constants.PicX),
        LastWX: (int)Math.Floor((camera.CameraX + Camera.ViewW - 1) / Constants.PicX),
        FirstWY: (int)Math.Floor(camera.CameraY / Constants.PicY),
        LastWY: (int)Math.Floor((camera.CameraY + Camera.ViewH - 1) / Constants.PicY));

    /// <summary>An inclusive world-tile rectangle. The four ints interleave the two axes
    /// (firstX, lastX, firstY, lastY rather than first, last, first, last), which is precisely the order a
    /// positional tuple gave no help remembering.</summary>
    private readonly record struct TileBounds(int FirstWX, int LastWX, int FirstWY, int LastWY);

    // Emits the Ground layer stack (drawn below entities).  Each layer cell is a packed LayerCell.
    // An anim-flagged layer is emitted only on its current animation frame (LayerCell.VisibleAnimIndex).  Door reveal: while an open
    // Key tile is open, the topmost populated Ground layer (the door graphic) is hidden, exposing
    // whatever sits beneath it.
    private static void EmitTileGround(ClientState state, RenderFrame frame, Camera camera)
    {
        var b = VisibleTileBounds(camera);
        int firstWX = b.FirstWX, lastWX = b.LastWX, firstWY = b.FirstWY, lastWY = b.LastWY;
        for (int wy = firstWY; wy <= lastWY; wy++)
        {
            if (wy < 0) continue;
            int row = wy / state.MapTilesY;
            if (row > 2) break;
            int localY = wy % state.MapTilesY;
            for (int wx = firstWX; wx <= lastWX; wx++)
            {
                if (wx < 0) continue;
                int col = wx / state.MapTilesX;
                if (col > 2) break;
                var map = state.NeighborMaps[col, row];
                if (map is null) continue;
                int localX = wx % state.MapTilesX;
                var tile = map.Tile[localX, localY];
                var (screenX, screenY) = camera.WorldTileToScreen(wx, wy, 0, 0);

                bool doorOpen = tile.Type == TileType.Key
                    && ((col == 1 && row == 1)
                        ? state.TempTile[localX, localY, (int)WorldLayer.Ground]
                        : state.NeighborTempTiles[col, row][localX, localY, (int)WorldLayer.Ground]);
                // While open, hide only the topmost populated Ground layer (the door graphic).
                int hideGround = doorOpen ? LayerCell.TopmostNonEmptyIndex(tile.Ground) : -1;
                int visibleAnim = LayerCell.VisibleAnimIndex(tile.Ground, state.MapAnimFrame);

                for (int k = 0; k < Constants.MaxGroundLayers; k++)
                {
                    int p = tile.Ground[k];
                    if (LayerCell.IsEmpty(p)) continue;
                    if (k == hideGround) continue;                     // open door reveals what's beneath
                    if (LayerCell.Anim(p) && k != visibleAnim) continue; // show only this frame's anim layer
                    frame.Below[k].Add(new TileDrawCmd(screenX, screenY, LayerCell.Tile(p), LayerCell.Sheet(p)));
                }
            }
        }
    }

    // Emits blood-pool ground decals (drawn below entities, above the base tiles).  Blood is a per-map LIST of
    // pool rectangles (server-driven, client-decayed); each pool renders as ONE decal centered on its footprint
    // (see GameplayScreen.DrawBloodInfluence).  Walks the 9 observable cells and draws each cell's map pools at
    // that cell's world offset — no tile scan, so a merged/absorbed pool is simply absent.
    private static void EmitBloodDecals(ClientState state, RenderFrame frame, Camera camera)
    {
        if (state.BloodByMap.Count == 0) return;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int mapNum = (col == 1 && row == 1) ? state.CenterMapNum : state.NeighborMapNums[col, row];
                if (mapNum <= 0 || !state.BloodByMap.TryGetValue(mapNum, out var pools) || pools.Count == 0) continue;
                int offX = col * state.MapTilesX;
                int offY = row * state.MapTilesY;
                foreach (var p in pools)
                {
                    if (p.Amount <= Constants.BloodVisibleEpsilon) continue;
                    int size = p.Size < 1 ? 1 : p.Size;
                    var (screenX, screenY) = camera.WorldTileToScreen(offX + p.X, offY + p.Y, 0, 0);
                    if (!BloodDecalOnScreen(screenX, screenY, size)) continue;
                    // Stable per-pool hash keyed on MAP + LOCAL pool tile (NOT world coords), so a seam cross that
                    // re-frames the observable area doesn't re-roll the blob variant/rotation/scale of a pool.
                    int seed = (mapNum * 73856093) ^ (p.X * 19349663) ^ (p.Y * 83492791);
                    frame.Blood.Add(new BloodDrawCmd(screenX, screenY, p.Amount, p.Freshness, seed, size, p.Layer));
                }
            }
        }
    }

    // A pool decal is centered on its footprint (screen origin + size*Pic/2) and its blob reaches ~size*Max/2 past
    // that center, so it stays visible well beyond its anchor tile.  Cull against the 512x384 map viewport by that
    // reach so a big pool whose anchor sits just off-screen still draws (the world pass is scissor-clipped).
    private static bool BloodDecalOnScreen(float screenX, float screenY, int size)
    {
        float cx = screenX + size * Constants.PicX * 0.5f;
        float cy = screenY + size * Constants.PicY * 0.5f;
        float reach = size * Constants.BloodDecalMaxSizePx * 0.5f;
        return cx + reach >= 0f && cx - reach <= Camera.ViewW
            && cy + reach >= 0f && cy - reach <= Camera.ViewH;
    }

    // Emits the Fringe layer stack (drawn above entities).  Anim-flagged fringe layers cycle by their
    // own frame too.  A fringe-layer Key door reveals through its topmost Fringe graphic while its FRINGE
    // door state is open — the deck equivalent of the ground door reveal in EmitTileBelow.
    private static void EmitTileFringe(ClientState state, RenderFrame frame, Camera camera)
    {
        var b = VisibleTileBounds(camera);
        int firstWX = b.FirstWX, lastWX = b.LastWX, firstWY = b.FirstWY, lastWY = b.LastWY;
        for (int wy = firstWY; wy <= lastWY; wy++)
        {
            if (wy < 0) continue;
            int row = wy / state.MapTilesY;
            if (row > 2) break;
            int localY = wy % state.MapTilesY;
            for (int wx = firstWX; wx <= lastWX; wx++)
            {
                if (wx < 0) continue;
                int col = wx / state.MapTilesX;
                if (col > 2) break;
                var map = state.NeighborMaps[col, row];
                if (map is null) continue;
                int localX = wx % state.MapTilesX;
                var tile = map.Tile[localX, localY];
                var (screenX, screenY) = camera.WorldTileToScreen(wx, wy, 0, 0);

                bool doorOpen = tile.FringeAttr is { Type: TileType.Key }
                    && ((col == 1 && row == 1)
                        ? state.TempTile[localX, localY, (int)WorldLayer.Fringe]
                        : state.NeighborTempTiles[col, row][localX, localY, (int)WorldLayer.Fringe]);
                int hideFringe = doorOpen ? LayerCell.TopmostNonEmptyIndex(tile.Fringe) : -1;
                int visibleAnim = LayerCell.VisibleAnimIndex(tile.Fringe, state.MapAnimFrame);

                for (int k = 0; k < Constants.MaxFringeLayers; k++)
                {
                    int p = tile.Fringe[k];
                    if (LayerCell.IsEmpty(p)) continue;
                    if (k == hideFringe) continue;                       // open fringe door reveals what's beneath
                    if (LayerCell.Anim(p) && k != visibleAnim) continue; // show only this frame's anim layer
                    frame.Above[k].Add(new TileDrawCmd(screenX, screenY, LayerCell.Tile(p), LayerCell.Sheet(p)));
                }
            }
        }
    }

    // Two-layer world: the Canopy visual stack — décor drawn OVER everything (both logical layers), e.g. a
    // treetop or roof above a bridge.  Same emit shape as EmitTileFringe, into frame.Canopy; the world-draw
    // paints it after the fringe-layer entity pass so a walker on the bridge passes UNDER the canopy.
    private static void EmitTileCanopy(ClientState state, RenderFrame frame, Camera camera)
    {
        var b = VisibleTileBounds(camera);
        int firstWX = b.FirstWX, lastWX = b.LastWX, firstWY = b.FirstWY, lastWY = b.LastWY;
        for (int wy = firstWY; wy <= lastWY; wy++)
        {
            if (wy < 0) continue;
            int row = wy / state.MapTilesY;
            if (row > 2) break;
            int localY = wy % state.MapTilesY;
            for (int wx = firstWX; wx <= lastWX; wx++)
            {
                if (wx < 0) continue;
                int col = wx / state.MapTilesX;
                if (col > 2) break;
                var map = state.NeighborMaps[col, row];
                if (map is null) continue;
                var tile = map.Tile[wx % state.MapTilesX, localY];
                var (screenX, screenY) = camera.WorldTileToScreen(wx, wy, 0, 0);
                int visibleAnim = LayerCell.VisibleAnimIndex(tile.Canopy, state.MapAnimFrame);

                for (int k = 0; k < Constants.MaxCanopyLayers; k++)
                {
                    int p = tile.Canopy[k];
                    if (LayerCell.IsEmpty(p)) continue;
                    if (LayerCell.Anim(p) && k != visibleAnim) continue; // show only this frame's anim layer
                    frame.Canopy[k].Add(new TileDrawCmd(screenX, screenY, LayerCell.Tile(p), LayerCell.Sheet(p)));
                }
            }
        }
    }

    // True when a sprite at this screen position is at least partly inside the 512×384 map
    // viewport.  Edge sprites still emit (and are scissor-clipped); only fully off-screen
    // entities are skipped.
    private static bool OnScreen(float screenX, float screenY) =>
        screenX > -Constants.PicX && screenX < Camera.ViewW
        && screenY > -Constants.PicY && screenY < Camera.ViewH;

    // Size-aware OnScreen for variable-size NPCs: a size-S sprite spans extentPx from its top-left anchor,
    // so it stays visible while the anchor is within extentPx of the top/left edges (the body extends +x/+y).
    private static bool OnScreenSized(float screenX, float screenY, int extentPx) =>
        screenX > -extentPx && screenX < Camera.ViewW
        && screenY > -extentPx && screenY < Camera.ViewH;

    // A light emitter is kept while its outer halo (3-tile reach) can still touch the viewport, i.e. up
    // to 3 tiles beyond the OnScreen band — so an entity that has stepped just off-screen keeps casting
    // light inward instead of the halo popping out together with its (OnScreen-culled) sprite.
    private const int LightHaloReach = Constants.PicX * 3;
    // Per-source variant: an emitter is kept while its halo of the given px radius can still touch the
    // viewport. Placed lights and light-emitting NPCs pass their own (variable) radius so a large halo isn't
    // culled early; the parameterless overload keeps the fixed 3-tile margin (players' torch = LightSpec.Torch).
    private static bool LightReachesR(float screenX, float screenY, float reachPx) =>
        screenX > -Constants.PicX - reachPx && screenX < Camera.ViewW + reachPx
        && screenY > -Constants.PicY - reachPx && screenY < Camera.ViewH + reachPx;
    private static bool LightReaches(float screenX, float screenY) =>
        LightReachesR(screenX, screenY, LightHaloReach);

    // Stable per-light flicker seeds, in separate id ranges so a light's flicker phase never jumps when the
    // Lights list reorders. Players use their index directly; a seed collision merely shares a phase (harmless).
    private static int NpcLightId(int mapNum, int slot) => 1_000_000 + mapNum * 1000 + slot;
    private static int TraversalLightId(int spawnMap, int spawnSlot) => 2_000_000 + spawnMap * 1000 + spawnSlot;

    // A size-N NPC's light reaches (N-1) tiles FURTHER than its authored Radius, so the authored value keeps
    // meaning "how far the glow spills PAST the body" at any footprint size, and the bright inner core (2/3 of
    // this, per LightModel) grows to cover the 64/96px body instead of a single tile.  Size 1 (players, small
    // NPCs, map-placed lights) uses the flat radius unchanged.
    private static float NpcLightRadiusPx(float radiusTiles, int size) =>
        (radiusTiles + (size - 1)) * Constants.PicX;

    // True when world tile (wx,wy) sits on an AlwaysLit map — exact cell bounds, no spillover. Emitters here
    // are suppressed entirely: the halo would be redundant over a map that is already fully bright. Keyed to
    // the map seam (not the light's soft spill) so a torch snaps on the instant its bearer steps off the lit
    // map. Deliberately simple: onset is symmetric regardless of what borders the map, and matches where
    // InAlwaysDark lifts. Keyed on the authored lighting, not on Moral: a safe map is not lit by virtue of
    // being safe, and a lit map need not be safe.
    private static bool InTownLight(ClientState state, int wx, int wy)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var map = state.NeighborMaps[col, row];
                if (state.LightingOf(map) != MapLighting.AlwaysLit) continue;
                int left = col * state.MapTilesX;
                int top = row * state.MapTilesY;
                int right = left + state.MapTilesX - 1;
                int bottom = top + state.MapTilesY - 1;
                if (wx >= left && wx <= right && wy >= top && wy <= bottom)
                    return true;
            }
        }

        return false;
    }

    // AlwaysLit maps stay bright at night. Every loaded observable cell whose map is lit emits a map-wide
    // area light (one viewport-sized soft box), rendered non-flickering in the light map. No viewport cull:
    // an adjacent lit map still spills light into the view even when its own cell has scrolled off-screen,
    // and the max-blended boxes tile seamlessly across contiguous lit maps. Nothing needs to exclude the dark
    // case here: Lighting resolves to exactly one of the two, so a map is never both.
    private static void EmitAlwaysLitMapLights(ClientState state, RenderFrame frame, Camera camera)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var map = state.NeighborMaps[col, row];
                if (state.LightingOf(map) != MapLighting.AlwaysLit) continue;
                var (sx, sy) = camera.WorldTileToScreen(
                    col * state.MapTilesX, row * state.MapTilesY, 0, 0);
                frame.AlwaysLitMapLights.Add(new MapLightCmd(sx, sy, MapPxW(state), MapPxH(state)));
            }
        }
    }

    // Map-placed light sources: each loaded observable cell contributes its authored lights, on the same
    // additive halo path as entity emitters. Unlike players/NPCs these are NOT town-light suppressed: a light
    // authored on a lit map is a deliberate decoration meant to show even inside the bright area (it still only
    // reads where there's darkness to tint — at night / on AlwaysDark maps). AlwaysDark maps are full-bright
    // regardless of time of day. Culled per-source by the light's own radius, and seeded for flicker by its Guid.
    private static void EmitMapPlacedLights(ClientState state, RenderFrame frame, Camera camera)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var map = state.NeighborMaps[col, row];
                if (map is null || map.Lights.Count == 0) continue;
                int offX = col * state.MapTilesX;
                int offY = row * state.MapTilesY;
                foreach (var pl in map.Lights)
                {
                    int wx = offX + pl.X;
                    int wy = offY + pl.Y;
                    var (screenX, screenY) = camera.WorldTileToScreen(wx, wy, 0, 0);
                    float radiusPx = pl.Light.Radius * Constants.PicX;
                    if (!LightReachesR(screenX, screenY, radiusPx)) continue;
                    float effectiveDark = InAlwaysDark(state, wx, wy) ? 1f : state.GetCurrentDarkness();
                    var lit = ReachAcrossStep(state, camera, wx, wy, 0f, 0f, pl.Layer, pl.Light.Radius);
                    frame.Lights.Add(new LightSourceCmd(screenX, screenY, pl.Light.Intensity, pl.Light.Rgb,
                        radiusPx, pl.Light.Flicker, pl.Id.GetHashCode(), effectiveDark, pl.Layer,
                        lit.FromScreenX, lit.FromScreenY, lit.Radius, lit.From,
                        lit.Into, lit.IntoScreenX, lit.IntoScreenY, lit.Blend));
                }
            }
        }
    }

    /// <summary>One light's occlusion across a step: the mask traced from the tile being LEFT, the mask
    /// traced from the tile being ENTERED, and how far between them the emitter is.
    ///
    /// <para>Reach is answered per tile, so a single mask can only change in one jump as an emitter crosses a
    /// border — the halo slides smoothly and its shadows do not. Tracing both ends and blending makes the
    /// change continuous. An entity's X/Y is already the DESTINATION the instant a step begins, with the
    /// offset counting a whole tile back to zero, so the tile being left is the one the offset points at.</para>
    ///
    /// <para>A standing emitter's two tiles are the same one: it traces once and blends nothing, which is
    /// what keeps the second trace something only moving things pay for.</para></summary>
    public readonly record struct LightReach(
        int Radius, byte[] From, float FromScreenX, float FromScreenY,
        byte[]? Into, float IntoScreenX, float IntoScreenY, float Blend);

    private static LightReach ReachAcrossStep(ClientState state, Camera camera,
        int wx, int wy, float xOffset, float yOffset, WorldLayer layer, float radiusTiles)
    {
        int r = Math.Max(0, (int)MathF.Ceiling(radiusTiles));
        int fromX = wx + Math.Sign(xOffset), fromY = wy + Math.Sign(yOffset);
        var (fsx, fsy) = camera.WorldTileToScreen(fromX, fromY, 0, 0);
        var from = CachedReach(state, fromX, fromY, layer, r, mounted: true);
        if (fromX == wx && fromY == wy) return new LightReach(r, from, fsx, fsy, null, 0f, 0f, 0f);

        var (isx, isy) = camera.WorldTileToScreen(wx, wy, 0, 0);
        var into = CachedReach(state, wx, wy, layer, r, mounted: true);
        float travelled = MathF.Max(MathF.Abs(xOffset), MathF.Abs(yOffset)) / Constants.PicX;
        return new LightReach(r, from, fsx, fsy, into, isx, isy, Math.Clamp(1f - travelled, 0f, 1f));
    }

    /// <summary>
    /// The reach masks for a light moving freely through the world — a spell bolt, which the SHELL appends
    /// after the frame is built — with the same cross-fade a walking emitter gets.
    ///
    /// <para>Reach is answered per TILE, so a mask can only change in a jump at a border. A body walking pays
    /// for two traces and blends between them; a bolt travels faster and over more borders, so the jump is
    /// worse for it, not better — the shadows snap square as it flies.</para>
    ///
    /// <para>The pair is taken along the DOMINANT axis of travel: the tile it is crossing into, the one behind
    /// it on that axis, and how far across it has come. A bolt that is not moving blends nothing, exactly as a
    /// standing emitter does not.</para>
    /// </summary>
    public static LightReach ReachAcrossTravel(ClientState state, Camera camera, float worldX, float worldY,
                                               float vx, float vy, WorldLayer layer, float radiusTiles)
    {
        int r = Math.Max(0, (int)MathF.Ceiling(radiusTiles));
        int intoX = (int)MathF.Floor(worldX / Constants.PicX);
        int intoY = (int)MathF.Floor(worldY / Constants.PicY);

        int fromX = intoX, fromY = intoY;
        float blend = 0f;
        if (MathF.Abs(vx) >= MathF.Abs(vy) && vx != 0f)
        {
            float frac = worldX / Constants.PicX - intoX;
            fromX = intoX + (vx > 0f ? -1 : 1);
            blend = vx > 0f ? frac : 1f - frac;
        }
        else if (vy != 0f)
        {
            float frac = worldY / Constants.PicY - intoY;
            fromY = intoY + (vy > 0f ? -1 : 1);
            blend = vy > 0f ? frac : 1f - frac;
        }

        // NOT mounted: a bolt is passing OVER these tiles, not fixed to them. Exempt its own tile and a burst
        // scattering against a wall lights the wall up as though it stopped nothing.
        var (fsx, fsy) = camera.WorldTileToScreen(fromX, fromY, 0, 0);
        var from = CachedReach(state, fromX, fromY, layer, r, mounted: false);
        if (fromX == intoX && fromY == intoY) return new LightReach(r, from, fsx, fsy, null, 0f, 0f, 0f);

        var (isx, isy) = camera.WorldTileToScreen(intoX, intoY, 0, 0);
        var into = CachedReach(state, intoX, intoY, layer, r, mounted: false);
        return new LightReach(r, from, fsx, fsy, into, isx, isy, Math.Clamp(blend, 0f, 1f));
    }

    // ── Reach cache ──────────────────────────────────────────────────────────
    // What a light reaches from a tile depends on the walls and doors around it and on nothing else — not on
    // the light, not on the frame. A wall lamp or a standing NPC therefore traces the same answer sixty times
    // a second, and a walking one re-traces a tile it or something else already asked about a moment ago.
    //
    // So the answers are kept, and thrown away wholesale the moment anything they were derived from moves.
    // Stamping rather than invalidating per tile: the inputs are nine maps and nine door sets, a door opening
    // three maps away can matter to a light near the seam, and working out which entries it reached would cost
    // more than re-tracing the handful of lights on screen.
    private static int _reachStamp;
    private static readonly Dictionary<(int X, int Y, WorldLayer Layer, int Radius, bool Mounted), byte[]> _reachCache = [];

    /// <summary>Counts how many times the reach cache has been dropped.
    ///
    /// <para>A mask array leaves the cache only through <c>DropCache</c>, so within one generation a given
    /// array instance always holds the same reach. That makes the array itself a valid cache key for
    /// anything derived from it — the renderer's GPU mask textures key on it, and re-derive only when this
    /// number moves.</para></summary>
    public static int ReachGeneration { get; private set; }

    /// <summary>Every input the occlusion trace reads, in one comparable number: which maps are loaded, what
    /// revision each is at, and how many times each map's doors have moved.</summary>
    private static int ReachStamp(ClientState state)
    {
        int h = 17;
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                h = h * 31 + (state.NeighborMaps[col, row]?.Revision ?? -1);
                h = h * 31 + state.NeighborMapNums[col, row];
                h = h * 31 + state.NeighborTempTiles[col, row].Version;
            }
        }

        return h;
    }

    // A cap rather than an eviction policy: entries are only ever added for tiles a light actually stood on,
    // and wandering NPCs accumulate them slowly. Dropping the lot costs the next frame's traces and nothing else.
    private const int MaxCachedReaches = 256;

    // Masks are recycled rather than dropped. A wandering emitter enters a new tile a couple of times a
    // second, and at a few kilobytes a mask that is a steady drip of garbage in the one place the render
    // path is otherwise allocation-free. A discarded mask is the exact size the next one needs.
    private static readonly Dictionary<int, Stack<byte[]>> _reachSpare = [];

    private static void DropCache()
    {
        foreach (var mask in _reachCache.Values)
        {
            if (!_reachSpare.TryGetValue(mask.Length, out var spares))
                _reachSpare[mask.Length] = spares = new Stack<byte[]>();
            spares.Push(mask);
        }

        _reachCache.Clear();
        ReachGeneration++;
    }

    /// <summary>Drops the cache when what it was traced from has moved, or when it has outgrown its cap.
    ///
    /// <para>🔴 Both happen HERE, at the top of a build, before a single mask has been handed out. A drop
    /// recycles mask arrays into the spare pool, and recycling one that a light already emitted this frame is
    /// holding hands two lights the same mask — one of them drawing the other's shadows for a frame.</para></summary>
    private static void TrimReachCache(ClientState state)
    {
        int stamp = ReachStamp(state);
        if (stamp != _reachStamp)
        {
            DropCache();
            _reachStamp = stamp;
        }
        else if (_reachCache.Count >= MaxCachedReaches)
        {
            DropCache();
        }
    }

    private static byte[] CachedReach(ClientState state, int wx, int wy, WorldLayer layer, int radius, bool mounted)
    {
        var key = (wx, wy, layer, radius, mounted);
        if (_reachCache.TryGetValue(key, out var hit)) return hit;

        int cells = LightOcclusion.MaskCells(radius);
        var mask = _reachSpare.TryGetValue(cells, out var spare) && spare.Count > 0
            ? spare.Pop()
            : new byte[cells];
        LightOcclusion.Fill(state, wx, wy, layer, radius, mask, mounted);
        _reachCache[key] = mask;
        return mask;
    }
    // True when world tile (wx,wy) sits on an AlwaysDark map — exact cell bounds, no spillover. Mirrors
    // InTownLight: keyed to the map seam so a light-bearer's halo snaps to full-bright the instant it steps
    // onto the dark map, exactly where the town-light suppression lifts. (The darkness OVERLAY still bleeds a
    // couple tiles past the seam for a soft edge — that visual skirt is intentionally NOT mirrored here.)
    private static bool InAlwaysDark(ClientState state, int wx, int wy)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var map = state.NeighborMaps[col, row];
                if (state.LightingOf(map) != MapLighting.AlwaysDark) continue;
                int left = col * state.MapTilesX;
                int top = row * state.MapTilesY;
                int right = left + state.MapTilesX - 1;
                int bottom = top + state.MapTilesY - 1;
                if (wx >= left && wx <= right && wy >= top && wy <= bottom)
                    return true;
            }
        }

        return false;
    }

    // Lighting and Indoors map overrides — must run before entity emitters so AlwaysDarkMapLights
    // is populated when EffectiveDarkness is computed for each light source.
    private static void EmitMapDarkOverrides(ClientState state, RenderFrame frame, Camera camera)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var map = state.NeighborMaps[col, row];
                if (map is null) continue;
                var (sx, sy) = camera.WorldTileToScreen(
                    col * state.MapTilesX, row * state.MapTilesY, 0, 0);
                if (state.LightingOf(map) == MapLighting.AlwaysDark)
                    frame.AlwaysDarkMapLights.Add(new MapLightCmd(sx, sy, MapPxW(state), MapPxH(state)));
                else if (state.IndoorsOf(map))
                    frame.IndoorsMapLights.Add(new MapLightCmd(sx, sy, MapPxW(state), MapPxH(state)));
            }
        }
    }

    // The nine cells of the observable area are all one size (the link rule), so one pair answers for
    // every cell in the grid.
    private static int MapPxW(ClientState state) => state.MapTilesX * Constants.PicX;
    private static int MapPxH(ClientState state) => state.MapTilesY * Constants.PicY;

    // World-tile offset of the grid cell holding a given map number, or null if that map
    // isn't one of the 9 currently loaded cells.  NeighborMapNums[1,1] is the center map.
    private static (int offX, int offY)? CellOffsetForMap(ClientState state, int mapNum)
    {
        if (mapNum <= 0) return null;
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (state.NeighborMapNums[col, row] == mapNum)
                    return (col * state.MapTilesX, row * state.MapTilesY);
            }
        }

        return null;
    }

    // ── Territory contest (participant-only capture-point flags/circles/names) ─────────────
    // state.Contest is non-null only for a war participant (the server gates the push). Each point renders on
    // whichever of the 9 observable cells currently holds its map; the radius circle can reach well past the
    // point's own tile, so cell-observability (not the tight per-tile OnScreen cull) is the gate here.
    private static void EmitContest(ClientState state, RenderFrame frame, Camera camera)
    {
        var contest = state.Contest;
        if (contest is null) return;

        // Markers are for finding an objective while you are AT the war. Standing somewhere else in the
        // world during war night is not that, so they are gated on being in the contested territory — or on
        // holding a point, which a border point's spill lets you do from a tile outside it.
        var layout = ContestLayout(state, contest);
        if (layout is null) return;

        int myGuild = state.Me.GuildId;
        float radiusPx = Constants.TerritoryCapturePointRadius * Constants.PicX;
        int lightId = ContestLightIdBase;
        foreach (var pt in contest.Points)
        {
            // A point on one of the nine loaded maps is placed from the grid, exactly as everything else is.
            // Anything further is placed through the territory layout, which reaches maps this client has
            // never seen — that is the whole point of an off-screen marker.
            var off = CellOffsetForMap(state, pt.Map);
            int wx, wy;
            if (off is not null)
            {
                wx = off.Value.offX + pt.X;
                wy = off.Value.offY + pt.Y;
            }
            else if (layout.Value.TryPlace(pt.Map, pt.X, pt.Y, out int lx, out int ly))
            {
                wx = lx;
                wy = ly;
            }
            else
            {
                continue;   // a point on a map the layout could not reach
            }

            var (screenX, screenY) = camera.WorldTileToScreen(wx, wy, 0f, 0f);
            ContestControl control =
                pt.OwnerGuild <= 0 ? ContestControl.Neutral :
                pt.OwnerGuild == myGuild ? ContestControl.Own : ContestControl.Enemy;
            // 🔴 OffScreen moves the LABEL to the viewport edge. It does not decide whether the zone is
            // drawn: a point standing just past the border still holds ground inside the view, and its ring
            // is the only thing that shows where that ground ends. The zone and the light are gated on
            // REACH — LightReachesR, the point's tile plus its radius — never on the tile alone.
            bool offScreen = screenX < 0 || screenY < 0
                             || screenX >= Camera.ViewW - Constants.PicX
                             || screenY >= Camera.ViewH - Constants.PicY;
            frame.ContestPoints.Add(new ContestPointCmd(screenX, screenY, control, pt.Label, pt.Layer, offScreen));

            // The flag lights its own capture radius, in the viewer's control color, so the zone reads at
            // night without hunting for the ring. Steady (a flag is not a flame) and UNOCCLUDED — the capture
            // test is pure distance with no line-of-sight term, so a light that stopped at a wall would draw a
            // smaller zone than the one being scored.
            float effectiveDark = InAlwaysDark(state, wx, wy) ? 1f : state.GetCurrentDarkness();
            if (LightReachesR(screenX, screenY, radiusPx))
            {
                frame.Lights.Add(new LightSourceCmd(screenX, screenY, 1f, ContestLightRgb(control),
                    radiusPx, FlickerStyle.None, lightId, effectiveDark, pt.Layer));
            }
            lightId++;
        }
    }

    /// <summary>The territory's map layout, anchored on the player, or null when they are not at the war.
    ///
    /// <para>The server sends every map of the territory placed on one tile grid. Anchoring that grid on the
    /// player's own map turns it into the same world-tile space the 3x3 already uses, so a point five maps
    /// away and a point on the next screen are placed by the same arithmetic.</para></summary>
    private readonly record struct ContestGrid(
        Dictionary<int, (int X, int Y)> Origins, int AnchorX, int AnchorY, int MyOriginX, int MyOriginY)
    {
        /// <summary>World-tile position of a tile on one of the territory's maps.</summary>
        public bool TryPlace(int map, int x, int y, out int wx, out int wy)
        {
            wx = wy = 0;
            if (!Origins.TryGetValue(map, out var o)) return false;
            wx = AnchorX + (o.X + x) - MyOriginX;
            wy = AnchorY + (o.Y + y) - MyOriginY;
            return true;
        }
    }

    private static ContestGrid? ContestLayout(ClientState state, TerritoryContestPacket contest)
    {
        if (contest.Layout.Count == 0 || !state.AtContest()) return null;
        var origins = new Dictionary<int, (int X, int Y)>(contest.Layout.Count);
        foreach (var m in contest.Layout) origins[m.Map] = (m.OriginX, m.OriginY);

        // Anchored on ANY loaded map the territory knows, not on the player's own. Usually they are the
        // same map — but a border point can be held from a tile just outside the territory, and there the
        // player's map is not in the layout at all while a neighbour of it is.
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int m = state.NeighborMapNums[col, row];
                if (m <= 0 || !origins.TryGetValue(m, out var o)) continue;
                var (ax, ay) = state.ToWorld(col, row, 0, 0);
                return new ContestGrid(origins, ax, ay, o.X, o.Y);
            }
        }
        return null;
    }

    // Flag-light ids sit in their own range, like the NPC/traversal seeds above.
    private const int ContestLightIdBase = 3_000_000;

    // The flag light's core color: a softer, lighter reading of the flag's own control color (which stays the
    // saturated ContestOwn/Enemy/Neutral in the shell), because a full-strength tint over a 10-tile radius
    // washes the ground it is meant to mark.
    private static uint ContestLightRgb(ContestControl control) => control switch
    {
        ContestControl.Own => 0x8CEBF5,     // soft cyan
        ContestControl.Enemy => 0xFFAAC8,   // light pink
        _ => 0xD2D2D7,                      // neutral, matching the gray flag
    };

    // ── Ground items ──────────────────────────────────────────────────────────

    private static void EmitItems(ClientState state, RenderFrame frame, Camera camera)
    {
        EmitItemArray(state, frame, camera, state.MapItems, CenterWorldOffX(state), CenterWorldOffY(state));

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (col == 1 && row == 1) continue;
                if (state.NeighborMaps[col, row] is null) continue; // tiles not loaded yet
                EmitItemArray(state, frame, camera, state.NeighborItems[col, row],
                    col * state.MapTilesX, row * state.MapTilesY);
            }
        }
    }

    private static void EmitItemArray(ClientState state, RenderFrame frame, Camera camera,
        Dictionary<int, MapItemRecord> mapItems, int offX, int offY)
    {
        foreach (var mi in mapItems.Values)
        {
            if (mi.Num == 0 || mi.Num > state.Limits.Items) continue;
            var itemDef = state.Items[mi.Num];
            if (itemDef is null || itemDef.Pic < 0) continue;

            var (screenX, screenY) = camera.WorldTileToScreen(offX + mi.X, offY + mi.Y, 0, 0);
            if (!OnScreen(screenX, screenY)) continue;
            frame.Items.Add(new ItemDrawCmd(screenX, screenY, itemDef.Pic, mi.Layer, itemDef.ItemSheet));
        }
    }

    // ── NPCs ─────────────────────────────────────────────────────────────────

    private static void EmitNpcs(ClientState state, RenderFrame frame, Camera camera, long tickNow, bool alwaysShowBars,
        TargetRef hovered, TargetRef target, bool showNames, float nameLineH)
    {
        // Center + neighbor maps share the same emit path. Hover and target both address NPCs by
        // (slot, mapNum), so each cell self-disambiguates against the cross-region hover/target.
        EmitNpcArray(state, frame, camera, state.MapNpcs, CenterWorldOffX(state), CenterWorldOffY(state),
            state.CenterMapNum, tickNow, alwaysShowBars, hovered, target, showNames, nameLineH);
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (col == 1 && row == 1) continue;
                if (state.NeighborMaps[col, row] is null) continue; // tiles not loaded yet
                EmitNpcArray(state, frame, camera, state.NeighborNpcs[col, row],
                    col * state.MapTilesX, row * state.MapTilesY,
                    state.NeighborMapNums[col, row], tickNow, alwaysShowBars, hovered, target, showNames, nameLineH);
            }
        }

        // Visiting (chasing) NPCs — drawn on whichever loaded cell currently holds them.
        EmitTraversalNpcs(state, frame, camera, tickNow, alwaysShowBars, hovered, target, showNames, nameLineH);
    }

    private static void EmitTraversalNpcs(ClientState state, RenderFrame frame, Camera camera,
        long tickNow, bool alwaysShowBars, TargetRef hovered, TargetRef target, bool showNames, float nameLineH)
    {
        foreach (var t in state.TraversalNpcs.Values)
        {
            var off = CellOffsetForMap(state, t.CurrentMapNum);
            if (off is null) continue; // currently on a map we don't observe
            bool hoveredHere = hovered.Kind == TargetKind.Traversal && hovered.A == t.SpawnMapNum && hovered.B == t.SpawnSlot;
            bool targetHere = target.Kind == TargetKind.Traversal && target.A == t.SpawnMapNum && target.B == t.SpawnSlot;
            EmitOneNpc(state, frame, camera, t, off.Value.offX, off.Value.offY,
                TraversalLightId(t.SpawnMapNum, t.SpawnSlot), tickNow, alwaysShowBars, hoveredHere, targetHere, showNames, nameLineH);
        }
    }

    private static void EmitNpcArray(ClientState state, RenderFrame frame, Camera camera, ClientMapNpc[] mapNpcs,
        int offX, int offY, int cellMapNum, long tickNow, bool alwaysShowBars,
        TargetRef hovered, TargetRef target, bool showNames, float nameLineH)
    {
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            var n = mapNpcs[i];
            if (n.Num == 0 || n.Num > state.Limits.Npcs)
            {
                // Dead slot may still hold "last words" drifters (see HandleNpcDead). Emit them at
                // the NPC's preserved last tile so they keep floating away instead of vanishing.
                if (n.ChatBubbleDrifters is { Count: > 0 })
                    EmitOrphanNpcBubble(frame, camera, n, offX, offY, tickNow);
                continue;
            }
            bool hoveredHere = hovered.Kind == TargetKind.Npc && hovered.A == i && hovered.B == cellMapNum;
            bool targetHere = target.Kind == TargetKind.Npc && target.A == i && target.B == cellMapNum;
            EmitOneNpc(state, frame, camera, n, offX, offY, NpcLightId(cellMapNum, i), tickNow, alwaysShowBars,
                hoveredHere, targetHere, showNames, nameLineH);
        }
    }

    /// <summary>Drifters left over after the NPC died — anchor them at the preserved tile so the
    /// "last words" rise from where the speaker fell. No def lookup (Num=0), so use a fixed
    /// above-sprite anchor approximation.</summary>
    private static void EmitOrphanNpcBubble(RenderFrame frame, Camera camera, ClientMapNpc n,
        int offX, int offY, long tickNow)
    {
        var (screenX, screenY) = camera.WorldTileToScreen(offX + n.X, offY + n.Y, 0f, 0f);
        if (!OnScreen(screenX, screenY)) return;
        float centerX = screenX + Constants.PicX / 2f;
        // Approximate where the head bubble would have sat: above the sprite by a name-and-gap stack.
        float baseY = screenY - LabelGap - ChatBubbleStyle.GapAboveName;
        foreach (var d in n.ChatBubbleDrifters!)
        {
            long elapsed = tickNow - d.DemotedMs;
            if (elapsed >= ChatBubbleStyle.FloatMs) continue;
            float t = elapsed / (float)ChatBubbleStyle.FloatMs;
            float floatPx = t * ChatBubbleStyle.FloatPx;
            float alpha = 1f - t;
            frame.ChatBubbles.Add(new ChatBubbleDrawCmd(centerX, baseY - floatPx, d.Text, d.Color, alpha, AnchorBelow: false));
        }
    }

    // During a cross-layer slide (stepping onto/off a ramp), draw the moving sprite on the HIGHER layer (Fringe)
    // until its walk-offset finishes, so it isn't occluded by the ramp/fringe tile art mid-slide ("sliding out
    // from under the ramp").  When the slide ends (offset 0) it commits to the destination layer.  Cross-layer is
    // always Ground↔Fringe, so the higher layer is Fringe.
    private static WorldLayer SlideRenderLayer(WorldLayer layer, WorldLayer prevLayer, float xOffset, float yOffset)
    {
        bool sliding = xOffset != 0f || yOffset != 0f;
        return sliding && prevLayer != layer ? WorldLayer.Fringe : layer;
    }

    // Renders one NPC (native slot or traversal guest) at world-tile origin (offX,offY).
    // Targeting/hover UI is supplied as resolved flags so it works for both addressing schemes.
    private static void EmitOneNpc(ClientState state, RenderFrame frame, Camera camera, ClientMapNpc n,
        int offX, int offY, int lightId, long tickNow, bool alwaysShowBars,
        bool hoveredHere, bool targetHere, bool showNames, float nameLineH)
    {
        if (n.Num == 0 || n.Num > state.Limits.Npcs) return;
        var def = state.NpcDefs[n.Num];
        if (def is null || def.Sprite < 0) return;
        int size = def.EffectiveSize;
        int spritePx = size * Constants.PicX;   // the sprite + its footprint span this many px, square

        var (screenX, screenY) = camera.WorldTileToScreen(offX + n.X, offY + n.Y, n.XOffset, n.YOffset);
        float centerX = screenX + spritePx / 2f; // horizontal center of the footprint (name/bar/arrow/bubble)
        // An AlwaysLit map suppresses the halo (redundant over it); AlwaysDark maps are exempt from that
        // suppression (see InTownLight). EffectiveDarkness is full inside a dark map (lit regardless of time
        // of day), otherwise it tracks the time-of-day darkness so the halo fades out by day.
        if (def.EmitsLight)
        {
            float radiusPx = NpcLightRadiusPx(def.Light.Radius, size);   // reach scales with the footprint (see helper)
            // Center the halo on the footprint (the shell offsets a light by +half a tile), so a big NPC's
            // light sits under its body rather than its top-left tile.
            float lightOx = screenX + (spritePx - Constants.PicX) / 2f;
            float lightOy = screenY + (spritePx - Constants.PicX) / 2f;
            if (!InTownLight(state, offX + n.X, offY + n.Y) && LightReachesR(lightOx, lightOy, radiusPx))
            {
                float effectiveDark = InAlwaysDark(state, offX + n.X, offY + n.Y) ? 1f : state.GetCurrentDarkness();
                var lightLayer = SlideRenderLayer(n.Layer, n.PrevLayer, n.XOffset, n.YOffset);
                // Each mask is anchored to the tile it was traced from, so a mid-step NPC's shadows stay put
                // in the world while its halo slides — the halo's own offset is already in screen space.
                var lit = ReachAcrossStep(state, camera, offX + n.X, offY + n.Y,
                                          n.XOffset, n.YOffset, lightLayer, radiusPx / Constants.PicX);
                frame.Lights.Add(new LightSourceCmd(lightOx, lightOy, def.Light.Intensity, def.Light.Rgb,
                    radiusPx, def.Light.Flicker, lightId, effectiveDark,
                    lightLayer,   // torch follows the sprite's slide layer
                    lit.FromScreenX, lit.FromScreenY, lit.Radius, lit.From,
                    lit.Into, lit.IntoScreenX, lit.IntoScreenY, lit.Blend));
            }
        }
        if (!OnScreenSized(screenX, screenY, spritePx)) return;
        long elapsed = tickNow - n.AttackTimer;
        bool showAtk = n.Attacking && elapsed < AttackFrameMs;
        bool lockWalk = n.Attacking && elapsed < AttackLockMs;
        int animFrame = AnimFrame(showAtk, lockWalk, (int)n.XOffset, (int)n.YOffset, n.Dir);
        int spriteRow = def.Sprite;

        frame.Npcs.Add(new SpriteDrawCmd(screenX, screenY, spriteRow, animFrame, n.Dir, size,
            SlideRenderLayer(n.Layer, n.PrevLayer, n.XOffset, n.YOffset), def.SpriteSheet));

        bool isSightAggro = def.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.Guard;
        bool isHostile = isSightAggro || def.Behavior == NpcBehavior.AttackWhenAttacked;
        bool showBars = isHostile && (alwaysShowBars
            || IsInCombat(n.LastCombatMs, tickNow)
            || (isSightAggro && n.HasTarget)
            || hoveredHere
            || targetHere);

        float npcHpFrac = n.DispHp;
        float npcMpFrac = NpcShowMpSp ? n.DispMp : -1f;
        float npcSpFrac = NpcShowMpSp ? n.DispSp : -1f;

        // Cooldown bar, folded into the vital group as its bottom row (shares the group outline): NPCs get the
        // same swing/cast cooldown bar as players. npcCdFrac < 0 omits the row.
        long npcCdMs = Constants.NpcAttackCooldownMs * (state.Weather == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L);
        long npcActionElapsed = tickNow - n.AttackTimer;
        // NPC cooldown rows honor the ShowOtherCooldownBars option.
        bool npcCdShown = _showOtherCooldownBars && n.AttackTimer > 0 && npcActionElapsed < npcCdMs;
        float npcCdFrac = npcCdShown ? Math.Clamp(1f - npcActionElapsed / (float)npcCdMs, 0f, 1f) : -1f;

        // Group height = vital rows + the cooldown row (when shown); the whole group sits above/below the sprite.
        int npcActualBarH = ((npcHpFrac >= 0 ? 1 : 0) + (npcMpFrac >= 0 ? 1 : 0) + (npcSpFrac >= 0 ? 1 : 0) + (npcCdShown ? 1 : 0)) * BarH;

        bool npcBelow = screenY < BelowSpriteThreshold;
        float npcBarTopY, npcNameY;
        bool nameAlignBottom;
        if (npcBelow)
        {
            // Below sprite: the whole bar group sits directly under the sprite; name below it (top-aligned).
            npcBarTopY = screenY + spritePx + LabelGap;
            npcNameY = showBars ? npcBarTopY + npcActualBarH + LabelGap : npcBarTopY;
            nameAlignBottom = false;
        }
        else
        {
            // Above sprite: the whole bar group + name sit above.
            npcBarTopY = screenY - LabelGap - npcActualBarH;
            npcNameY = showBars ? npcBarTopY - LabelGap : screenY - LabelGap;
            nameAlignBottom = true;
        }

        if (showNames && !string.IsNullOrEmpty(def.Name))
        {
            int npcNameColor = def.Behavior switch
            {
                NpcBehavior.Guard => GameColor.Yellow,
                NpcBehavior.Friendly or NpcBehavior.Stationary => GameColor.BrightGreen,
                _ => GameColor.White,
            };
            frame.Names.Add(new TextDrawCmd(centerX, npcNameY, def.Name, npcNameColor, nameAlignBottom, Layer: n.Layer));
        }

        // Vendor marker: a gold '$' one line above the NPC name for a keeper NPC. Shown
        // independent of the name toggle — it's a functional "this NPC vends" indicator.
        if (state.NpcKeeperShop[n.Num] != 0)
            frame.Names.Add(new TextDrawCmd(centerX, npcNameY, "$", GameColor.Yellow, nameAlignBottom, LineOffset: 1, Layer: n.Layer));

        // Quest marker: a "?" (accept one here) or "!" (turn one in here) glyph above the name — colored when the
        // player can act, gray for a quest already accepted and still running. Stacks a line higher when the NPC is
        // also a keeper so it doesn't collide with the "$".
        int questGlyph = state.NpcQuestGlyph[n.Num];
        if (questGlyph != ClientState.QuestGlyphNone)
        {
            (string glyph, int color) = questGlyph switch
            {
                ClientState.QuestGlyphYellowBang => ("!", GameColor.Yellow),
                ClientState.QuestGlyphBlueBang => ("!", GameColor.BrightBlue),
                ClientState.QuestGlyphYellowQuestion => ("?", GameColor.Yellow),
                ClientState.QuestGlyphBlueQuestion => ("?", GameColor.BrightBlue),
                _ => ("!", GameColor.Gray),   // QuestGlyphGrayBang
            };
            int questLine = state.NpcKeeperShop[n.Num] != 0 ? 2 : 1;
            frame.Names.Add(new TextDrawCmd(centerX, npcNameY, glyph, color, nameAlignBottom, LineOffset: questLine, Layer: n.Layer));
        }

        // Conversation marker: a literal "..." above the name for an NPC that has a dialogue tree — yellow when
        // this character hasn't spoken to it yet, gray once spoken (per-character visited-log). Stacks above the
        // "$" and quest glyphs so all three can show at once.
        int convGlyph = state.NpcConvGlyph[n.Num];
        if (convGlyph != ClientState.ConvGlyphNone)
        {
            int convColor = convGlyph == ClientState.ConvGlyphUnspoken ? GameColor.Yellow : GameColor.Gray;
            int convLine = 1 + (state.NpcKeeperShop[n.Num] != 0 ? 1 : 0)
                             + (state.NpcQuestGlyph[n.Num] != ClientState.QuestGlyphNone ? 1 : 0);
            frame.Names.Add(new TextDrawCmd(centerX, npcNameY, "...", convColor, nameAlignBottom, LineOffset: convLine, Layer: n.Layer));
        }

        bool npcOutOfRange = targetHere && TargetOutOfRangeWorld(state, offX + n.X, offY + n.Y, def.EffectiveSize);
        bool npcNoLos = targetHere && !npcOutOfRange && TargetNoLineOfSightWorld(state, offX + n.X, offY + n.Y, n.Layer);
        if (targetHere)
            frame.TargetArrows.Add(new TargetArrowCmd(centerX, npcNameY, nameAlignBottom, npcOutOfRange, npcNoLos));

        if (showBars)
        {
            frame.Bars.Add(new BarDrawCmd(
                centerX,
                npcBarTopY,
                npcHpFrac, npcMpFrac, npcSpFrac,
                npcCdFrac,
                alwaysShowBars && IsInCombat(n.LastCombatMs, tickNow),
                IsTarget: alwaysShowBars && targetHere,
                OutOfRange: alwaysShowBars && npcOutOfRange,
                Size: size,
                Layer: n.Layer));
        }

        EmitNpcBubble(frame, n, tickNow, centerX, npcNameY, nameAlignBottom, nameLineH);
    }

    /// <summary>Emit head bubble + any drifters for an NPC. Drifters emitted first (oldest at top
    /// of stack) so the head draws on top during the brief overlap moment after a rapid replace.
    /// When the name has been flipped below the sprite (nameAlignBottom=false), the bubble follows
    /// — anchored to the name's bottom and drifting downward instead of upward.</summary>
    private static void EmitNpcBubble(RenderFrame frame, ClientMapNpc n, long tickNow,
        float centerX, float nameY, bool nameAlignBottom, float nameLineH)
    {
        bool hasHead = n.ChatBubbleText != null && tickNow < n.ChatBubbleEndMs;
        bool hasDrifters = n.ChatBubbleDrifters is { Count: > 0 };
        if (!hasHead && !hasDrifters) return;

        // Above sprite: anchor the bubble bottom above the name baseline (nameY is baseline).
        // Below sprite: anchor the bubble top below the name (nameY is name top, so name bottom = nameY + lineH).
        bool anchorBelow = !nameAlignBottom;
        float anchorY = anchorBelow
            ? nameY + nameLineH + LabelGap + ChatBubbleStyle.GapAboveName
            : nameY - LabelGap - ChatBubbleStyle.GapAboveName;
        float driftSign = anchorBelow ? +1f : -1f;

        if (hasDrifters)
        {
            foreach (var d in n.ChatBubbleDrifters!)
            {
                long elapsed = tickNow - d.DemotedMs;
                if (elapsed >= ChatBubbleStyle.FloatMs) continue;  // tick should have cleaned this; defensive
                float t = elapsed / (float)ChatBubbleStyle.FloatMs;
                float floatPx = t * ChatBubbleStyle.FloatPx;
                float alpha = 1f - t;
                frame.ChatBubbles.Add(new ChatBubbleDrawCmd(centerX, anchorY + driftSign * floatPx, d.Text, d.Color, alpha, anchorBelow));
            }
        }
        if (hasHead)
            frame.ChatBubbles.Add(new ChatBubbleDrawCmd(centerX, anchorY, n.ChatBubbleText!, n.ChatBubbleColor, 1f, anchorBelow));
    }

    // ── Players ───────────────────────────────────────────────────────────────

    private static void EmitPlayers(ClientState state, RenderFrame frame, Camera camera, long tickNow, bool alwaysShowBars,
        TargetRef hovered, TargetRef target,
        bool showOtherNames, bool showSelfName, int myIndex, float nameLineH)
    {
        // Local player is emitted last so their sprite draws on top of every other player in the
        // Players layer — without this, any remote player whose slot index is higher than MyIndex
        // would draw over the local sprite during overlap (movement, same-tile stacking, etc.).
        int meSlot = state.MyIndex;
        // Per-tile corpse counter so several corpses on one tile stack their name labels. Reuse the
        // static buffer (cleared here) instead of allocating a dictionary every frame on this render hot path.
        _corpseStack.Clear();
        var corpseStack = _corpseStack;
        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            if (i == meSlot) continue;
            EmitOnePlayer(state, frame, camera, i, tickNow, alwaysShowBars, hovered, target,
                showOtherNames, showSelfName, myIndex, nameLineH, corpseStack);
        }
        if (meSlot >= 1 && meSlot <= Constants.MaxPlayers)
        {
            EmitOnePlayer(state, frame, camera, meSlot, tickNow, alwaysShowBars, hovered, target,
                showOtherNames, showSelfName, myIndex, nameLineH, corpseStack);
        }
    }

    // Whether the viewer's guild is in a LIVE war (past warmup) with the observed player's guild — drives the
    // overhead crossed-swords marker. Allocation-free (this runs per visible player per frame); the Wars list
    // is tiny (<= a handful). Territory-war swords ride the same marker, shown only when both players are
    // in the contested territory.
    private static bool ViewerAtWarWith(ClientState state, int observedGuildId)
    {
        if (observedGuildId <= 0) return false;
        var gi = state.GuildInfo;
        if (gi is null || !gi.InGuild) return false;
        foreach (var w in gi.Wars)
        {
            if (w.OpponentIndex == observedGuildId && w.Status != GuildWarStatus.Warmup)
                return true;
        }

        return false;
    }

    // Territory-war swords: the observed player is a RIVAL participant in the viewer's live contest, standing in
    // the contested territory. Rides the same overhead crossed-swords marker as a grudge war.
    private static bool ViewerContestOpponent(ClientState state, PlayerRecord p)
    {
        var c = state.Contest;
        if (c is null || p.GuildId <= 0 || p.GuildId == state.Me.GuildId) return false;
        bool participant = false;
        foreach (var s in c.Scores) if (s.GuildId == p.GuildId) { participant = true; break; }
        return participant && MapGroupOfLoaded(state, p.Map) == c.TerritoryIndex;
    }

    // The MapGroup of a currently-loaded map (center or a neighbor cell), or 0 if not loaded — for the
    // territory-membership check above without a server round-trip.
    private static int MapGroupOfLoaded(ClientState state, int mapNum)
    {
        if (mapNum == state.CenterMapNum) return state.Map.MapGroup;
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (state.NeighborMapNums[col, row] == mapNum && state.NeighborMaps[col, row] is { } m)
                    return m.MapGroup;
            }
        }

        return 0;
    }

    private static void EmitOnePlayer(ClientState state, RenderFrame frame, Camera camera, int i,
        long tickNow, bool alwaysShowBars,
        TargetRef hovered, TargetRef target,
        bool showOtherNames, bool showSelfName, int myIndex, float nameLineH,
        Dictionary<(int, int, int), int> corpseStack)
    {
        var p = state.Players[i];
        if (string.IsNullOrEmpty(p.Name) || p.Sprite < 0) return;

        // Place each player at their own map's grid cell.  The local player is always
        // centered; others are positioned via their server-synced map number.
        int offX, offY;
        if (i == state.MyIndex)
        {
            offX = CenterWorldOffX(state);
            offY = CenterWorldOffY(state);
        }
        else
        {
            var off = CellOffsetForMap(state, p.Map);
            if (off is null) return; // on a map we don't currently observe
            (offX, offY) = off.Value;
        }

        var (screenX, screenY) = camera.WorldTileToScreen(offX + p.X, offY + p.Y, p.XOffset, p.YOffset);
        // An AlwaysLit map suppresses the halo; AlwaysDark maps are exempt (see InTownLight / EmitOneNpc).
        if (!InTownLight(state, offX + p.X, offY + p.Y) && LightReaches(screenX, screenY))
        {
            float effectiveDark = InAlwaysDark(state, offX + p.X, offY + p.Y) ? 1f : state.GetCurrentDarkness();
            var torch = LightSpec.Torch;   // players are the fixed default torch
            var torchLayer = SlideRenderLayer(p.Layer, p.PrevLayer, p.XOffset, p.YOffset);
            var lit = ReachAcrossStep(state, camera, offX + p.X, offY + p.Y,
                                      p.XOffset, p.YOffset, torchLayer, torch.Radius);
            frame.Lights.Add(new LightSourceCmd(screenX, screenY, torch.Intensity, torch.Rgb,
                torch.Radius * Constants.PicX, torch.Flicker, i, effectiveDark,
                torchLayer,   // torch follows the sprite's slide layer
                lit.FromScreenX, lit.FromScreenY, lit.Radius, lit.From,
                lit.Into, lit.IntoScreenX, lit.IntoScreenY, lit.Blend));
        }
        if (!OnScreen(screenX, screenY)) return;

        // Corpse: a dead player shows no live sprite/bars — a tile-sized (32x32) red X drawn in
        // DrawWorld, plus the name, always shown (unaffected by the name toggle). When several corpses share
        // one tile, stack their names DOWNWARD a line each so none overlap. The tile stays passable
        // server-side; offline players already leave the map (so no corpse shows for a logged-off owner).
        if (p.Dead)
        {
            var tileKey = (p.Map, p.X, p.Y);
            int stackIndex = corpseStack.GetValueOrDefault(tileKey);
            corpseStack[tileKey] = stackIndex + 1;
            if (stackIndex == 0)   // one red X per tile, however many corpses stack on it
                frame.Corpses.Add(new CorpseDrawCmd(screenX, screenY, p.Layer));
            // The corpse's name is a WORLD-layer label (drawn with the red X, below items/NPCs/players) — NOT
            // the floating Names overlay — so a live entity walking over it always draws on top. Stacked
            // DOWNWARD a line each when corpses share a tile so they don't overlap.
            frame.CorpseNames.Add(new TextDrawCmd(screenX + Constants.PicX / 2f, screenY + Constants.PicY + LabelGap,
                p.Name, GameColor.White, LineOffset: -stackIndex, Layer: p.Layer));
            return;
        }

        long elapsed = tickNow - p.AttackTimer;
        bool showAtk = p.Attacking && elapsed < AttackFrameMs;
        bool lockWalk = p.Attacking && elapsed < AttackLockMs;
        int animFrame = AnimFrame(showAtk, lockWalk, (int)p.XOffset, (int)p.YOffset, p.Dir);
        int spriteRow = p.Sprite;

        frame.Players.Add(new SpriteDrawCmd(screenX, screenY, spriteRow, animFrame, p.Dir,
            Layer: SlideRenderLayer(p.Layer, p.PrevLayer, p.XOffset, p.YOffset), Sheet: p.SpriteSheet));

        long nowUtcForGrace = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool showAsPk = p.IsPk(nowUtcForGrace) && p.PkGraceUntilUtc <= nowUtcForGrace;
        // Observer mode reads as a bystander: grey overhead, whatever the access colour would have been.
        // Only the world name — chat and the HUD keep PlayerNameColor so an admin stays identifiable there.
        int nameColor = p.GodMode ? GameColor.Gray : PlayerNameColor.For(showAsPk, p.Access);
        // Aggressor flash: when the player has thrown the first hit at a clean target inside
        // the 30 s aggressor window (and isn't yet a solid-red PKer), alternate the name color
        // between BrightRed and Yellow at ~1.25 Hz so observers see a clearly-flashing warning
        // distinct from PK red.
        if (!showAsPk && nowUtcForGrace < p.AggressorUntilUtc)
            nameColor = (tickNow / 400) % 2 == 0 ? GameColor.BrightRed : GameColor.Yellow;

        bool hoveredHere = hovered.Kind == TargetKind.Player && hovered.A == i;
        bool plrTargetHere = target.Kind == TargetKind.Player && target.A == i;
        bool showBars = alwaysShowBars || IsInCombat(p.LastCombatMs, tickNow) || hoveredHere || plrTargetHere;

        bool showFullVitals = PlayerShowMpSp
            || i == state.MyIndex
            || (state.Party.Active && i == state.Party.Index);
        float plrHpFrac = p.DispHp;
        float plrMpFrac = showFullVitals ? p.DispMp : -1f;
        float plrSpFrac = showFullVitals ? p.DispSp : -1f;

        // Cooldown bar, folded into the vital group as its bottom row (shares the group's one outline): spans the
        // swing/cast cooldown (doubled by Heavy Wind) so the downtime cadence is readable.
        // plrCdFrac < 0 omits the row. `elapsed` = tickNow - AttackTimer.
        long cdMs = Constants.PlayerAttackCooldownMs * (state.Weather == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L);
        // Show the cooldown row only if this entity's option is on: the local player's own bar (i == myIndex)
        // honors ShowCooldownBar; every other player honors ShowOtherCooldownBars.
        bool cdShown = p.AttackTimer > 0 && elapsed < cdMs
            && (i == myIndex ? _showCooldownBar : _showOtherCooldownBars);
        float plrCdFrac = cdShown ? Math.Clamp(1f - elapsed / (float)cdMs, 0f, 1f) : -1f;

        // Group height = vital rows + the cooldown row (when shown); the whole group sits above/below the sprite.
        int plrActualBarH = ((plrHpFrac >= 0 ? 1 : 0) + (plrMpFrac >= 0 ? 1 : 0) + (plrSpFrac >= 0 ? 1 : 0) + (cdShown ? 1 : 0)) * BarH;

        bool plrBelow = screenY < BelowSpriteThreshold;
        float plrBarTopY, plrNameY;
        bool plrNameAlignBottom;
        if (plrBelow)
        {
            // Below sprite: the whole bar group sits directly under the sprite; name below it (top-aligned).
            plrBarTopY = screenY + Constants.PicY + LabelGap;
            plrNameY = showBars ? plrBarTopY + plrActualBarH + LabelGap : plrBarTopY;
            plrNameAlignBottom = false;
        }
        else
        {
            // Above sprite: the whole bar group + name sit above.
            plrBarTopY = screenY - LabelGap - plrActualBarH;
            plrNameY = showBars ? plrBarTopY - LabelGap : screenY - LabelGap;
            plrNameAlignBottom = true;
        }

        bool showThisName = (i == myIndex) ? showSelfName : showOtherNames;
        if (showThisName)
        {
            frame.Names.Add(new TextDrawCmd(screenX + Constants.PicX / 2, plrNameY, p.Name, nameColor, plrNameAlignBottom, Layer: p.Layer));
            // ONE overhead guild line directly above the player name, in the guild's chosen color (a neutral
            // default until the leader picks one): "Guild {Rank} ({Standing})". The guild's color (distinct from
            // the white player name) already sets it apart, so the name is plain — no angle brackets.
            // The member's rank word (Officer+) always renders to the RIGHT of the name; the guild's seasonal
            // standing "(N)" is appended only when the leader toggle is on and the guild is ranked.
            // A crossed-swords marker prefixes the line when the viewer's guild is at war with this guild. The
            // rank word + standing are assembled + localized in the draw layer (which owns the string table);
            // only the numeric rank + standing travel on the command. Shares the name's show rules + alignment.
            if (!string.IsNullOrEmpty(p.GuildName))
            {
                int guildRgb = p.GuildColor != 0 ? p.GuildColor : GuildNameDefaultRgb;
                bool atWar = ViewerAtWarWith(state, p.GuildId) || ViewerContestOpponent(state, p);
                int rankWord = p.GuildRank >= GuildRank.Officer ? (int)p.GuildRank : 0;   // 0 = plain Member, no word
                int standing = p.GuildShowRank && p.GuildStanding > 0 ? p.GuildStanding : 0;   // 0 = don't show
                frame.Names.Add(new TextDrawCmd(screenX + Constants.PicX / 2, plrNameY, p.GuildName, GameColor.White,
                    plrNameAlignBottom, RgbOverride: guildRgb, LineOffset: 1, GuildRankWord: rankWord, AtWar: atWar,
                    GuildStanding: standing, Layer: p.Layer));
            }
        }

        bool plrOutOfRange = plrTargetHere && TargetOutOfRangeWorld(state, offX + p.X, offY + p.Y);
        bool plrNoLos = plrTargetHere && !plrOutOfRange && TargetNoLineOfSightWorld(state, offX + p.X, offY + p.Y, p.Layer);
        if (plrTargetHere)
            frame.TargetArrows.Add(new TargetArrowCmd(screenX + Constants.PicX / 2, plrNameY, plrNameAlignBottom, plrOutOfRange, plrNoLos));

        if (showBars)
        {
            frame.Bars.Add(new BarDrawCmd(
                screenX + Constants.PicX / 2,
                plrBarTopY,
                plrHpFrac, plrMpFrac, plrSpFrac,
                plrCdFrac,
                alwaysShowBars && IsInCombat(p.LastCombatMs, tickNow),
                IsTarget: alwaysShowBars && plrTargetHere,
                OutOfRange: alwaysShowBars && plrOutOfRange,
                Layer: p.Layer));
        }

        EmitPlayerBubble(frame, p, tickNow, screenX + Constants.PicX / 2, plrNameY, plrNameAlignBottom, nameLineH);
    }

    /// <summary>Emit head bubble + any drifters for a player. See <c>EmitNpcBubble</c> for the model.</summary>
    private static void EmitPlayerBubble(RenderFrame frame, PlayerRecord p, long tickNow,
        float centerX, float nameY, bool nameAlignBottom, float nameLineH)
    {
        bool hasHead = p.ChatBubbleText != null && tickNow < p.ChatBubbleEndMs;
        bool hasDrifters = p.ChatBubbleDrifters is { Count: > 0 };
        if (!hasHead && !hasDrifters) return;

        bool anchorBelow = !nameAlignBottom;
        float anchorY = anchorBelow
            ? nameY + nameLineH + LabelGap + ChatBubbleStyle.GapAboveName
            : nameY - LabelGap - ChatBubbleStyle.GapAboveName;
        float driftSign = anchorBelow ? +1f : -1f;

        if (hasDrifters)
        {
            foreach (var d in p.ChatBubbleDrifters!)
            {
                long elapsed = tickNow - d.DemotedMs;
                if (elapsed >= ChatBubbleStyle.FloatMs) continue;
                float t = elapsed / (float)ChatBubbleStyle.FloatMs;
                float floatPx = t * ChatBubbleStyle.FloatPx;
                float alpha = 1f - t;
                frame.ChatBubbles.Add(new ChatBubbleDrawCmd(centerX, anchorY + driftSign * floatPx, d.Text, d.Color, alpha, anchorBelow));
            }
        }
        if (hasHead)
            frame.ChatBubbles.Add(new ChatBubbleDrawCmd(centerX, anchorY, p.ChatBubbleText!, p.ChatBubbleColor, 1f, anchorBelow));
    }

    private static float Frac(int cur, int max) =>
        max > 0 ? Math.Clamp((float)cur / max, 0f, 1f) : -1f;

    private static bool IsInCombat(long lastCombatMs, long tickNow) =>
        ClientState.InCombatAt(lastCombatMs, tickNow);

    // True when a target at WORLD tile (targetWX,targetWY) is outside the local player's spell
    // circle, computed as if the player stood in the middle of the map regardless of camera
    // edge-clamping (so a clamp map that renders extra tiles never widens range). Mirrors the
    // server cast gate so a gray arrow means "can't cast it". The circle is symmetric so the
    // check is the same for any target type — no separate mutual variant needed. World coords
    // keep it correct for entities on neighbor cells (not just the center map).
    private static bool TargetOutOfRangeWorld(ClientState state, int targetWX, int targetWY, int targetSize = 1)
    {
        var me = state.Me;
        int myWX = CenterWorldOffX(state) + me.X, myWY = CenterWorldOffY(state) + me.Y;
        // Footprint-aware so the gray arrow matches the server: an oversize NPC is in range by its body, not (X,Y).
        return !WorldCoordHelper.IsInSpellRange(myWX, myWY, 1, targetWX, targetWY, targetSize);
    }

    // Inverse of ClientLineOfSight.HasClearFromLocalPlayer — same algorithm, framed as the
    // "should this arrow turn gray?" predicate the emit sites already use.
    private static bool TargetNoLineOfSightWorld(ClientState state, int targetWX, int targetWY, WorldLayer targetLayer)
        => !ClientLineOfSight.HasClearFromLocalPlayer(state, targetWX, targetWY, targetLayer);

    /// <summary>
    /// Frame 0 = neutral, 1 = stride, 2 = attack.
    /// While attacking: within the first 500ms → frame 2; 500–1000ms → frame 0 (idle, not walk).
    /// Walk stride frame switches at the tile midpoint (offset crosses ±PicY/2).
    /// </summary>
    private static int AnimFrame(bool attacking, bool lockWalk, int xOffset, int yOffset, Direction dir)
    {
        if (attacking) return 2;
        if (lockWalk) return 0;  // attack in progress, frame expired — show idle not walk
        if (xOffset == 0 && yOffset == 0) return 0;
        return dir switch
        {
            // Positive offset (starts +PicX/Y, decreases to 0): stride when < half
            Direction.Up => yOffset < (Constants.PicY / 2) ? 1 : 0,
            Direction.Left => xOffset < (Constants.PicX / 2) ? 1 : 0,
            // Negative offset (starts -PicX/Y, increases to 0): stride when < -half
            Direction.Down => yOffset < -(Constants.PicY / 2) ? 1 : 0,
            Direction.Right => xOffset < -(Constants.PicX / 2) ? 1 : 0,
            _ => 0,
        };
    }
}
