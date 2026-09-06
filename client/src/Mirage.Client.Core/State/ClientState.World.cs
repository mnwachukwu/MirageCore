using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>Ambient world state and the frame-level bookkeeping over it: territory contests,
/// weather and time of day, animation clocks, and the grid shift on a seamless crossing.</summary>
public sealed partial class ClientState
{
    // ── Territory contest render state (participant-only, pushed live) ─────────────
    /// <summary>Latest live territory-contest render state (capture points + KotH scores), or null when no
    /// contest is active for us. Pushed only to participant-guild members; drives the in-world flags/circles/
    /// names + the capture-status/score HUD. An Active=false push at war end sets this back to null.</summary>
    public TerritoryContestPacket? Contest { get; private set; }

    public void SetContest(TerritoryContestPacket? contest) => Contest = contest;

    /// <summary>The capture point whose radius this player is standing in, or null.
    ///
    /// <para>Measured in world coordinates across the 3x3, which is the reach the SERVER scores with — a
    /// point near a map edge spills its zone over the seam, so the tile it is held from is not always on the
    /// point's own map.</para></summary>
    public ContestPointView? ContestPointHeld()
    {
        if (Contest is not { Active: true } contest) return null;
        var (myWx, myWy) = CenterToWorld(Me.X, Me.Y);
        foreach (var pt in contest.Points)
        {
            if (pt.Layer != Me.Layer) continue;
            if (!ContestCellOf(pt.Map, out int col, out int row)) continue;
            var (ptWx, ptWy) = ToWorld(col, row, pt.X, pt.Y);
            if (TerritoryContestFormulas.WithinRadius(myWx, myWy, ptWx, ptWy, Constants.TerritoryCapturePointRadius))
                return pt;
        }
        return null;
    }

    /// <summary>True when this player is AT the contest: standing in the contested territory, or holding one
    /// of its points from wherever that point's zone reaches. Gates the score HUD and the off-screen
    /// point markers, which are both for someone taking part rather than passing through.</summary>
    public bool AtContest()
    {
        if (Contest is not { Active: true } contest) return false;
        return Map.MapGroup == contest.TerritoryIndex || ContestPointHeld() is not null;
    }

    /// <summary>Which 3x3 cell a map occupies; false when it is not one of the nine loaded.</summary>
    public bool ContestCellOf(int mapNum, out int col, out int row)
    {
        for (col = 0; col < 3; col++)
            for (row = 0; row < 3; row++)
                if (NeighborMapNums[col, row] == mapNum) return true;
        col = row = 0;
        return false;
    }

    // ── World events ──────────────────────────────────────────────────────────

    public WeatherType Weather { get; set; }
    public TimePhase TimePhase { get; set; }
    public float TimeProgress { get; set; }
    public long TimePhaseReceivedMs { get; set; }

    public float GetInterpolatedProgress()
    {
        long phaseDuration = TimePhase switch
        {
            TimePhase.Dusk => Constants.TodDuskDurationMs,
            TimePhase.Night => Constants.TodNightDurationMs,
            TimePhase.Dawn => Constants.TodDawnDurationMs,
            _ => Constants.TodDayDurationMs,
        };
        float elapsed = (Environment.TickCount64 - TimePhaseReceivedMs) / (float)phaseDuration;
        return Math.Clamp(TimeProgress + elapsed, 0f, 1f);
    }

    public float GetCurrentDarkness()
    {
        float p = GetInterpolatedProgress();
        return TimePhase switch
        {
            TimePhase.Day => 0f,
            TimePhase.Dusk => p,
            TimePhase.Night => 1f,
            TimePhase.Dawn => 1f - p,
            _ => 0f,
        };
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    /// <summary>Advances by one every <see cref="Constants.MapAnimIntervalMs"/> (250 ms). Animated tile
    /// stacks pick their visible frame from this counter via <see cref="LayerCell.VisibleAnimIndex"/>.</summary>
    public int MapAnimFrame { get; set; }
    public long MapAnimTimer { get; set; }

    // ── HUD snap flag ─────────────────────────────────────────────────────────

    /// <summary>
    /// Set by the packet handler when our HP hits 0 (death). Survives within-frame
    /// packet batches so the HUD can snap bars on respawn even if HP=0 and HP=MaxHp
    /// both arrive before the next Tick().
    /// </summary>
    public bool SnapVitals { get; set; }

    /// <summary>Latest party-partner snapshot pushed by the server; empty Name = not in a party.</summary>
    public PartySnapshot Party { get; } = new();

    /// <summary>Clear all map entities when warping to a new map.</summary>
    public void ClearMapState()
    {
        for (int i = 1; i <= PlayerSlots; i++)
        {
            if (i == MyIndex) continue;
            Players[i] = new PlayerRecord();
        }
        // Snap our own movement interpolation so we appear instantly at the warp destination.
        var me = Players[MyIndex];
        me.XOffset = 0;
        me.YOffset = 0;
        me.Moving = MovementType.None;
        // Clear the attack animation + cooldown so the player isn't left mid-swing or on cooldown at the
        // warp destination (the server makes the matching reset in its death paths).
        me.Attacking = false;
        me.AttackTimer = 0;
        MapItems.Clear();
        for (int i = 1; i <= Constants.MaxMapNpcs; i++) MapNpcs[i] = new ClientMapNpc();
        TraversalNpcs.Clear();
        TempTile.Clear();
        BloodByMap.Clear();
        // Drop stale neighbor maps, their map numbers, and their entities; the server
        // re-pushes them for the new center.  Keep the center cell ([1,1]).
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (c == 1 && r == 1) continue;
                NeighborMaps[c, r] = null;
                NeighborMapNums[c, r] = 0;
                var npcCell = NeighborNpcs[c, r];
                for (int i = 1; i <= Constants.MaxMapNpcs; i++) npcCell[i] = new ClientMapNpc();
                NeighborItems[c, r].Clear();
                NeighborTempTiles[c, r].Clear();
            }
        }

        MapStateCleared?.Invoke();
    }

    /// <summary>
    /// Seamless border crossing: re-frame the whole 3×3 grid so the cell the player crossed into
    /// becomes the new center, preserving every already-loaded map and its entities (no flicker, no
    /// reload).  The data slides one cell opposite <paramref name="crossDir"/>; the row/column that
    /// scrolls off is dropped and the newly-revealed edge is left empty for the server to fill.
    /// Traversal NPCs need no shifting — they're keyed by identity and placed by CurrentMapNum each frame.
    /// </summary>
    /// <summary>The 3×3 cell a step in <paramref name="dir"/> crosses into; the center for anything else.
    /// One definition, because both the client's predicted cross and the server's confirmed one have to
    /// agree about which cell is about to become the center.</summary>
    public static (int Col, int Row) CellToward(Direction dir) => dir switch
    {
        Direction.Up => (1, 0),
        Direction.Down => (1, 2),
        Direction.Left => (0, 1),
        Direction.Right => (2, 1),
        _ => (1, 1),
    };

    /// <summary>The map loaded in that cell, or null if nothing is there yet.
    ///
    /// <para>🔴 Not the same question as whether the center map DECLARES a neighbor that way. The grid is
    /// filled asynchronously — a warp empties it and the server re-pushes each cell — so there is a real
    /// window where <see cref="MapRecord.Down"/> names a map and the cell below is still empty. Crossing
    /// on the declaration alone shifts that emptiness into the center, and <see cref="Map"/> is typed as
    /// though it can never be null.</para></summary>
    public MapRecord? NeighborToward(Direction dir)
    {
        (int col, int row) = CellToward(dir);
        return NeighborMaps[col, row];
    }

    public void ShiftGrid(Direction crossDir)
    {
        (int dc, int dr) = crossDir switch
        {
            Direction.Up => (0, 1),     // crossed up → content slides down a row; new center = old [1,0]
            Direction.Down => (0, -1),
            Direction.Left => (1, 0),
            Direction.Right => (-1, 0),
            _ => (0, 0),
        };
        if (dc == 0 && dr == 0) return;

        // Maps + map numbers shift in place — here [1,1] genuinely holds the center.
        ShiftCells(NeighborMaps, dc, dr, () => null);
        ShiftCells(NeighborMapNums, dc, dr, () => 0);

        // Entity grids keep the center in MapNpcs/MapItems/TempTile (their NeighborX[1,1] is unused).
        // Park the center array in [1,1], shift, then pull the new center back out and leave [1,1] fresh.
        NeighborNpcs[1, 1] = MapNpcs;
        ShiftCells(NeighborNpcs, dc, dr, InitMapNpcs);
        MapNpcs = NeighborNpcs[1, 1];
        NeighborNpcs[1, 1] = InitMapNpcs();

        NeighborItems[1, 1] = MapItems;
        ShiftCells(NeighborItems, dc, dr, InitMapItems);
        MapItems = NeighborItems[1, 1];
        NeighborItems[1, 1] = InitMapItems();

        NeighborTempTiles[1, 1] = TempTile;
        ShiftCells(NeighborTempTiles, dc, dr, NewTempTile);
        TempTile = NeighborTempTiles[1, 1];
        NeighborTempTiles[1, 1] = NewTempTile();

        CenterMapNum = NeighborMapNums[1, 1];

        // Blood is keyed by map number (not shifted); drop maps that just scrolled out of the observable area.
        PruneBloodToObserved();

        // Prune visiting guests that fell out of the shifted grid.  Unlike the per-cell arrays (re-init'd
        // above), the identity-keyed TraversalNpcs dict isn't shifted — so when WE move away from a guest
        // it would otherwise linger at its stale last-seen tile.  The server only despawns a guest when
        // the GUEST leaves view, not when the player does, so without this a guest that dies/moves while
        // off-view stays stale and (now that guests block movement) leaves a phantom collision on return.
        // Any guest still in the new region is re-sent by the region re-sync, so dropping it here is safe.
        List<(int, int)>? drop = null;
        foreach (var kv in TraversalNpcs)
        {
            if (CellForMap(kv.Value.CurrentMapNum) is null)
                (drop ??= new()).Add(kv.Key);
        }

        if (drop is not null)
        {
            foreach (var key in drop)
                TraversalNpcs.Remove(key);
        }

        // World-pixel offset the data slid. Subscribers re-anchor their world-pixel state to match.
        GridShifted?.Invoke(dc * MapTilesX * Constants.PicX,
                            dr * MapTilesY * Constants.PicY);
    }

    private static OpenDoors NewTempTile() => new();

    // Slides a 3×3 grid's contents by (dc,dr): new[c,r] = old[c-dc,r-dr], filling off-grid cells fresh.
    private static void ShiftCells<T>(T[,] grid, int dc, int dr, Func<T> fresh)
    {
        var old = (T[,])grid.Clone();
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                int sc = c - dc, sr = r - dr;
                grid[c, r] = (sc is >= 0 and <= 2 && sr is >= 0 and <= 2) ? old[sc, sr] : fresh();
            }
        }
    }

    // Classes and map groups
    /// <summary>1-based; index 0 is unused dummy. Sized dynamically from server.</summary>
    public ClassRecord[] Classes { get; set; } = new ClassRecord[1]; // placeholder until server sends

    // MapGroup defs, cached like the other shared defs: filled in bulk at join (SendMapGroups) and
    // refreshed per-group on a live editor save (UpdateMapGroup). The client resolves a map's EFFECTIVE
    // inheritable values against these on demand via the *Of helpers below — the client-side mirror of the
    // server's GameWorld.*Of(mapNum) — instead of the server baking resolved values into each map packet. That
    // is what lets a group edit land live with no map reload or revision bump. Index 0 unused; a null slot means
    // "no such group" and resolves to the map's own raw values / hard defaults.
    public MapGroupRecord?[] MapGroups { get; private set; } = new MapGroupRecord?[RecordLimits.Default.MapGroups + 1];

    /// <summary>The cached MapGroup a map belongs to, or null (group-less map, or the group not yet received).</summary>
    public MapGroupRecord? GroupOf(MapRecord? map)
    {
        int g = map?.MapGroup ?? 0;
        return g > 0 && g <= Limits.MapGroups ? MapGroups[g] : null;
    }

    // Effective inheritable map values — resolve the map's own value over its group's over the hard default via
    // the shared MapGroupResolve, mirroring GameWorld.*Of on the server. Null-map-safe so render/predict sites can
    // pass an unloaded neighbor cell without a guard.
    public MapMoral MoralOf(MapRecord? map) => map is null ? MapMoral.None : MapGroupResolve.Moral(map, GroupOf(map));
    public int MusicOf(MapRecord? map) => map is null ? 0 : MapGroupResolve.Music(map, GroupOf(map));
    public bool IndoorsOf(MapRecord? map) => map is not null && MapGroupResolve.Indoors(map, GroupOf(map));
    /// <summary>How this map treats the day/night cycle. One answer rather than two flags: AlwaysLit and
    /// AlwaysDark are mutually exclusive, and resolving them separately would let a map's "lit" and its group's
    /// "dark" both come out true. An unloaded neighbor cell lights normally.</summary>
    public MapLighting LightingOf(MapRecord? map) =>
        map is null ? MapLighting.Normal : MapGroupResolve.Lighting(map, GroupOf(map));
}
