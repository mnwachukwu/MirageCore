using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>
/// Typed send methods for all C→S packets.
/// Each method builds the appropriate packet POCO and hands it to the transport.
/// </summary>
public sealed class ClientPacketSender
{
    private readonly IClientTransport _transport;
    // Lazy getter for the current UI locale, supplied by the Shell layer. Read at send time so a
    // pre-session locale change (player switches language in Options on the login screen) is
    // reflected on the next Login/NewAccount/etc. without having to thread the locale through
    // every call site.
    private Func<string>? _localeProvider;
    // Same shape as the locale getter. Unset means an empty key, which every server treats as "no machine
    // key" rather than as a value.
    private Func<string>? _machineKeyProvider;

    public ClientPacketSender(IClientTransport transport) => _transport = transport;

    /// <summary>Wires the current-locale getter, called by every pre-session sender so the
    /// server can localize auth-error responses before a session exists. Typically called once
    /// from the Shell layer with <c>() => _language</c>.</summary>
    public void SetLocaleProvider(Func<string> provider) => _localeProvider = provider;

    /// <summary>Wires the machine-key getter — see <see cref="Mirage.Shared.MachineKey"/>. Read at send
    /// time, on the two packets that can be refused for it.</summary>
    public void SetMachineKeyProvider(Func<string> provider) => _machineKeyProvider = provider;

    private string CurrentLocale => _localeProvider?.Invoke() ?? "en";

    private string CurrentMachineKey => _machineKeyProvider?.Invoke() ?? "";

    // ── Account / pre-login ───────────────────────────────────────────────────

    public void SendGetClasses()
        => _transport.Send(new GetClassesPacket());

    public void SendNewAccount(string username, string password)
        => _transport.Send(new NewAccountPacket
        {
            Username = username,
            Password = password,
            Locale = CurrentLocale,
            MachineKey = CurrentMachineKey,
        });

    public void SendDelAccount(string username, string password)
        => _transport.Send(new DelAccountPacket { Username = username, Password = password, Locale = CurrentLocale });

    public void SendChangePassword(string username, string password, string newPassword)
        => _transport.Send(new ChangePasswordPacket { Username = username, Password = password, NewPassword = newPassword, Locale = CurrentLocale });

    public void SendLogin(string username, string password)
        => _transport.Send(new LoginPacket
        {
            Username = username,
            Password = password,
            Major = Constants.ClientMajor,
            Minor = Constants.ClientMinor,
            Revision = Constants.ClientRevision,
            Locale = CurrentLocale,
            MachineKey = CurrentMachineKey,
        });

    public void SendSetLanguage(string locale)
        => _transport.Send(new SetLanguagePacket { Locale = locale });

    public void SendAddChar(string name, Sex sex, int classId)
        => _transport.Send(new AddCharPacket { Name = name, Sex = sex, Class = classId });

    public void SendDelChar(int slot)
        => _transport.Send(new DelCharPacket { Slot = slot });

    public void SendUseChar(int slot)
        => _transport.Send(new UseCharPacket { Slot = slot, Locale = CurrentLocale });

    public void SendLogoutToCharSelect()
        => _transport.Send(new LogoutToCharSelectPacket());

    // ── Map ───────────────────────────────────────────────────────────────────

    public void SendNeedMap(int mapNum, int revision)
        => _transport.Send(new NeedMapPacket { MapNum = mapNum, Revision = revision });

    public void SendNeedNeighborMap(int mapNum, int col, int row)
        => _transport.Send(new NeedNeighborMapPacket { MapNum = mapNum, Col = col, Row = row });

    public void SendRequestRegionSync()
        => _transport.Send(new RequestRegionSyncPacket());

    public void SendMapData(int mapNum)
        => _transport.Send(new MapDataClientPacket { MapNum = mapNum });

    // ── Movement ─────────────────────────────────────────────────────────────

    public void SendPlayerMove(Direction dir, MovementType movement)
        => _transport.Send(new PlayerMovePacket { Dir = dir, Movement = movement });

    public void SendPlayerDir(Direction dir)
        => _transport.Send(new PlayerDirPacket { Dir = dir });

    // ── Combat / spells ───────────────────────────────────────────────────────

    public void SendAttack()
        => _transport.Send(new AttackPacket());

    /// <summary>C→S: interact with the map NPC at (map, slot). Auto (the melee-key default) lets the server pick
    /// the NPC's best role — its quest/gossip menu if it has an actionable quest, else its keeper shop/inn; Shop
    /// forces the shop (the gossip-menu "Shop"/"Inn" item). The server range-gates (r=5).</summary>
    public void SendNpcInteract(int map, int slot, NpcInteractChoice choice = NpcInteractChoice.Auto)
        => _transport.Send(new NpcInteractPacket { MapNum = map, NpcSlot = slot, Choice = choice });

    /// <summary>C→S: accept quest <paramref name="questNum"/> from the open giver menu. Which NPC that is comes
    /// from the menu the server opened, so it is not sent; the server checks the role and eligibility.</summary>
    public void SendQuestAccept(int questNum)
        => _transport.Send(new QuestAcceptPacket { QuestNum = questNum });

    /// <summary>C→S: turn quest <paramref name="questNum"/> in at the open turn-in menu.</summary>
    public void SendQuestTurnIn(int questNum)
        => _transport.Send(new QuestTurnInPacket { QuestNum = questNum });

    /// <summary>C→S: abandon quest <paramref name="questNum"/> (from the quest-log panel — no NPC needed).</summary>
    public void SendQuestAbandon(int questNum)
        => _transport.Send(new QuestAbandonPacket { QuestNum = questNum });

    // The client picks the target opportunistically from the rendered viewport (sprite-pixel
    // hit test) and sends its identity proposal along with the clicked tile.  The server
    // validates the proposal by identity (not by tile), so a moving entity mid-step is still
    // acquirable.  ProposedType: 0=player, 1=npc, 2=self, 3=traversal, 255=none.
    public void SendSearch(int x, int y, int mapNum, byte proposedType, int proposedId, int proposedMap)
        => _transport.Send(new SearchPacket
        {
            MapNum = mapNum, X = x, Y = y,
            ProposedType = proposedType, ProposedId = proposedId, ProposedMap = proposedMap,
        });

    public void SendDropTarget()
        => _transport.Send(new DropTargetPacket());

    public void SendCast(int spellSlot, bool self = false)
        => _transport.Send(new CastPacket { Spell = spellSlot, Self = self });

    public void SendSetPreparedSpell(int slot)
        => _transport.Send(new SetPreparedSpellPacket { Slot = slot });

    public void SendForgetSpell(int slot)
        => _transport.Send(new ForgetSpellPacket { Slot = slot });

    /// <summary>Bind an action-bar slot to an item or spell NUMBER, or clear it with
    /// <see cref="HotkeyKind.None"/>. The server echoes the whole bar back either way.</summary>
    public void SendSetHotkey(int slot, HotkeyKind kind, int num)
        => _transport.Send(new SetHotkeyPacket { Slot = slot, Kind = (byte)kind, Num = (short)num });

    // ── Inventory ─────────────────────────────────────────────────────────────

    public void SendUseItem(int invSlot)
        => _transport.Send(new UseItemPacket { Slot = invSlot });

    public void SendMapGetItem()
        => _transport.Send(new MapGetItemPacket());

    /// <summary>Tile menu → take one named drop. Identified by its stable per-map slot, so a pile
    /// that shifted while the menu was open still yields the item that was clicked.</summary>
    public void SendMapPickUp(int mapNum, int slot)
        => _transport.Send(new MapPickUpPacket { MapNum = mapNum, Slot = slot });

    /// <summary>Tile menu → take everything on one square this player can claim. A tile rather than a
    /// list of slots: the set is decided server-side at the moment of the request, so anything that
    /// dropped or was taken since the menu opened is accounted for without the client being right.</summary>
    public void SendMapPickUpAll(int mapNum, int x, int y, WorldLayer layer)
        => _transport.Send(new MapPickUpAllPacket { MapNum = mapNum, X = x, Y = y, Layer = layer });

    public void SendMapDropItem(int invSlot, int quantity)
        => _transport.Send(new MapDropItemPacket { Slot = invSlot, Quantity = quantity });

    public void SendMapDropBulk(int itemNum, int quantity)
        => _transport.Send(new MapDropBulkPacket { ItemNum = itemNum, Quantity = quantity });

    public void SendSortInventory()
        => _transport.Send(new SortInventoryPacket());

    // ── Inn ──────────────────────────────────────────────────────────────────

    public void SendConfirmSetSpawn()
        => _transport.Send(new ConfirmSetSpawnPacket());

    // ── Bank ─────────────────────────────────────────────────────────────────

    public void SendBankOpen()
        => _transport.Send(new BankOpenPacket());

    public void SendBankDeposit(int invSlot, int quantity)
        => _transport.Send(new BankDepositPacket { InvSlot = invSlot, Quantity = quantity });

    public void SendBankWithdraw(int bankSlot, int quantity)
        => _transport.Send(new BankWithdrawPacket { BankSlot = bankSlot, Quantity = quantity });

    public void SendBankDepositBulk(int itemNum, int quantity)
        => _transport.Send(new BankDepositBulkPacket { ItemNum = itemNum, Quantity = quantity });

    public void SendBankWithdrawBulk(int itemNum, int quantity)
        => _transport.Send(new BankWithdrawBulkPacket { ItemNum = itemNum, Quantity = quantity });

    public void SendBankSort()
        => _transport.Send(new BankSortPacket());

    // ── Shop ─────────────────────────────────────────────────────────────────

    /// <summary>Trade against one barter row. <paramref name="multiples"/> applies the row that many times —
    /// it is a rate, not a single swap — and the server refuses outright if the payout would not fit.</summary>
    public void SendShopBarter(int shopNum, int barterSlot, int multiples = 1)
        => _transport.Send(new ShopBarterPacket { ShopNum = shopNum, BarterSlot = barterSlot, Multiples = multiples });

    /// <summary>Buy from the open shop. <paramref name="quantity"/> applies to anything; the server clamps
    /// it to what the purse covers, refuses if the bag cannot take all of it, and prices it.</summary>
    public void SendShopBuy(int shopNum, int salesSlot, int quantity = 1)
        => _transport.Send(new ShopBuyPacket { ShopNum = shopNum, SalesSlot = salesSlot, Quantity = quantity });

    /// <summary>Sell an inventory slot to the open shop. <paramref name="quantity"/> 0 means the whole
    /// stack; the server prices it, so nothing here proposes a value.</summary>
    public void SendShopSell(int invSlot, int quantity = 0)
        => _transport.Send(new ShopSellPacket { InvSlot = invSlot, Quantity = quantity });

    public void SendFixItem(int invSlot)
        => _transport.Send(new FixItemPacket { InvSlot = invSlot });

    // ── Stats ─────────────────────────────────────────────────────────────────

    public void SendTrainStats(int str, int def, int intPts, int spd)
        => _transport.Send(new TrainStatsPacket { Str = str, Def = def, Int = intPts, Spd = spd });

    // ── Social ────────────────────────────────────────────────────────────────

    public void SendSayMsg(string msg)
        => _transport.Send(new SayMsgPacket { Msg = msg });

    public void SendEmoteMsg(string msg)
        => _transport.Send(new EmoteMsgPacket { Msg = msg });

    public void SendYell(string msg)
        => _transport.Send(new YellMsgPacket { Msg = msg });

    public void SendBroadcastMsg(string msg)
        => _transport.Send(new BroadcastMsgPacket { Msg = msg });

    public void SendRoll(byte max)
        => _transport.Send(new RollPacket { Max = max });

    public void SendNoticeMsg(string msg)
        => _transport.Send(new NoticeMsgPacket { Msg = msg });

    public void SendAdminMsg(string msg)
        => _transport.Send(new AdminMsgPacket { Msg = msg });

    public void SendPlayerMsg(string target, string msg)
        => _transport.Send(new PlayerMsgPacket { Target = target, Msg = msg });

    // ── Party ─────────────────────────────────────────────────────────────────

    public void SendPartyRequest(string targetName)
        => _transport.Send(new PartyRequestPacket { Target = targetName });

    public void SendJoinParty(int targetIndex)
        => _transport.Send(new JoinPartyPacket { Target = targetIndex });

    public void SendLeaveParty()
        => _transport.Send(new LeavePartyPacket());

    // ── Mail ──────────────────────────────────────────────────────────────────

    public void SendMailMarkRead(int id)
        => _transport.Send(new MailMarkReadPacket { Id = id });

    public void SendMailDelete(int id, bool outbox = false)
        => _transport.Send(new MailDeletePacket { Id = id, Outbox = outbox });

    public void SendMailClaim(int id)
        => _transport.Send(new MailClaimPacket { Id = id });

    public void SendMailSend(string recipient, string subject, string body, List<MailSendAttach> attach, int codPrice = 0)
        => _transport.Send(new MailSendPacket { Recipient = recipient, Subject = subject, Body = body, Attach = attach, CodPrice = codPrice });

    public void SendMailPayCod(int id)
        => _transport.Send(new MailPayCodPacket { Id = id });

    public void SendMarketOpen()
        => _transport.Send(new MarketOpenPacket());

    public void SendMarketCreate(int invSlot, int quantity, int price)
        => _transport.Send(new MarketCreatePacket { InvSlot = invSlot, Quantity = quantity, Price = price });

    public void SendMarketBuy(int id, int quantity = 0)
        => _transport.Send(new MarketBuyPacket { Id = id, Quantity = quantity });

    public void SendMarketCancel(int id)
        => _transport.Send(new MarketCancelPacket { Id = id });

    public void SendMarketRefresh()
        => _transport.Send(new MarketRefreshPacket());

    public void SendMarketClose()
        => _transport.Send(new MarketClosePacket());

    public void SendTradeInvite(string target) => _transport.Send(new TradeInvitePacket { Target = target });
    public void SendTradeRespond(bool accept) => _transport.Send(new TradeRespondPacket { Accept = accept });
    public void SendTradeOfferAdd(int invSlot, int quantity) => _transport.Send(new TradeOfferAddPacket { InvSlot = invSlot, Quantity = quantity });
    public void SendTradeOfferRemove(int index) => _transport.Send(new TradeOfferRemovePacket { Index = index });
    public void SendTradeConfirm(bool confirmed) => _transport.Send(new TradeConfirmPacket { Confirmed = confirmed });
    public void SendTradeCancel() => _transport.Send(new TradeCancelPacket());

    // ── Guild ─────────────────────────────────────────────────────────────────

    public void SendGuildCreate(string name)
        => _transport.Send(new GuildCreatePacket { Name = name });

    public void SendGuildDisband()
        => _transport.Send(new GuildDisbandPacket());

    public void SendGuildInvite(string targetName)
        => _transport.Send(new GuildOfferInitiatePacket { TargetName = targetName, IsRequest = false });

    public void SendGuildJoinRequest(string targetName)
        => _transport.Send(new GuildOfferInitiatePacket { TargetName = targetName, IsRequest = true });

    public void SendGuildOfferResponse(bool accept)
        => _transport.Send(new GuildOfferRespondPacket { Accept = accept });

    public void SendGuildSetOpen(bool open)
        => _transport.Send(new GuildSetOpenPacket { Open = open });

    public void SendGuildSetShowRank(bool show)
        => _transport.Send(new GuildSetShowRankPacket { Show = show });

    // Creator debug.
    public void SendGuildReset(SettlementScope scope)
        => _transport.Send(new AdminGuildResetPacket { Scope = scope });
    public void SendTerritoryWarDebug(TerritoryWarDebugAction action)
        => _transport.Send(new AdminTerritoryWarPacket { Action = action });

    public void SendGuildLeave()
        => _transport.Send(new GuildLeavePacket());

    public void SendGuildKick(string login)
        => _transport.Send(new GuildKickPacket { Login = login });

    public void SendGuildPromote(string login)
        => _transport.Send(new GuildPromotePacket { Login = login });

    public void SendGuildDemote(string login)
        => _transport.Send(new GuildDemotePacket { Login = login });

    public void SendGuildTransfer(string login)
        => _transport.Send(new GuildTransferPacket { Login = login });

    public void SendGuildSetMotd(string motd)
        => _transport.Send(new GuildSetMotdPacket { Motd = motd });

    public void SendGuildSetLabels(IReadOnlyList<GuildLabel> labels)
        => _transport.Send(new GuildSetLabelsPacket { Labels = new List<GuildLabel>(labels) });

    /// <summary>Set the guild's overhead color (packed 0xRRGGBB). The server re-validates against
    /// <c>GuildColorPolicy</c> and rejects a reserved palette color.</summary>
    public void SendGuildSetColor(int rgb)
        => _transport.Send(new GuildSetColorPacket { Rgb = rgb });

    /// <summary>Send a guild-chat line (<paramref name="officer"/> = the leader/officer-only channel).</summary>
    public void SendGuildChat(string msg, bool officer)
        => _transport.Send(new GuildChatPacket { Msg = msg, Officer = officer });

    // ── Guild vault + quests ──────────────────────────────────────────────────

    /// <summary>Donate gold from the player into the guild vault.</summary>
    public void SendGuildDonate(int amount)
        => _transport.Send(new GuildDonatePacket { Amount = amount });

    /// <summary>Donate valor (war currency) from the player into the guild vault (auto-offsets weekly tax).</summary>
    public void SendGuildDonateValor(int amount)
        => _transport.Send(new GuildDonateValorPacket { Amount = amount });

    /// <summary>(Officer+) pay one week's tax late to restore suspended guild perks.</summary>
    public void SendGuildPayTax()
        => _transport.Send(new GuildPayTaxPacket());

    /// <summary>(Leader) acquire a new guild quest.</summary>
    public void SendGuildQuestAcquire()
        => _transport.Send(new GuildQuestAcquirePacket());

    /// <summary>(Leader) abandon the active guild quest, forfeiting progress (no refund).</summary>
    public void SendGuildQuestAbandon()
        => _transport.Send(new GuildQuestAbandonPacket());

    // ── Guild discovery ────────────────────────────────────────────────────────

    /// <summary>Request the open-for-membership guild list (the guildless browser).</summary>
    public void SendGuildBrowseRequest()
        => _transport.Send(new GuildBrowseRequestPacket());

    public void SendGuildApply(int guildIndex)
        => _transport.Send(new GuildApplyPacket { Index = guildIndex });

    public void SendGuildReviewApplication(string login, bool accept)
        => _transport.Send(new GuildReviewApplicationPacket { Login = login, Accept = accept });

    // ── Guild wars ──────────────────────────────────────────────────────────────

    /// <summary>Declare war on a guild by name (the client has no guild-index list). Leader acts directly;
    /// an Officer's send is queued for Leader review server-side. Doubles as "return a declaration" when the
    /// named guild has already declared on us.</summary>
    public void SendGuildWarDeclareByName(string guildName)
        => _transport.Send(new GuildWarDeclareByNamePacket { TargetName = guildName });

    /// <summary>Retract a still one-sided declaration against <paramref name="opponentIndex"/> (Officer+ —
    /// leader direct, officer queued).</summary>
    public void SendGuildWarRetract(int opponentIndex)
        => _transport.Send(new GuildWarRetractPacket { OpponentIndex = opponentIndex });

    /// <summary>(Leader) accept or deny a pending officer war-request, addressed by its (kind, target).</summary>
    public void SendGuildWarReviewRequest(GuildWarRequestKind kind, int targetIndex, bool accept)
        => _transport.Send(new GuildWarReviewRequestPacket { Kind = kind, TargetIndex = targetIndex, Accept = accept });

    /// <summary>A peace action on a mutual war (offer/withdraw/accept/reject). Offer is Officer+ (queued for a
    /// non-leader); withdraw/accept/reject are Leader-only. With no ante locked, an offer must carry an
    /// <paramref name="offering"/> (the vault gold staked as the pot); it is ignored for the other actions.</summary>
    public void SendGuildWarPeace(int opponentIndex, GuildWarPeaceAction action, long offering = 0)
        => _transport.Send(new GuildWarPeacePacket { OpponentIndex = opponentIndex, Action = action, Offering = offering });

    /// <summary>(Leader) a wager action on a mutual war (propose/accept/reject/withdraw). <paramref name="amount"/>
    /// is the proposed ante for Propose (ignored otherwise).</summary>
    public void SendGuildWarWager(int opponentIndex, GuildWarWagerAction action, long amount = 0)
        => _transport.Send(new GuildWarWagerPacket { OpponentIndex = opponentIndex, Action = action, Amount = amount });

    /// <summary>Register a challenge for a territory at the next war night (Leader acts, Officer queues).</summary>
    public void SendGuildTerritoryChallenge(int territoryIndex)
        => _transport.Send(new GuildTerritoryChallengePacket { TerritoryIndex = territoryIndex });

    /// <summary>Withdraw our pending challenge for a territory (Officer+; cost not refunded).</summary>
    public void SendGuildTerritoryWithdraw(int territoryIndex)
        => _transport.Send(new GuildTerritoryWithdrawPacket { TerritoryIndex = territoryIndex });

    // ── Death & respawn ────────────────────────────────────────────────────────

    /// <summary>Request respawn after death — honored server-side only once the respawn timer has elapsed.</summary>
    public void SendRespawnRequest()
        => _transport.Send(new RespawnRequestPacket());

    /// <summary>Ask for a fresh <c>GuildInfoPacket</c> — sent when the Guild tab opens, so the roster's
    /// live online column is current (a member going offline can't push one).</summary>
    public void SendGuildInfoRequest()
        => _transport.Send(new GuildInfoRequestPacket());

    public void SendGuildLeaderboardRequest()
        => _transport.Send(new GuildLeaderboardRequestPacket());

    public void SendSeasonArchiveRequest(int season)
        => _transport.Send(new SeasonArchiveRequestPacket { Season = season });

    // ── Social (friends / ignore) ─────────────────────────────────────────────
    // Adds take a CHARACTER name (what the player can see/right-click); the server resolves it to that
    // character's account, which is what the lists actually store. Removes take the row's own login.

    public void SendSocialAddFriend(string charName)
        => _transport.Send(new SocialAddFriendPacket { Name = charName });

    public void SendSocialAddIgnore(string charName)
        => _transport.Send(new SocialAddIgnorePacket { Name = charName });

    public void SendSocialRemoveFriend(string login)
        => _transport.Send(new SocialRemoveFriendPacket { Login = login });

    public void SendSocialRemoveIgnore(string login)
        => _transport.Send(new SocialRemoveIgnorePacket { Login = login });

    // ── Lookups and self-service commands (/who, /played, /home) ─────────────

    public void SendWhoIsOnline()
        => _transport.Send(new WhoIsOnlinePacket());

    public void SendPlayerInfoRequest(string target)
        => _transport.Send(new PlayerInfoRequestPacket { Target = target });

    /// <summary>/played — request the server's playtime readout (current character + account total).</summary>
    public void SendPlayedRequest()
        => _transport.Send(new PlayedRequestPacket());

    /// <summary>/home — ask to be sent to this character's spawn point.</summary>
    public void SendHomeRequest()
        => _transport.Send(new HomeRequestPacket());

    /// <summary>/homecd — ask how long is left on the /home cooldown, without spending it.</summary>
    public void SendHomeCooldownRequest()
        => _transport.Send(new HomeCooldownRequestPacket());

    public void SendRequestLocation()
        => _transport.Send(new RequestLocationPacket());

    // ── Admin ─────────────────────────────────────────────────────────────────

    public void SendWarpMeTo(string target)
        => _transport.Send(new WarpMeToPacket { Target = target });

    public void SendWarpToMe(string target)
        => _transport.Send(new WarpToMePacket { Target = target });

    public void SendGodMode()
        => _transport.Send(new GodModePacket());

    public void SendWarpTo(short mapNum)
        => _transport.Send(new WarpToPacket { MapNum = mapNum });

    public void SendSetSprite(short sprite)
        => _transport.Send(new SetSpritePacket { Sprite = sprite });

    public void SendKick(string target, int minutes = 0)
        => _transport.Send(new KickPlayerPacket { Target = target, Minutes = minutes });

    public void SendBan(string target)
        => _transport.Send(new BanPlayerPacket { Target = target });

    public void SendMute(string target, int minutes = 0)
        => _transport.Send(new MutePlayerPacket { Target = target, Minutes = minutes });

    public void SendRefreshBanList()
        => _transport.Send(new RefreshBanListPacket());

    /// <summary>Bans the account and the machine behind it. Takes a CHARACTER name, unlike the lifts:
    /// the target has to be online for their machine key to exist at all.</summary>
    public void SendHwBan(string target)
        => _transport.Send(new HwBanPlayerPacket { Target = target });

    // Lifting a punishment. The target is an ACCOUNT, not a character — the three above take a name off
    // the screen, and nobody kicked or banned is on the screen to be named.
    public void SendUnban(string target)
        => _transport.Send(new UnbanPlayerPacket { Target = target });

    public void SendHwUnban(string target)
        => _transport.Send(new HwUnbanPlayerPacket { Target = target });

    public void SendUnkick(string target)
        => _transport.Send(new UnkickPlayerPacket { Target = target });

    public void SendUnmute(string target)
        => _transport.Send(new UnmutePlayerPacket { Target = target });

    public void SendRequestModeration()
        => _transport.Send(new RequestModerationPacket());

    public void SendSetAccess(string target, AdminLevel level)
        => _transport.Send(new SetAccessPacket { Target = target, Level = level });

    public void SendMapRespawn()
        => _transport.Send(new MapRespawnPacket());

    public void SendMapReport()
        => _transport.Send(new MapReportPacket());

    public void SendSetMotd(string motd)
        => _transport.Send(new SetMotdPacket { Motd = motd });

    public void SendSetTimeOfDay(TimePhase phase)
        => _transport.Send(new SetTimeOfDayPacket { Phase = phase });

    public void SendSetWeather(WeatherType weather)
        => _transport.Send(new SetWeatherPacket { Weather = weather });
}
