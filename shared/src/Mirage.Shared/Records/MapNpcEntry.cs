using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>One entry in a map's dense NPC spawn list (<see cref="MapRecord.Npcs"/>): the NPC type to spawn
/// (<see cref="Npc"/>, 1..MaxNpcs) plus an OPTIONAL fixed spawn tile (<see cref="PinX"/>/<see cref="PinY"/>;
/// both null = spawn at a random walkable tile, as before) on a given <see cref="PinLayer"/>. The 0-based list
/// index maps to the runtime spawn post: entry <c>i</c> drives <c>GameWorld.MapNpcs[map, i + 1]</c>. The pin
/// rides WITH its entry, so removing a middle row (which slides later entries down to lower posts) keeps each
/// pin bound to its own NPC — the reason a dense list can't keep pins in a separate slot-keyed list.
/// Carried whole on the wire (editor
/// round-trip), so keep it a plain record.
///
/// <para>Two-layer world: <see cref="PinLayer"/> is the plane the NPC spawns on (Ground vs the bridge Fringe);
/// pins are keyed by (tile, layer), so a Ground pin and a Fringe pin may STACK on the same tile. Defaults to
/// Ground and is omitted from JSON when Ground, so Ground-only maps stay byte-identical.</para></summary>
public readonly record struct MapNpcEntry(
    [property: JsonPropertyName("npc")] int Npc,
    [property: JsonPropertyName("pinX")] int? PinX,
    [property: JsonPropertyName("pinY")] int? PinY,
    [property: JsonPropertyName("pinLayer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer PinLayer = WorldLayer.Ground,
    [property: JsonPropertyName("pinDir"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Direction? PinDir = null)
{
    /// <summary>True when this entry pins a fixed spawn tile (both coordinates set).</summary>
    [JsonIgnore]
    public bool HasPin => PinX is not null && PinY is not null;

    /// <summary>Which way this NPC is facing when it spawns, or null to face a random way.
    ///
    /// <para>Worth authoring for anything that stays put: a shopkeeper behind a counter, a guard at a gate,
    /// a speaker at the front of a room. A wanderer turns as it walks and forgets this immediately, so it
    /// only stays true for an NPC that does not move.</para>
    ///
    /// <para>Null rather than a default member, so "face north" and "face wherever" are different
    /// statements — a default would make every unauthored NPC on a map line up facing the same way.</para></summary>
    [JsonIgnore]
    public bool HasPinnedFacing => PinDir is not null;
}
