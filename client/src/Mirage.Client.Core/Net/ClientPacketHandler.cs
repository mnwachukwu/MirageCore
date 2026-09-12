using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Net;

/// <summary>Payload for <see cref="ClientPacketHandler.SpellCast"/>: the caster's tile + walk offsets, the
/// resolved <see cref="SpellType"/> (drives the FX color/shape), and the target identity so the shell can
/// home the projectile to the live target (or play the FX in place for a self-cast/unresolved target).</summary>
public readonly record struct SpellCastFx(
    int CasterMap, int CasterX, int CasterY, float CasterXOff, float CasterYOff, int CasterSize,
    SpellType Type, TargetRef Target);

/// <summary>Payload for <see cref="ClientPacketHandler.EntityDied"/>: a killed entity's target identity +
/// pre-clear render state, so the shell can hold a delayed-death sprite in place until a killing spell bolt
/// visibly lands. Works uniformly for NPCs, traversal guests, and (via the server death signal) players.</summary>
public readonly record struct EntityDeathFx(TargetRef Target, int SpriteRow, int Map, int X, int Y, float XOff, float YOff, Direction Dir, int Size = 1);

/// <summary>
/// Processes all S→C JSON lines, updates <see cref="ClientState"/>, and raises
/// <see cref="IClientEvents"/> for Shell/MenuLogic subscribers.
///
/// Dispatches every inbound server packet into client state.
/// </summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── IClientEvents ─────────────────────────────────────────────────────────

    public event Action<string, AlertCode>? AlertMessage;
    public event Action? InGame;
    public event Action? MapReady;
    public event Action<ChatMsgPacket>? ChatMessage;
    public event Action? InventoryChanged;
    public event Action? CharacterListReceived;
    public event Action<int>? MapItemChanged;
    public event Action<int>? MapNpcChanged;
    public event Action<int>? ShopOpened;
    public event Action? OpenInn;
    public event Action<int, int>? OpenNpcQuestMenu;
    public event Action<int, int, int>? OpenNpcConversation;   // map, slot, conversation number
    public event Action<string, int>? PartyRequest;
    public event Action<GuildOfferNotifyPacket>? GuildOffer;
    public event Action<string>? TradeInvite;
    public event Action<int>? PlayersOnlineChanged;
    public event Action<TargetRef>? TargetAssigned;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ClientState _state;
    private readonly ClientPacketSender _sender;
    private readonly IMapCache _mapCache;

    public ClientPacketHandler(ClientState state, ClientPacketSender sender, IMapCache mapCache)
    {
        _state = state;
        _sender = sender;
        _mapCache = mapCache;
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    public void Handle(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Read the header without building a DOM, and hand it back rather than paying for a second
        // scan. It carries whether a top-level "index" is present, which is what resolves the two
        // commands used in both directions — the registry picks the right shape from it.
        var header = PacketSerializer.ReadHeader(line);
        if (header.Cmd is null) return;

        var packet = PacketSerializer.TryDeserialize(line, header);
        if (packet is null) return;

        switch (packet)
        {
            // Pre-login / account
            case AlertMsgPacket p:
                HandleAlertMsg(p);
                break;
            case ServerHelloPacket p:
                HandleServerHello(p);
                break;
            case QueueUpdatePacket p:
                HandleQueueUpdate(p);
                break;
            case SendCharsPacket p:
                HandleSendChars(p);
                break;

            // Entering the game
            case WelcomePacket p:
                HandleWelcome(p);
                break;
            case PlayerInGamePacket:
                HandlePlayerInGame();
                break;

            // Movement. These two commands are also sent the other way, carrying no index; the
            // registry resolves which shape arrived, so only the inbound form reaches here.
            case SendPlayerMovePacket p:
                HandleSendPlayerMove(p);
                break;
            case SendPlayerDirPacket p:
                HandleSendPlayerDir(p);
                break;

            // Map loading
            case CheckForMapPacket p:
                HandleCheckForMap(p);
                break;
            case SeamlessCrossPacket p:
                HandleSeamlessCross(p);
                break;
            case SendMapPacket p:
                HandleSendMap(p);
                break;
            case MapItemsPacket p:
                HandleMapItems(p);
                break;
            case MapNpcsPacket p:
                HandleMapNpcs(p);
                break;

            // World data (sent once on join)
            case SendItemsPacket p:
                HandleSendItems(p);
                break;
            case SendNpcsPacket p:
                HandleSendNpcs(p);
                break;
            case SendShopsPacket p:
                HandleSendShops(p);
                break;
            case SendMapGroupsPacket p:
                HandleSendMapGroups(p);
                break;
            case SendQuestsPacket p:
                HandleSendQuests(p);
                break;
            case SendConversationsPacket p:
                HandleSendConversations(p);
                break;

            // Live edits broadcast by the server after an editor save (no restart needed).
            case UpdateItemPacket p:
                HandleUpdateItem(p);
                break;
            case UpdateNpcPacket p:
                HandleUpdateNpc(p);
                break;
            case UpdateShopPacket p:
                HandleUpdateShop(p);
                break;
            case UpdateQuestPacket p:
                HandleUpdateQuest(p);
                break;
            case UpdateConversationPacket p:
                HandleUpdateConversation(p);
                break;
            case UpdateSpellPacket p:
                HandleUpdateSpell(p);
                break;
            case UpdateMapGroupPacket p:
                HandleUpdateMapGroup(p);
                break;

            // Player state
            case SendInventoryPacket p:
                HandleSendInventory(p);
                break;
            case InventoryUpdatePacket p:
                HandleInventoryUpdate(p);
                break;
            case EquippedGearPacket p:
                HandleEquippedGear(p);
                break;
            case PlayerHotkeysPacket p:
                HandlePlayerHotkeys(p);
                break;

            // Map entities
            case SendPlayerDataPacket p:
                HandleSendPlayerData(p);
                break;
            case AggressorRefreshPacket p:
                HandleAggressorRefresh(p);
                break;
            case LeftGamePacket p:
                HandleLeftGame(p);
                break;
            case LeaveMapPacket p:
                HandleLeaveMap(p);
                break;
            case PlayerXYPacket p:
                HandlePlayerXY(p);
                break;

            // NPC
            case NpcSpawnPacket p:
                HandleNpcSpawn(p);
                break;
            case NpcMovePacket p:
                HandleNpcMove(p);
                break;
            case NpcDirPacket p:
                HandleNpcDir(p);
                break;
            case NpcDeadPacket p:
                HandleNpcDead(p);
                break;
            case NpcTargetPacket p:
                HandleNpcTarget(p);
                break;
            case TraversalNpcPacket p:
                HandleTraversalNpc(p);
                break;
            case NpcDespawnPacket p:
                HandleNpcDespawn(p);
                break;

            // Combat
            case SetTargetPacket p:
                HandleSetTarget(p);
                break;
            case ClearTargetPacket:
                HandleClearTarget();
                break;

            // World events
            case WeatherPacket p:
                HandleWeather(p);
                break;
            case TimeOfDayPacket p:
                HandleTimeOfDay(p);
                break;
            case MapKeyPacket p:
                HandleMapKey(p);
                break;

            // Chat
            case ChatMsgPacket p:
                HandleChatMsg(p);
                break;
            case ChatBubblePacket p:
                HandleChatBubble(p);
                break;
            case NpcChatBubblePacket p:
                HandleNpcChatBubble(p);
                break;

            // Bank
            case SendBankPacket p:
                HandleSendBank(p);
                break;
            case BankSlotUpdatePacket p:
                HandleBankSlotUpdate(p);
                break;

            // Shop
            case ShopContentsPacket p:
                HandleShopContents(p);
                break;
            case OpenInnPacket p:
                HandleOpenInn(p);
                break;

            // Quests (per-player log + the melee-key gossip-menu trigger)
            case QuestLogPacket p:
                HandleQuestLog(p);
                break;
            case OpenNpcQuestMenuPacket p:
                OpenNpcQuestMenu?.Invoke(p.MapNum, p.NpcSlot);
                break;

            // NPC conversations (defs at join, the character's spoken-set, and the open-panel trigger)
            case ConversationLogPacket p:
                HandleConversationLog(p);
                break;
            case OpenNpcConversationPacket p:
                OpenNpcConversation?.Invoke(p.MapNum, p.NpcSlot, p.ConvNum);
                break;

            // Party
            case PartyRequestNotifyPacket p:
                HandlePartyRequest(p);
                break;
            case GuildOfferNotifyPacket p:
                HandleGuildOffer(p);
                break;
            case PartyVitalsPacket p:
                HandlePartyVitals(p);
                break;

            // Mail
            case MailboxPacket p:
                HandleMailbox(p);
                break;
            case MarketListPacket p:
                HandleMarketList(p);
                break;
            case TradeInviteNotifyPacket p:
                TradeInvite?.Invoke(p.FromName);
                break;
            case TradeWindowPacket p:
                _state.SetTradeWindow(p.PartnerName, p.MyOffer, p.TheirOffer, p.MyConfirmed, p.TheirConfirmed, p.Open);
                break;

            // Social panel (guild roster + friends/ignore + open-guild browser)
            case GuildInfoPacket p:
                HandleGuildInfo(p);
                break;
            case SocialListPacket p:
                HandleSocialList(p);
                break;
            case ModerationListPacket p:
                HandleModerationList(p);
                break;
            case GuildBrowsePacket p:
                HandleGuildBrowse(p);
                break;

            case PlayersOnlinePacket p:
                HandlePlayersOnline(p);
                break;
        }
    }

    // ── Pre-login / account ────────────────────────────────────────────────────

    private void HandleAlertMsg(AlertMsgPacket p) => AlertMessage?.Invoke(p.Message, p.Code);

    /// <summary>What this server is, before we have told it anything about ourselves. Currently just the
    /// player limit, which is what every per-frame pass bounds itself by — see
    /// <see cref="ClientState.PlayerSlots"/>.</summary>
    private void HandleServerHello(ServerHelloPacket p)
    {
        _state.PlayerSlots = p.MaxPlayers;
        _state.GameName = p.GameName;
        // Before a single record arrives, so every table is already the right size to receive them.
        _state.ApplyServerLimits(p.Records);
        // What character creation may offer. Held from the greeting because the screen is reached
        // without asking the server anything.
        _state.Appearances = p.Appearances;
        GameNameChanged?.Invoke(_state.GameName);
    }

    /// <summary>Raised when a server names the world. The window title lives on the game object rather
    /// than in <see cref="ClientState"/>, so it has to be told rather than read.</summary>
    public event Action<string>? GameNameChanged;

    /// <summary>The server is full and we are waiting for a slot. Stores the numbers and nothing else —
    /// the sentence is written by the screen, out of the CLIENT's string table, so the line a player reads
    /// while waiting is in the language their menus are already in.</summary>
    private void HandleQueueUpdate(QueueUpdatePacket p)
    {
        _state.QueuePosition = p.Position;
        _state.QueueTotal = p.Total;
    }

    private void HandleSendChars(SendCharsPacket p)
    {
        _state.CharSlots = p.Chars;
        // Logout-to-char-select reuses this packet to bounce us back. The server's DisbandParty
        // only pushes a clear to the *partner*, so without this the prior session's snapshot would
        // linger and the overlay would still draw on the next login.
        _state.Party.Clear();
        CharacterListReceived?.Invoke();
    }

    // ── Entering the game ─────────────────────────────────────────────────────

    private void HandleWelcome(WelcomePacket p)
    {
        _state.MyIndex = p.Index;
    }

    private void HandlePlayerInGame()
    {
        _state.InGame = true;
        InGame?.Invoke();
    }
}
