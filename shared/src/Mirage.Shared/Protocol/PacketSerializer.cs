using Mirage.Shared.Protocol.Packets;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Mirage.Shared.Protocol;

public static class PacketSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The header fields a dispatcher needs before it can pick a concrete packet type: the
    /// <c>cmd</c> discriminator, and whether a top-level <c>index</c> field is present.
    /// <para><see cref="HasIndex"/> exists for the shared-cmd pairs — PlayerMove/SendPlayerMove and
    /// PlayerDir/SendPlayerDir deliberately reuse one <c>cmd</c> string, and the only thing that
    /// tells them apart on the wire is that the S→C form carries an index and the C→S form does
    /// not. <see cref="Cmd"/> is null when the line is not a JSON object, carries no <c>cmd</c>, or
    /// carries one that is not a string.</para>
    /// </summary>
    public readonly record struct PacketHeader(string? Cmd, bool HasIndex);

    // UTF-8 property names to compare against, so the scan never materializes a key string.
    private static readonly byte[] CmdUtf8 = "cmd"u8.ToArray();
    private static readonly byte[] IndexUtf8 = "index"u8.ToArray();

    // Lines above this encoded size go through the array pool instead of the stack. The server
    // rate-limits non-admins to 1000 bytes/s (PacketHandler.HandlePacket), so ordinary gameplay
    // traffic stays on the stack path; only bulk editor saves and map payloads rent.
    private const int StackScanLimit = 1024;

    /// <summary>
    /// Reads <see cref="PacketHeader"/> off a JSON line without building a DOM. Allocation-free:
    /// the UTF-8 bytes go to the stack (or the array pool for large lines) and the reader stops as
    /// soon as it has both fields, so it never walks a big payload it doesn't care about.
    /// <para>Never throws — an unreadable line yields <c>default</c>, which every caller treats as
    /// "drop this packet", matching the behavior of the DOM parse this replaced.</para>
    /// </summary>
    public static PacketHeader ReadHeader(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return default;

        int maxBytes = Encoding.UTF8.GetMaxByteCount(line.Length);
        if (maxBytes <= StackScanLimit)
        {
            Span<byte> stackBuf = stackalloc byte[StackScanLimit];
            return ScanHeader(stackBuf[..Encoding.UTF8.GetBytes(line, stackBuf)]);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try { return ScanHeader(rented.AsSpan(0, Encoding.UTF8.GetBytes(line, rented))); }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    // Walks only the top level: every container value is Skip()ed the moment its start token
    // appears, so the reader never descends and a nested "cmd"/"index" key cannot be mistaken for
    // the real one. That also makes the first EndObject we see the close of the outer object.
    private static PacketHeader ScanHeader(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        string? cmd = null;
        bool hasIndex = false;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return default;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                bool isCmd = reader.ValueTextEquals(CmdUtf8);
                bool isIndex = reader.ValueTextEquals(IndexUtf8);

                if (!reader.Read()) break;
                // A non-string cmd leaves Cmd null, which the caller treats as "drop this packet".
                if (isCmd && reader.TokenType == JsonTokenType.String) cmd = reader.GetString();
                else if (isIndex && reader.TokenType != JsonTokenType.Null) hasIndex = true;

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                if (cmd is not null && hasIndex) break;
            }
        }
        catch (JsonException) { return default; }

        return new PacketHeader(cmd, hasIndex);
    }

#if DEBUG
    // Fires once on first use. Verifies every IPacket subtype survives a round-trip through
    // TryDeserialize, catching missing or mis-wired switch entries at startup rather than
    // silently dropping packets at runtime.
    //
    // Known limitation — shared-cmd pairs (PlayerMove/SendPlayerMove, PlayerDir/SendPlayerDir):
    // these two pairs intentionally reuse the same cmd string; the receiver picks the right type
    // by inspecting the "index" field before the main switch runs. For those pairs the round-trip
    // will always return the C→S type (whichever the switch maps the cmd to), so the type-identity
    // check is skipped — only non-null is verified. This means a wrong-type wiring inside a
    // shared-cmd group is not caught here. A full test suite is the proper fix for that gap.
    static PacketSerializer() => ValidateAllRegistered();

    private static void ValidateAllRegistered()
    {
        var allPackets = typeof(IPacket).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPacket).IsAssignableFrom(t))
            .Select(t => (IPacket?)Activator.CreateInstance(t))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        // Cmds shared by multiple types are intentionally ambiguous — see note above.
        var sharedCmds = allPackets
            .GroupBy(p => p.Cmd)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var errors = allPackets
            .Select(p =>
            {
                var roundTripped = TryDeserialize(Serialize(p));
                if (roundTripped is null)
                    return $"{p.GetType().Name} (cmd: \"{p.Cmd}\") — missing from switch";
                if (!sharedCmds.Contains(p.Cmd) && roundTripped.GetType() != p.GetType())
                    return $"{p.GetType().Name} (cmd: \"{p.Cmd}\") — switch maps to wrong type {roundTripped.GetType().Name}";
                return null;
            })
            .Where(msg => msg is not null)
            .ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"PacketSerializer.TryDeserialize has errors: {string.Join(", ", errors)}");
        }
    }
#endif

    /// <summary>Serialize a packet POCO to a JSON line (terminated with \n).</summary>
    public static string Serialize<T>(T packet) where T : IPacket =>
        JsonSerializer.Serialize(packet, packet.GetType(), Options) + "\n";

    /// <summary>
    /// Deserialize a JSON line to the correct IPacket subtype by reading the "cmd" field.
    /// Returns null if the line is unrecognized or malformed.
    /// </summary>
    public static IPacket? TryDeserialize(string line)
    {
        string? cmd = ReadHeader(line).Cmd;
        return cmd is null ? null : TryDeserialize(line, cmd);
    }

    /// <summary>
    /// Same as <see cref="TryDeserialize(string)"/> but for a caller that has already read the
    /// header via <see cref="ReadHeader"/> — it passes the <c>cmd</c> back in rather than paying for
    /// a second scan. Used by the client dispatcher, which needs the header up front to tell the
    /// shared-cmd pairs apart.
    /// </summary>
    public static IPacket? TryDeserialize(string line, string cmd)
    {
        try
        {
            return cmd switch
            {
                // Account / pre-login
                PacketNames.GetClasses => JsonSerializer.Deserialize<GetClassesPacket>(line, Options),
                PacketNames.NewAccount => JsonSerializer.Deserialize<NewAccountPacket>(line, Options),
                PacketNames.DelAccount => JsonSerializer.Deserialize<DelAccountPacket>(line, Options),
                PacketNames.ChangePassword => JsonSerializer.Deserialize<ChangePasswordPacket>(line, Options),
                PacketNames.Login => JsonSerializer.Deserialize<LoginPacket>(line, Options),
                PacketNames.AddChar => JsonSerializer.Deserialize<AddCharPacket>(line, Options),
                PacketNames.DelChar => JsonSerializer.Deserialize<DelCharPacket>(line, Options),
                PacketNames.UseChar => JsonSerializer.Deserialize<UseCharPacket>(line, Options),
                PacketNames.LogoutToCharSelect => JsonSerializer.Deserialize<LogoutToCharSelectPacket>(line, Options),
                PacketNames.SetLanguage => JsonSerializer.Deserialize<SetLanguagePacket>(line, Options),

                // Chat
                PacketNames.SayMsg => JsonSerializer.Deserialize<SayMsgPacket>(line, Options),
                PacketNames.EmoteMsg => JsonSerializer.Deserialize<EmoteMsgPacket>(line, Options),
                PacketNames.YellMsg => JsonSerializer.Deserialize<YellMsgPacket>(line, Options),
                PacketNames.BroadcastMsg => JsonSerializer.Deserialize<BroadcastMsgPacket>(line, Options),
                PacketNames.NoticeMsg => JsonSerializer.Deserialize<NoticeMsgPacket>(line, Options),
                PacketNames.AdminMsg => JsonSerializer.Deserialize<AdminMsgPacket>(line, Options),
                PacketNames.PlayerMsg => JsonSerializer.Deserialize<PlayerMsgPacket>(line, Options),
                PacketNames.Roll => JsonSerializer.Deserialize<RollPacket>(line, Options),

                // Guild
                PacketNames.GuildCreate => JsonSerializer.Deserialize<GuildCreatePacket>(line, Options),
                PacketNames.GuildDisband => JsonSerializer.Deserialize<GuildDisbandPacket>(line, Options),
                PacketNames.GuildOfferInitiate => JsonSerializer.Deserialize<GuildOfferInitiatePacket>(line, Options),
                PacketNames.GuildOfferRespond => JsonSerializer.Deserialize<GuildOfferRespondPacket>(line, Options),
                PacketNames.GuildOfferNotify => JsonSerializer.Deserialize<GuildOfferNotifyPacket>(line, Options),
                PacketNames.GuildSetOpen => JsonSerializer.Deserialize<GuildSetOpenPacket>(line, Options),
                PacketNames.GuildSetShowRank => JsonSerializer.Deserialize<GuildSetShowRankPacket>(line, Options),
                PacketNames.GuildLeave => JsonSerializer.Deserialize<GuildLeavePacket>(line, Options),
                PacketNames.GuildKick => JsonSerializer.Deserialize<GuildKickPacket>(line, Options),
                PacketNames.GuildPromote => JsonSerializer.Deserialize<GuildPromotePacket>(line, Options),
                PacketNames.GuildDemote => JsonSerializer.Deserialize<GuildDemotePacket>(line, Options),
                PacketNames.GuildTransfer => JsonSerializer.Deserialize<GuildTransferPacket>(line, Options),
                PacketNames.GuildSetMotd => JsonSerializer.Deserialize<GuildSetMotdPacket>(line, Options),
                PacketNames.GuildSetLabels => JsonSerializer.Deserialize<GuildSetLabelsPacket>(line, Options),
                PacketNames.GuildSetColor => JsonSerializer.Deserialize<GuildSetColorPacket>(line, Options),
                PacketNames.GuildDonate => JsonSerializer.Deserialize<GuildDonatePacket>(line, Options),
                PacketNames.GuildDonateValor => JsonSerializer.Deserialize<GuildDonateValorPacket>(line, Options),
                PacketNames.GuildPayTax => JsonSerializer.Deserialize<GuildPayTaxPacket>(line, Options),
                PacketNames.GuildQuestAcquire => JsonSerializer.Deserialize<GuildQuestAcquirePacket>(line, Options),
                PacketNames.GuildQuestAbandon => JsonSerializer.Deserialize<GuildQuestAbandonPacket>(line, Options),
                PacketNames.GuildChat => JsonSerializer.Deserialize<GuildChatPacket>(line, Options),
                PacketNames.GuildBrowseRequest => JsonSerializer.Deserialize<GuildBrowseRequestPacket>(line, Options),
                PacketNames.GuildBrowse => JsonSerializer.Deserialize<GuildBrowsePacket>(line, Options),
                PacketNames.GuildApply => JsonSerializer.Deserialize<GuildApplyPacket>(line, Options),
                PacketNames.GuildReviewApplication => JsonSerializer.Deserialize<GuildReviewApplicationPacket>(line, Options),
                PacketNames.GuildInfo => JsonSerializer.Deserialize<GuildInfoPacket>(line, Options),
                PacketNames.GuildInfoRequest => JsonSerializer.Deserialize<GuildInfoRequestPacket>(line, Options),
                PacketNames.GuildWarDeclare => JsonSerializer.Deserialize<GuildWarDeclarePacket>(line, Options),
                PacketNames.GuildWarDeclareByName => JsonSerializer.Deserialize<GuildWarDeclareByNamePacket>(line, Options),
                PacketNames.GuildWarRetract => JsonSerializer.Deserialize<GuildWarRetractPacket>(line, Options),
                PacketNames.GuildWarReviewRequest => JsonSerializer.Deserialize<GuildWarReviewRequestPacket>(line, Options),
                PacketNames.GuildWarPeace => JsonSerializer.Deserialize<GuildWarPeacePacket>(line, Options),
                PacketNames.GuildWarWager => JsonSerializer.Deserialize<GuildWarWagerPacket>(line, Options),
                PacketNames.GuildTerritoryChallenge => JsonSerializer.Deserialize<GuildTerritoryChallengePacket>(line, Options),
                PacketNames.TerritoryContest => JsonSerializer.Deserialize<TerritoryContestPacket>(line, Options),
                PacketNames.AdminGuildReset => JsonSerializer.Deserialize<AdminGuildResetPacket>(line, Options),
                PacketNames.AdminTerritoryWar => JsonSerializer.Deserialize<AdminTerritoryWarPacket>(line, Options),
                PacketNames.GuildLeaderboard => JsonSerializer.Deserialize<GuildLeaderboardPacket>(line, Options),
                PacketNames.GuildLeaderboardRequest => JsonSerializer.Deserialize<GuildLeaderboardRequestPacket>(line, Options),
                PacketNames.SeasonArchiveRequest => JsonSerializer.Deserialize<SeasonArchiveRequestPacket>(line, Options),
                PacketNames.SeasonArchive => JsonSerializer.Deserialize<SeasonArchivePacket>(line, Options),
                PacketNames.GuildTerritoryWithdraw => JsonSerializer.Deserialize<GuildTerritoryWithdrawPacket>(line, Options),
                PacketNames.GuildWarAttrition => JsonSerializer.Deserialize<GuildWarAttritionPacket>(line, Options),

                // Social (friends / ignore)
                PacketNames.SocialList => JsonSerializer.Deserialize<SocialListPacket>(line, Options),
                PacketNames.SocialAddFriend => JsonSerializer.Deserialize<SocialAddFriendPacket>(line, Options),
                PacketNames.SocialAddIgnore => JsonSerializer.Deserialize<SocialAddIgnorePacket>(line, Options),
                PacketNames.SocialRemoveFriend => JsonSerializer.Deserialize<SocialRemoveFriendPacket>(line, Options),
                PacketNames.SocialRemoveIgnore => JsonSerializer.Deserialize<SocialRemoveIgnorePacket>(line, Options),

                // Mail
                PacketNames.Mailbox => JsonSerializer.Deserialize<MailboxPacket>(line, Options),
                PacketNames.MailMarkRead => JsonSerializer.Deserialize<MailMarkReadPacket>(line, Options),
                PacketNames.MailDelete => JsonSerializer.Deserialize<MailDeletePacket>(line, Options),
                PacketNames.MailClaim => JsonSerializer.Deserialize<MailClaimPacket>(line, Options),
                PacketNames.MailSend => JsonSerializer.Deserialize<MailSendPacket>(line, Options),
                PacketNames.MailPayCod => JsonSerializer.Deserialize<MailPayCodPacket>(line, Options),

                // Marketplace
                PacketNames.MarketList => JsonSerializer.Deserialize<MarketListPacket>(line, Options),
                PacketNames.MarketOpen => JsonSerializer.Deserialize<MarketOpenPacket>(line, Options),
                PacketNames.MarketCreate => JsonSerializer.Deserialize<MarketCreatePacket>(line, Options),
                PacketNames.MarketBuy => JsonSerializer.Deserialize<MarketBuyPacket>(line, Options),
                PacketNames.MarketCancel => JsonSerializer.Deserialize<MarketCancelPacket>(line, Options),
                PacketNames.MarketRefresh => JsonSerializer.Deserialize<MarketRefreshPacket>(line, Options),
                PacketNames.MarketClose => JsonSerializer.Deserialize<MarketClosePacket>(line, Options),

                // Direct trade
                PacketNames.TradeInvite => JsonSerializer.Deserialize<TradeInvitePacket>(line, Options),
                PacketNames.TradeRespond => JsonSerializer.Deserialize<TradeRespondPacket>(line, Options),
                PacketNames.TradeOfferAdd => JsonSerializer.Deserialize<TradeOfferAddPacket>(line, Options),
                PacketNames.TradeOfferRemove => JsonSerializer.Deserialize<TradeOfferRemovePacket>(line, Options),
                PacketNames.TradeConfirm => JsonSerializer.Deserialize<TradeConfirmPacket>(line, Options),
                PacketNames.TradeCancel => JsonSerializer.Deserialize<TradeCancelPacket>(line, Options),
                PacketNames.TradeInviteNotify => JsonSerializer.Deserialize<TradeInviteNotifyPacket>(line, Options),
                PacketNames.TradeWindow => JsonSerializer.Deserialize<TradeWindowPacket>(line, Options),

                // Player quests
                PacketNames.QuestLog => JsonSerializer.Deserialize<QuestLogPacket>(line, Options),
                PacketNames.QuestAccept => JsonSerializer.Deserialize<QuestAcceptPacket>(line, Options),
                PacketNames.QuestTurnIn => JsonSerializer.Deserialize<QuestTurnInPacket>(line, Options),
                PacketNames.QuestAbandon => JsonSerializer.Deserialize<QuestAbandonPacket>(line, Options),
                PacketNames.SendQuests => JsonSerializer.Deserialize<SendQuestsPacket>(line, Options),
                PacketNames.OpenNpcQuestMenu => JsonSerializer.Deserialize<OpenNpcQuestMenuPacket>(line, Options),

                // NPC conversations
                PacketNames.SendConversations => JsonSerializer.Deserialize<SendConversationsPacket>(line, Options),
                PacketNames.ConversationLog => JsonSerializer.Deserialize<ConversationLogPacket>(line, Options),
                PacketNames.OpenNpcConversation => JsonSerializer.Deserialize<OpenNpcConversationPacket>(line, Options),

                // Movement
                PacketNames.PlayerMove => JsonSerializer.Deserialize<PlayerMovePacket>(line, Options),
                PacketNames.PlayerDir => JsonSerializer.Deserialize<PlayerDirPacket>(line, Options),

                // Combat / spells
                PacketNames.Attack => JsonSerializer.Deserialize<AttackPacket>(line, Options),
                PacketNames.Search => JsonSerializer.Deserialize<SearchPacket>(line, Options),
                PacketNames.DropTarget => JsonSerializer.Deserialize<DropTargetPacket>(line, Options),
                PacketNames.Cast => JsonSerializer.Deserialize<CastPacket>(line, Options),

                // Inventory / items
                PacketNames.UseItem => JsonSerializer.Deserialize<UseItemPacket>(line, Options),
                PacketNames.MapGetItem => JsonSerializer.Deserialize<MapGetItemPacket>(line, Options),
                PacketNames.MapPickUp => JsonSerializer.Deserialize<MapPickUpPacket>(line, Options),
                PacketNames.MapPickUpAll => JsonSerializer.Deserialize<MapPickUpAllPacket>(line, Options),
                PacketNames.MapDropItem => JsonSerializer.Deserialize<MapDropItemPacket>(line, Options),
                PacketNames.MapDropBulk => JsonSerializer.Deserialize<MapDropBulkPacket>(line, Options),
                PacketNames.SortInventory => JsonSerializer.Deserialize<SortInventoryPacket>(line, Options),

                // Stats
                PacketNames.TrainStats => JsonSerializer.Deserialize<TrainStatsPacket>(line, Options),
                PacketNames.GetStats => JsonSerializer.Deserialize<GetStatsPacket>(line, Options),
                PacketNames.RequestLocation => JsonSerializer.Deserialize<RequestLocationPacket>(line, Options),

                // Map
                PacketNames.RequestNewMap => JsonSerializer.Deserialize<RequestNewMapPacket>(line, Options),
                PacketNames.MapData => JsonSerializer.Deserialize<MapDataClientPacket>(line, Options),
                PacketNames.NeedMap => JsonSerializer.Deserialize<NeedMapPacket>(line, Options),
                PacketNames.NeedNeighborMap => JsonSerializer.Deserialize<NeedNeighborMapPacket>(line, Options),
                PacketNames.RequestRegionSync => JsonSerializer.Deserialize<RequestRegionSyncPacket>(line, Options),

                // Bank
                PacketNames.BankOpen => JsonSerializer.Deserialize<BankOpenPacket>(line, Options),
                PacketNames.BankDeposit => JsonSerializer.Deserialize<BankDepositPacket>(line, Options),
                PacketNames.BankWithdraw => JsonSerializer.Deserialize<BankWithdrawPacket>(line, Options),
                PacketNames.BankDepositBulk => JsonSerializer.Deserialize<BankDepositBulkPacket>(line, Options),
                PacketNames.BankWithdrawBulk => JsonSerializer.Deserialize<BankWithdrawBulkPacket>(line, Options),
                PacketNames.BankSort => JsonSerializer.Deserialize<BankSortPacket>(line, Options),
                PacketNames.SendBank => JsonSerializer.Deserialize<SendBankPacket>(line, Options),
                PacketNames.BankSlotUpdate => JsonSerializer.Deserialize<BankSlotUpdatePacket>(line, Options),

                // Inn
                PacketNames.ConfirmSetSpawn => JsonSerializer.Deserialize<ConfirmSetSpawnPacket>(line, Options),
                PacketNames.RespawnRequest => JsonSerializer.Deserialize<RespawnRequestPacket>(line, Options),

                // Shop / trade
                PacketNames.NpcInteract => JsonSerializer.Deserialize<NpcInteractPacket>(line, Options),
                PacketNames.ShopBarter => JsonSerializer.Deserialize<ShopBarterPacket>(line, Options),
                PacketNames.ShopBuy => JsonSerializer.Deserialize<ShopBuyPacket>(line, Options),
                PacketNames.ShopSell => JsonSerializer.Deserialize<ShopSellPacket>(line, Options),
                PacketNames.FixItem => JsonSerializer.Deserialize<FixItemPacket>(line, Options),

                // Party
                PacketNames.Party => JsonSerializer.Deserialize<PartyRequestPacket>(line, Options),
                PacketNames.JoinParty => JsonSerializer.Deserialize<JoinPartyPacket>(line, Options),
                PacketNames.LeaveParty => JsonSerializer.Deserialize<LeavePartyPacket>(line, Options),

                // Spells
                PacketNames.Spells => JsonSerializer.Deserialize<SpellsRequestPacket>(line, Options),
                PacketNames.SetPreparedSpell => JsonSerializer.Deserialize<SetPreparedSpellPacket>(line, Options),
                PacketNames.ForgetSpell => JsonSerializer.Deserialize<ForgetSpellPacket>(line, Options),
                PacketNames.SetHotkey => JsonSerializer.Deserialize<SetHotkeyPacket>(line, Options),

                // Who is online
                PacketNames.WhoIsOnline => JsonSerializer.Deserialize<WhoIsOnlinePacket>(line, Options),

                // Admin
                PacketNames.WarpMeTo => JsonSerializer.Deserialize<WarpMeToPacket>(line, Options),
                PacketNames.WarpToMe => JsonSerializer.Deserialize<WarpToMePacket>(line, Options),
                PacketNames.GodMode => JsonSerializer.Deserialize<GodModePacket>(line, Options),
                PacketNames.WarpTo => JsonSerializer.Deserialize<WarpToPacket>(line, Options),
                PacketNames.SetSprite => JsonSerializer.Deserialize<SetSpritePacket>(line, Options),
                PacketNames.SetAccess => JsonSerializer.Deserialize<SetAccessPacket>(line, Options),
                PacketNames.KickPlayer => JsonSerializer.Deserialize<KickPlayerPacket>(line, Options),
                PacketNames.BanPlayer => JsonSerializer.Deserialize<BanPlayerPacket>(line, Options),
                PacketNames.MutePlayer => JsonSerializer.Deserialize<MutePlayerPacket>(line, Options),
                PacketNames.RefreshBanList => JsonSerializer.Deserialize<RefreshBanListPacket>(line, Options),
                PacketNames.HwBanPlayer => JsonSerializer.Deserialize<HwBanPlayerPacket>(line, Options),
                PacketNames.UnbanPlayer => JsonSerializer.Deserialize<UnbanPlayerPacket>(line, Options),
                PacketNames.UnkickPlayer => JsonSerializer.Deserialize<UnkickPlayerPacket>(line, Options),
                PacketNames.UnmutePlayer => JsonSerializer.Deserialize<UnmutePlayerPacket>(line, Options),
                PacketNames.HwUnbanPlayer => JsonSerializer.Deserialize<HwUnbanPlayerPacket>(line, Options),
                PacketNames.RequestModeration => JsonSerializer.Deserialize<RequestModerationPacket>(line, Options),
                PacketNames.ModerationList => JsonSerializer.Deserialize<ModerationListPacket>(line, Options),
                PacketNames.MapRespawn => JsonSerializer.Deserialize<MapRespawnPacket>(line, Options),
                PacketNames.MapReport => JsonSerializer.Deserialize<MapReportPacket>(line, Options),
                PacketNames.SetMotd => JsonSerializer.Deserialize<SetMotdPacket>(line, Options),
                PacketNames.SetTimeOfDay => JsonSerializer.Deserialize<SetTimeOfDayPacket>(line, Options),
                PacketNames.SetWeather => JsonSerializer.Deserialize<SetWeatherPacket>(line, Options),
                PacketNames.PlayerInfoRequest => JsonSerializer.Deserialize<PlayerInfoRequestPacket>(line, Options),
                PacketNames.PlayedRequest => JsonSerializer.Deserialize<PlayedRequestPacket>(line, Options),
                PacketNames.HomeRequest => JsonSerializer.Deserialize<HomeRequestPacket>(line, Options),
                PacketNames.HomeCooldownRequest => JsonSerializer.Deserialize<HomeCooldownRequestPacket>(line, Options),

                // S→C (client side deserializes these)
                PacketNames.AlertMsg => JsonSerializer.Deserialize<AlertMsgPacket>(line, Options),
                PacketNames.ServerHello => JsonSerializer.Deserialize<ServerHelloPacket>(line, Options),
                PacketNames.QueueUpdate => JsonSerializer.Deserialize<QueueUpdatePacket>(line, Options),
                PacketNames.SendClasses => JsonSerializer.Deserialize<SendClassesPacket>(line, Options),
                PacketNames.NewCharClasses => JsonSerializer.Deserialize<NewCharClassesPacket>(line, Options),
                PacketNames.SendChars => JsonSerializer.Deserialize<SendCharsPacket>(line, Options),
                PacketNames.Welcome => JsonSerializer.Deserialize<WelcomePacket>(line, Options),
                PacketNames.PlayerInGame => JsonSerializer.Deserialize<PlayerInGamePacket>(line, Options),
                PacketNames.SendPlayerData => JsonSerializer.Deserialize<SendPlayerDataPacket>(line, Options),
                PacketNames.AggressorRefresh => JsonSerializer.Deserialize<AggressorRefreshPacket>(line, Options),
                PacketNames.LeftGame => JsonSerializer.Deserialize<LeftGamePacket>(line, Options),
                PacketNames.SendMap => JsonSerializer.Deserialize<SendMapPacket>(line, Options),
                PacketNames.JoinMap => JsonSerializer.Deserialize<JoinMapPacket>(line, Options),
                PacketNames.LeaveMap => JsonSerializer.Deserialize<LeaveMapPacket>(line, Options),
                PacketNames.PlayerXY => JsonSerializer.Deserialize<PlayerXYPacket>(line, Options),
                PacketNames.ChatMsg => JsonSerializer.Deserialize<ChatMsgPacket>(line, Options),
                PacketNames.ChatBubble => JsonSerializer.Deserialize<ChatBubblePacket>(line, Options),
                PacketNames.NpcChatBubble => JsonSerializer.Deserialize<NpcChatBubblePacket>(line, Options),
                PacketNames.SendItems => JsonSerializer.Deserialize<SendItemsPacket>(line, Options),
                PacketNames.UpdateItem => JsonSerializer.Deserialize<UpdateItemPacket>(line, Options),
                PacketNames.SendNpcs => JsonSerializer.Deserialize<SendNpcsPacket>(line, Options),
                PacketNames.SendMapGroups => JsonSerializer.Deserialize<SendMapGroupsPacket>(line, Options),
                PacketNames.UpdateNpc => JsonSerializer.Deserialize<UpdateNpcPacket>(line, Options),
                PacketNames.MapNpcs => JsonSerializer.Deserialize<MapNpcsPacket>(line, Options),
                PacketNames.SendInventory => JsonSerializer.Deserialize<SendInventoryPacket>(line, Options),
                PacketNames.InventoryUpdate => JsonSerializer.Deserialize<InventoryUpdatePacket>(line, Options),
                PacketNames.EquippedGear => JsonSerializer.Deserialize<EquippedGearPacket>(line, Options),
                PacketNames.MapItems => JsonSerializer.Deserialize<MapItemsPacket>(line, Options),
                PacketNames.SendHp => JsonSerializer.Deserialize<SendHpPacket>(line, Options),
                PacketNames.SendMp => JsonSerializer.Deserialize<SendMpPacket>(line, Options),
                PacketNames.SendSp => JsonSerializer.Deserialize<SendSpPacket>(line, Options),
                PacketNames.SendStats => JsonSerializer.Deserialize<SendStatsPacket>(line, Options),
                PacketNames.Weather => JsonSerializer.Deserialize<WeatherPacket>(line, Options),
                PacketNames.TimeOfDay => JsonSerializer.Deserialize<TimeOfDayPacket>(line, Options),
                PacketNames.PlayersOnline => JsonSerializer.Deserialize<PlayersOnlinePacket>(line, Options),
                PacketNames.PlayerAttack => JsonSerializer.Deserialize<PlayerAttackPacket>(line, Options),
                PacketNames.PlayerCast => JsonSerializer.Deserialize<PlayerCastPacket>(line, Options),
                PacketNames.PlayerDeath => JsonSerializer.Deserialize<PlayerDeathPacket>(line, Options),
                PacketNames.NpcAttack => JsonSerializer.Deserialize<NpcAttackPacket>(line, Options),
                PacketNames.NpcCast => JsonSerializer.Deserialize<NpcCastPacket>(line, Options),
                PacketNames.NpcDamage => JsonSerializer.Deserialize<NpcDamagePacket>(line, Options),
                PacketNames.CombatText => JsonSerializer.Deserialize<CombatTextPacket>(line, Options),
                PacketNames.BloodUpdate => JsonSerializer.Deserialize<BloodUpdatePacket>(line, Options),
                PacketNames.NpcSpawn => JsonSerializer.Deserialize<NpcSpawnPacket>(line, Options),
                PacketNames.NpcMove => JsonSerializer.Deserialize<NpcMovePacket>(line, Options),
                PacketNames.TraversalNpc => JsonSerializer.Deserialize<TraversalNpcPacket>(line, Options),
                PacketNames.NpcDespawn => JsonSerializer.Deserialize<NpcDespawnPacket>(line, Options),
                PacketNames.NpcDir => JsonSerializer.Deserialize<NpcDirPacket>(line, Options),
                PacketNames.SendShops => JsonSerializer.Deserialize<SendShopsPacket>(line, Options),
                PacketNames.ShopContents => JsonSerializer.Deserialize<ShopContentsPacket>(line, Options),
                PacketNames.OpenInn => JsonSerializer.Deserialize<OpenInnPacket>(line, Options),
                PacketNames.UpdateShop => JsonSerializer.Deserialize<UpdateShopPacket>(line, Options),
                PacketNames.UpdateQuest => JsonSerializer.Deserialize<UpdateQuestPacket>(line, Options),
                PacketNames.UpdateConversation => JsonSerializer.Deserialize<UpdateConversationPacket>(line, Options),
                PacketNames.SendSpells => JsonSerializer.Deserialize<SendSpellsPacket>(line, Options),
                PacketNames.UpdateSpell => JsonSerializer.Deserialize<UpdateSpellPacket>(line, Options),
                PacketNames.PlayerSpells => JsonSerializer.Deserialize<PlayerSpellsPacket>(line, Options),
                PacketNames.PlayerHotkeys => JsonSerializer.Deserialize<PlayerHotkeysPacket>(line, Options),
                PacketNames.PartyRequest => JsonSerializer.Deserialize<PartyRequestNotifyPacket>(line, Options),
                PacketNames.PartyVitals => JsonSerializer.Deserialize<PartyVitalsPacket>(line, Options),

                // World events
                PacketNames.CheckForMap => JsonSerializer.Deserialize<CheckForMapPacket>(line, Options),
                PacketNames.SeamlessCross => JsonSerializer.Deserialize<SeamlessCrossPacket>(line, Options),
                PacketNames.MapKey => JsonSerializer.Deserialize<MapKeyPacket>(line, Options),
                PacketNames.NpcDead => JsonSerializer.Deserialize<NpcDeadPacket>(line, Options),
                PacketNames.NpcTarget => JsonSerializer.Deserialize<NpcTargetPacket>(line, Options),
                PacketNames.SetTarget => JsonSerializer.Deserialize<SetTargetPacket>(line, Options),
                PacketNames.ClearTarget => JsonSerializer.Deserialize<ClearTargetPacket>(line, Options),
                // SendPlayerDir uses same cmd as PlayerDir; client handles by presence of "index" field
                // PacketNames.SendPlayerDir => handled as PlayerDirPacket on server (C→S), SendPlayerDirPacket on client

                // Editor
                PacketNames.EditorLogin => JsonSerializer.Deserialize<EditorLoginPacket>(line, Options),
                PacketNames.EditorRequestItem => JsonSerializer.Deserialize<EditorRequestItemPacket>(line, Options),
                PacketNames.EditorRequestNpc => JsonSerializer.Deserialize<EditorRequestNpcPacket>(line, Options),
                PacketNames.EditorRequestShop => JsonSerializer.Deserialize<EditorRequestShopPacket>(line, Options),
                PacketNames.EditorRequestQuest => JsonSerializer.Deserialize<EditorRequestQuestPacket>(line, Options),
                PacketNames.EditorRequestConversation => JsonSerializer.Deserialize<EditorRequestConversationPacket>(line, Options),
                PacketNames.EditorRequestSpell => JsonSerializer.Deserialize<EditorRequestSpellPacket>(line, Options),
                PacketNames.EditorRequestMap => JsonSerializer.Deserialize<EditorRequestMapPacket>(line, Options),
                PacketNames.EditorRequestClass => JsonSerializer.Deserialize<EditorRequestClassPacket>(line, Options),
                PacketNames.EditorLock => JsonSerializer.Deserialize<EditorLockPacket>(line, Options),
                PacketNames.EditorUnlock => JsonSerializer.Deserialize<EditorUnlockPacket>(line, Options),
                PacketNames.EditorLocks => JsonSerializer.Deserialize<EditorLocksPacket>(line, Options),
                PacketNames.EditorRequestAllItems => JsonSerializer.Deserialize<EditorRequestAllItemsPacket>(line, Options),
                PacketNames.EditorRequestAllNpcs => JsonSerializer.Deserialize<EditorRequestAllNpcsPacket>(line, Options),
                PacketNames.EditorRequestAllShops => JsonSerializer.Deserialize<EditorRequestAllShopsPacket>(line, Options),
                PacketNames.EditorRequestAllQuests => JsonSerializer.Deserialize<EditorRequestAllQuestsPacket>(line, Options),
                PacketNames.EditorRequestAllConversations => JsonSerializer.Deserialize<EditorRequestAllConversationsPacket>(line, Options),
                PacketNames.EditorRequestAllSpells => JsonSerializer.Deserialize<EditorRequestAllSpellsPacket>(line, Options),
                PacketNames.EditorRequestAllClasses => JsonSerializer.Deserialize<EditorRequestAllClassesPacket>(line, Options),
                PacketNames.EditorRequestMapGroup => JsonSerializer.Deserialize<EditorRequestMapGroupPacket>(line, Options),
                PacketNames.EditorRequestAllMapGroups => JsonSerializer.Deserialize<EditorRequestAllMapGroupsPacket>(line, Options),
                PacketNames.EditorRequestAllMaps => JsonSerializer.Deserialize<EditorRequestAllMapsPacket>(line, Options),
                PacketNames.EditorRequestAccounts => JsonSerializer.Deserialize<EditorRequestAccountsPacket>(line, Options),
                PacketNames.EditorAccountList => JsonSerializer.Deserialize<EditorAccountListPacket>(line, Options),
                PacketNames.EditorRequestAccount => JsonSerializer.Deserialize<EditorRequestAccountPacket>(line, Options),
                PacketNames.EditorAccount => JsonSerializer.Deserialize<EditorAccountPacket>(line, Options),
                PacketNames.EditorSaveAccount => JsonSerializer.Deserialize<EditorSaveAccountPacket>(line, Options),
                PacketNames.EditorRenameChar => JsonSerializer.Deserialize<EditorRenameCharPacket>(line, Options),
                PacketNames.EditorGiveItem => JsonSerializer.Deserialize<EditorGiveItemPacket>(line, Options),
                PacketNames.EditorTakeItem => JsonSerializer.Deserialize<EditorTakeItemPacket>(line, Options),
                PacketNames.EditorLearnSpell => JsonSerializer.Deserialize<EditorLearnSpellPacket>(line, Options),
                PacketNames.EditorForgetSpell => JsonSerializer.Deserialize<EditorForgetSpellPacket>(line, Options),
                PacketNames.EditorBankGive => JsonSerializer.Deserialize<EditorBankGivePacket>(line, Options),
                PacketNames.EditorBankTake => JsonSerializer.Deserialize<EditorBankTakePacket>(line, Options),
                PacketNames.EditorSetQuestStatus => JsonSerializer.Deserialize<EditorSetQuestStatusPacket>(line, Options),
                PacketNames.EditorNotice => JsonSerializer.Deserialize<EditorNoticePacket>(line, Options),
                PacketNames.EditorSaveMapGroup => JsonSerializer.Deserialize<EditorSaveMapGroupPacket>(line, Options),
                PacketNames.EditorSaveClass => JsonSerializer.Deserialize<EditorSaveClassPacket>(line, Options),
                PacketNames.UpdateClass => JsonSerializer.Deserialize<UpdateClassPacket>(line, Options),
                PacketNames.EditorSaveItem => JsonSerializer.Deserialize<EditorSaveItemPacket>(line, Options),
                PacketNames.EditorSaveNpc => JsonSerializer.Deserialize<EditorSaveNpcPacket>(line, Options),
                PacketNames.EditorSaveShop => JsonSerializer.Deserialize<EditorSaveShopPacket>(line, Options),
                PacketNames.EditorSaveQuest => JsonSerializer.Deserialize<EditorSaveQuestPacket>(line, Options),
                PacketNames.EditorSaveConversation => JsonSerializer.Deserialize<EditorSaveConversationPacket>(line, Options),
                PacketNames.EditorSaveSpell => JsonSerializer.Deserialize<EditorSaveSpellPacket>(line, Options),
                PacketNames.EditorSaveMap => JsonSerializer.Deserialize<EditorSaveMapPacket>(line, Options),
                PacketNames.EditorLoginResponse => JsonSerializer.Deserialize<EditorLoginResponsePacket>(line, Options),
                PacketNames.EditorData => JsonSerializer.Deserialize<EditorDataPacket>(line, Options),
                PacketNames.EditorAllItems => JsonSerializer.Deserialize<EditorAllItemsPacket>(line, Options),
                PacketNames.EditorAllNpcs => JsonSerializer.Deserialize<EditorAllNpcsPacket>(line, Options),
                PacketNames.EditorAllShops => JsonSerializer.Deserialize<EditorAllShopsPacket>(line, Options),
                PacketNames.EditorAllQuests => JsonSerializer.Deserialize<EditorAllQuestsPacket>(line, Options),
                PacketNames.EditorAllConversations => JsonSerializer.Deserialize<EditorAllConversationsPacket>(line, Options),
                PacketNames.EditorAllSpells => JsonSerializer.Deserialize<EditorAllSpellsPacket>(line, Options),
                PacketNames.EditorAllClasses => JsonSerializer.Deserialize<EditorAllClassesPacket>(line, Options),
                PacketNames.UpdateMapGroup => JsonSerializer.Deserialize<UpdateMapGroupPacket>(line, Options),
                PacketNames.EditorAllMapGroups => JsonSerializer.Deserialize<EditorAllMapGroupsPacket>(line, Options),
                PacketNames.EditorAllMaps => JsonSerializer.Deserialize<EditorAllMapsPacket>(line, Options),

                _ => null,
            };
        }
        catch { return null; }
    }
}
