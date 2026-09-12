using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using System.Text.Json;

namespace Mirage.Shared.Protocol;

/// <summary>
/// Every packet the engine itself speaks, as rows in a <see cref="PacketRegistry"/>.
///
/// <para><b>This is the whole of what Core puts on the wire.</b> A game adds its own rows from its
/// module; the composition root builds one table from Core's rows and every module's, and installs it
/// on <see cref="PacketSerializer.Registry"/>. Because the server's router, the client's dispatcher,
/// and the editor's connection all parse through that one table, a packet registered once is readable
/// by all three.</para>
///
/// <para><b>Two commands are used in both directions</b> — <c>playermove</c> and <c>playerdir</c> —
/// and the only thing separating the two shapes on the wire is that the server-to-client form carries
/// a top-level <c>index</c>. Those rows take both readers and pick by that.</para>
/// </summary>
public static class CorePackets
{
    /// <summary>A table holding only Core's rows. What a server with no game module parses with.</summary>
    public static PacketRegistry Build() => Register(new PacketRegistry.Builder()).Build();

    /// <summary>Adds Core's rows to a table under construction, so a composition root can put a game's
    /// rows in the same one.</summary>
    public static PacketRegistry.Builder Register(PacketRegistry.Builder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // ── Used in both directions, told apart by the index field ────────────
        builder.Register(PacketNames.PlayerMove,
            withIndex: Read<SendPlayerMovePacket>,
            withoutIndex: Read<PlayerMovePacket>);
        builder.Register(PacketNames.PlayerDir,
            withIndex: Read<SendPlayerDirPacket>,
            withoutIndex: Read<PlayerDirPacket>);

        // Account / pre-login
        builder.Register(PacketNames.GetClasses, Read<GetClassesPacket>);
        builder.Register(PacketNames.NewAccount, Read<NewAccountPacket>);
        builder.Register(PacketNames.DelAccount, Read<DelAccountPacket>);
        builder.Register(PacketNames.ChangePassword, Read<ChangePasswordPacket>);
        builder.Register(PacketNames.Login, Read<LoginPacket>);
        builder.Register(PacketNames.AddChar, Read<AddCharPacket>);
        builder.Register(PacketNames.DelChar, Read<DelCharPacket>);
        builder.Register(PacketNames.UseChar, Read<UseCharPacket>);
        builder.Register(PacketNames.LogoutToCharSelect, Read<LogoutToCharSelectPacket>);
        builder.Register(PacketNames.SetLanguage, Read<SetLanguagePacket>);

        // Chat
        builder.Register(PacketNames.SayMsg, Read<SayMsgPacket>);
        builder.Register(PacketNames.EmoteMsg, Read<EmoteMsgPacket>);
        builder.Register(PacketNames.YellMsg, Read<YellMsgPacket>);
        builder.Register(PacketNames.BroadcastMsg, Read<BroadcastMsgPacket>);
        builder.Register(PacketNames.NoticeMsg, Read<NoticeMsgPacket>);
        builder.Register(PacketNames.AdminMsg, Read<AdminMsgPacket>);
        builder.Register(PacketNames.PlayerMsg, Read<PlayerMsgPacket>);
        builder.Register(PacketNames.Roll, Read<RollPacket>);

        // Guild
        builder.Register(PacketNames.GuildCreate, Read<GuildCreatePacket>);
        builder.Register(PacketNames.GuildDisband, Read<GuildDisbandPacket>);
        builder.Register(PacketNames.GuildOfferInitiate, Read<GuildOfferInitiatePacket>);
        builder.Register(PacketNames.GuildOfferRespond, Read<GuildOfferRespondPacket>);
        builder.Register(PacketNames.GuildOfferNotify, Read<GuildOfferNotifyPacket>);
        builder.Register(PacketNames.GuildSetOpen, Read<GuildSetOpenPacket>);
        builder.Register(PacketNames.GuildSetShowRank, Read<GuildSetShowRankPacket>);
        builder.Register(PacketNames.GuildLeave, Read<GuildLeavePacket>);
        builder.Register(PacketNames.GuildKick, Read<GuildKickPacket>);
        builder.Register(PacketNames.GuildPromote, Read<GuildPromotePacket>);
        builder.Register(PacketNames.GuildDemote, Read<GuildDemotePacket>);
        builder.Register(PacketNames.GuildTransfer, Read<GuildTransferPacket>);
        builder.Register(PacketNames.GuildSetMotd, Read<GuildSetMotdPacket>);
        builder.Register(PacketNames.GuildSetLabels, Read<GuildSetLabelsPacket>);
        builder.Register(PacketNames.GuildSetColor, Read<GuildSetColorPacket>);
        builder.Register(PacketNames.GuildDonate, Read<GuildDonatePacket>);
        builder.Register(PacketNames.GuildChat, Read<GuildChatPacket>);
        builder.Register(PacketNames.GuildBrowseRequest, Read<GuildBrowseRequestPacket>);
        builder.Register(PacketNames.GuildBrowse, Read<GuildBrowsePacket>);
        builder.Register(PacketNames.GuildApply, Read<GuildApplyPacket>);
        builder.Register(PacketNames.GuildReviewApplication, Read<GuildReviewApplicationPacket>);
        builder.Register(PacketNames.GuildInfo, Read<GuildInfoPacket>);
        builder.Register(PacketNames.GuildInfoRequest, Read<GuildInfoRequestPacket>);

        // Social (friends / ignore)
        builder.Register(PacketNames.SocialList, Read<SocialListPacket>);
        builder.Register(PacketNames.SocialAddFriend, Read<SocialAddFriendPacket>);
        builder.Register(PacketNames.SocialAddIgnore, Read<SocialAddIgnorePacket>);
        builder.Register(PacketNames.SocialRemoveFriend, Read<SocialRemoveFriendPacket>);
        builder.Register(PacketNames.SocialRemoveIgnore, Read<SocialRemoveIgnorePacket>);

        // Mail
        builder.Register(PacketNames.Mailbox, Read<MailboxPacket>);
        builder.Register(PacketNames.MailMarkRead, Read<MailMarkReadPacket>);
        builder.Register(PacketNames.MailDelete, Read<MailDeletePacket>);
        builder.Register(PacketNames.MailClaim, Read<MailClaimPacket>);
        builder.Register(PacketNames.MailSend, Read<MailSendPacket>);
        builder.Register(PacketNames.MailPayCod, Read<MailPayCodPacket>);

        // Marketplace
        builder.Register(PacketNames.MarketList, Read<MarketListPacket>);
        builder.Register(PacketNames.MarketOpen, Read<MarketOpenPacket>);
        builder.Register(PacketNames.MarketCreate, Read<MarketCreatePacket>);
        builder.Register(PacketNames.MarketBuy, Read<MarketBuyPacket>);
        builder.Register(PacketNames.MarketCancel, Read<MarketCancelPacket>);
        builder.Register(PacketNames.MarketRefresh, Read<MarketRefreshPacket>);
        builder.Register(PacketNames.MarketClose, Read<MarketClosePacket>);

        // Direct trade
        builder.Register(PacketNames.TradeInvite, Read<TradeInvitePacket>);
        builder.Register(PacketNames.TradeRespond, Read<TradeRespondPacket>);
        builder.Register(PacketNames.TradeOfferAdd, Read<TradeOfferAddPacket>);
        builder.Register(PacketNames.TradeOfferRemove, Read<TradeOfferRemovePacket>);
        builder.Register(PacketNames.TradeConfirm, Read<TradeConfirmPacket>);
        builder.Register(PacketNames.TradeCancel, Read<TradeCancelPacket>);
        builder.Register(PacketNames.TradeInviteNotify, Read<TradeInviteNotifyPacket>);
        builder.Register(PacketNames.TradeWindow, Read<TradeWindowPacket>);

        // Player quests
        builder.Register(PacketNames.QuestLog, Read<QuestLogPacket>);
        builder.Register(PacketNames.QuestAccept, Read<QuestAcceptPacket>);
        builder.Register(PacketNames.QuestTurnIn, Read<QuestTurnInPacket>);
        builder.Register(PacketNames.QuestAbandon, Read<QuestAbandonPacket>);
        builder.Register(PacketNames.SendQuests, Read<SendQuestsPacket>);
        builder.Register(PacketNames.OpenNpcQuestMenu, Read<OpenNpcQuestMenuPacket>);

        // NPC conversations
        builder.Register(PacketNames.SendConversations, Read<SendConversationsPacket>);
        builder.Register(PacketNames.ConversationLog, Read<ConversationLogPacket>);
        builder.Register(PacketNames.OpenNpcConversation, Read<OpenNpcConversationPacket>);

        // Movement

        // Combat / spells
        builder.Register(PacketNames.Search, Read<SearchPacket>);
        builder.Register(PacketNames.DropTarget, Read<DropTargetPacket>);

        // Inventory / items
        builder.Register(PacketNames.UseItem, Read<UseItemPacket>);
        builder.Register(PacketNames.MapGetItem, Read<MapGetItemPacket>);
        builder.Register(PacketNames.MapPickUp, Read<MapPickUpPacket>);
        builder.Register(PacketNames.MapPickUpAll, Read<MapPickUpAllPacket>);
        builder.Register(PacketNames.MapDropItem, Read<MapDropItemPacket>);
        builder.Register(PacketNames.MapDropBulk, Read<MapDropBulkPacket>);
        builder.Register(PacketNames.SortInventory, Read<SortInventoryPacket>);

        // Stats
        builder.Register(PacketNames.RequestLocation, Read<RequestLocationPacket>);

        // Map
        builder.Register(PacketNames.RequestNewMap, Read<RequestNewMapPacket>);
        builder.Register(PacketNames.MapData, Read<MapDataClientPacket>);
        builder.Register(PacketNames.NeedMap, Read<NeedMapPacket>);
        builder.Register(PacketNames.NeedNeighborMap, Read<NeedNeighborMapPacket>);
        builder.Register(PacketNames.RequestRegionSync, Read<RequestRegionSyncPacket>);

        // Bank
        builder.Register(PacketNames.BankOpen, Read<BankOpenPacket>);
        builder.Register(PacketNames.BankDeposit, Read<BankDepositPacket>);
        builder.Register(PacketNames.BankWithdraw, Read<BankWithdrawPacket>);
        builder.Register(PacketNames.BankDepositBulk, Read<BankDepositBulkPacket>);
        builder.Register(PacketNames.BankWithdrawBulk, Read<BankWithdrawBulkPacket>);
        builder.Register(PacketNames.BankSort, Read<BankSortPacket>);
        builder.Register(PacketNames.SendBank, Read<SendBankPacket>);
        builder.Register(PacketNames.BankSlotUpdate, Read<BankSlotUpdatePacket>);

        // Inn
        builder.Register(PacketNames.ConfirmSetSpawn, Read<ConfirmSetSpawnPacket>);
        builder.Register(PacketNames.RespawnRequest, Read<RespawnRequestPacket>);

        // Shop / trade
        builder.Register(PacketNames.NpcInteract, Read<NpcInteractPacket>);
        builder.Register(PacketNames.ShopBarter, Read<ShopBarterPacket>);
        builder.Register(PacketNames.ShopBuy, Read<ShopBuyPacket>);
        builder.Register(PacketNames.ShopSell, Read<ShopSellPacket>);
        builder.Register(PacketNames.FixItem, Read<FixItemPacket>);

        // Party
        builder.Register(PacketNames.Party, Read<PartyRequestPacket>);
        builder.Register(PacketNames.JoinParty, Read<JoinPartyPacket>);
        builder.Register(PacketNames.LeaveParty, Read<LeavePartyPacket>);

        // Spells
        builder.Register(PacketNames.SetHotkey, Read<SetHotkeyPacket>);

        // Who is online
        builder.Register(PacketNames.WhoIsOnline, Read<WhoIsOnlinePacket>);

        // Admin
        builder.Register(PacketNames.WarpMeTo, Read<WarpMeToPacket>);
        builder.Register(PacketNames.WarpToMe, Read<WarpToMePacket>);
        builder.Register(PacketNames.GodMode, Read<GodModePacket>);
        builder.Register(PacketNames.WarpTo, Read<WarpToPacket>);
        builder.Register(PacketNames.SetSprite, Read<SetSpritePacket>);
        builder.Register(PacketNames.SetAccess, Read<SetAccessPacket>);
        builder.Register(PacketNames.KickPlayer, Read<KickPlayerPacket>);
        builder.Register(PacketNames.BanPlayer, Read<BanPlayerPacket>);
        builder.Register(PacketNames.MutePlayer, Read<MutePlayerPacket>);
        builder.Register(PacketNames.RefreshBanList, Read<RefreshBanListPacket>);
        builder.Register(PacketNames.HwBanPlayer, Read<HwBanPlayerPacket>);
        builder.Register(PacketNames.UnbanPlayer, Read<UnbanPlayerPacket>);
        builder.Register(PacketNames.UnkickPlayer, Read<UnkickPlayerPacket>);
        builder.Register(PacketNames.UnmutePlayer, Read<UnmutePlayerPacket>);
        builder.Register(PacketNames.HwUnbanPlayer, Read<HwUnbanPlayerPacket>);
        builder.Register(PacketNames.RequestModeration, Read<RequestModerationPacket>);
        builder.Register(PacketNames.ModerationList, Read<ModerationListPacket>);
        builder.Register(PacketNames.MapRespawn, Read<MapRespawnPacket>);
        builder.Register(PacketNames.MapReport, Read<MapReportPacket>);
        builder.Register(PacketNames.SetMotd, Read<SetMotdPacket>);
        builder.Register(PacketNames.SetTimeOfDay, Read<SetTimeOfDayPacket>);
        builder.Register(PacketNames.SetWeather, Read<SetWeatherPacket>);
        builder.Register(PacketNames.PlayerInfoRequest, Read<PlayerInfoRequestPacket>);
        builder.Register(PacketNames.PlayedRequest, Read<PlayedRequestPacket>);
        builder.Register(PacketNames.HomeRequest, Read<HomeRequestPacket>);
        builder.Register(PacketNames.HomeCooldownRequest, Read<HomeCooldownRequestPacket>);

        // S→C (client side deserializes these)
        builder.Register(PacketNames.AlertMsg, Read<AlertMsgPacket>);
        builder.Register(PacketNames.ServerHello, Read<ServerHelloPacket>);
        builder.Register(PacketNames.QueueUpdate, Read<QueueUpdatePacket>);
        builder.Register(PacketNames.SendClasses, Read<SendClassesPacket>);
        builder.Register(PacketNames.NewCharClasses, Read<NewCharClassesPacket>);
        builder.Register(PacketNames.SendChars, Read<SendCharsPacket>);
        builder.Register(PacketNames.Welcome, Read<WelcomePacket>);
        builder.Register(PacketNames.PlayerInGame, Read<PlayerInGamePacket>);
        builder.Register(PacketNames.SendPlayerData, Read<SendPlayerDataPacket>);
        builder.Register(PacketNames.AggressorRefresh, Read<AggressorRefreshPacket>);
        builder.Register(PacketNames.LeftGame, Read<LeftGamePacket>);
        builder.Register(PacketNames.SendMap, Read<SendMapPacket>);
        builder.Register(PacketNames.JoinMap, Read<JoinMapPacket>);
        builder.Register(PacketNames.LeaveMap, Read<LeaveMapPacket>);
        builder.Register(PacketNames.PlayerXY, Read<PlayerXYPacket>);
        builder.Register(PacketNames.ChatMsg, Read<ChatMsgPacket>);
        builder.Register(PacketNames.ChatBubble, Read<ChatBubblePacket>);
        builder.Register(PacketNames.NpcChatBubble, Read<NpcChatBubblePacket>);
        builder.Register(PacketNames.SendItems, Read<SendItemsPacket>);
        builder.Register(PacketNames.UpdateItem, Read<UpdateItemPacket>);
        builder.Register(PacketNames.SendNpcs, Read<SendNpcsPacket>);
        builder.Register(PacketNames.SendMapGroups, Read<SendMapGroupsPacket>);
        builder.Register(PacketNames.UpdateNpc, Read<UpdateNpcPacket>);
        builder.Register(PacketNames.MapNpcs, Read<MapNpcsPacket>);
        builder.Register(PacketNames.SendInventory, Read<SendInventoryPacket>);
        builder.Register(PacketNames.InventoryUpdate, Read<InventoryUpdatePacket>);
        builder.Register(PacketNames.EquippedGear, Read<EquippedGearPacket>);
        builder.Register(PacketNames.MapItems, Read<MapItemsPacket>);
        builder.Register(PacketNames.Weather, Read<WeatherPacket>);
        builder.Register(PacketNames.TimeOfDay, Read<TimeOfDayPacket>);
        builder.Register(PacketNames.PlayersOnline, Read<PlayersOnlinePacket>);
        builder.Register(PacketNames.NpcSpawn, Read<NpcSpawnPacket>);
        builder.Register(PacketNames.NpcMove, Read<NpcMovePacket>);
        builder.Register(PacketNames.TraversalNpc, Read<TraversalNpcPacket>);
        builder.Register(PacketNames.NpcDespawn, Read<NpcDespawnPacket>);
        builder.Register(PacketNames.NpcDir, Read<NpcDirPacket>);
        builder.Register(PacketNames.SendShops, Read<SendShopsPacket>);
        builder.Register(PacketNames.ShopContents, Read<ShopContentsPacket>);
        builder.Register(PacketNames.OpenInn, Read<OpenInnPacket>);
        builder.Register(PacketNames.UpdateShop, Read<UpdateShopPacket>);
        builder.Register(PacketNames.UpdateQuest, Read<UpdateQuestPacket>);
        builder.Register(PacketNames.UpdateConversation, Read<UpdateConversationPacket>);
        builder.Register(PacketNames.UpdateSpell, Read<UpdateSpellPacket>);
        builder.Register(PacketNames.PlayerHotkeys, Read<PlayerHotkeysPacket>);
        builder.Register(PacketNames.PartyRequest, Read<PartyRequestNotifyPacket>);
        builder.Register(PacketNames.PartyVitals, Read<PartyVitalsPacket>);

        // World events
        builder.Register(PacketNames.CheckForMap, Read<CheckForMapPacket>);
        builder.Register(PacketNames.SeamlessCross, Read<SeamlessCrossPacket>);
        builder.Register(PacketNames.MapKey, Read<MapKeyPacket>);
        builder.Register(PacketNames.NpcDead, Read<NpcDeadPacket>);
        builder.Register(PacketNames.NpcTarget, Read<NpcTargetPacket>);
        builder.Register(PacketNames.SetTarget, Read<SetTargetPacket>);
        builder.Register(PacketNames.ClearTarget, Read<ClearTargetPacket>);
        // SendPlayerDir uses same cmd as PlayerDir; client handles by presence of "index" field
        // PacketNames.SendPlayerDir => handled as PlayerDirPacket on server (C→S), SendPlayerDirPacket on client

        // Editor
        builder.Register(PacketNames.EditorLogin, Read<EditorLoginPacket>);
        builder.Register(PacketNames.EditorRequestItem, Read<EditorRequestItemPacket>);
        builder.Register(PacketNames.EditorRequestNpc, Read<EditorRequestNpcPacket>);
        builder.Register(PacketNames.EditorRequestShop, Read<EditorRequestShopPacket>);
        builder.Register(PacketNames.EditorRequestQuest, Read<EditorRequestQuestPacket>);
        builder.Register(PacketNames.EditorRequestConversation, Read<EditorRequestConversationPacket>);
        builder.Register(PacketNames.EditorRequestSpell, Read<EditorRequestSpellPacket>);
        builder.Register(PacketNames.EditorRequestMap, Read<EditorRequestMapPacket>);
        builder.Register(PacketNames.EditorRequestClass, Read<EditorRequestClassPacket>);
        builder.Register(PacketNames.EditorLock, Read<EditorLockPacket>);
        builder.Register(PacketNames.EditorUnlock, Read<EditorUnlockPacket>);
        builder.Register(PacketNames.EditorLocks, Read<EditorLocksPacket>);
        builder.Register(PacketNames.EditorRequestAllItems, Read<EditorRequestAllItemsPacket>);
        builder.Register(PacketNames.EditorRequestAllNpcs, Read<EditorRequestAllNpcsPacket>);
        builder.Register(PacketNames.EditorRequestAllShops, Read<EditorRequestAllShopsPacket>);
        builder.Register(PacketNames.EditorRequestAllQuests, Read<EditorRequestAllQuestsPacket>);
        builder.Register(PacketNames.EditorRequestAllConversations, Read<EditorRequestAllConversationsPacket>);
        builder.Register(PacketNames.EditorRequestAllSpells, Read<EditorRequestAllSpellsPacket>);
        builder.Register(PacketNames.EditorRequestAllClasses, Read<EditorRequestAllClassesPacket>);
        builder.Register(PacketNames.EditorRequestMapGroup, Read<EditorRequestMapGroupPacket>);
        builder.Register(PacketNames.EditorRequestAllMapGroups, Read<EditorRequestAllMapGroupsPacket>);
        builder.Register(PacketNames.EditorRequestAllMaps, Read<EditorRequestAllMapsPacket>);
        builder.Register(PacketNames.EditorRequestAccounts, Read<EditorRequestAccountsPacket>);
        builder.Register(PacketNames.EditorAccountList, Read<EditorAccountListPacket>);
        builder.Register(PacketNames.EditorRequestAccount, Read<EditorRequestAccountPacket>);
        builder.Register(PacketNames.EditorAccount, Read<EditorAccountPacket>);
        builder.Register(PacketNames.EditorSaveAccount, Read<EditorSaveAccountPacket>);
        builder.Register(PacketNames.EditorRenameChar, Read<EditorRenameCharPacket>);
        builder.Register(PacketNames.EditorGiveItem, Read<EditorGiveItemPacket>);
        builder.Register(PacketNames.EditorTakeItem, Read<EditorTakeItemPacket>);
        builder.Register(PacketNames.EditorLearnSpell, Read<EditorLearnSpellPacket>);
        builder.Register(PacketNames.EditorForgetSpell, Read<EditorForgetSpellPacket>);
        builder.Register(PacketNames.EditorBankGive, Read<EditorBankGivePacket>);
        builder.Register(PacketNames.EditorBankTake, Read<EditorBankTakePacket>);
        builder.Register(PacketNames.EditorSetQuestStatus, Read<EditorSetQuestStatusPacket>);
        builder.Register(PacketNames.EditorNotice, Read<EditorNoticePacket>);
        builder.Register(PacketNames.EditorSaveMapGroup, Read<EditorSaveMapGroupPacket>);
        builder.Register(PacketNames.EditorSaveClass, Read<EditorSaveClassPacket>);
        builder.Register(PacketNames.UpdateClass, Read<UpdateClassPacket>);
        builder.Register(PacketNames.EditorSaveItem, Read<EditorSaveItemPacket>);
        builder.Register(PacketNames.EditorSaveNpc, Read<EditorSaveNpcPacket>);
        builder.Register(PacketNames.EditorSaveShop, Read<EditorSaveShopPacket>);
        builder.Register(PacketNames.EditorSaveQuest, Read<EditorSaveQuestPacket>);
        builder.Register(PacketNames.EditorSaveConversation, Read<EditorSaveConversationPacket>);
        builder.Register(PacketNames.EditorSaveSpell, Read<EditorSaveSpellPacket>);
        builder.Register(PacketNames.EditorSaveMap, Read<EditorSaveMapPacket>);
        builder.Register(PacketNames.EditorLoginResponse, Read<EditorLoginResponsePacket>);
        builder.Register(PacketNames.EditorData, Read<EditorDataPacket>);
        builder.Register(PacketNames.EditorAllItems, Read<EditorAllItemsPacket>);
        builder.Register(PacketNames.EditorAllNpcs, Read<EditorAllNpcsPacket>);
        builder.Register(PacketNames.EditorAllShops, Read<EditorAllShopsPacket>);
        builder.Register(PacketNames.EditorAllQuests, Read<EditorAllQuestsPacket>);
        builder.Register(PacketNames.EditorAllConversations, Read<EditorAllConversationsPacket>);
        builder.Register(PacketNames.EditorAllSpells, Read<EditorAllSpellsPacket>);
        builder.Register(PacketNames.EditorAllClasses, Read<EditorAllClassesPacket>);
        builder.Register(PacketNames.UpdateMapGroup, Read<UpdateMapGroupPacket>);
        builder.Register(PacketNames.EditorAllMapGroups, Read<EditorAllMapGroupsPacket>);
        builder.Register(PacketNames.EditorAllMaps, Read<EditorAllMapsPacket>);

        return builder;
    }

    /// <summary>Reads one line as <typeparamref name="T"/>, or null when it does not fit that shape.
    ///
    /// <para>Never throws. A line that arrives malformed, truncated, or carrying a value of the wrong
    /// type is a line to drop, not a reason to take down the connection reading it — and every caller
    /// already treats null as "drop this packet".</para></summary>
    private static IPacket? Read<T>(string line, bool hasIndex) where T : IPacket
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, PacketSerializer.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
