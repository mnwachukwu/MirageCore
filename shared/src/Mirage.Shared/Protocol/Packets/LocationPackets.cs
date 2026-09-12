using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record RequestLocationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestLocation;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

