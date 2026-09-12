using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C: a map's stains changed. Carries the map's WHOLE current list.
///
/// <para><b>A full-list replace, not a diff.</b> Stains merge as they overlap, so a deposit can make two of
/// them become one — and there is no honest per-stain removal to send for the one that stopped existing.
/// Replacing the list says what is true now and needs no removal wire at all.</para>
///
/// <para><b>Only a CHANGE is sent.</b> Drying is not a change: both sides run the same linear decay from the
/// same constant, so a map that is merely fading costs nothing, which is most maps most of the time.</para>
/// </summary>
public sealed record DecalUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DecalUpdate;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("decals")] public IReadOnlyList<Entry> Decals { get; init; } = [];

    /// <summary>One stain. <paramref name="Amount"/> and <paramref name="Peak"/> ride as bytes quantized
    /// over [0, <see cref="Constants.DecalMaxAmount"/>]: a stain is a smudge on the ground, and a byte of
    /// precision is more than an eye can tell apart.</summary>
    public readonly record struct Entry(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("a")] byte Amount,
        [property: JsonPropertyName("p")] byte Peak,
        [property: JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground);

    /// <summary>Quantize an amount for the wire.</summary>
    public static byte Quantize(float amount) =>
        (byte)Math.Clamp((int)MathF.Round(amount / Constants.DecalMaxAmount * 255f), 0, 255);

    /// <summary>The inverse of <see cref="Quantize"/>.</summary>
    public static float Dequantize(byte b) => b / 255f * Constants.DecalMaxAmount;
}
