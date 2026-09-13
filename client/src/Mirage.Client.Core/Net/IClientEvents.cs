using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>
/// Events fired by <see cref="ClientPacketHandler"/> when server packets cause state changes
/// that the Shell needs to react to (screen transitions, panel toggles, etc.).
/// All events are raised on whatever thread processes packets — Shell subscribers must
/// marshal to the UI thread if necessary.
/// </summary>
public interface IClientEvents
{
    /// <summary>Server sent an alert message (bad password, server full, etc.).</summary>
    event Action<string, AlertCode>? AlertMessage;

    /// <summary>Server confirmed we are fully in the game world.</summary>
    event Action? InGame;

    /// <summary>Map data confirmed and join-data received — map is ready to render.</summary>
    event Action? MapReady;

    /// <summary>A chat line was received from the server. The packet carries optional speaker
    /// identity (name, access, frozen PK status) so the chat panel can color the name and tag a
    /// right-click span; system messages leave those fields null.</summary>
    event Action<ChatMsgPacket>? ChatMessage;

    /// <summary>Inventory contents changed (full sync or single slot update).</summary>
    event Action? InventoryChanged;

    /// <summary>Server sent the character list (switch to CharSelect screen).</summary>
    event Action? CharacterListReceived;

    /// <summary>An item spawned or despawned in the given map-item slot.</summary>
    event Action<int>? MapItemChanged;

    /// <summary>An NPC spawned or died in the given map-NPC slot.</summary>
    event Action<int>? MapNpcChanged;

    /// <summary>Entered a shop map; the given shop's trade list is now loaded.</summary>
    event Action<int>? ShopOpened;

    /// <summary>An NPC interact opened a keeper's inn — raise the (client-local) Inn panel.</summary>
    event Action? OpenInn;

    /// <summary>An NPC interact resolved to a conversation — open the conversation panel for the NPC at
    /// (mapNum, npcSlot) on conversation (convNum). The client holds the cached tree and walks it locally.</summary>
    event Action<int, int, int>? OpenNpcConversation;

    /// <summary>Another player sent a party request to us.</summary>
    event Action<string, int>? PartyRequest;

    /// <summary>A guild invite or join-request offer arrived for us (show the accept/decline prompt).</summary>
    event Action<GuildOfferNotifyPacket>? GuildOffer;

    /// <summary>A direct-trade invite arrived (the inviter's character name); show the accept/decline prompt.</summary>
    event Action<string>? TradeInvite;

    /// <summary>Server broadcast an updated total players online count.</summary>
    event Action<int>? PlayersOnlineChanged;

    /// <summary>Server assigned a new target to the local player (e.g. auto-target on melee hit).</summary>
    event Action<TargetRef>? TargetAssigned;

}
