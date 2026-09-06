using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>
/// Deserializes and dispatches every inbound client→server JSON packet, for both game players and editor
/// sessions. Each handler validates the request — slot ranges, access level, mute/proximity gates — and then
/// delegates to the owning game system; a field that a legitimate client could not have sent is treated as a
/// hacking attempt and disconnects the sender. Both entry points are posted to the game thread by
/// <c>ReceiveLoop</c>, so handlers mutate world state lock-free; work that must touch persistence is offloaded
/// to an async continuation that hops back via <see cref="GameLoop.Post"/>.
/// </summary>
public sealed partial class PacketHandler
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly IPacketDispatcher _dispatcher;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly PlayerSaver _saver;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly MovementSystem _movement;
    private readonly CombatSystem _combat;
    private readonly ItemSystem _items;
    private readonly SpellSystem _spells;
    private readonly ShopSystem _shop;
    private readonly BankSystem _bank;
    private readonly PlayerSpawnSystem _playerSpawn;
    private readonly PartySystem _party;
    private readonly GuildSystem _guilds;
    private readonly GuildWarSystem _guildWar;
    private readonly GuildTerritorySystem _territory;
    private readonly GuildScheduleSystem _guildSchedule;
    private readonly MailSystem _mail;
    private readonly MarketSystem _market;
    private readonly TradeSystem _trade;
    private readonly QuestSystem _quests;
    private readonly ConversationSystem _conversations;
    private readonly SocialSystem _social;
    private readonly SpawnSystem _spawn;
    private readonly TimeOfDaySystem _tod;
    private readonly WeatherSystem _weather;
    private readonly GameLoop _gameLoop;
    // Lifting a punishment, shared with the server console so it is written once.
    private readonly ModerationSystem _moderation;
    private readonly ILogger<PacketHandler> _logger;

    // The same seams the game systems get from GameSystem. PacketHandler is not a GameSystem (it
    // dispatches TO the systems rather than being one), so it holds them directly. Both default to
    // the real implementations, keeping every existing construction site behavior-identical.
    private readonly IClock _clock;
    private readonly IRandomSource _rng;
    private readonly ServerConfig _config;

    /// <summary>Now as a Unix second, off the injected clock — used by the ban/mute expiry, playtime
    /// and mail-maturity handlers.</summary>
    private long NowUtc => _clock.UtcNowUnix;

    // ── Map-observer sends ────────────────────────────────────────────────────
    //
    // The same audience helpers GameSystem gives the game systems. PacketHandler is not a GameSystem
    // (it dispatches TO the systems rather than being one), so it declares its own rather than reach
    // through GameWorld into a raw HashSet<int>[] at each call site.

    /// <summary>Sends a packet to everyone observing <paramref name="mapNum"/>.</summary>
    private void SendToMap(int mapNum, IPacket packet) =>
        _dispatcher.SendToObservers(_world.MapObservers[mapNum], packet);

    /// <summary>Map-observer send that skips one player — typically the actor who caused the event.</summary>
    private void SendToMapBut(int mapNum, int exclude, IPacket packet) =>
        _dispatcher.SendToObserversBut(_world.MapObservers[mapNum], exclude, packet);

    /// <summary>Per-recipient localized chat to a map's observers. Takes the metadata rather than a
    /// color and channel because the callers here are speaker-attributed (a yell), and the speaker
    /// login in the metadata is what the ignore-list filter keys on.</summary>
    private void ChatToMap(int mapNum, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToObservers(_world.MapObservers[mapNum], key, meta, args);

    public PacketHandler(
        GameWorld world, PlayerManager pm,
        IPacketDispatcher dispatcher, IPersistenceService persistence, IBackgroundPersistence bg, PlayerSaver saver,
        JoinLeaveSystem joinLeave, MovementSystem movement, CombatSystem combat,
        ItemSystem items, SpellSystem spells, ShopSystem shop, BankSystem bank, PlayerSpawnSystem playerSpawn,
        PartySystem party, GuildSystem guilds, GuildWarSystem guildWar, GuildTerritorySystem territory, GuildScheduleSystem guildSchedule, MailSystem mail, MarketSystem market, TradeSystem trade, QuestSystem quests, ConversationSystem conversations, SocialSystem social, SpawnSystem spawn, TimeOfDaySystem tod, WeatherSystem weather, GameLoop gameLoop,
        ILogger<PacketHandler> logger,
        IClock? clock = null, IRandomSource? rng = null, ServerConfig? config = null)
    {
        _world = world;
        _pm = pm;
        _dispatcher = dispatcher;
        _persistence = persistence;
        _bg = bg;
        _saver = saver;
        _joinLeave = joinLeave;
        _movement = movement;
        _combat = combat;
        _items = items;
        _spells = spells;
        _shop = shop;
        _bank = bank;
        _playerSpawn = playerSpawn;
        _party = party;
        _guilds = guilds;
        _guildWar = guildWar;
        _territory = territory;
        _guildSchedule = guildSchedule;
        _mail = mail;
        _market = market;
        _trade = trade;
        _quests = quests;
        _conversations = conversations;
        _social = social;
        _spawn = spawn;
        _tod = tod;
        _weather = weather;
        _gameLoop = gameLoop;
        // Built here rather than injected: it is stateless over three services this already holds, and a
        // new required constructor argument would land on every harness that builds a PacketHandler.
        _moderation = new ModerationSystem(persistence, pm, saver, config);
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
        _rng = rng ?? SharedRandom.Instance;
        _config = config ?? ServerConfig.Default;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Game player packet dispatch
    // ═══════════════════════════════════════════════════════════════════════════

    // Per-session inbound budgets for non-admins, both reset at the start of each window; exceeding
    // either one is treated as flooding and disconnects the sender.
    private const long FloodWindowMs = 1000;            // rolling budget window, ms; longer = stricter
    private const int FloodMaxBytesPerWindow = 1000;    // inbound JSON characters allowed per window; higher = more permissive
    private const int FloodMaxPacketsPerWindow = 25;    // inbound packets allowed per window; higher = more permissive

    /// <summary>Playing, and not a corpse.
    ///
    /// <para>🔴 The gate for anything a dead player must not do: an action that moves them, changes world
    /// state, or moves an item or a coin. Read-only and social paths — chat, /who, map loading, targeting,
    /// closing a panel, abandoning something — keep asking <see cref="ServerPlayer.IsPlaying"/> alone, and
    /// respawn deliberately asks the opposite.</para>
    ///
    /// <para>The client closes every panel on death and gates its right-click menu, but it does NOT gate
    /// the chat input, so every slash command remains typable from a corpse and arrives here regardless.
    /// This is the half that counts.</para></summary>
    private bool IsActing(int index) => _pm[index].IsPlaying && !_pm[index].Char.Dead;

    /// <summary>What a corpse may still have DELIVERED to a handler.
    ///
    /// <para>🔴 An ALLOW-list, so a packet added later is refused by default instead of by somebody
    /// remembering to guard it. Market and NpcInteract were both missed exactly that way — they change
    /// world state, and nothing stopped a corpse using them until they were found by hand.</para>
    ///
    /// <para>Two different reasons appear below and they are not the same thing. Most entries are actions
    /// genuinely ALLOWED while dead. The last group is actions that are REFUSED — but by a handler that
    /// says so, because a typed slash command that silently does nothing reads as broken. Both have to be
    /// delivered; only the second kind then turns itself away.</para></summary>
    private static bool AllowedWhileDead(IPacket packet) => packet is
        // Session and account: mostly pre-login, where nobody is dead — listed so the gate never becomes
        // the reason a player cannot get out of a character.
        GetClassesPacket or NewAccountPacket or LoginPacket or AddCharPacket or DelCharPacket
        or UseCharPacket or DelAccountPacket or ChangePasswordPacket or LogoutToCharSelectPacket
        or SetLanguagePacket

        // The one thing a corpse is FOR.
        or RespawnRequestPacket

        // Speech. A corpse may talk; CombatSystem decides what it may do.
        or SayMsgPacket or EmoteMsgPacket or YellMsgPacket or BroadcastMsgPacket or RollPacket
        or NoticeMsgPacket or AdminMsgPacket or PlayerMsgPacket or GuildChatPacket

        // Asking, never changing.
        or WhoIsOnlinePacket or PlayerInfoRequestPacket or GetStatsPacket or PlayedRequestPacket
        or RequestLocationPacket or MapReportPacket or HomeCooldownRequestPacket or SpellsRequestPacket
        or GuildInfoRequestPacket or GuildBrowseRequestPacket or GuildLeaderboardRequestPacket
        or SeasonArchiveRequestPacket or RequestModerationPacket

        // Map streaming. Refusing these leaves the death screen with nothing to draw over.
        or MapDataClientPacket or NeedMapPacket or NeedNeighborMapPacket or RequestNewMapPacket
        or RequestRegionSyncPacket

        // Account bookkeeping with no world effect.
        or SocialAddFriendPacket or SocialRemoveFriendPacket or SocialAddIgnorePacket
        or SocialRemoveIgnorePacket or MailMarkReadPacket or MailDeletePacket

        // Targeting is inert: attacking and casting both refuse a corpse already.
        or DropTargetPacket or SearchPacket

        // Getting OUT of something must always work, whatever state you are in.
        or TradeCancelPacket or MarketClosePacket

        // Operator tools. An admin's own death is not a reason their moderation stops working.
        or KickPlayerPacket or BanPlayerPacket or MutePlayerPacket or UnbanPlayerPacket
        or UnkickPlayerPacket or UnmutePlayerPacket or HwBanPlayerPacket or HwUnbanPlayerPacket
        or RefreshBanListPacket or SetAccessPacket or SetMotdPacket or AdminGuildResetPacket
        or AdminTerritoryWarPacket or MapRespawnPacket or SetTimeOfDayPacket or SetWeatherPacket
        or GodModePacket or SetSpritePacket

        // Delivered ONLY so their handler can refuse them out loud — see the note above.
        or HomeRequestPacket or TradeInvitePacket or JoinPartyPacket or LeavePartyPacket
        or WarpToPacket or WarpMeToPacket or WarpToMePacket;

    /// <summary>The same gate for a TYPED command: refuses a corpse and says so, because a slash command
    /// that silently does nothing reads as broken. Returns true when the caller should stop.</summary>
    private bool RefuseWhileDead(int index)
    {
        if (!_pm[index].Char.Dead) return false;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_WhileDead,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
        return true;
    }

    /// <summary>Dispatch one JSON line from the game player at <paramref name="index"/>. Non-admins are
    /// flood-limited first; an unrecognized packet type is ignored, and a handler that throws is logged
    /// rather than allowed to tear down the game loop.</summary>
    public void HandlePacket(int index, string jsonLine)
    {
        var sp = _pm[index];
        long now = Environment.TickCount64;
        if (now >= sp.DataTimer + FloodWindowMs)
        {
            sp.DataTimer = now;
            sp.DataBytes = 0;
            sp.DataPackets = 0;
        }
        sp.DataBytes += jsonLine.Length;
        sp.DataPackets++;
        if (sp.Char?.Access is null or <= AdminLevel.Player)
        {
            if (sp.DataBytes > FloodMaxBytesPerWindow)
            {
                HackingAttempt(index, "Data Flooding");
                return;
            }
            if (sp.DataPackets > FloodMaxPacketsPerWindow)
            {
                HackingAttempt(index, "Packet Flooding");
                return;
            }
        }

        IPacket? packet = PacketSerializer.TryDeserialize(jsonLine);
        if (packet is null) return;

        // The corpse gate. Everything a dead player may still have delivered is named in
        // AllowedWhileDead; anything else stops here, so a handler added later is refused by default.
        // The per-handler IsActing checks stay as they are — they are what let a typed command explain
        // itself, and they keep each handler honest on its own terms.
        if (_pm[index].IsPlaying && _pm[index].Char.Dead && !AllowedWhileDead(packet)) return;

        try
        {
            switch (packet)
            {
                // ── Pre-login ────────────────────────────────────────────────
                case GetClassesPacket p:
                    HandleGetClasses(index, p);
                    break;
                case NewAccountPacket p:
                    HandleNewAccount(index, p);
                    break;
                case DelAccountPacket p:
                    HandleDelAccount(index, p);
                    break;
                case ChangePasswordPacket p:
                    HandleChangePassword(index, p);
                    break;
                case LoginPacket p:
                    HandleLogin(index, p);
                    break;
                case AddCharPacket p:
                    HandleAddChar(index, p);
                    break;
                case DelCharPacket p:
                    HandleDelChar(index, p);
                    break;
                case UseCharPacket p:
                    HandleUseChar(index, p);
                    break;
                case LogoutToCharSelectPacket:
                    HandleLogoutToCharSelect(index);
                    break;

                // ── Map loading ──────────────────────────────────────────────
                case RequestNewMapPacket:
                    HandleRequestNewMap(index);
                    break;
                case MapDataClientPacket:
                    HandleMapDataClient(index);
                    break;
                case NeedMapPacket:
                    HandleRequestNewMap(index);
                    break;
                case SetHotkeyPacket p:
                    HandleSetHotkey(index, p);
                    break;
                case NeedNeighborMapPacket p:
                    HandleNeedNeighborMap(index, p);
                    break;
                case RequestRegionSyncPacket:
                    HandleRequestRegionSync(index);
                    break;

                // ── Chat / social ────────────────────────────────────────────
                case SetLanguagePacket p:
                    HandleSetLanguage(index, p);
                    break;
                case SayMsgPacket p:
                    HandleSayMsg(index, p);
                    break;
                case EmoteMsgPacket p:
                    HandleEmoteMsg(index, p);
                    break;
                case YellMsgPacket p:
                    HandleYellMsg(index, p);
                    break;
                case BroadcastMsgPacket p:
                    HandleBroadcastMsg(index, p);
                    break;
                case NoticeMsgPacket p:
                    HandleNoticeMsg(index, p);
                    break;
                case AdminMsgPacket p:
                    HandleAdminMsg(index, p);
                    break;
                case PlayerMsgPacket p:
                    HandlePlayerMsg(index, p);
                    break;
                case RollPacket p:
                    HandleRoll(index, p);
                    break;
                case GuildCreatePacket p:
                    HandleGuildCreate(index, p);
                    break;
                case GuildDisbandPacket p:
                    HandleGuildDisband(index, p);
                    break;
                case GuildOfferInitiatePacket p:
                    HandleGuildOfferInitiate(index, p);
                    break;
                case GuildOfferRespondPacket p:
                    HandleGuildOfferRespond(index, p);
                    break;
                case GuildSetOpenPacket p:
                    HandleGuildSetOpen(index, p);
                    break;
                case GuildSetShowRankPacket p:
                    HandleGuildSetShowRank(index, p);
                    break;
                case GuildLeavePacket p:
                    HandleGuildLeave(index, p);
                    break;
                case GuildKickPacket p:
                    HandleGuildKick(index, p);
                    break;
                case GuildPromotePacket p:
                    HandleGuildPromote(index, p);
                    break;
                case GuildDemotePacket p:
                    HandleGuildDemote(index, p);
                    break;
                case GuildTransferPacket p:
                    HandleGuildTransfer(index, p);
                    break;
                case GuildSetMotdPacket p:
                    HandleGuildSetMotd(index, p);
                    break;
                case GuildSetLabelsPacket p:
                    HandleGuildSetLabels(index, p);
                    break;
                case GuildSetColorPacket p:
                    HandleGuildSetColor(index, p);
                    break;
                case GuildDonatePacket p:
                    _guilds.DonateGold(index, p.Amount);
                    break;
                case GuildDonateValorPacket p:
                    _guilds.DonateValor(index, p.Amount);
                    break;
                case GuildPayTaxPacket:
                    _guilds.PayTaxLate(index);
                    break;
                case GuildQuestAcquirePacket:
                    _guilds.AcquireQuest(index);
                    break;
                case GuildQuestAbandonPacket:
                    _guilds.AbandonQuest(index);
                    break;
                case GuildChatPacket p:
                    HandleGuildChat(index, p);
                    break;
                case GuildWarDeclarePacket p:
                    _guildWar.DeclareWar(index, p.TargetGuildIndex);
                    break;
                case GuildWarDeclareByNamePacket p:
                    _guildWar.DeclareWarByName(index, p.TargetName);
                    break;
                case GuildWarRetractPacket p:
                    _guildWar.RetractWar(index, p.OpponentIndex);
                    break;
                case GuildWarReviewRequestPacket p:
                    _guildWar.ReviewRequest(index, p.Kind, p.TargetIndex, p.Accept);
                    break;
                case GuildWarPeacePacket p:
                    HandleGuildWarPeace(index, p);
                    break;
                case GuildWarWagerPacket p:
                    HandleGuildWarWager(index, p);
                    break;
                case GuildTerritoryChallengePacket p:
                    _territory.ChallengeTerritory(index, p.TerritoryIndex);
                    break;
                case GuildLeaderboardRequestPacket:
                    _guildSchedule.SendLeaderboard(index);
                    break;
                case SeasonArchiveRequestPacket p:
                    _guildSchedule.SendSeasonArchive(index, p.Season);
                    break;
                case GuildTerritoryWithdrawPacket p:
                    _territory.WithdrawChallenge(index, p.TerritoryIndex);
                    break;
                case AdminGuildResetPacket p:
                    HandleAdminGuildReset(index, p);
                    break;
                case AdminTerritoryWarPacket p:
                    HandleAdminTerritoryWar(index, p);
                    break;
                case GuildBrowseRequestPacket:
                    HandleGuildBrowseRequest(index);
                    break;
                case GuildApplyPacket p:
                    HandleGuildApply(index, p);
                    break;
                case GuildReviewApplicationPacket p:
                    HandleGuildReviewApplication(index, p);
                    break;
                case MailMarkReadPacket p:
                    HandleMailMarkRead(index, p);
                    break;
                case MailDeletePacket p:
                    HandleMailDelete(index, p);
                    break;
                case MailClaimPacket p:
                    HandleMailClaim(index, p);
                    break;
                case MailSendPacket p:
                    HandleMailSend(index, p);
                    break;
                case MailPayCodPacket p:
                    HandleMailPayCod(index, p);
                    break;

                case MarketOpenPacket:
                    HandleMarketOpen(index);
                    break;
                case MarketCreatePacket p:
                    HandleMarketCreate(index, p);
                    break;
                case MarketBuyPacket p:
                    HandleMarketBuy(index, p);
                    break;
                case MarketCancelPacket p:
                    HandleMarketCancel(index, p);
                    break;
                case MarketRefreshPacket:
                    HandleMarketRefresh(index);
                    break;
                case MarketClosePacket:
                    HandleMarketClose(index);
                    break;

                case TradeInvitePacket p:
                    HandleTradeInvite(index, p);
                    break;
                case TradeRespondPacket p:
                    HandleTradeRespond(index, p);
                    break;
                case TradeOfferAddPacket p:
                    HandleTradeOfferAdd(index, p);
                    break;
                case TradeOfferRemovePacket p:
                    HandleTradeOfferRemove(index, p);
                    break;
                case TradeConfirmPacket p:
                    HandleTradeConfirm(index, p);
                    break;
                case TradeCancelPacket:
                    HandleTradeCancel(index);
                    break;

                case QuestAcceptPacket p:
                    HandleQuestAccept(index, p);
                    break;
                case QuestTurnInPacket p:
                    HandleQuestTurnIn(index, p);
                    break;
                case QuestAbandonPacket p:
                    HandleQuestAbandon(index, p);
                    break;

                case GuildInfoRequestPacket:
                    HandleGuildInfoRequest(index);
                    break;
                case SocialAddFriendPacket p:
                    HandleSocialAddFriend(index, p);
                    break;
                case SocialAddIgnorePacket p:
                    HandleSocialAddIgnore(index, p);
                    break;
                case SocialRemoveFriendPacket p:
                    HandleSocialRemoveFriend(index, p);
                    break;
                case SocialRemoveIgnorePacket p:
                    HandleSocialRemoveIgnore(index, p);
                    break;
                case WhoIsOnlinePacket:
                    HandleWhosOnline(index);
                    break;
                case PlayerInfoRequestPacket p:
                    HandlePlayerInfoRequest(index, p);
                    break;
                case PlayedRequestPacket:
                    HandlePlayedRequest(index);
                    break;
                case HomeRequestPacket:
                    HandleHomeRequest(index);
                    break;
                case HomeCooldownRequestPacket:
                    HandleHomeCooldownRequest(index);
                    break;
                case GetStatsPacket:
                    HandleGetStats(index);
                    break;

                // ── Movement ─────────────────────────────────────────────────
                case PlayerMovePacket p:
                    HandlePlayerMove(index, p);
                    break;
                case PlayerDirPacket p:
                    HandlePlayerDir(index, p);
                    break;

                // ── Combat ───────────────────────────────────────────────────
                case AttackPacket:
                    HandleAttack(index);
                    break;
                case SearchPacket p:
                    HandleSearch(index, p);
                    break;
                case DropTargetPacket:
                    HandleDropTarget(index);
                    break;

                // ── Items ────────────────────────────────────────────────────
                case UseItemPacket p:
                    HandleUseItem(index, p);
                    break;
                case MapGetItemPacket:
                    HandleMapGetItem(index);
                    break;
                case MapPickUpPacket p:
                    HandleMapPickUp(index, p);
                    break;
                case MapPickUpAllPacket p:
                    HandleMapPickUpAll(index, p);
                    break;
                case MapDropItemPacket p:
                    HandleMapDropItem(index, p);
                    break;
                case MapDropBulkPacket p:
                    HandleMapDropBulk(index, p);
                    break;
                case SortInventoryPacket:
                    HandleSortInventory(index);
                    break;

                // ── Stats ────────────────────────────────────────────────────
                case TrainStatsPacket p:
                    HandleTrainStats(index, p);
                    break;

                // ── Bank ─────────────────────────────────────────────────────
                case BankOpenPacket:
                    HandleBankOpen(index);
                    break;
                case BankDepositPacket p:
                    HandleBankDeposit(index, p);
                    break;
                case BankWithdrawPacket p:
                    HandleBankWithdraw(index, p);
                    break;
                case BankDepositBulkPacket p:
                    HandleBankDepositBulk(index, p);
                    break;
                case BankWithdrawBulkPacket p:
                    HandleBankWithdrawBulk(index, p);
                    break;
                case BankSortPacket:
                    HandleBankSort(index);
                    break;

                // ── Inn ──────────────────────────────────────────────────────
                case ConfirmSetSpawnPacket:
                    _playerSpawn.ConfirmSetSpawn(index);
                    break;
                case RespawnRequestPacket:
                    _combat.RespawnPlayer(index);
                    break;

                // ── Shop ─────────────────────────────────────────────────────
                case NpcInteractPacket p:
                    HandleNpcInteract(index, p);
                    break;
                case ShopBarterPacket p:
                    HandleShopBarter(index, p);
                    break;
                case ShopBuyPacket p:
                    HandleShopBuy(index, p);
                    break;
                case ShopSellPacket p:
                    HandleShopSell(index, p);
                    break;
                case FixItemPacket p:
                    HandleFixItem(index, p);
                    break;

                // ── Party ────────────────────────────────────────────────────
                case PartyRequestPacket p:
                    HandlePartyRequest(index, p);
                    break;
                case JoinPartyPacket p:
                    HandleJoinParty(index, p);
                    break;
                case LeavePartyPacket:
                    HandleLeaveParty(index);
                    break;

                // ── Spells ───────────────────────────────────────────────────
                case SpellsRequestPacket:
                    HandleSpellsRequest(index);
                    break;
                case CastPacket p:
                    HandleCast(index, p);
                    break;
                case SetPreparedSpellPacket p:
                    HandleSetPreparedSpell(index, p);
                    break;
                case ForgetSpellPacket p:
                    HandleForgetSpell(index, p);
                    break;

                // ── Info requests ────────────────────────────────────────────
                case RequestLocationPacket:
                    HandleRequestLocation(index);
                    break;

                // ── Admin ────────────────────────────────────────────────────
                case WarpMeToPacket p:
                    HandleWarpMeTo(index, p);
                    break;
                case GodModePacket:
                    HandleGodMode(index);
                    break;
                case WarpToMePacket p:
                    HandleWarpToMe(index, p);
                    break;
                case WarpToPacket p:
                    HandleWarpTo(index, p);
                    break;
                case SetSpritePacket p:
                    HandleSetSprite(index, p);
                    break;
                case MapRespawnPacket:
                    HandleMapRespawn(index);
                    break;
                case MapReportPacket:
                    HandleMapReport(index);
                    break;
                case KickPlayerPacket p:
                    HandleKickPlayer(index, p);
                    break;
                case BanPlayerPacket p:
                    HandleBanPlayer(index, p);
                    break;
                case MutePlayerPacket p:
                    HandleMutePlayer(index, p);
                    break;
                case RefreshBanListPacket:
                    HandleRefreshBanList(index);
                    break;
                case HwBanPlayerPacket p:
                    HandleHwBanPlayer(index, p);
                    break;
                case UnbanPlayerPacket p:
                    HandleUnbanPlayer(index, p);
                    break;
                case HwUnbanPlayerPacket p:
                    HandleHwUnbanPlayer(index, p);
                    break;
                case UnkickPlayerPacket p:
                    HandleUnkickPlayer(index, p);
                    break;
                case UnmutePlayerPacket p:
                    HandleUnmutePlayer(index, p);
                    break;
                case RequestModerationPacket p:
                    HandleRequestModeration(index, p);
                    break;
                case SetAccessPacket p:
                    HandleSetAccess(index, p);
                    break;
                case SetMotdPacket p:
                    HandleSetMotd(index, p);
                    break;
                case SetTimeOfDayPacket p:
                    HandleSetTimeOfDay(index, p);
                    break;
                case SetWeatherPacket p:
                    HandleSetWeather(index, p);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PacketHandler error for index {Index}, packet {Cmd}", index, packet.GetType().Name);
        }
    }
}
