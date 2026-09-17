using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C: a line of text floats up off a body.
///
/// <para>🔴 <b>What it SAYS is a game's business; where it goes is the engine's.</b> A number, a word, a
/// name — Core has no opinion and spawns none of its own. What the client owns is the awkward part:
/// centering on an oversize body's footprint rather than its anchor tile, keeping it anchored across a
/// seam crossing, and holding it until an in-flight projectile lands so the text and the impact read as
/// one event.</para>
///
/// <para><b>Addressed to a BODY rather than to a tile</b>, so it follows. A tile
/// is where the body was when the packet was built, and a damage number that stays behind while its
/// target walks away reads as a bug.</para>
///
/// <para>Sent to the viewport rather than to the observers of a map: you see this happen to
/// somebody, at the range you would see them.</para>
/// </summary>
public sealed record FloatingTextPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.FloatingText;

    /// <summary>True when <see cref="Index"/> names an NPC slot rather than a player.</summary>
    [JsonPropertyName("npc")] public bool IsNpc { get; init; }

    /// <summary>The player's index, or the NPC's slot on <see cref="NpcMap"/>.</summary>
    [JsonPropertyName("idx")] public int Index { get; init; }

    /// <summary>The map the NPC's slot belongs to. Zero for a player.</summary>
    [JsonPropertyName("nmap")] public int NpcMap { get; init; }

    /// <summary>Where the body is standing, which is where the text starts from.</summary>
    [JsonPropertyName("map")] public int MapNum { get; init; }

    [JsonPropertyName("x")] public int X { get; init; }

    [JsonPropertyName("y")] public int Y { get; init; }

    /// <summary>What it says, already in the words a player will read.</summary>
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;

    /// <summary>Packed 0xRRGGBB, from <see cref="Extensibility.GameColor"/> or a game's own.</summary>
    [JsonPropertyName("rgb")] public uint Rgb { get; init; }

    /// <summary>How much of a burst comes with it, 0 for none. Drawn in the world's own decal color, so
    /// a game picks what a splatter looks like once rather than per hit.</summary>
    [JsonPropertyName("burst")] public float Splatter { get; init; }
}
