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

    /// <summary>HP, MP, or SP changed for the given player index.</summary>
    event Action<int>? VitalsChanged;

    /// <summary>Server sent the character list (switch to CharSelect screen).</summary>
    event Action? CharacterListReceived;

    /// <summary>Class list received (used by NewChar screen to populate dropdown).</summary>
    event Action? ClassListReceived;

    /// <summary>An item spawned or despawned in the given map-item slot.</summary>
    event Action<int>? MapItemChanged;

    /// <summary>An NPC spawned or died in the given map-NPC slot.</summary>
    event Action<int>? MapNpcChanged;

    /// <summary>Entered a shop map; the given shop's trade list is now loaded.</summary>
    event Action<int>? ShopOpened;

    /// <summary>An NPC interact opened a keeper's inn — raise the (client-local) Inn panel.</summary>
    event Action? OpenInn;

    /// <summary>A melee-key NPC interact hit a quest-giver/turn-in — open the client-built quest menu for the NPC
    /// at (mapNum, npcSlot). The client already holds the quest defs + log, so it builds the menu locally.</summary>
    event Action<int, int>? OpenNpcQuestMenu;

    /// <summary>An NPC interact resolved to a conversation — open the conversation panel for the NPC at
    /// (mapNum, npcSlot) on conversation (convNum). The client holds the cached tree and walks it locally.</summary>
    event Action<int, int, int>? OpenNpcConversation;

    /// <summary>Stats updated — stat POINTS > 0, so the Training panel can be shown.</summary>
    event Action? TrainingReady;

    /// <summary>Player spells received from server; arg is the persisted prepared-spell slot (0 = none).</summary>
    event Action<int>? PreparedSpellReceived;

    /// <summary>Another player sent a party request to us.</summary>
    event Action<string, int>? PartyRequest;

    /// <summary>A guild invite or join-request offer arrived for us (show the accept/decline prompt).</summary>
    event Action<GuildOfferNotifyPacket>? GuildOffer;

    /// <summary>A direct-trade invite arrived (the inviter's character name); show the accept/decline prompt.</summary>
    event Action<string>? TradeInvite;

    /// <summary>Server broadcast an updated total players online count.</summary>
    event Action<int>? PlayersOnlineChanged;

    /// <summary>Local player gained one or more levels.</summary>
    event Action? LevelUp;

    /// <summary>Server assigned a new target to the local player (e.g. auto-target on melee hit).</summary>
    event Action<TargetRef>? TargetAssigned;

    /// <summary>
    /// Vital changed in a way that should produce a floating combat number.
    /// delta > 0 = heal/gain, delta < 0 = damage/loss.
    /// Args: entityIndex, delta, type, isNpc, isCrit, npcMap (the NPC's map for isNpc — so the number
    /// floats on a neighbor map too; 0/ignored for players, which resolve by their own record).
    /// </summary>
    event Action<int, int, VitalType, bool, bool, int>? VitalDelta;

    /// <summary>An entity blocked or dodged an attack — the client floats localized cyan text over it.</summary>
    event Action<CombatTextPacket>? CombatText;
}
