using Mirage.Client.Core.State;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Lazy-allocates the per-entity chat-bubble drifter list and pushes the current
/// head bubble into it. Two overloads — one for players, one for NPCs — because the
/// drifter record types are tightly coupled to their owners (<see cref="ChatBubbleDrifter"/>
/// vs <see cref="NpcChatBubbleDrifter"/>). Same shape, different types; merging them
/// into one shared type is in-scope for a future record refactor but not here.
/// </summary>
public static class ChatBubbleManager
{
    /// <summary>Append the player's current head bubble (if any) to the drifter list,
    /// lazy-allocating the list on first use. Caller decides whether to keep or clear
    /// <see cref="PlayerRecord.ChatBubbleText"/> after — see
    /// <see cref="NaturallyExpire(PlayerRecord, long)"/> for the "demote and clear" combo
    /// used when a bubble times out, vs the demote-only variant used when a new bubble
    /// is replacing the head.</summary>
    public static void DemoteHeadToDrifter(PlayerRecord player, long now)
    {
        if (player.ChatBubbleText is null) return;
        player.ChatBubbleDrifters ??= new List<ChatBubbleDrifter>(4);
        player.ChatBubbleDrifters.Add(new ChatBubbleDrifter(player.ChatBubbleText, player.ChatBubbleColor, now));
    }

    /// <summary>Demote + clear the head — the natural-expiry combo used when a bubble's
    /// <see cref="PlayerRecord.ChatBubbleEndMs"/> elapses without a replacement.</summary>
    public static void NaturallyExpire(PlayerRecord player, long now)
    {
        DemoteHeadToDrifter(player, now);
        player.ChatBubbleText = null;
    }

    public static void DemoteHeadToDrifter(ClientMapNpc npc, long now)
    {
        if (npc.ChatBubbleText is null) return;
        npc.ChatBubbleDrifters ??= new List<NpcChatBubbleDrifter>(4);
        npc.ChatBubbleDrifters.Add(new NpcChatBubbleDrifter(npc.ChatBubbleText, npc.ChatBubbleColor, now));
    }

    public static void NaturallyExpire(ClientMapNpc npc, long now)
    {
        DemoteHeadToDrifter(npc, now);
        npc.ChatBubbleText = null;
    }
}
