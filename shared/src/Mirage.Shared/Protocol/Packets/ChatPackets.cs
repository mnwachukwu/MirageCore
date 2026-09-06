using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record SayMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SayMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record EmoteMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EmoteMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record BroadcastMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BroadcastMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

/// <summary>A yell — heard across the speaker's whole observable region (their cell and its
/// neighbors), one tier louder than a viewport-local "say".</summary>
public sealed record YellMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.YellMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record NoticeMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NoticeMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record AdminMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AdminMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record PlayerMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerMsg;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}

public sealed record RollPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Roll;
    [JsonPropertyName("max")] public byte Max { get; init; } = 100;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record ChatMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ChatMsg;
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
    [JsonPropertyName("color")] public int Color { get; init; }
    // Classification for client-side tab filtering. Every server send site tags this; there is
    // no sensible default that wouldn't silently mis-bucket missed sites.
    [JsonPropertyName("ch")] public ChatChannel Channel { get; init; }
    // Optional speaker identity for player-originated chat. Null on system messages.
    // SpeakerShowAsPk is frozen at send time so chat history keeps the color the speaker
    // had when speaking, even after their PK timer expires.
    [JsonPropertyName("sn")] public string? SpeakerName { get; init; }
    [JsonPropertyName("sa")] public AdminLevel? SpeakerAccess { get; init; }
    [JsonPropertyName("sp")] public bool? SpeakerShowAsPk { get; init; }
}

/// <summary>Player-spoken bubble. Kind picks the border color: 0=Say (silver), 1=Yell (yellow),
/// 2=Broadcast (pink). Broadcast scopes are intentionally larger than yell — the client gates
/// rendering on viewport so latent observers only see the bubble if they enter the speaker's
/// region during its lifetime.</summary>
public sealed record ChatBubblePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ChatBubble;
    [JsonPropertyName("idx")] public int PlayerIndex { get; init; }
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
    [JsonPropertyName("kind")] public byte Kind { get; init; }
}

/// <summary>NPC-spoken bubble for AttackSay. Kind: 0=Hostile (red), 1=Friendly/Stationary (green).
/// Always sent target-only to match the existing AttackSay chat-log scoping.
///
/// Addressing: native slot is identified by (<see cref="MapNum"/>, <see cref="NpcSlot"/>); a
/// traversal guest is identified by (<see cref="SpawnMap"/>, <see cref="SpawnSlot"/>) with
/// <see cref="NpcSlot"/>=0, since guests don't occupy a slot in any current map.  The client
/// dispatches on whichever pair is populated.</summary>
public sealed record NpcChatBubblePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcChatBubble;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int NpcSlot { get; init; }
    [JsonPropertyName("smap")] public int SpawnMap { get; init; }
    [JsonPropertyName("sslot")] public int SpawnSlot { get; init; }
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
    [JsonPropertyName("kind")] public byte Kind { get; init; }
}
