using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>
/// Central mutable game state for the client — one instance owns everything the renderer,
/// panels, and packet handlers read.
/// All arrays are 1-based (index 0 is an unused dummy) to match the server convention.
/// </summary>
public sealed partial class ClientState
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public int MyIndex { get; set; }
    public bool InGame { get; set; }
    public bool GettingMap { get; set; }

    /// <summary>When the player last arrived somewhere by a blocking load — a warp, a teleport, a join.
    /// 0 once the beat it owes has been served.
    ///
    /// <para>🔴 Arriving IS a step, and it costs the beat every other step costs. The movement gate in
    /// <c>InputProcessor.ProcessMovement</c> otherwise opens the instant the player lands: it opens as
    /// soon as the slide offsets are zero, and arriving sets them to zero outright rather than sliding
    /// them there. A warp is asked for by walking onto a tile, so the key that asked for it is still down
    /// on landing — and that is the whole distinction this draws. A tap ends inside the beat and moves
    /// nobody; a key still held when it expires walks on at the ordinary cadence.</para>
    ///
    /// <para>The beat runs from the ARRIVAL, so the wait is the load plus the beat. It starts when the
    /// player can see where they are, rather than overlapping a load during which there was nothing to
    /// look at.</para></summary>
    public long ArrivedAtMs { get; set; }

    /// <summary>
    /// Raised at the end of <see cref="ShiftGrid"/> with the world-pixel offset the data slid
    /// (dxPixels, dyPixels). Subscribers like the floating-text layer that store positions in the
    /// world-pixel frame need to re-anchor by the same offset so they stay over the spot they were
    /// spawned at instead of drifting off-screen when the player crosses a seam.
    /// </summary>
    public event Action<int, int>? GridShifted;

    /// <summary>
    /// Raised at the end of <see cref="ClearMapState"/> — i.e. on a true warp/teleport/full reload
    /// (seamless crossings re-frame via <see cref="ShiftGrid"/> and raise <see cref="GridShifted"/>
    /// instead). Subscribers that hold world-pixel state decoupled from the entity records this
    /// method resets (e.g. the floating-text layer) must drop it here, or it lingers over the old
    /// map's coord frame at the warp destination until it ages out.
    /// </summary>
    public event Action? MapStateCleared;

    // Seamless-crossing prediction: the client shifts its grid the instant it steps off an edge
    // (no round-trip pause), then reconciles with the server.  PendingCrossToMap != 0 means a
    // predicted cross is awaiting confirmation: a matching SeamlessCross confirms it; a self
    // move-correction means the server rejected it and we revert by reloading PendingCrossFromMap.
    public int PendingCrossToMap { get; private set; }
    public int PendingCrossFromMap { get; private set; }
    public int PendingCrossFromRevision { get; private set; }

    public void BeginPendingCross(int fromMap, int fromRevision, int toMap)
    {
        PendingCrossFromMap = fromMap;
        PendingCrossFromRevision = fromRevision;
        PendingCrossToMap = toMap;
    }

    public void ClearPendingCross()
    {
        PendingCrossToMap = 0;
        PendingCrossFromMap = 0;
        PendingCrossFromRevision = 0;
    }

    /// <summary>Place the local player on the freshly-crossed-into map at (x, y), snap the
    /// movement interpolation, and clear the predicted-cross state. Single point of mutation
    /// for the post-cross fields so the 5-field assignment doesn't drift across call sites.</summary>
    public void ApplySeamlessCross(int mapNum, int x, int y, WorldLayer layer)
    {
        var me = Me;
        me.Map = mapNum;
        me.X = x;
        me.Y = y;
        me.Layer = layer;   // two-layer world: a bridge continues across the seam (server-authoritative)
        me.XOffset = 0;
        me.YOffset = 0;
        me.Moving = MovementType.None;
    }
    public string AccountName { get; set; } = "";
    public string CurrentCharName { get; set; } = "";

    /// <summary>Shortcut to the local player's record.</summary>
    public PlayerRecord Me => Players[MyIndex];

    /// <summary>How long after the last blow a fighter still counts as in combat. Matches the server's
    /// <c>CombatSystem.CombatDurationMs</c>, which is the authority; this copy is what the client uses to
    /// gray out what the server would refuse.</summary>
    public const long CombatWindowMs = 10_000;

    /// <summary>Whether combatant <paramref name="lastCombatMs"/> is still in combat at
    /// <paramref name="nowMs"/>. Zero means never fought, which must not read as in combat forever.</summary>
    public static bool InCombatAt(long lastCombatMs, long nowMs) =>
        lastCombatMs > 0 && nowMs - lastCombatMs < CombatWindowMs;

    /// <summary>Whether the local player is in combat right now.</summary>
    public bool AmIInCombat() => InCombatAt(Me.LastCombatMs, Environment.TickCount64);

    // ── Players (1-based, 1..MaxPlayers) ─────────────────────────────────────

    /// <summary>
    /// The highest slot THIS server will ever use, from its pre-login hello.
    ///
    /// <para>Every per-frame pass over players walks this instead of <see cref="Constants.MaxPlayers"/>,
    /// which is the PROTOCOL ceiling — the largest slot the wire can carry, and far more than a typical
    /// server runs. A world configured for twenty costs twenty checks a pass, not five hundred.</para>
    ///
    /// <para>Starts at the ceiling, and that is the only safe direction to be wrong in: too high wastes a
    /// few checks on empty slots, too low would skip a real player mid-step. The hello arrives on every
    /// connection before login, so no world pass ever runs on a value from an earlier server.</para>
    ///
    /// <para><see cref="Players"/> itself stays ceiling-sized. It is one array of references, it is what
    /// the protocol permits, and sizing it from the wire would mean allocating it after construction for
    /// nothing.</para>
    /// </summary>
    public int PlayerSlots
    {
        get;
        set => field = Math.Clamp(value, 1, Constants.MaxPlayers);
    } = Constants.MaxPlayers;

    /// <summary>
    /// What to call the game we are connected to — the window title, the menu, the HUD.
    ///
    /// <para><b>A client has no game identity of its own.</b> It ships branded with the ENGINE's name
    /// (<see cref="Constants.GameName"/>) and wears it until a server's pre-login hello names the game,
    /// from which point it shows that. This is a deliberate handshake, not a bait and switch: launching
    /// "Mirage Source Remastered" and arriving in "Brightwater" is how a single client reaches every
    /// server, and it is documented as a known limitation.</para>
    ///
    /// <para>NOT a file path, ever. <c>AppPaths</c> stays on the engine name so a player's settings folder
    /// never moves when they join a differently-named game.</para>
    /// </summary>
    public string GameName
    {
        get;
        set => field = value.Trim() is { Length: > 0 } named ? named : Constants.GameName;
    } = Constants.GameName;

    public PlayerRecord[] Players { get; } = InitPlayers();

    // ── Record ceilings ───────────────────────────────────────────────────────

    /// <summary>
    /// How many of each record family the CONNECTED SERVER has. Every bounds check on a record number
    /// reads this, and the tables below are cut from it.
    ///
    /// <para><b>Not a compiled-in ceiling.</b> That was the bug: <c>Constants.Max*</c> is <c>const</c> and
    /// therefore baked into every shipped client, so a client built against 1000 items would reject item
    /// 1200 as out of range on a server that had authored it — either throwing, or silently treating a
    /// legitimate record as a hacking attempt.</para>
    ///
    /// <para>Starts at the stock values so a freshly-constructed state is usable, and is replaced the
    /// moment a server says otherwise. The hello arrives before any record packet, so nothing is ever
    /// bounded by the starting values in a real session.</para>
    /// </summary>
    public RecordLimits Limits { get; private set; } = RecordLimits.Default;

    /// <summary>Adopts a server's ceilings and re-cuts every record table to match. Called from the
    /// pre-login hello, before the server sends a single record.</summary>
    public void ApplyServerLimits(RecordLimits limits)
    {
        Limits = limits.Clamped(RecordLimits.Ceiling);

        Items = new ItemRecord[Limits.Items + 1];
        NpcDefs = new NpcRecord[Limits.Npcs + 1];
        NpcKeeperShop = new int[Limits.Npcs + 1];
        NpcQuestGlyph = new int[Limits.Npcs + 1];
        NpcConvGlyph = new int[Limits.Npcs + 1];
        ShopDefs = new ShopRecord[Limits.Shops + 1];
        SpellDefs = new SpellRecord[Limits.Spells + 1];
        QuestDefs = new QuestRecord[Limits.Quests + 1];
        ConvDefs = new ConversationRecord[Limits.Conversations + 1];
        MapGroups = new MapGroupRecord?[Limits.MapGroups + 1];
    }

    private static PlayerRecord[] InitPlayers()
    {
        var arr = new PlayerRecord[Constants.MaxPlayers + 1];
        for (int i = 1; i <= Constants.MaxPlayers; i++) arr[i] = new PlayerRecord();
        arr[0] = new PlayerRecord(); // dummy
        return arr;
    }

    // ── Maps: seamless 3×3 grid ([col, row], center = [1,1]) ──────────────────

    /// <summary>
    /// The current map and its 8 pre-loaded neighbors.  A null cell means that
    /// neighbor isn't loaded (or doesn't exist), which clamps camera scrolling in
    /// that direction.  The center cell [1,1] is always present.
    /// </summary>
    public MapRecord?[,] NeighborMaps { get; } = InitNeighborMaps();

    private static MapRecord?[,] InitNeighborMaps()
    {
        var g = new MapRecord?[3, 3];
        g[1, 1] = new MapRecord();
        return g;
    }

    /// <summary>The current ("center") map — alias for <c>NeighborMaps[1,1]</c>.</summary>
    public MapRecord Map
    {
        get => NeighborMaps[1, 1]!;
        set => NeighborMaps[1, 1] = value;
    }

    /// <summary>Server map number of the current ("center") map.</summary>
    public int CenterMapNum { get; set; }

    // ── World-tile coordinates ────────────────────────────────────────────────
    // The 3x3 neighbourhood's stride, taken from the CENTER map. A map may only link to maps of its own
    // size, so every cell measures the same and a world coordinate means one thing across the whole grid.

    /// <summary>The neighbourhood's tile width — the center map's own.</summary>
    public int MapTilesX => NeighborMaps[1, 1]?.Width ?? Constants.DefaultMapWidth;

    /// <summary>The neighbourhood's tile height — the center map's own.</summary>
    public int MapTilesY => NeighborMaps[1, 1]?.Height ?? Constants.DefaultMapHeight;

    /// <summary>World-tile coordinate of a local tile within a grid cell. The center map's (0,0) sits at
    /// (<see cref="MapTilesX"/>, <see cref="MapTilesY"/>), so neighbors fit on every side.</summary>
    public (int worldX, int worldY) ToWorld(int col, int row, int localX, int localY)
        => (col * MapTilesX + localX, row * MapTilesY + localY);

    /// <summary>World-tile coordinate of a local tile on the CENTER map.</summary>
    public (int worldX, int worldY) CenterToWorld(int localX, int localY)
        => (MapTilesX + localX, MapTilesY + localY);

    /// <summary>Inverse of <see cref="ToWorld"/>: the grid cell and local tile a world coordinate lands
    /// on. The cell may be outside the 3x3, so callers check before indexing.</summary>
    public (int col, int row, int localX, int localY) FromWorld(int worldX, int worldY)
    {
        int col = worldX / MapTilesX;
        int row = worldY / MapTilesY;
        return (col, row, worldX - col * MapTilesX, worldY - row * MapTilesY);
    }

    /// <summary>
    /// Server map number occupying each 3×3 grid cell ([col,row]); 0 = no map.
    /// [1,1] mirrors <see cref="CenterMapNum"/>.  Lets the client route incoming
    /// entity packets (tagged with mapNum) to the right cell.
    /// </summary>
    public int[,] NeighborMapNums { get; } = new int[3, 3];

    /// <summary>Door-open state per tile on the center map; indexed [x, y, (int)WorldLayer].
    /// Two-plane world: a fringe-deck door on a bridge is independent of the ground door beneath it.
    /// Settable so a seamless crossing can swap it with a neighbor cell's grid (see ShiftGrid).</summary>
    public OpenDoors TempTile { get; private set; } = new();

    /// <summary>
    /// Door-open state for the 8 neighbor maps ([col,row]; [1,1] unused — center uses
    /// <see cref="TempTile"/>).  Mirrors the center's door tracking so neighbor doors predict
    /// collision identically.  Each cell is an always-allocated [x,y,layer] bool grid.
    /// </summary>
    public OpenDoors[,] NeighborTempTiles { get; } = InitNeighborTempTiles();

    private static OpenDoors[,] InitNeighborTempTiles()
    {
        var g = new OpenDoors[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = new OpenDoors();
        }

        return g;
    }

    /// <summary>Open-door set for a map number — the center's, a neighbor cell's, or null.</summary>
    public OpenDoors? TempTilesForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        if (mapNum == CenterMapNum) return TempTile;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1) && NeighborMapNums[c, r] == mapNum)
                    return NeighborTempTiles[c, r];
            }
        }

        return null;
    }

    /// <summary>One blood pool: a size×size tile RECTANGLE (top-left X,Y in map-local coords) with a shared
    /// stain <see cref="Amount"/> (drives the decal's blob SIZE + droplet COUNT) and <see cref="Freshness"/>
    /// (0..1 OPACITY: a hit redarkens to 1, then it fades with the amount).  Mirrors the server's BloodPool;
    /// pools overlap freely.</summary>
    public sealed class BloodPool
    {
        public int X;
        public int Y;
        public int Size;
        public float Amount;
        public float Freshness;
        public WorldLayer Layer;
    }

    /// <summary>Blood pools keyed by MAP NUMBER (not by observable-cell).  A deposit makes the server replace a
    /// map's WHOLE list (<c>BloodUpdatePacket</c>); <c>BloodProcessor</c> replays the shared linear decay locally
    /// each frame and drops dried pools.  Keyed by map num, so a seamless crossing needs no grid shuffle — it
    /// just prunes maps that scrolled out of view (<see cref="PruneBloodToObserved"/>).</summary>
    public Dictionary<int, List<BloodPool>> BloodByMap { get; } = new();

    /// <summary>The pool list for a map, created empty on first use.</summary>
    public List<BloodPool> BloodPoolsForMap(int mapNum)
    {
        if (!BloodByMap.TryGetValue(mapNum, out var list))
            BloodByMap[mapNum] = list = new List<BloodPool>();
        return list;
    }

    /// <summary>True when <paramref name="mapNum"/> is the center or one of the 8 neighbor maps currently in view.</summary>
    public bool IsObservedMap(int mapNum)
    {
        if (mapNum <= 0) return false;
        if (mapNum == CenterMapNum) return true;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                if (NeighborMapNums[c, r] == mapNum) return true;
        }

        return false;
    }

    /// <summary>Drop blood for maps that scrolled out of the observable 3×3 (called after a seam re-frame); the
    /// server re-snapshots any map that comes back into view, so this can't lose live blood we still need.</summary>
    public void PruneBloodToObserved()
    {
        if (BloodByMap.Count == 0) return;
        List<int>? drop = null;
        foreach (int m in BloodByMap.Keys)
            if (!IsObservedMap(m)) (drop ??= new()).Add(m);
        if (drop is not null)
            foreach (int m in drop) BloodByMap.Remove(m);
    }

    /// <summary>How much gold the character is carrying, or 0 when the stack is absent.</summary>
    public long PlayerGold()
    {
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var slot = Me?.Inv?[i];
            if (slot is not null && Items[slot.Num]?.Name == "Gold") return slot.Quantity;
        }
        return 0;
    }
}
