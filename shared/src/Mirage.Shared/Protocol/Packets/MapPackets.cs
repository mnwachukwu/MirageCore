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

    // Only the inheritable fields the client resolves against. Anything the client's render/predict paths
    // never read stays off the wire here.
    public sealed record GroupData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("music")] int Music,
        [property: JsonPropertyName("indoors")] bool? Indoors,
        [property: JsonPropertyName("alwaysLit")] bool? AlwaysLit,
        [property: JsonPropertyName("alwaysDark")] bool? AlwaysDark,
        [property: JsonPropertyName("passThrough")] bool? PlayersPassThrough,
        [property: JsonPropertyName("exitMap")] int ExitMap,
        [property: JsonPropertyName("exitX")] int ExitX,
        [property: JsonPropertyName("exitY")] int ExitY
    );
}

/// <summary>A map, whole.
///
/// <para>⚠ The map travels as a <see cref="Records.MapRecord"/> serialized by the SAME converters that read
/// and write a map file — <c>TileArrayConverter</c> and <c>TileRecordConverter</c>, both attached to the
/// types themselves, so they apply here without any options plumbing. There is one description of a tile in
/// this codebase and this packet uses it.</para>
///
/// <para>⚠ A hand-written projection beside it is how a tile field comes to exist in one place and not the
/// other, and the failure is silent: a payload the wire does not carry arrives at its type's DEFAULT rather
/// than failing, so an authored value is replaced by a plausible wrong one with nothing to report it. Do not
/// reintroduce a second description of a tile for any reason, compactness included.</para>
///
/// <para>The map's own properties — its size, name, links, group-inheritable fields and greeting — are read
/// off <see cref="Map"/>. Callers say <c>packet.Map.Width</c>.</para></summary>
public sealed record SendMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    // Seamless-scroll grid cell this map occupies on the client (center = 1,1).
    // Neighbor pre-loads carry their own cell; the center map keeps the default.
    [JsonPropertyName("col")] public int Col { get; init; } = 1;
    [JsonPropertyName("row")] public int Row { get; init; } = 1;

    /// <summary>The map itself — every tile, every attribute, every property.
    ///
    /// <para>Group-inheritable fields on it are RAW: 0 (ints) or null (Moral and the bools) mean "inherit
    /// from the MapGroup", and each side resolves the effective value against its own cached group. The
    /// group ships independently, so a group edit reaches players without touching a map's revision.</para>
    ///
    /// <para>The greeting and the NPC authoring pins are blanked by <c>PacketBuilder.SendMap</c> on the copy
    /// a game client gets: the server speaks the greeting itself, and the client renders NPCs from live spawn
    /// packets. ⚠ Blanked on the copy — never by a second shape for the same data.</para></summary>
    [JsonPropertyName("map")] public Records.MapRecord Map { get; init; } = new();
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
