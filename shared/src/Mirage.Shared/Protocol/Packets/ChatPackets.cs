using Mirage.Shared.Extensibility;
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
    // Which channel it reads on, by id: one of Core's five names, or one this game declared. Every
    // server send site tags this; there is no sensible default that wouldn't silently mis-bucket
    // missed sites.
    [JsonPropertyName("ch")] public string Channel { get; init; } = ChatChannels.System;
    // Optional speaker identity for player-originated chat. Null on system messages.
    // SpeakerShowAsMarked is frozen at send time so chat history keeps the color the speaker
    // had when speaking, even after the mark expires.
    [JsonPropertyName("sn")] public string? SpeakerName { get; init; }
    [JsonPropertyName("sa")] public AdminLevel? SpeakerAccess { get; init; }
    [JsonPropertyName("sp")] public bool? SpeakerShowAsMarked { get; init; }
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

/// <summary>
/// S→C, once per session: the chat channels this game declared.
///
/// <para>Core's own five need no announcing — a client that can draw chat at all knows them. These are
/// the ones the world added, and the options panel offers them under their own heading.</para>
/// </summary>
public sealed record ChatChannelsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ChatChannels;

    [JsonPropertyName("channels")] public IReadOnlyList<ChatChannelSpec> Channels { get; init; } = [];
}

/// <summary>The bubble over a creature speaking its line, sent to the one player who was noticed so it
/// lands beside the same line in the chat log. Its color is not here: the client draws it in the color
/// this creature's NAME is drawn in, which the game declared.
///
/// <para>Addressing: a native is identified by (<see cref="MapNum"/>, <see cref="NpcSlot"/>), a
/// traversal guest by (<see cref="SpawnMap"/>, <see cref="SpawnSlot"/>) with <see cref="NpcSlot"/>
/// at 0, since a guest occupies no slot on the map it is standing on. The client dispatches on
/// whichever pair is populated.</para></summary>
public sealed record NpcChatBubblePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcChatBubble;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int NpcSlot { get; init; }
    [JsonPropertyName("smap")] public int SpawnMap { get; init; }
    [JsonPropertyName("sslot")] public int SpawnSlot { get; init; }
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
}
