using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the NPC conversation DEFINITIONS (1-based; speaker NPC + node tree), sent once at join like
/// items/npcs/quests. The client caches these and walks a tree locally when a conversation opens; only a
/// terminal hand-off choice round-trips. Only non-empty conversations are included.</summary>
public sealed record SendConversationsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendConversations;
    [JsonPropertyName("convs")] public List<ConvData> Conversations { get; init; } = new();

    /// <summary>One conversation definition. Nodes/choices ride the shared records (like a quest's objectives
    /// ride the shared Objective record).</summary>
    public sealed record ConvData
    {
        [JsonPropertyName("num")] public int Num { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("speaker")] public int SpeakerNpc { get; init; }
        [JsonPropertyName("root")] public int RootNodeId { get; init; }
        [JsonPropertyName("nodes")] public List<ConversationNode> Nodes { get; init; } = new();
    }
}

/// <summary>S→C: the set of conversation numbers this CHARACTER has already spoken to (the visited-log),
/// replaced wholesale on any change. The client colors each talkable NPC's overhead "..." glyph yellow
/// (unspoken) or gray (spoken) from this set. Pushed at join and again whenever a new conversation is opened.</summary>
public sealed record ConversationLogPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ConversationLog;
    [JsonPropertyName("spoken")] public int[] Spoken { get; init; } = System.Array.Empty<int>();
}

/// <summary>S→C: open the client conversation panel for the NPC at (map, slot) — the reply to an NpcInteract
/// that resolved talk-first (or a context-menu "Talk"). Carries the conversation number so the client opens the
/// exact cached tree; map+slot let a hand-off choice re-issue an NpcInteract at the same NPC.</summary>
public sealed record OpenNpcConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.OpenNpcConversation;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int NpcSlot { get; init; }
    [JsonPropertyName("conv")] public int ConvNum { get; init; }
}
