using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.Net;

/// <summary>
/// Bundles the chat-packet metadata that's constant across all recipients of a broadcast: color,
/// channel, and (for player-originated chat) the speaker triplet. Mirrors <c>ChatMsgPacket</c>'s
/// shape so the dispatcher can pick the right <c>PacketBuilder.ChatMsg</c> overload based on
/// whether <see cref="SpeakerName"/> is set.
///
/// <see cref="SpeakerLogin"/> is the enforcement hook for the ignore list: set it on any
/// player-originated chat and the dispatcher silently drops that message for recipients who ignore
/// that account. It is deliberately NOT sent on the wire — it exists only to filter recipients.
/// Leave it null for engine/system messages, which are never suppressible.
/// </summary>
public readonly record struct ChatMetadata(
    int Color,
    ChatChannel Channel,
    string? SpeakerName = null,
    AdminLevel? SpeakerAccess = null,
    bool? SpeakerShowAsPk = null,
    string? SpeakerLogin = null);

public interface IPacketDispatcher
{
    void SendTo(int index, IPacket packet);
    void SendToAll(IPacket packet);
    void SendToAllBut(int exclude, IPacket packet);
    /// <summary>
    /// Sends to every player index in <paramref name="observers"/> (typically
    /// <c>GameWorld.MapObservers[mapNum]</c>) — i.e. everyone who can see that map,
    /// on it or on a neighbor.  This (and <see cref="SendToObserversBut"/>) is the audience
    /// primitive for the seamless world; there is no "send to one map cell's occupants" form,
    /// because in a contiguous world the audience for an event is who can <i>see</i> it, not who
    /// stands on a particular cell.  Cell-local "earshot" is handled one layer up via a viewport
    /// filter over an observer set.
    /// </summary>
    void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet);
    void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet);
    /// <summary>
    /// Sends to the tighter <i>viewport</i> (earshot) audience: the subset of the speaker's observers
    /// who fall within the speaker's centered viewport band.  Viewport ⊂ observers.  Used for
    /// proximity-local chat text (say, emote, cast/drop/door notices) — not entity sync, which stays
    /// observer-scoped because the whole region is rendered.
    /// </summary>
    void SendToViewport(int speakerIndex, IPacket packet);
    /// <summary>
    /// NPC-anchored viewport send: same earshot scope as <see cref="SendToViewport"/> but the
    /// speaker is a tile position (mapNum,x,y) instead of a player index.  Used for NPC cast
    /// announcements so observers hear them at the same range a player's cast announcement
    /// would carry — not the whole observer region.
    /// </summary>
    void SendToViewportAt(int mapNum, int x, int y, IPacket packet);

    /// <summary>Broadcast a prebuilt chat-bubble packet to a speaker's audience, SKIPPING recipients who
    /// ignore the speaker's account — so a bubble respects the ignore list the same way the chat text does.
    /// <paramref name="wholeRegion"/> false = the speaker's viewport (say-range), true = all map observers
    /// (yell-range). Unlike the plain viewport/observer sends, this always filters by
    /// <paramref name="senderLogin"/>.</summary>
    void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion);

    void SendToAdmins(IPacket packet);

    // ── Guild send-scopes ────────────────────────────────────────────────────
    /// <summary>Sends to every online player whose account belongs to guild
    /// <paramref name="guildId"/> (1-based; a guildId < 1 reaches no one). The guild-scoped
    /// counterpart of <see cref="SendToAll"/>; recipients are found by a linear scan of the slots
    /// (there is no per-guild roster index).</summary>
    void SendToGuild(int guildId, IPacket packet);
    void SendToGuildBut(int guildId, int exclude, IPacket packet);

    // ── Per-recipient localized chat ─────────────────────────────────────────
    // Each method iterates its recipient set on the dispatcher side and resolves the
    // localized text per recipient via ServerStrings.ForPlayer(index, key, args) before
    // building the wire packet. The recipient sees the message in their own session locale.

    void SendLocalizedChatTo(int index, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToAll(string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendLocalizedChatToAdmins(string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    /// <summary>Per-recipient localized chat to every online member of guild
    /// <paramref name="guildId"/> — the Guild channel and guild system notices.</summary>
    void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    /// <summary>Per-recipient localized chat to the guild's Leader + Officers only — the Guild
    /// Officer (<c>/o</c>) channel.</summary>
    void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args);

    void SendToEditor(int editorIndex, IPacket packet);
    /// <summary>Every authenticated editor session. Used for the record-lock table, which every editor has
    /// to agree on for the indicators to mean anything.</summary>
    void SendToAllEditors(IPacket packet);
    void Disconnect(int index);
    void DisconnectEditor(int editorIndex);
    /// <summary>
    /// Drains any pending sends to <paramref name="index"/> then closes the connection.
    /// Use this instead of <see cref="Disconnect"/> when a packet was just enqueued
    /// (e.g. AlertAndDisconnect) so the packet is guaranteed to reach the client.
    /// </summary>
    void GracefulDisconnect(int index);
    void GracefulDisconnectEditor(int editorIndex);
}
