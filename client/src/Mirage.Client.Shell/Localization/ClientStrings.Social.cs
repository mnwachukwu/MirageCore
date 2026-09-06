using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>The social panel (friends, guild roster, vault, wars, territory), the death overlay,
/// guild labels, and mail.</summary>
public static partial class ClientStrings
{
    // ── SocialPanel ───────────────────────────────────────────────────────────
    public const string SocialPanel_Title = nameof(SocialPanel_Title);
    public const string SocialPanel_FriendsTab = nameof(SocialPanel_FriendsTab);
    public const string SocialPanel_IgnoreTab = nameof(SocialPanel_IgnoreTab);
    public const string SocialPanel_GuildTab = nameof(SocialPanel_GuildTab);
    public const string SocialPanel_NoFriends = nameof(SocialPanel_NoFriends);
    public const string SocialPanel_NoIgnored = nameof(SocialPanel_NoIgnored);
    public const string SocialPanel_NoGuild = nameof(SocialPanel_NoGuild);
    public const string SocialPanel_RemoveButton = nameof(SocialPanel_RemoveButton);
    public const string SocialPanel_LeaveButton = nameof(SocialPanel_LeaveButton);
    public const string SocialPanel_KickButton = nameof(SocialPanel_KickButton);
    public const string SocialPanel_PromoteButton = nameof(SocialPanel_PromoteButton);
    public const string SocialPanel_DemoteButton = nameof(SocialPanel_DemoteButton);
    public const string SocialPanel_DisbandButton = nameof(SocialPanel_DisbandButton);
    // A row's character columns, shown only while that account is online; {Char} + {Level}.
    public const string SocialPanel_OnlineFormat = nameof(SocialPanel_OnlineFormat);
    public const string SocialPanel_Offline = nameof(SocialPanel_Offline);
    public const string SocialPanel_Online = nameof(SocialPanel_Online);
    // Guild second-level sub-tabs.
    public const string SocialPanel_SubTabMain = nameof(SocialPanel_SubTabMain);
    public const string SocialPanel_SubTabRoster = nameof(SocialPanel_SubTabRoster);
    public const string SocialPanel_SubTabVault = nameof(SocialPanel_SubTabVault);
    public const string SocialPanel_SubTabQuests = nameof(SocialPanel_SubTabQuests);
    public const string SocialPanel_SubTabWars = nameof(SocialPanel_SubTabWars);
    public const string SocialPanel_SubTabTerritories = nameof(SocialPanel_SubTabTerritories);
    public const string SocialPanel_SubTabStandings = nameof(SocialPanel_SubTabStandings);
    // Seasonal leaderboard (Standings sub-tab).
    public const string SocialPanel_SeasonHeader = nameof(SocialPanel_SeasonHeader);
    public const string SocialPanel_History = nameof(SocialPanel_History);
    public const string SocialPanel_Current = nameof(SocialPanel_Current);
    public const string SocialPanel_ColPlacing = nameof(SocialPanel_ColPlacing);
    public const string SocialPanel_ArchiveHeader = nameof(SocialPanel_ArchiveHeader);
    public const string SocialPanel_NoArchive = nameof(SocialPanel_NoArchive);
    public const string SocialPanel_ColGuild = nameof(SocialPanel_ColGuild);
    public const string SocialPanel_ColScore = nameof(SocialPanel_ColScore);
    public const string SocialPanel_ColKD = nameof(SocialPanel_ColKD);
    public const string SocialPanel_ColSize = nameof(SocialPanel_ColSize);
    // Roster table column headers.
    public const string SocialPanel_ColRank = nameof(SocialPanel_ColRank);
    public const string SocialPanel_ColAccount = nameof(SocialPanel_ColAccount);
    public const string SocialPanel_ColCharacter = nameof(SocialPanel_ColCharacter);
    public const string SocialPanel_ColClass = nameof(SocialPanel_ColClass);
    public const string SocialPanel_ColLevel = nameof(SocialPanel_ColLevel);
    public const string SocialPanel_ColLastSeen = nameof(SocialPanel_ColLastSeen);
    // Territories table column headers + the unclaimed-owner placeholder.
    public const string SocialPanel_ColTerritory = nameof(SocialPanel_ColTerritory);
    public const string SocialPanel_ColOwner = nameof(SocialPanel_ColOwner);
    public const string SocialPanel_ColWeeksHeld = nameof(SocialPanel_ColWeeksHeld);
    public const string SocialPanel_ColPrevIncome = nameof(SocialPanel_ColPrevIncome);
    public const string SocialPanel_ColPending = nameof(SocialPanel_ColPending);
    public const string SocialPanel_ColIncomeWeek = nameof(SocialPanel_ColIncomeWeek);
    public const string SocialPanel_ColContesting = nameof(SocialPanel_ColContesting);
    public const string SocialPanel_Unclaimed = nameof(SocialPanel_Unclaimed);
    // Territory war-night challenge actions.
    public const string SocialPanel_ChallengeButton = nameof(SocialPanel_ChallengeButton);
    public const string SocialPanel_ChallengeConfirmFormat = nameof(SocialPanel_ChallengeConfirmFormat);
    public const string SocialPanel_WithdrawChallengeButton = nameof(SocialPanel_WithdrawChallengeButton);
    // Main page level-progress bar; {Cur} + {Max} + {Next}. Max shows a plain "Max Level".
    public const string SocialPanel_LevelProgressFormat = nameof(SocialPanel_LevelProgressFormat);
    public const string SocialPanel_LevelMax = nameof(SocialPanel_LevelMax);
    // Vault page header (the vault and quests are separate pages).
    public const string SocialPanel_VaultHeader = nameof(SocialPanel_VaultHeader);
    // Guild tab header; {Name} + {Level}.
    public const string SocialPanel_GuildHeaderFormat = nameof(SocialPanel_GuildHeaderFormat);
    public const string SocialPanel_RankLeader = nameof(SocialPanel_RankLeader);
    public const string SocialPanel_RankOfficer = nameof(SocialPanel_RankOfficer);
    public const string SocialPanel_RankMember = nameof(SocialPanel_RankMember);
    public const string SocialPanel_TransferButton = nameof(SocialPanel_TransferButton);
    public const string SocialPanel_MotdButton = nameof(SocialPanel_MotdButton);
    public const string SocialPanel_LabelsButton = nameof(SocialPanel_LabelsButton);
    public const string SocialPanel_ColorButton = nameof(SocialPanel_ColorButton);
    public const string SocialPanel_ColorPrompt = nameof(SocialPanel_ColorPrompt);
    public const string SocialPanel_ColorReserved = nameof(SocialPanel_ColorReserved);
    public const string SocialPanel_SaveButton = nameof(SocialPanel_SaveButton);
    public const string SocialPanel_MotdPrompt = nameof(SocialPanel_MotdPrompt);
    public const string SocialPanel_LabelsHeader = nameof(SocialPanel_LabelsHeader);
    // Guildless create-guild on-ramp.
    public const string SocialPanel_CreateHeader = nameof(SocialPanel_CreateHeader);
    public const string SocialPanel_CreateCostFormat = nameof(SocialPanel_CreateCostFormat);
    public const string SocialPanel_CreateNamePrompt = nameof(SocialPanel_CreateNamePrompt);
    // Discovery — open toggle, browser, applications.
    public const string SocialPanel_OpenOn = nameof(SocialPanel_OpenOn);
    public const string SocialPanel_OpenOff = nameof(SocialPanel_OpenOff);
    // Leader toggle for showing the guild's seasonal standing "(N)" in the overhead cluster.
    public const string SocialPanel_StandingOn = nameof(SocialPanel_StandingOn);
    public const string SocialPanel_StandingOff = nameof(SocialPanel_StandingOff);
    public const string SocialPanel_AppsFormat = nameof(SocialPanel_AppsFormat);
    public const string SocialPanel_ApplyButton = nameof(SocialPanel_ApplyButton);
    public const string SocialPanel_ApproveButton = nameof(SocialPanel_ApproveButton);
    public const string SocialPanel_RejectButton = nameof(SocialPanel_RejectButton);
    public const string SocialPanel_BrowseHeader = nameof(SocialPanel_BrowseHeader);
    public const string SocialPanel_AppsHeader = nameof(SocialPanel_AppsHeader);
    public const string SocialPanel_NoOpenGuilds = nameof(SocialPanel_NoOpenGuilds);
    public const string SocialPanel_NoApplications = nameof(SocialPanel_NoApplications);
    public const string SocialPanel_BrowseRowFormat = nameof(SocialPanel_BrowseRowFormat);
    // Vault & quests sub-view.
    public const string SocialPanel_QuestsButton = nameof(SocialPanel_QuestsButton);
    public const string SocialPanel_QuestsHeader = nameof(SocialPanel_QuestsHeader);
    public const string SocialPanel_VaultFormat = nameof(SocialPanel_VaultFormat);
    public const string SocialPanel_VaultValorFormat = nameof(SocialPanel_VaultValorFormat);
    public const string SocialPanel_VaultPendingFormat = nameof(SocialPanel_VaultPendingFormat);
    public const string SocialPanel_PerksSuspended = nameof(SocialPanel_PerksSuspended);
    // Weekly financial-health dashboard on the vault page (discrete per-type running totals) + war-tab daily cost.
    public const string SocialPanel_WeeklyHeader = nameof(SocialPanel_WeeklyHeader);
    public const string SocialPanel_WeeklyIncomeFormat = nameof(SocialPanel_WeeklyIncomeFormat);
    public const string SocialPanel_WeeklyTaxFormat = nameof(SocialPanel_WeeklyTaxFormat);
    public const string SocialPanel_WeeklyTaxValorFormat = nameof(SocialPanel_WeeklyTaxValorFormat);
    public const string SocialPanel_WeeklyDonationsFormat = nameof(SocialPanel_WeeklyDonationsFormat);
    public const string SocialPanel_WeeklyWarCostsFormat = nameof(SocialPanel_WeeklyWarCostsFormat);
    // Vault donor log (recent gold/valor donations + the donor account).
    public const string SocialPanel_DonorLogHeader = nameof(SocialPanel_DonorLogHeader);
    public const string SocialPanel_DonorLogEmpty = nameof(SocialPanel_DonorLogEmpty);
    public const string SocialPanel_DonorRowGold = nameof(SocialPanel_DonorRowGold);
    public const string SocialPanel_DonorRowValor = nameof(SocialPanel_DonorRowValor);
    public const string SocialPanel_DonationsTab = nameof(SocialPanel_DonationsTab);
    public const string SocialPanel_SpendingTab = nameof(SocialPanel_SpendingTab);
    public const string SocialPanel_SpendingLogEmpty = nameof(SocialPanel_SpendingLogEmpty);
    public const string SocialPanel_SpendingRow = nameof(SocialPanel_SpendingRow);
    public const string SocialPanel_WarDailyCostFormat = nameof(SocialPanel_WarDailyCostFormat);
    public const string SocialPanel_DonateButton = nameof(SocialPanel_DonateButton);
    public const string SocialPanel_DonatePrompt = nameof(SocialPanel_DonatePrompt);
    public const string SocialPanel_DonateValorButton = nameof(SocialPanel_DonateValorButton);
    public const string SocialPanel_DonateValorPrompt = nameof(SocialPanel_DonateValorPrompt);
    public const string SocialPanel_PayTaxButton = nameof(SocialPanel_PayTaxButton);
    public const string SocialPanel_QuestNone = nameof(SocialPanel_QuestNone);
    public const string SocialPanel_QuestObjectiveFormat = nameof(SocialPanel_QuestObjectiveFormat);
    public const string SocialPanel_QuestRewardFormat = nameof(SocialPanel_QuestRewardFormat);
    public const string SocialPanel_QuestRewardGoldOnlyFormat = nameof(SocialPanel_QuestRewardGoldOnlyFormat);
    public const string SocialPanel_QuestTimeFormat = nameof(SocialPanel_QuestTimeFormat);
    public const string SocialPanel_QuestAcquireButton = nameof(SocialPanel_QuestAcquireButton);
    public const string SocialPanel_QuestAbandonButton = nameof(SocialPanel_QuestAbandonButton);
    // Quest confirmations: acquire shows the cost ({Cost}); abandon warns progress + gold are lost.
    public const string SocialPanel_QuestAcquireConfirmFormat = nameof(SocialPanel_QuestAcquireConfirmFormat);
    public const string SocialPanel_QuestAbandonConfirm = nameof(SocialPanel_QuestAbandonConfirm);
    // War sub-view.
    public const string SocialPanel_WarsButton = nameof(SocialPanel_WarsButton);
    public const string SocialPanel_WarsHeader = nameof(SocialPanel_WarsHeader);
    public const string SocialPanel_NoWars = nameof(SocialPanel_NoWars);
    // War list row; {Name} + {Status}.
    public const string SocialPanel_WarRowFormat = nameof(SocialPanel_WarRowFormat);
    public const string SocialPanel_WarStatusWarmup = nameof(SocialPanel_WarStatusWarmup);
    public const string SocialPanel_WarStatusAggressor = nameof(SocialPanel_WarStatusAggressor);
    public const string SocialPanel_WarStatusDefender = nameof(SocialPanel_WarStatusDefender);
    public const string SocialPanel_WarStatusMutual = nameof(SocialPanel_WarStatusMutual);
    // Selected-war status area.
    public const string SocialPanel_WarHeaderFormat = nameof(SocialPanel_WarHeaderFormat);   // {Name}
    public const string SocialPanel_WarWarmupFormat = nameof(SocialPanel_WarWarmupFormat);   // {Mins}+{Secs}
    public const string SocialPanel_WarRetractLockFormat = nameof(SocialPanel_WarRetractLockFormat);
    public const string SocialPanel_WarRetractReady = nameof(SocialPanel_WarRetractReady);
    public const string SocialPanel_WarSelfLimiting = nameof(SocialPanel_WarSelfLimiting);
    public const string SocialPanel_WarDefenderNote = nameof(SocialPanel_WarDefenderNote);
    public const string SocialPanel_WarScoreFormat = nameof(SocialPanel_WarScoreFormat);     // {Ours}+{Theirs}
    public const string SocialPanel_WarFavorUs = nameof(SocialPanel_WarFavorUs);
    public const string SocialPanel_WarFavorThem = nameof(SocialPanel_WarFavorThem);
    public const string SocialPanel_WarFavorEven = nameof(SocialPanel_WarFavorEven);
    public const string SocialPanel_WarTrendFormat = nameof(SocialPanel_WarTrendFormat);     // {Favor}+{Dir}
    public const string SocialPanel_WarTrendUp = nameof(SocialPanel_WarTrendUp);
    public const string SocialPanel_WarTrendDown = nameof(SocialPanel_WarTrendDown);
    public const string SocialPanel_WarTrendFlat = nameof(SocialPanel_WarTrendFlat);
    public const string SocialPanel_WarPeaceIncoming = nameof(SocialPanel_WarPeaceIncoming);
    public const string SocialPanel_WarPeaceOutgoing = nameof(SocialPanel_WarPeaceOutgoing);
    public const string SocialPanel_WarPeaceIncomingGold = nameof(SocialPanel_WarPeaceIncomingGold);   // {Gold}
    public const string SocialPanel_WarPeaceOutgoingGold = nameof(SocialPanel_WarPeaceOutgoingGold);   // {Gold}
    // War action buttons + prompts.
    public const string SocialPanel_WarDeclareButton = nameof(SocialPanel_WarDeclareButton);
    public const string SocialPanel_WarDeclarePrompt = nameof(SocialPanel_WarDeclarePrompt);
    public const string SocialPanel_WarRetractButton = nameof(SocialPanel_WarRetractButton);
    public const string SocialPanel_WarPeaceButton = nameof(SocialPanel_WarPeaceButton);
    public const string SocialPanel_WarWithdrawButton = nameof(SocialPanel_WarWithdrawButton);
    public const string SocialPanel_WarAcceptButton = nameof(SocialPanel_WarAcceptButton);
    public const string SocialPanel_WarRejectButton = nameof(SocialPanel_WarRejectButton);
    public const string SocialPanel_WarPeaceOfferPrompt = nameof(SocialPanel_WarPeaceOfferPrompt);
    // Wager row: buttons, the ante prompt, and the status lines.
    public const string SocialPanel_WarWagerButton = nameof(SocialPanel_WarWagerButton);
    public const string SocialPanel_WarWagerWithdrawButton = nameof(SocialPanel_WarWagerWithdrawButton);
    public const string SocialPanel_WarWagerAcceptButton = nameof(SocialPanel_WarWagerAcceptButton);
    public const string SocialPanel_WarWagerRejectButton = nameof(SocialPanel_WarWagerRejectButton);
    public const string SocialPanel_WarWagerPrompt = nameof(SocialPanel_WarWagerPrompt);
    public const string SocialPanel_WarAnteFormat = nameof(SocialPanel_WarAnteFormat);               // {Ante}+{Pot}
    public const string SocialPanel_WarWagerIncomingFormat = nameof(SocialPanel_WarWagerIncomingFormat); // {Gold}
    public const string SocialPanel_WarWagerOutgoingFormat = nameof(SocialPanel_WarWagerOutgoingFormat); // {Gold}
    public const string SocialPanel_WarWagerWindowFormat = nameof(SocialPanel_WarWagerWindowFormat);  // {Mins}+{Secs}
    public const string SocialPanel_WarReqsFormat = nameof(SocialPanel_WarReqsFormat);       // {Count}
    // War-requests review overlay.
    public const string SocialPanel_WarReqsHeader = nameof(SocialPanel_WarReqsHeader);
    public const string SocialPanel_NoWarReqs = nameof(SocialPanel_NoWarReqs);
    public const string SocialPanel_WarReqRowFormat = nameof(SocialPanel_WarReqRowFormat);   // {By}+{Kind}+{Target}
    public const string SocialPanel_WarReqKindDeclare = nameof(SocialPanel_WarReqKindDeclare);
    public const string SocialPanel_WarReqKindRetract = nameof(SocialPanel_WarReqKindRetract);
    public const string SocialPanel_WarReqKindPeace = nameof(SocialPanel_WarReqKindPeace);
    public const string SocialPanel_WarReqAccept = nameof(SocialPanel_WarReqAccept);
    public const string SocialPanel_WarReqDeny = nameof(SocialPanel_WarReqDeny);

    // ── Death & respawn panel ─────────────────────────────────────────────────
    public const string DeathPanel_Title = nameof(DeathPanel_Title);
    public const string DeathPanel_Respawn = nameof(DeathPanel_Respawn);

    // ── Guild labels (the leader-picked descriptive tags) ─────────────────────
    public const string GuildLabel_Pvp = nameof(GuildLabel_Pvp);
    public const string GuildLabel_Pve = nameof(GuildLabel_Pve);
    public const string GuildLabel_Leveling = nameof(GuildLabel_Leveling);
    public const string GuildLabel_CasualSocial = nameof(GuildLabel_CasualSocial);
    public const string GuildLabel_Hardcore = nameof(GuildLabel_Hardcore);
    public const string GuildLabel_OrganizedWars = nameof(GuildLabel_OrganizedWars);
    public const string GuildLabel_ItemFarming = nameof(GuildLabel_ItemFarming);
    public const string GuildLabel_NewbieFocused = nameof(GuildLabel_NewbieFocused);
    public const string GuildLabel_VeteranFocused = nameof(GuildLabel_VeteranFocused);

    // ── MailPanel ─────────────────────────────────────────────────────────────
    public const string MailPanel_Title = nameof(MailPanel_Title);
    public const string MailPanel_Empty = nameof(MailPanel_Empty);
    public const string MailPanel_NoSelection = nameof(MailPanel_NoSelection);
    // Reading-pane meta line under the subject; {Sender} + {Time} placeholders.
    public const string MailPanel_MetaFormat = nameof(MailPanel_MetaFormat);
    public const string MailPanel_Claim = nameof(MailPanel_Claim);
    public const string MailPanel_Reply = nameof(MailPanel_Reply);
    public const string MailPanel_ReplyPrefix = nameof(MailPanel_ReplyPrefix);
    public const string MailPanel_AttachmentsHeader = nameof(MailPanel_AttachmentsHeader);
    public const string MailPanel_ColSender = nameof(MailPanel_ColSender);
    public const string MailPanel_ColDate = nameof(MailPanel_ColDate);
    public const string MailPanel_ColSubject = nameof(MailPanel_ColSubject);
    public const string MailPanel_ColRecipient = nameof(MailPanel_ColRecipient);
    // Sent/outbox view + in-transit status.
    public const string MailPanel_MetaFormatSent = nameof(MailPanel_MetaFormatSent);
    public const string MailPanel_InTransit = nameof(MailPanel_InTransit);
    public const string MailPanel_EmptySent = nameof(MailPanel_EmptySent);
    public const string MailPanel_TabInbox = nameof(MailPanel_TabInbox);
    public const string MailPanel_TabOutbox = nameof(MailPanel_TabOutbox);
    // Compose sub-view (player-to-player send).
    public const string MailPanel_Compose = nameof(MailPanel_Compose);
    public const string MailPanel_MultiHint = nameof(MailPanel_MultiHint);
    public const string MailPanel_MultiNoAttachWarn = nameof(MailPanel_MultiNoAttachWarn);
    public const string MailPanel_BlankRecipientWarn = nameof(MailPanel_BlankRecipientWarn);
    public const string MailPanel_TooManyRecipientsWarn = nameof(MailPanel_TooManyRecipientsWarn);
    public const string MailPanel_InvalidRecipientWarn = nameof(MailPanel_InvalidRecipientWarn);
    public const string MailPanel_CharCount = nameof(MailPanel_CharCount);
    public const string MailPanel_EstDelivery = nameof(MailPanel_EstDelivery);
    public const string MailPanel_CannotAffordWarn = nameof(MailPanel_CannotAffordWarn);
    public const string MailPanel_CodPriceLabel = nameof(MailPanel_CodPriceLabel);
    public const string MailPanel_CodNet = nameof(MailPanel_CodNet);
    public const string MailPanel_CodNeedsItemWarn = nameof(MailPanel_CodNeedsItemWarn);
    public const string MailPanel_CodSingleOnlyWarn = nameof(MailPanel_CodSingleOnlyWarn);
    public const string MailPanel_PayCod = nameof(MailPanel_PayCod);
    public const string MailPanel_CodLocked = nameof(MailPanel_CodLocked);
    public const string MailPanel_CodCannotAfford = nameof(MailPanel_CodCannotAfford);
    public const string MailPanel_ReturnsLine = nameof(MailPanel_ReturnsLine);
    public const string MailPanel_CodOutbox = nameof(MailPanel_CodOutbox);
    public const string MailPanel_CostToSend = nameof(MailPanel_CostToSend);
    public const string MailPanel_NoSubjectWarn = nameof(MailPanel_NoSubjectWarn);
    public const string MailPanel_DeletesLine = nameof(MailPanel_DeletesLine);
    public const string MailPanel_CountdownDay = nameof(MailPanel_CountdownDay);
    public const string MailPanel_CountdownDays = nameof(MailPanel_CountdownDays);
    public const string MailPanel_CountdownHour = nameof(MailPanel_CountdownHour);
    public const string MailPanel_CountdownHours = nameof(MailPanel_CountdownHours);
    public const string MailPanel_CountdownMinute = nameof(MailPanel_CountdownMinute);
    public const string MailPanel_CountdownMinutes = nameof(MailPanel_CountdownMinutes);
    public const string MailPanel_To = nameof(MailPanel_To);
    public const string MailPanel_Subject = nameof(MailPanel_Subject);
    public const string MailPanel_Body = nameof(MailPanel_Body);
    public const string MailPanel_Attach = nameof(MailPanel_Attach);
    public const string MailPanel_Unstage = nameof(MailPanel_Unstage);
    public const string MailPanel_Send = nameof(MailPanel_Send);
    public const string MailPanel_AttachHeader = nameof(MailPanel_AttachHeader);
    // Panel title when there is unread mail; {Count} placeholder.
    public const string MailPanel_TitleUnreadFormat = nameof(MailPanel_TitleUnreadFormat);
}
