using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record RequestNewMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestNewMap;
}

public sealed record NeedMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NeedMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("revision")] public int Revision { get; init; }
}

// C→S: client confirms it has received map data
public sealed record MapDataClientPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapData;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

// C→S: client's disk cache is stale for a pre-loaded neighbor; request its full data.
// Unlike NeedMap (which serves the player's own map), this honors the specific mapNum,
// validated server-side to be one of the player's currently observable neighbors.
public sealed record NeedNeighborMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NeedNeighborMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("col")] public int Col { get; init; }
    [JsonPropertyName("row")] public int Row { get; init; }
}

/// <summary>C→S: after a seamless border crossing the client has shifted its grid and asks the server
/// to re-sync its now-current observable region (newly-revealed edge maps, their entities, players).</summary>
public sealed record RequestRegionSyncPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestRegionSync;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the MapGroup definitions, shipped as an independent client-cached def like
/// items/npcs/shops. Sent in bulk at join and re-broadcast per-group on a live editor save (via
/// UpdateMapGroupPacket). The client resolves each map's EFFECTIVE inheritable values against its cached group
/// on demand (ClientState.*Of + MapGroupResolve), so a group edit reaches online players without a map reload
/// or a revision bump.</summary>
public sealed record SendMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMapGroups;
    [JsonPropertyName("groups")] public GroupData[] Groups { get; init; } = [];

    // Only the inheritable fields the client resolves against. Territory / ControllingGuild are server- and
    // contest-side concerns the client's render/predict paths never read, so they stay off the wire here.
    public sealed record GroupData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("moral")] MapMoral? Moral,
        [property: JsonPropertyName("music")] int Music,
        [property: JsonPropertyName("indoors")] bool? Indoors,
        [property: JsonPropertyName("alwaysLit")] bool? AlwaysLit,
        [property: JsonPropertyName("alwaysDark")] bool? AlwaysDark,
        [property: JsonPropertyName("bootMap")] int BootMap,
        [property: JsonPropertyName("bootX")] int BootX,
        [property: JsonPropertyName("bootY")] int BootY
    );
}

public sealed record SendMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("revision")] public int Revision { get; init; }
    // Seamless-scroll grid cell this map occupies on the client (center = 1,1).
    // Neighbor pre-loads carry their own cell; the center map keeps the default.
    [JsonPropertyName("col")] public int Col { get; init; } = 1;
    [JsonPropertyName("row")] public int Row { get; init; } = 1;
    // The map's size in tiles. Tiles travel sparsely, so the grid's extent cannot be inferred from them —
    // a map of empty tiles would arrive as no tiles at all. Defaults keep a packet written without them
    // meaning a default-sized map.
    [JsonPropertyName("w")] public int Width { get; init; } = Constants.DefaultMapWidth;
    [JsonPropertyName("h")] public int Height { get; init; } = Constants.DefaultMapHeight;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("up")] public int Up { get; init; }
    [JsonPropertyName("down")] public int Down { get; init; }
    [JsonPropertyName("left")] public int Left { get; init; }
    [JsonPropertyName("right")] public int Right { get; init; }
    // MapGroup-inheritable fields. Always RAW here — the map's own value, with 0 (int fields) or
    // null (Moral + the bools) meaning "inherit from the MapGroup". BOTH the editor (authoring the inherit/
    // override state) and the game client now receive raw and resolve the effective value themselves against
    // their cached group (client: ClientState.*Of + MapGroupResolve; server-side gameplay: GameWorld.*Of). The
    // group is shipped independently (SendMapGroupsPacket at join + UpdateMapGroupPacket on live edit), so a
    // group edit reaches online players without touching any map's revision.
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysLit")] public bool? AlwaysLit { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    // Map-enter/leave greeting. Editor-authored, editor-only: PacketBuilder.SendMap leaves
    // these blank for the game client (which never speaks them — the server does, from its own MapRecord), and
    // carries them only to the editor so a map load/save round-trips the greeting.
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    // The map's MapGroup reference (0 = none). Raw id in both flavors — the group id isn't itself resolved;
    // it drives the effective-value fallback above (game client) and round-trips the editor's group pick.
    [JsonPropertyName("mapGroup")] public int MapGroup { get; init; }
    [JsonPropertyName("tiles")] public TileData[] Tiles { get; init; } = [];
    // NPC spawn entries: the map's dense NPC list — each entry's NPC type plus its optional
    // fixed spawn pin. The editor gets full entries (pins included) for the authoring round-trip; the game
    // client gets the same entries with pins stripped (PacketBuilder.SendMap), since it renders NPCs from live
    // spawn packets and never needs the pins.
    [JsonPropertyName("npcs")] public MapNpcEntry[] Npcs { get; init; } = [];
    [JsonPropertyName("lights")] public PlacedLight[] Lights { get; init; } = [];

    // Ground/Fringe carry the tile's layer stacks as packed LayerCell ints (see LayerCell), trimmed of
    // trailing empties to stay compact on the wire.  Tiles that are fully default are omitted from
    // SendMapPacket.Tiles entirely (the client rebuilds from a blank grid), so this is a sparse format.
    public sealed record TileData(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("g")] int[] Ground,
        [property: JsonPropertyName("f")] int[] Fringe,
        [property: JsonPropertyName("type")] TileType Type,
        // The ground attribute's authored fields; null when this kind of tile has none (Walkable,
        // Blocked, NpcAvoid), which is the overwhelming majority — so the wire got smaller, not bigger.
        [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AttrFields? Fields,
        // Canopy: the third visual stack, drawn atop everything (mirrors Ground/Fringe, trailing-empties trimmed).
        [property: JsonPropertyName("c")] int[] Canopy,
        // Fringe-layer gameplay attribute; non-null iff a walkable fringe layer (bridge top) exists here.
        [property: JsonPropertyName("fa"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FringeData? FringeAttr
    )
    {
        /// <summary>Build a wire tile from a map tile, trimming trailing-empty layers.</summary>
        public static TileData From(int x, int y, TileRecord t) =>
            new(x, y, Trim(t.Ground), Trim(t.Fringe), t.Type, AttrFields.FromGround(t),
                Trim(t.Canopy),
                t.FringeAttr is { } fa ? new FringeData(fa.Type, AttrFields.From(fa)) : null);

        /// <summary>Materialize a fresh <see cref="TileRecord"/> from this wire tile.</summary>
        public TileRecord ToTile()
        {
            var tile = new TileRecord { Type = Type };
            if (Fields is { } f) tile = f.ApplyTo(tile);
            tile = tile.WithArt(LayerType.Ground, Ground)
                       .WithArt(LayerType.Fringe, Fringe)
                       .WithArt(LayerType.Canopy, Canopy);
            if (FringeAttr is { } fa)
            {
                var attr = new Records.FringeAttr { Type = fa.Type };
                if (fa.Fields is { } ff) attr = ff.ApplyTo(attr);
                tile = tile with { FringeAttr = attr };
            }
            return tile;
        }

        /// <summary>True when a tile carries nothing — no layers, default type, no fields, no fringe layer —
        /// so it can be omitted from a sparse SendMap.</summary>
        public static bool IsDefault(TileRecord t) =>
            t.Type == TileType.Walkable && t.FringeAttr is null && t.HasNoArt;

        private static int[] Trim(ReadOnlySpan<int> layers)
        {
            int last = -1;
            for (int i = 0; i < layers.Length; i++)
                if (!LayerCell.IsEmpty(layers[i])) last = i;
            if (last < 0) return [];
            return layers[..(last + 1)].ToArray();
        }

        private static void CopyClamped(int[]? src, int[] dest)
        {
            if (src is null) return;
            int n = Math.Min(src.Length, dest.Length);
            for (int i = 0; i < n; i++) dest[i] = src[i];
        }
    }

    /// <summary>Compact wire form of a tile's fringe-layer attribute (see <see cref="Records.FringeAttr"/>).
    /// Its PRESENCE (non-null on <see cref="TileData"/>) marks that a walkable fringe layer exists.</summary>
    public sealed record FringeData(
        [property: JsonPropertyName("type")] TileType Type,
        [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AttrFields? Fields
    );

    /// <summary>The authored fields of one tile attribute, on either plane — the wire form of the field
    /// set shared by <see cref="TileRecord"/>, <see cref="Records.FringeAttr"/> and <see cref="TileAttr"/>.
    /// <para>One record for both planes rather than the fields being spelled out twice, and null (rather
    /// than a run of zeros) when the tile's type authors nothing — which is true of every Walkable,
    /// Blocked and NpcAvoid tile, i.e. nearly all of them. Keys stay terse because this is machine-read;
    /// the on-disk format spells them out in full instead.</para></summary>
    public sealed record AttrFields
    {
        [property: JsonPropertyName("wm"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public short WarpMap { get; init; }
        [property: JsonPropertyName("wx"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public ushort WarpX { get; init; }
        [property: JsonPropertyName("wy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public ushort WarpY { get; init; }
        [property: JsonPropertyName("wl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public WorldLayer WarpLayer { get; init; }
        [property: JsonPropertyName("in"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public short ItemNum { get; init; }
        [property: JsonPropertyName("iv"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public short ItemQuantity { get; init; }
        [property: JsonPropertyName("ir"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public short ItemRespawnSecs { get; init; }
        [property: JsonPropertyName("kn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public short KeyItemNum { get; init; }
        [property: JsonPropertyName("kc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool KeyIsConsumed { get; init; }
        [property: JsonPropertyName("dx"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public ushort DoorX { get; init; }
        [property: JsonPropertyName("dy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public ushort DoorY { get; init; }
        [property: JsonPropertyName("dl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public WorldLayer DoorLayer { get; init; }
        [property: JsonPropertyName("rg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Direction RampGroundSide { get; init; }
        // What the wall lets THROUGH, carried as the exception so an ordinary wall omits both. A Blocked
        // tile that stops everything sends no fields at all (see FromGround), and the receiving record's
        // own defaults are already solid.
        [property: JsonPropertyName("lp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool LightPasses { get; init; }
        [property: JsonPropertyName("sp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool SightPasses { get; init; }

        public static AttrFields? FromGround(TileRecord t) =>
            !TileAttrRules.UsesAnyField(t.Type) || Solid(t.Type, t.BlocksLight, t.BlocksSight) ? null : new AttrFields
        {
            LightPasses = !t.BlocksLight, SightPasses = !t.BlocksSight,
            WarpMap = t.WarpMap, WarpX = t.WarpX, WarpY = t.WarpY, WarpLayer = t.WarpLayer,
            ItemNum = t.ItemNum, ItemQuantity = t.ItemQuantity, ItemRespawnSecs = t.ItemRespawnSecs,
            KeyItemNum = t.KeyItemNum, KeyIsConsumed = t.KeyIsConsumed,
            DoorX = t.DoorX, DoorY = t.DoorY, DoorLayer = t.DoorLayer,
            RampGroundSide = t.RampGroundSide,
        };

        // A plain wall carries nothing: every other field is meaningless on it, and both of its own are
        // at their default.
        private static bool Solid(TileType type, bool light, bool sight) =>
            TileAttrRules.UsesBlocked(type) && light && sight;

        public static AttrFields? From(Records.FringeAttr a) =>
            !TileAttrRules.UsesAnyField(a.Type) || Solid(a.Type, a.BlocksLight, a.BlocksSight) ? null : new AttrFields
        {
            LightPasses = !a.BlocksLight, SightPasses = !a.BlocksSight,
            WarpMap = a.WarpMap, WarpX = a.WarpX, WarpY = a.WarpY, WarpLayer = a.WarpLayer,
            ItemNum = a.ItemNum, ItemQuantity = a.ItemQuantity, ItemRespawnSecs = a.ItemRespawnSecs,
            KeyItemNum = a.KeyItemNum, KeyIsConsumed = a.KeyIsConsumed,
            DoorX = a.DoorX, DoorY = a.DoorY, DoorLayer = a.DoorLayer,
            RampGroundSide = a.RampGroundSide,
        };

        public TileRecord ApplyTo(TileRecord t) => t with
        {
            WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
            ItemNum = ItemNum, ItemQuantity = ItemQuantity, ItemRespawnSecs = ItemRespawnSecs,
            KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
            DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
            RampGroundSide = RampGroundSide,
            BlocksLight = !LightPasses, BlocksSight = !SightPasses,
        };

        public Records.FringeAttr ApplyTo(Records.FringeAttr a) => a with
        {
            WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
            ItemNum = ItemNum, ItemQuantity = ItemQuantity, ItemRespawnSecs = ItemRespawnSecs,
            KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
            DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
            RampGroundSide = RampGroundSide,
            BlocksLight = !LightPasses, BlocksSight = !SightPasses,
        };
    }
}

public sealed record JoinMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.JoinMap;
    [JsonPropertyName("index")] public int Index { get; init; }
    // Full player data is sent in a separate SendPlayerDataPacket
}

public sealed record LeaveMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LeaveMap;
    [JsonPropertyName("index")] public int Index { get; init; }
}

public sealed record PlayerXYPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerXY;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
}
