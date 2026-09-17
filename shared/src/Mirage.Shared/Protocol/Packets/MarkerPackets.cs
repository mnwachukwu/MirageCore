using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C: what a game has marked on one map. Carries that map's WHOLE current list as this client may see
/// it.
///
/// <para><b>A full-list replace, not a diff.</b> A marker can move, change color, change its meter, or
/// stop being visible to this particular player, and every one of those is a different shape of change.
/// Saying what is true now covers all of them and needs no removal wire.</para>
///
/// <para><b>Per client, not per map.</b> A marker may name who can see it, so two people standing on one
/// square can be owed different lists, as anything a side holds privately needs.
/// A map nobody has marked sends an empty list, which is how a client clears one.</para>
/// </summary>
public sealed record MarkerUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MarkerUpdate;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("marks")] public IReadOnlyList<Entry> Markers { get; init; } = [];

    /// <summary>One marker. Everything past the square is optional and drawn only when it is set: no
    /// label draws no label, no radius draws no ring, no ceiling draws no meter.</summary>
    public readonly record struct Entry(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("rgb")] int Rgb,
        [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string Label = "",
        [property: JsonPropertyName("radius"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Radius = 0,
        [property: JsonPropertyName("v"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long Value = 0,
        [property: JsonPropertyName("c"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long Ceiling = 0,
        [property: JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground);
}
