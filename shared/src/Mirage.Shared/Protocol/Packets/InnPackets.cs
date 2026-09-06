using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>C→S: player confirmed the set-spawn cost dialog at an Inn.</summary>
public sealed record ConfirmSetSpawnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ConfirmSetSpawn;
}
