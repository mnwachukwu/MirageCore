using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.World;

public sealed class GameWorld
{
    /// <summary>How many of each record family this world has room for, from the operator's config.
    ///
    /// <para>Every bounds check on a record number reads THIS rather than a constant, and every array
    /// below is cut from it, so a check can never disagree with the allocation it is guarding.</para></summary>
    public RecordLimits Limits { get; }

    // World data IDs stay 1-based: indices 0..Max, with 0 as an unused dummy.
    // Changing the 1-based model would break all saved data, migration, and the wire protocol; the
    // LENGTHS are the operator's to set (see Limits).
    /// <summary>What this world calls itself, from `world.json`; blank when it has none. Operator-facing
    /// only — a player sees the GAME's name, never this. Held here so an editor session can be told which
    /// set of records it has opened.</summary>
    public string WorldName { get; set; } = "";

    public MapRecord[] Maps { get; }
    public TempTileState[] TempTiles { get; }
    public ItemRecord[] Items { get; }
    public NpcRecord[] Npcs { get; }
    public ShopRecord[] Shops { get; }
    public SpellRecord[] Spells { get; }
    public QuestRecord[] Quests { get; }
    public ConversationRecord[] Conversations { get; }

    public ClassRecord[] Classes { get; } = new ClassRecord[Constants.MaxClasses + 1];

    // Guilds are runtime-created and UNBOUNDED (no cap): a sparse map keyed by guild Index (like
    // MapBlood), each entry backed by guilds/guild{Index}.json. There is no fixed slot array.
    public Dictionary<int, GuildRecord> Guilds { get; } = new();

    /// <summary>The largest guild number ever issued, retired ones included. Seeded from disk at boot and
    /// bumped on every creation, so a number is spoken for permanently. A disbanded guild keeps its file,
    /// flagged <see cref="Records.GuildRecord.Disbanded"/> — see
    /// <see cref="Persistence.IPersistenceService.RetireGuildAsync"/>.</summary>
    public int HighestGuildNumber { get; set; }

    // Player marketplace listings, keyed by global listing id; loaded at boot, mutated by MarketSystem on the
    // game thread. Unbounded like Guilds (the per-seller cap bounds each account's share).
    public Dictionary<int, MarketListing> MarketListings { get; } = new();

    // Rolling completed-sales history (seller Sales tab + on-disk admin audit), bounded to MaxMarketSalesLog.
    public List<MarketSale> MarketSales { get; } = new();

    // Map groups are also UNBOUNDED (guild-style), keyed by group Index, backed by
    // map_groups/map_group{Index}.json. Maps reference a group via MapRecord.MapGroup.
    public Dictionary<int, MapGroupRecord> MapGroups { get; } = new();

    // What each territory has become since the server started running — owner, income, war-night queue.
    // Keyed by the MAP GROUP index, because a territory IS the maps of its group and the key is the whole of
    // the link. Backed by data/territories/territory{group}.json, so none of it rides along in a world.
    public Dictionary<int, TerritoryRecord> Territories { get; } = new();

    // Perpetual season-leaderboard archive, loaded from seasons/season{N}.json on boot and appended
    // when a season ends; ascending by season number. Served to the historical-season browser (read-only).
    public List<SeasonArchive> SeasonArchives { get; } = new();

    // Income accumulators (guild PendingPerkIncome + territory PendingTerritoryIncome) mutate per-kill in memory; these
    // sets flag which guilds/territories have unsaved accrual so the periodic save + shutdown flush persist
    // them (GuildScheduleSystem.FlushDirtyAccumulators) — the accrual sites just Add here, no per-kill write.
    public HashSet<int> DirtyGuilds { get; } = new();
    public HashSet<int> DirtyTerritories { get; } = new();

    // ── Effective map properties ─────────────────────────────────────────────────
    // A map's inheritable properties (Moral/Music/Shop/Indoors/lighting/Boot) are nullable: null = inherit
    // from the map's MapGroup. ALWAYS resolve them through these helpers, never a raw Maps[n].X read, so the
    // group fallback is honored everywhere. Group-less maps take the fast path (GroupOf returns null).
    public MapGroupRecord? GroupOf(int mapNum)
    {
        int gid = Maps[mapNum].MapGroup;
        return gid > 0 ? MapGroups.GetValueOrDefault(gid) : null;
    }
    public MapMoral MoralOf(int mapNum) => MapGroupResolve.Moral(Maps[mapNum], GroupOf(mapNum));
    public int MusicOf(int mapNum) => MapGroupResolve.Music(Maps[mapNum], GroupOf(mapNum));

    /// <summary>The EFFECTIVE map-enter/leave greeting for <paramref name="mapNum"/>,
    /// resolving each field map-over-group. The single source the server speaks the greeting from
    /// (MovementSystem.OnJoinMap/OnLeaveMap) and detects a greeting change against (MovementSystem).</summary>
    public MapGreeting GreetingOf(int mapNum) => MapGroupResolve.Greeting(Maps[mapNum], GroupOf(mapNum));

    /// <summary>The shop/inn whose <see cref="ShopRecord.Keeper"/> is NPC template
    /// <paramref name="npcNum"/>, or 0 if none. Linear scan over
    /// the small, static shop table.</summary>
    public int ShopAssignedToNpc(int npcNum)
    {
        if (npcNum <= 0) return 0;
        for (int s = 1; s <= Limits.Shops; s++)
            if (Shops[s].Keeper == npcNum) return s;
        return 0;
    }

    /// <summary>Keeper-shop KIND for the client wire: 0 = none, 1 = store, 2 = inn. Drives the $ vendor glyph,
    /// the attack-key/right-click interact routing, and the right-click menu label (Shop vs Inn). Recomputed
    /// and re-broadcast (UpdateNpcPacket) whenever a shop's Keeper or ShopType changes.</summary>
    public int KeeperShopKind(int npcNum)
    {
        int s = ShopAssignedToNpc(npcNum);
        if (s <= 0) return 0;
        return Shops[s].ShopType == ShopType.Inn ? 2 : 1;
    }

    /// <summary>The NPC conversation attached to NPC template <paramref name="npcNum"/> via
    /// <see cref="ConversationRecord.SpeakerNpc"/>, or 0 if none. First non-empty match wins (one
    /// conversation per NPC); linear scan over the small, static table (mirrors <see cref="ShopAssignedToNpc"/>).</summary>
    public int ConversationForNpc(int npcNum)
    {
        if (npcNum <= 0) return 0;
        for (int c = 1; c <= Limits.Conversations; c++)
            if (Conversations[c].SpeakerNpc == npcNum && Conversations[c].TrimmedName.Length > 0) return c;
        return 0;
    }

    /// <summary>True when the map NPC at (<paramref name="mapNum"/>, <paramref name="npcSlot"/>) exists, is
    /// observed by player <paramref name="index"/>, sits within interaction range (r=5, cross-map-aware) of
    /// <paramref name="pc"/>, and CONNECTS to them across the two layers; the out-param is its NPC template number
    /// (0 when false). The world-geometry core shared by the interact spine (PacketHandler.TryResolveInteractNpc),
    /// the quest accept/turn-in proximity checks and the active-shop re-validation (ServerPlayer.ActiveShop) — a
    /// modified client can neither interact nor trade at a keeper from afar, nor across planes.</summary>
    public bool IsNpcInInteractRange(int index, PlayerRecord pc, int mapNum, int npcSlot, out int npcNum)
    {
        npcNum = 0;
        if (mapNum < 1 || mapNum > Limits.Maps) return false;
        if (npcSlot < 1 || npcSlot > Constants.MaxMapNpcs) return false;
        if (!IsObserving(index, mapNum)) return false;
        var mn = MapNpcs[mapNum, npcSlot];
        if (mn.Num <= 0) return false;
        var grid = WorldCoordHelper.BuildMapGrid(Maps, pc.Map);
        var (myWX, myWY) = grid.CenterToWorld(pc.X, pc.Y);
        var tw = grid.ToWorldRelative(mapNum, mn.X, mn.Y);
        // Footprint-aware r=5: interacting with an oversize NPC counts its whole body, not just its anchor.
        if (tw is null || !WorldCoordHelper.IsInSpellRange(myWX, myWY, 1, tw.Value.worldX, tw.Value.worldY, Npcs[mn.Num].EffectiveSize)) return false;
        // Two-layer world: a keeper on the bridge deck and a player on the ground beneath it are a few pixels apart
        // on screen but not on the same plane. Same connect rule combat and spell targeting use (LayerConnects is
        // range-agnostic): same layer always, across them only from a ramp's mount side — so you can talk to a
        // keeper on the deck while standing at the ramp foot, but not from anywhere else on the ground.
        if (!LayerLogic.LayerConnects(new ServerTileView(this, grid), myWX, myWY, pc.Layer,
                tw.Value.worldX, tw.Value.worldY, mn.Layer))
        {
            return false;
        }

        npcNum = mn.Num;
        return true;
    }

    /// <summary>The NPC template standing in <paramref name="mapNum"/>'s <paramref name="npcSlot"/>, or 0 when
    /// the slot is empty or out of range. Asks WHO is there and nothing else — no distance, no planes, no
    /// observer test.
    ///
    /// <para>What an open shop session re-checks. A session is opened within reach of its keeper and stays open
    /// after that, so the question a purchase asks is not "am I still standing there" but "is the keeper I
    /// opened this with still the one in that slot" — which a keeper dying and its slot respawning as something
    /// else would answer no.</para></summary>
    public int NpcTemplateAt(int mapNum, int npcSlot)
    {
        if (mapNum < 1 || mapNum > Limits.Maps) return 0;
        if (npcSlot < 1 || npcSlot > Constants.MaxMapNpcs) return 0;
        return MapNpcs[mapNum, npcSlot].Num;
    }

    /// <summary>Is a dropped item close enough for <paramref name="pc"/> to reach, and on a plane that
    /// connects to theirs? The same world-geometry rule as <see cref="IsNpcInInteractRange"/>, and for the
    /// same reason: the tile menu offers pick-up from a distance, so the SERVER has to be the one deciding
    /// what "close enough" means. A modified client cannot vacuum a map from across it.
    ///
    /// <para>Size 1 rather than a footprint — an item occupies its own tile and nothing more.</para>
    ///
    /// <para>Note this is a strictly WIDER gate than standing on the item, which is the point: the reason
    /// loot was scattered across neighbouring tiles in other engines is that a player could stand on the
    /// pile and deny it. Reaching from r=5 removes the problem at its source instead.</para></summary>
    public bool IsMapItemInReach(int index, PlayerRecord pc, int mapNum, MapItemRecord mi)
    {
        if (mapNum < 1 || mapNum > Limits.Maps) return false;
        if (mi.Num <= 0 || mi.Num > Limits.Items) return false;
        if (!IsObserving(index, mapNum)) return false;

        var grid = WorldCoordHelper.BuildMapGrid(Maps, pc.Map);
        var (myWX, myWY) = grid.CenterToWorld(pc.X, pc.Y);
        var tw = grid.ToWorldRelative(mapNum, mi.X, mi.Y);
        if (tw is null || !WorldCoordHelper.IsInSpellRange(myWX, myWY, 1, tw.Value.worldX, tw.Value.worldY, 1))
            return false;

        return LayerLogic.LayerConnects(new ServerTileView(this, grid), myWX, myWY, pc.Layer,
                                        tw.Value.worldX, tw.Value.worldY, mi.Layer);
    }
    public bool IndoorsOf(int mapNum) => MapGroupResolve.Indoors(Maps[mapNum], GroupOf(mapNum));
    /// <summary>How this map treats the day/night cycle. ONE answer, because the two authored flags are
    /// mutually exclusive and only mean something as a pair. See MapGroupResolve.Lighting.</summary>
    public MapLighting LightingOf(int mapNum) => MapGroupResolve.Lighting(Maps[mapNum], GroupOf(mapNum));
    public int BootMapOf(int mapNum) => MapGroupResolve.BootMap(Maps[mapNum], GroupOf(mapNum));
    public int BootXOf(int mapNum) => MapGroupResolve.BootX(Maps[mapNum], GroupOf(mapNum));
    public int BootYOf(int mapNum) => MapGroupResolve.BootY(Maps[mapNum], GroupOf(mapNum));

    public bool IsRealMap(int mapNum) => mapNum > 0 && mapNum <= Limits.Maps;

    /// <summary>The closest real tile to a REMEMBERED position — where a character was standing when it
    /// was last saved, or the respawn point it paid for.
    ///
    /// <para>A remembered position is bookkeeping, and gets repaired. An authored warp destination is
    /// content, and gets refused and reported instead (<c>MovementSystem.IsWarpDestinationValid</c>) —
    /// the tile that points nowhere is for its author to correct, whereas a player whose map shrank
    /// under them has nothing to correct and would simply be locked out.</para>
    ///
    /// <para><paramref name="fallback"/> covers only the case where the map itself is gone, and is
    /// clamped on the same terms, so a misconfigured spawn point cannot strand anyone either.</para></summary>
    public (int Map, int X, int Y) RepairPosition(int mapNum, int x, int y, (int Map, int X, int Y) fallback)
    {
        if (IsRealMap(mapNum)) return ClampToMap(mapNum, x, y);
        if (IsRealMap(fallback.Map)) return ClampToMap(fallback.Map, fallback.X, fallback.Y);
        return ClampToMap(1, 0, 0);
    }

    private (int Map, int X, int Y) ClampToMap(int mapNum, int x, int y) =>
        (mapNum, Math.Clamp(x, 0, Maps[mapNum].Width - 1), Math.Clamp(y, 0, Maps[mapNum].Height - 1));

    // ── The upper plane as somewhere to stand ────────────────────────────────────
    // Which deck tiles a body can actually be PUT on: the ones joined to a ramp. Cached because it is a
    // whole-world answer — see IsFringeSpawnable — and rebuilt whenever a map changes under it.
    private Dictionary<int, bool[]>? _fringeReach;

    /// <summary>Drop the cached fringe reachability. Called whenever a map is authored: its own deck, its
    /// ramps or its LINKS may all have moved, and a link change is felt by the maps either side of it.</summary>
    public void InvalidateFringeReach() => _fringeReach = null;

    /// <summary>Can a body stand on the upper plane at this tile — is it a deck, joined to a ramp?
    ///
    /// <para>🔴 The question is not answerable one map at a time. The fringe plane runs on through a seam
    /// exactly like the ground does, so a bridge can begin on one map and the ramp that reaches it stand on
    /// another, a series of decks along. Asking only "does THIS map have a ramp" refuses every deck in such
    /// a chain but the one, and accepts a stranded deck on a map that happens to have a ramp elsewhere on
    /// it. So the reachable set is flooded once across the whole world, from every ramp, and cached.</para>
    ///
    /// <para>Deck ART is what makes a tile a surface: <see cref="LayerLogic.AttrFor"/> reads the fringe
    /// plane as Walkable wherever nothing says otherwise, so "unblocked up there" is true of open sky over
    /// most of every map.</para></summary>
    public bool IsFringeSpawnable(int mapNum, int x, int y)
    {
        var reach = _fringeReach ??= BuildFringeReach();
        if (!reach.TryGetValue(mapNum, out var cells)) return false;
        var map = Maps[mapNum];
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
        return cells[y * map.Width + x];
    }

    /// <summary>True when any tile of this map can take a spawn on the upper plane — the cheap gate that
    /// keeps a ground-only map on exactly the path it was always on.</summary>
    public bool HasSpawnableFringe(int mapNum) =>
        (_fringeReach ??= BuildFringeReach()).ContainsKey(mapNum);

    private static bool IsRamp(in TileRecord t, out Direction groundSide)
    {
        if (t.FringeAttr is { Type: TileType.LayerRamp } fa)
        {
            groundSide = fa.RampGroundSide;
            return true;
        }
        groundSide = default;
        return false;
    }
    private static bool IsRamp(in TileRecord t) => IsRamp(t, out _);

    // A surface up top: authored fringe art with nothing blocking it. A ramp counts — it is how you arrive.
    private static bool IsDeck(in TileRecord t) =>
        !LayerCell.IsEmpty(t.Fringe[0])
        && LayerLogic.AttrFor(t, WorldLayer.Fringe).Type == TileType.Walkable;

    private static Direction Opposite(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };

    /// <summary>Is this one step legal ON the fringe plane, by <see cref="LayerLogic"/>'s ramp geometry?
    ///
    /// <para>🔴 A ramp is a CORRIDOR, not a doorway. Its sides are a wall on both planes, so the only way
    /// off the top of one is up-ramp — the direction opposite its ground side — and the only way onto one
    /// is along that same axis. Treating a ramp as joined to whatever it happens to touch marks decks
    /// reachable that a player cannot actually mount, and mobs get put on them.</para>
    ///
    /// <para>The foot of a ramp is left out on purpose: stepping down it toward its ground side leaves the
    /// fringe plane entirely (<see cref="LayerLogic.ResolveLayer"/>), which is the way DOWN, not a way along
    /// the deck.</para></summary>
    private static bool FringeStepAllowed(in TileRecord from, in TileRecord to, Direction dir)
    {
        bool fromRamp = IsRamp(from, out var fromGround), toRamp = IsRamp(to, out var toGround);
        if (fromRamp == toRamp) return true;               // deck to deck, or along one ramp block
        return fromRamp
            ? dir == Opposite(fromGround)                  // off the top of the ramp onto the deck
            : dir == toGround || dir == Opposite(toGround); // onto the ramp, along its mount axis
    }

    private Dictionary<int, bool[]> BuildFringeReach()
    {
        var reach = new Dictionary<int, bool[]>();
        var seen = new HashSet<(int Map, int X, int Y)>();
        var queue = new Queue<(int Map, int X, int Y)>();

        // Every ramp in the world is a way up, so every ramp is a seed.
        for (int m = 1; m <= Limits.Maps; m++)
        {
            var map = Maps[m];
            if (map is null) continue;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (IsRamp(map.Tile[x, y]) && seen.Add((m, x, y))) queue.Enqueue((m, x, y));
        }

        while (queue.Count > 0)
        {
            var (m, x, y) = queue.Dequeue();
            var map = Maps[m];
            var here = map.Tile[x, y];
            foreach (var (dx, dy, dir) in _adjacent)
            {
                // Off an edge is not off the world: step onto the neighbour's opposite edge. A map only ever
                // links to maps its own size, so the crossing keeps its index.
                int nx = x + dx, ny = y + dy, nm = m;
                if (nx < 0) { nm = map.Left; nx = map.Width - 1; }
                else if (nx >= map.Width) { nm = map.Right; nx = 0; }
                else if (ny < 0) { nm = map.Up; ny = map.Height - 1; }
                else if (ny >= map.Height) { nm = map.Down; ny = 0; }
                if (nm <= 0 || nm > Limits.Maps) continue;

                var next = Maps[nm];
                if (next is null || nx >= next.Width || ny >= next.Height) continue;
                var t = next.Tile[nx, ny];
                if (!IsDeck(t) && !IsRamp(t)) continue;
                if (!FringeStepAllowed(here, t, dir)) continue;
                if (!seen.Add((nm, nx, ny))) continue;
                queue.Enqueue((nm, nx, ny));

                // Only a DECK is somewhere to be put down; a ramp is a way through, not a spawn post.
                if (!IsDeck(t)) continue;
                if (!reach.TryGetValue(nm, out var cells))
                    reach[nm] = cells = new bool[next.Width * next.Height];
                cells[ny * next.Width + nx] = true;
            }
        }
        return reach;
    }

    private static readonly (int dx, int dy, Direction dir)[] _adjacent =
    {
        (0, -1, Direction.Up), (0, 1, Direction.Down), (-1, 0, Direction.Left), (1, 0, Direction.Right),
    };

    /// <summary>The map's MapGroup iff it is a contestable TERRITORY (Territory = true), else null — the
    /// territory-income hook's fast gate (a group-less or non-territory map returns null).</summary>
    public MapGroupRecord? TerritoryGroupOf(int mapNum)
    {
        var g = GroupOf(mapNum);
        return g is { Territory: true } ? g : null;
    }

    /// <summary>The state of the territory that <paramref name="groupIndex"/>'s maps make up, made on first
    /// ask.
    ///
    /// <para>A territory group with no file yet is simply unclaimed, so there is nothing to seed and nothing
    /// to migrate: declaring a territory in the editor is enough, and the record reaches disk the moment
    /// something changes it. Ask through here rather than indexing <see cref="Territories"/>, or a first
    /// challenge on a fresh territory has nowhere to land.</para></summary>
    public TerritoryRecord TerritoryFor(int groupIndex)
    {
        if (Territories.TryGetValue(groupIndex, out var t)) return t;
        t = new TerritoryRecord { MapGroup = groupIndex };
        Territories[groupIndex] = t;
        return t;
    }

    /// <summary>The territory <paramref name="mapNum"/> stands in, or null when its map has no group or the
    /// group is not contestable.</summary>
    public TerritoryRecord? TerritoryOf(int mapNum) =>
        TerritoryGroupOf(mapNum) is { } g ? TerritoryFor(g.Index) : null;

    /// <summary>Every contestable group paired with its state, for the sweeps that visit all of them
    /// (war-night resolution, the settlement, the Territories tab).</summary>
    public IEnumerable<(MapGroupRecord Group, TerritoryRecord State)> AllTerritories()
    {
        foreach (var g in MapGroups.Values)
            if (g.Territory) yield return (g, TerritoryFor(g.Index));
    }

    // ── Live territory contest coordination ──────────────────────────────────────
    // Runtime-only projection of the active KotH contests, published by GuildTerritorySystem so MovementSystem
    // (non-participant entry warnings) and SpawnSystem (NPC spawn suppression) can read the war state without a
    // GuildTerritorySystem reference (which would cycle — GuildTerritorySystem already depends on SpawnSystem
    // for the despawns). Empty whenever no contest is running.
    public List<ContestZone> ContestZones { get; } = new();

    /// <summary>True while <paramref name="mapNum"/> is in a territory with a live contest — NPCs neither spawn
    /// nor respawn there for the whole war state (setup + contest + cooldown).</summary>
    public bool IsContestSuppressedMap(int mapNum)
    {
        foreach (var z in ContestZones)
            if (z.Maps.Contains(mapNum)) return true;
        return false;
    }

    /// <summary>The same suppression, asked of one NPC. <see cref="NpcBehavior.Guard"/> is exempt: a guard is
    /// the map's own standing defence, not contestable PvE, so it holds its post for the whole war state.
    ///
    /// <para>🔴 The despawn and the respawn have to read the SAME rule. Suppressing a guard's respawn while
    /// the war-start despawn spared it clears the map of guards the first time one dies and never brings them
    /// back — a divergence that only shows up mid-war.</para></summary>
    public bool IsContestSuppressedNpc(int mapNum, int npcNum) =>
        IsContestSuppressedMap(mapNum)
        && (npcNum <= 0 || npcNum > Limits.Npcs || Npcs[npcNum].Behavior != NpcBehavior.Guard);

    // Dropped/spawned items per map: a dynamic list (no cap on raw size — voluntary-drop cap is
    // enforced in ItemSystem.PlayerMapDropItem against PlayerDropped count only, so death drops and
    // NPC drops can pile on without limit).  Each record carries its own stable Slot id assigned by
    // AllocateMapItemSlot, which is what packets reference instead of a list index.
    public List<MapItemRecord>[] MapItems { get; }

    // Monotonic per-map slot-id counter.  Reset on ClearMapItems / HandleMapRespawn and also whenever
    // the map's item list drains to empty (see ItemSystem.RemoveMapItem) — so slot ids stay small and
    // human-readable in logs on every map type, not just Safe maps with active janitors.
    private readonly int[] _nextItemSlotId;

    // Map NPC slots stay fixed-size and 1-based: index 0 unused, 1..MaxMapNpcs in use.
    public MapNpcRecord[,] MapNpcs { get; }

    /// <summary>Reserve a fresh, stable per-map slot id for a new map item.</summary>
    public int AllocateMapItemSlot(int mapNum) => ++_nextItemSlotId[mapNum];

    /// <summary>Reset the slot-id counter for a map (called when its item list is wholly cleared).</summary>
    public void ResetMapItemSlotIds(int mapNum) => _nextItemSlotId[mapNum] = 0;

    /// <summary>Lookup a map item by stable slot id; returns null if no such slot is live.</summary>
    public MapItemRecord? MapItemBySlot(int mapNum, int slotId)
    {
        if (mapNum <= 0 || mapNum > Limits.Maps || slotId <= 0) return null;
        var list = MapItems[mapNum];
        for (int i = 0; i < list.Count; i++)
            if (list[i].Slot == slotId) return list[i];
        return null;
    }

    // Seamless chase: per-map list of visiting (chasing) NPCs that crossed in from a neighbor.
    // Unlimited size, in-memory only; each is keyed by its permanent (SpawnMapNum, SpawnSlot).
    public List<TraversalNpcRecord>[] MapTraversalNpcs { get; }

    // Blood pools: sparse per-map field (float sim grid + a Dirty flag grid), driven by BloodSystem.
    // Created on the first deposit for a map and removed when the map fully dries — so only maps that
    // have seen recent combat allocate a grid (versus a dense 1000-entry array).  Server-authoritative:
    // the client mirrors just the float amounts and replays decay locally.
    public Dictionary<int, BloodField> MapBlood { get; } = new();

    public WeatherType Weather { get; set; }
    public TimePhase TimePhase { get; set; }
    public float TimeProgress { get; set; }
    public string Motd { get; set; } = "";

    /// <summary>The active weather affecting <paramref name="map"/>. Global today (all maps share
    /// <see cref="Weather"/>); this is the single chokepoint a future indoor/outdoor session gates on,
    /// so interiors can be sheltered without touching every effect site.</summary>
    public WeatherType WeatherOn(int map) => Weather;

    /// <summary>NPC max HP for the CURRENT conditions: the pure formula, composed with the Night boost
    /// (<see cref="Constants.NpcNightHpMultiplier"/>) and the Snow max-HP reduction
    /// (<see cref="Constants.WeatherSnowMaxHpMultiplier"/>). ALL server-side max-HP derivations route
    /// through here so the boost/reduction is consistent everywhere (spawn, packets, regen clamp, resets,
    /// the EXP damage-share denominator, heal clamps). The editor keeps calling the pure
    /// <see cref="StatFormulas.GetNpcMaxHp(NpcRecord)"/> (it has no time/weather concept).</summary>
    public int EffectiveNpcMaxHp(NpcRecord npc)
    {
        double m = 1.0;
        if (this.TimePhase == TimePhase.Night) m *= Constants.NpcNightHpMultiplier;
        if (this.Weather == WeatherType.Snow) m *= Constants.WeatherSnowMaxHpMultiplier;
        return (int)Math.Round(StatFormulas.GetNpcMaxHp(npc) * m, MidpointRounding.AwayFromZero);
    }

    /// <summary>NPC max MP for current conditions. Only Snow affects MP (-20%); Night does not.</summary>
    public int EffectiveNpcMaxMp(NpcRecord npc)
    {
        int baseMax = StatFormulas.GetNpcMaxMp(npc);
        return this.Weather == WeatherType.Snow
            ? (int)Math.Round(baseMax * Constants.WeatherSnowMaxMpMultiplier, MidpointRounding.AwayFromZero)
            : baseMax;
    }

    /// <summary>NPC max SP for current conditions. Only Snow affects SP (-20%); Night does not.</summary>
    public int EffectiveNpcMaxSp(NpcRecord npc)
    {
        int baseMax = StatFormulas.GetNpcMaxSp(npc);
        return this.Weather == WeatherType.Snow
            ? (int)Math.Round(baseMax * Constants.WeatherSnowMaxSpMultiplier, MidpointRounding.AwayFromZero)
            : baseMax;
    }

    // PlayersOnMap[mapNum] — true when at least one player is on the map (skip NPC AI for empty maps)
    public bool[] PlayersOnMap { get; }

    // Seamless scrolling: MapObservers[mapNum] = player indices that can SEE the map (i.e. are
    // standing on it or on one of its 8 neighbors).  Entity broadcasts and NPC AI are driven off
    // this set so neighbor maps stay live and synced.  Maintained by Add/Remove ObserverMaps.
    public HashSet<int>[] MapObservers { get; }

    /// <summary>Whether player <paramref name="index"/> can currently SEE <paramref name="mapNum"/> —
    /// they are standing on it or on one of its eight neighbors. The membership counterpart to the
    /// broadcast helpers: use this to gate a per-player decision (credit this kill, hear this line,
    /// accept this interaction), not to pick a broadcast audience.</summary>
    public bool IsObserving(int index, int mapNum) => MapObservers[mapNum].Contains(index);

    /// <summary>The player-slot width every per-player array in this world is cut to. Held so the map-NPC
    /// records allocated below size their damage ledgers to the server's real limit rather than the
    /// protocol ceiling — 21,000 records × two arrays makes that difference tens of megabytes.</summary>
    private readonly int _playerSlots;

    public GameWorld(Configuration.ServerConfig? config = null)
    {
        var settings = config ?? Configuration.ServerConfig.Default;
        _playerSlots = settings.MaxPlayers;
        Limits = settings.Records;

        // Every array is cut from Limits, and every bounds check downstream reads the same object, so a
        // check can never disagree with the allocation it is guarding.
        Maps = Fill<MapRecord>(Limits.Maps);
        TempTiles = Fill<TempTileState>(Limits.Maps);
        Items = Fill<ItemRecord>(Limits.Items);
        Npcs = Fill<NpcRecord>(Limits.Npcs);
        Shops = Fill<ShopRecord>(Limits.Shops);
        Spells = Fill<SpellRecord>(Limits.Spells);
        Quests = Fill<QuestRecord>(Limits.Quests);
        Conversations = Fill<ConversationRecord>(Limits.Conversations);

        MapItems = Fill<List<MapItemRecord>>(Limits.Maps);
        MapTraversalNpcs = Fill<List<TraversalNpcRecord>>(Limits.Maps);
        MapObservers = Fill<HashSet<int>>(Limits.Maps);
        PlayersOnMap = new bool[Limits.Maps + 1];
        _nextItemSlotId = new int[Limits.Maps + 1];

        for (int i = 0; i <= Constants.MaxClasses; i++) Classes[i] = new ClassRecord();

        // NPC slot dimension is 1-based: index 0 left as null (never accessed), 1..MaxMapNpcs initialized.
        MapNpcs = new MapNpcRecord[Limits.Maps + 1, Constants.MaxMapNpcs + 1];
        for (int m = 0; m <= Limits.Maps; m++)
        {
            for (int s = 1; s <= Constants.MaxMapNpcs; s++)
                MapNpcs[m, s] = new MapNpcRecord(_playerSlots);
        }
    }

    /// <summary>A 1-based slot array of <paramref name="count"/> usable entries, every one constructed —
    /// index 0 included, as the unused dummy every read site relies on being non-null.</summary>
    private static T[] Fill<T>(int count) where T : new()
    {
        var arr = new T[count + 1];
        for (int i = 0; i <= count; i++) arr[i] = new T();
        return arr;
    }

    // ── Observer-set maintenance ──────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="into"/> with the distinct, valid maps observed from a center (the center +
    /// its up-to-8 neighbors) and returns the count.  Callers pass a small stack buffer (≤9 entries), so
    /// this allocates nothing — replacing a yield-iterator + <c>HashSet</c> that churned on the per-swing
    /// combat path.  Dedup is a linear scan: at ≤9 entries that's cheaper than hashing.
    /// </summary>
    public int ObservedMapsInto(int centerMapNum, Span<int> into)
    {
        var grid = WorldCoordHelper.BuildMapGrid(Maps, centerMapNum);
        int n = 0;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                int m = grid[c, r];
                if (m <= 0 || m > Limits.Maps) continue;
                bool dup = false;
                for (int k = 0; k < n; k++)
                {
                    if (into[k] == m)
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup) into[n++] = m;
            }
        }

        return n;
    }

    public void AddObserver(int index, int centerMapNum)
    {
        Span<int> maps = stackalloc int[9];
        int n = ObservedMapsInto(centerMapNum, maps);
        for (int i = 0; i < n; i++) MapObservers[maps[i]].Add(index);
    }

    public void RemoveObserver(int index, int centerMapNum)
    {
        Span<int> maps = stackalloc int[9];
        int n = ObservedMapsInto(centerMapNum, maps);
        for (int i = 0; i < n; i++) MapObservers[maps[i]].Remove(index);
    }

    /// <summary>Full sweep used on logout so a leaving player can never leak into an observer set.</summary>
    public void RemoveObserverFromAll(int index)
    {
        for (int m = 0; m <= Limits.Maps; m++) MapObservers[m].Remove(index);
    }

    /// <summary>Two NPCs never attack each other on sight when they share a kind (same template
    /// <see cref="MapNpcRecord.Num"/>) OR a non-zero <see cref="NpcRecord.Group"/>.  Group 0 is the
    /// "ungrouped" sentinel, so two ungrouped NPCs are allied only if they are literally the same
    /// kind — i.e. Group 0 preserves the original same-type-only behavior.  Equality on Group makes
    /// the rule symmetric by construction: a one-sided group assignment grants no protection either
    /// way, surfacing a mis-set group as in-game infighting during testing.
    ///
    /// <para>🔴 It lives on the WORLD because both halves of a fight ask it and neither owns it: the AI
    /// decides who a mob walks at, and a wide swing's strike strip decides who else that swing catches.
    /// Hanging it off either system gives the other a dependency it has no other reason to hold — routing
    /// it through <c>CombatSystem</c> broke the AI targeting tests, which construct the brain with a null
    /// combat system precisely because targeting has no business needing one.</para></summary>
    public bool AreNpcsAllied(int numA, int numB)
    {
        if (numA == numB) return true;                    // same kind (original same-type peace)
        int groupA = Npcs[numA].Group;
        return groupA != 0 && groupA == Npcs[numB].Group;  // same non-zero group
    }

    /// <summary>
    /// True when any live NPC — native slot or visiting traversal NPC — stands on (x,y) of a map,
    /// excluding the optional <paramref name="exclude"/> NPC (by reference).  Allocation-free; used
    /// for movement collision so traversal guests block tiles like native NPCs do.
    /// </summary>
    public bool IsTileOccupiedByNpc(int mapNum, int x, int y, MapNpcRecord? exclude = null, WorldLayer? layer = null)
    {
        // A non-null layer restricts the match to NPCs on that logical layer (two-layer world), so a
        // fringe-layer body doesn't block a ground-layer mover at the same (x,y) and vice versa.  null =
        // any layer (legacy callers / spawn on the ground where every NPC is Ground anyway).
        // Within-map: any NPC anchored on this map whose footprint covers (x,y).
        if (AnyNpcFootprintCoversLocal(mapNum, x, y, exclude, layer)) return true;

        // Cross-seam: a large NPC anchored on the LEFT / UP / UP-LEFT neighbor can spill its body onto this
        // map's top/left border tiles (footprints extend +x/+y from the top-left anchor).  Only relevant on
        // the leftmost / topmost (MaxNpcSize-1) tiles, and only large NPCs ever spill — so this is a cheap
        // fast-bail everywhere else.
        if (x < Constants.MaxNpcSize - 1 || y < Constants.MaxNpcSize - 1)
        {
            var grid = WorldCoordHelper.BuildMapGrid(Maps, mapNum);
            var (qwx, qwy) = grid.CenterToWorld(x, y);   // query tile in world space (center cell = mapNum)
            if (NeighborBigNpcCovers(in grid, 0, 1, qwx, qwy, exclude, layer)) return true;  // left
            if (NeighborBigNpcCovers(in grid, 1, 0, qwx, qwy, exclude, layer)) return true;  // up
            if (NeighborBigNpcCovers(in grid, 0, 0, qwx, qwy, exclude, layer)) return true;  // up-left
        }
        return false;
    }

    /// <summary>The NPC whose body covers this map tile — native or visiting — and the slot to address it by
    /// (0 for a guest, which rides its own universal identity). Null when nothing stands there.
    ///
    /// <para>🔴 Ask the TILE, not the rosters. A sweep that walks the native slot array has to be taught
    /// about every kind of body that can stand on a map, and forgets the ones added since — which is how a
    /// wide mob came to swing straight through a visitor. Taught once, here, beside the occupancy test that
    /// already knew about both.</para></summary>
    public (MapNpcRecord Npc, int Slot)? NpcCoveringLocal(int mapNum, int x, int y, WorldLayer? layer = null)
    {
        for (int s = 1; s <= Constants.MaxMapNpcs; s++)
        {
            var n = MapNpcs[mapNum, s];
            if (n.Num > 0 && n.Hp > 0 && LayerMatches(n, layer) && NpcFootprintCoversLocal(n, x, y)) return (n, s);
        }
        var guests = MapTraversalNpcs[mapNum];
        for (int i = 0; i < guests.Count; i++)
        {
            var t = guests[i];
            if (t.Num > 0 && t.Hp > 0 && LayerMatches(t, layer) && NpcFootprintCoversLocal(t, x, y)) return (t, 0);
        }
        return null;
    }

    private bool AnyNpcFootprintCoversLocal(int mapNum, int x, int y, MapNpcRecord? exclude, WorldLayer? layer)
    {
        for (int s = 1; s <= Constants.MaxMapNpcs; s++)
        {
            var n = MapNpcs[mapNum, s];
            if (n.Num > 0 && !ReferenceEquals(n, exclude) && LayerMatches(n, layer) && NpcFootprintCoversLocal(n, x, y)) return true;
        }
        var list = MapTraversalNpcs[mapNum];
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t.Num > 0 && !ReferenceEquals(t, exclude) && LayerMatches(t, layer) && NpcFootprintCoversLocal(t, x, y)) return true;
        }
        return false;
    }

    // A non-null filter layer restricts occupancy to NPCs on that layer; null matches any.
    private static bool LayerMatches(MapNpcRecord n, WorldLayer? layer) => layer is not { } L || n.Layer == L;

    // True if the NPC's SxS footprint (top-left anchor at its X,Y) covers local tile (x,y) on its own map.
    // For a size-1 NPC this reduces to the classic single-tile test.
    private bool NpcFootprintCoversLocal(MapNpcRecord n, int x, int y)
        => WorldCoordHelper.FootprintContains(n.X, n.Y, Npcs[n.Num].EffectiveSize, x, y);

    // True if a LARGE NPC (size > 1) anchored on the grid neighbor cell (col,row) has a footprint that, in
    // world space, covers the query world tile (qwx,qwy).  Size-1 NPCs never spill across a seam, so they are
    // skipped cheaply.  Both native slots and traversal guests on the neighbor are checked.
    private bool NeighborBigNpcCovers(in MapGrid grid, int col, int row, int qwx, int qwy, MapNpcRecord? exclude, WorldLayer? layer)
    {
        int m = grid[col, row];
        if (m <= 0) return false;
        for (int s = 1; s <= Constants.MaxMapNpcs; s++)
        {
            var n = MapNpcs[m, s];
            if (n.Num <= 0 || ReferenceEquals(n, exclude) || !LayerMatches(n, layer)) continue;
            int size = Npcs[n.Num].EffectiveSize;
            if (size <= 1) continue;
            var (awx, awy) = grid.ToWorld(col, row, n.X, n.Y);
            if (WorldCoordHelper.FootprintContains(awx, awy, size, qwx, qwy)) return true;
        }
        var list = MapTraversalNpcs[m];
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t.Num <= 0 || ReferenceEquals(t, exclude) || !LayerMatches(t, layer)) continue;
            int size = Npcs[t.Num].EffectiveSize;
            if (size <= 1) continue;
            var (awx, awy) = grid.ToWorld(col, row, t.X, t.Y);
            if (WorldCoordHelper.FootprintContains(awx, awy, size, qwx, qwy)) return true;
        }
        return false;
    }
}
