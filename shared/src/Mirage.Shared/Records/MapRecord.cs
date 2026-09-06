using Mirage.Shared.Serialization;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One authored map: its identity, its four neighbor links, the MapGroup-inheritable properties, the
/// tile grid, and the NPC-spawn and light lists.
/// <para>Several properties are only HALF the answer on their own — they fall back to the map's
/// <see cref="MapGroup"/> when unset. Always read those through <c>MapGroupResolve</c> or the
/// <c>GameWorld.*Of(mapNum)</c> helpers; a raw read gives the map's own value and silently misses the
/// inherited one.</para>
/// </summary>
public sealed class MapRecord
{
    /// <summary>Internal identifier, shown in editor lists and tools — not player-facing.</summary>
    public string Name { get; set; } = string.Empty;
    // Player-facing name shown in the client HUD. When blank the client falls back to Name, then to a
    // generic "Map N". Name stays the internal identifier (used by editor lists and tools).
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Bumped on every save. A client holding an older revision re-fetches the map on next
    /// observe, which is how an edit reaches players without a reconnect.</summary>
    public int Revision { get; set; }
    // Neighbor map numbers for the seamless world (0 = no map that way). Diagonals are not stored —
    // WorldCoordHelper.BuildMapGrid derives them by composing two cardinal links.
    public int Up { get; set; }
    public int Down { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }

    // ── MapGroup-inheritable properties ──────────────────────────────────────────
    // ALWAYS read these through MapGroupResolve / the GameWorld.*Of(mapNum) helpers so the group fallback is
    // honored — a raw read returns the map's own value only. The int fields (Music/Boot) use 0 as the
    // "not set → inherit the group" sentinel (0 == absent, with no distinct meaning apart from "unset").
    // Moral and the environment bools are NULLABLE instead — MapMoral.None and false are real, meaningful
    // values with no spare sentinel: null = "not set → inherit", an explicit value overrides the group, and
    // null on both map+group resolves to the hard default (None / false). The greeting strings use "" (blank)
    // as their inherit sentinel and resolve per-field (map's own wins if non-blank, else the group's).
    public MapMoral? Moral { get; set; }
    public int Music { get; set; }

    /// <summary>Where this map puts a player who leaves it other than by walking: they die here, or they log
    /// out here. 0 = inherit the MapGroup's, so a whole dungeon can name one exit once.
    ///
    /// <para>It outranks the player's own Inn-purchased respawn point. What dying in a place costs is the
    /// map author's call, not something a player can buy their way out of.</para></summary>
    public int BootMap { get; set; }
    public int BootX { get; set; }
    public int BootY { get; set; }
    public bool? Indoors { get; set; }

    // The two lighting overrides are MUTUALLY EXCLUSIVE — a map cannot be both — and are resolved together by
    // MapGroupResolve.Lighting rather than read directly, so the exclusivity survives inheritance. Independent
    // of Moral: whether you can be attacked here and whether you can see here are separate claims.
    public bool? AlwaysLit { get; set; }
    public bool? AlwaysDark { get; set; }

    // ── Map-enter/leave greeting ─────────────────────────────────────────────────
    // The flavor lines spoken when a player steps onto / off this map. GreetingSpeaker is who "says" them.
    // All three inherit the MapGroup per-field when blank; resolve through GameWorld.GreetingOf /
    // MapGroupResolve.Greeting.
    public string GreetingSpeaker { get; set; } = string.Empty;
    public string JoinSay { get; set; } = string.Empty;
    public string LeaveSay { get; set; } = string.Empty;

    // Index into MapGroups (0 = none). The group supplies the fallbacks above + an optional territory.
    /// <summary>Index into MapGroups (0 = none). The group supplies the inheritable fallbacks above
    /// plus an optional territory.</summary>
    public int MapGroup { get; set; }

    // The tile grid, 0-based on both axes. Its dimensions are the map's size — see Width/Height below.
    [JsonConverter(typeof(TileArrayConverter))]
    public TileRecord[,] Tile { get; set; } = TileGrid.Empty(Constants.MaxMapX + 1, Constants.MaxMapY + 1);

    /// <summary>The map's width in tiles, read off the grid itself.</summary>
    [JsonIgnore] public int Width => Tile.GetLength(0);

    /// <summary>The map's height in tiles, read off the grid itself.</summary>
    [JsonIgnore] public int Height => Tile.GetLength(1);

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) names a real tile on this map.
    /// Every coordinate that arrives from authored data, a config file or a saved character is checked
    /// through here before it reaches <see cref="Tile"/>.</summary>
    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>True when nobody has put anything on this map: a slot the world was padded out to rather
    /// than a place.
    ///
    /// <para>Tiles are compared against a freshly built one rather than field by field, so a tile property
    /// added later is covered without this having to be remembered. Size is deliberately not part of it —
    /// a map resized and then cleared is still empty.</para></summary>
    [JsonIgnore]
    public bool IsBlank
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(DisplayName)) return false;
            if (Npcs.Count > 0 || Lights.Count > 0) return false;
            if (Up != 0 || Down != 0 || Left != 0 || Right != 0) return false;
            if (MapGroup != 0 || Music != 0 || BootMap != 0 || BootX != 0 || BootY != 0) return false;
            // All four are nullable on purpose: null is "unset", and None / false are real answers a map
            // can give. Setting any of them is authoring.
            if (Moral is not null || Indoors is not null || AlwaysLit is not null || AlwaysDark is not null) return false;
            if (!string.IsNullOrWhiteSpace(GreetingSpeaker) || !string.IsNullOrWhiteSpace(JoinSay)
                || !string.IsNullOrWhiteSpace(LeaveSay))
            {
                return false;
            }

            var blank = new TileRecord();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (Tile[x, y] != blank) return false;
                }
            }

            return true;
        }
    }

    /// <summary>Replaces one tile with what <paramref name="edit"/> makes of it —
    /// <c>map.EditTile(3, 4, t =&gt; t with { Type = TileType.Blocked })</c>.
    ///
    /// <para>A tile is a value, so changing one means producing a new one and storing it back. This is that
    /// round trip written once. A coordinate off the map is ignored.</para></summary>
    public void EditTile(int x, int y, Func<TileRecord, TileRecord> edit)
    {
        if (Contains(x, y)) Tile[x, y] = edit(Tile[x, y]);
    }

    // NPC spawn entries: a dense 0-based list (0..Count-1, cap MaxMapNpcs). Entry i drives the runtime spawn
    // post MapNpcs[map, i + 1]; each entry carries the NPC type plus its optional fixed spawn tile. Only
    // non-empty rows are stored, so a reload restores exactly the authored rows.
    public List<MapNpcEntry> Npcs { get; set; } = new();

    // Placed light sources: sparse list, at most one per tile. Emitted client-side at night / in
    // AlwaysDark maps. Empty by default, so old maps deserialize with no lights (no migration).
    public List<PlacedLight> Lights { get; set; } = new();

    public MapRecord() { }

    /// <summary>A map of a given size, filled with empty tiles.</summary>
    public MapRecord(int width, int height) => Tile = TileGrid.Empty(width, height);
}
