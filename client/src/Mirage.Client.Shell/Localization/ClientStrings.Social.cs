using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>The social panel (friends, guild roster, vault), the death overlay, guild labels,
/// and mail.</summary>
public static partial class ClientStrings
{
    // ── SocialPanel ───────────────────────────────────────────────────────────
    public const string SocialPanel_Title = nameof(SocialPanel_Title);
    public const string SocialPanel_FriendsTab = nameof(SocialPanel_FriendsTab);
    public const string SocialPanel_IgnoreTab = nameof(SocialPanel_IgnoreTab);
    public const string SocialPanel_GuildTab = nameof(SocialPanel_GuildTab);
    public const string SocialPanel_NoFriends = nameof(SocialPanel_NoFriends);
    public const string SocialPanel_NoIgnored = nameof(SocialPanel_NoIgnored);
    public const string SocialPanel_RemoveButton = nameof(SocialPanel_RemoveButton);
    public const string SocialPanel_LeaveButton = nameof(SocialPanel_LeaveButton);
    public const string SocialPanel_KickButton = nameof(SocialPanel_KickButton);
    public const string SocialPanel_PromoteButton = nameof(SocialPanel_PromoteButton);
    public const string SocialPanel_DemoteButton = nameof(SocialPanel_DemoteButton);
    public const string SocialPanel_DisbandButton = nameof(SocialPanel_DisbandButton);
    // A row's character column, shown only while that account is online.
    public const string SocialPanel_OnlineFormat = nameof(SocialPanel_OnlineFormat);
    public const string SocialPanel_Offline = nameof(SocialPanel_Offline);
    public const string SocialPanel_Online = nameof(SocialPanel_Online);
    // Guild second-level sub-tabs.
    public const string SocialPanel_SubTabMain = nameof(SocialPanel_SubTabMain);
    public const string SocialPanel_SubTabRoster = nameof(SocialPanel_SubTabRoster);
    public const string SocialPanel_SubTabVault = nameof(SocialPanel_SubTabVault);
    // Roster table column headers.
    public const string SocialPanel_ColRank = nameof(SocialPanel_ColRank);
    public const string SocialPanel_ColAccount = nameof(SocialPanel_ColAccount);
    public const string SocialPanel_ColCharacter = nameof(SocialPanel_ColCharacter);
    public const string SocialPanel_ColLastSeen = nameof(SocialPanel_ColLastSeen);
    // Vault page header.
    public const string SocialPanel_VaultHeader = nameof(SocialPanel_VaultHeader);
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
    public const string SocialPanel_CreateCostFormat = nameof(SocialPanel_CreateCostFormat);
    public const string SocialPanel_CreateNamePrompt = nameof(SocialPanel_CreateNamePrompt);
    // Discovery — open toggle, browser, applications.
    public const string SocialPanel_OpenOn = nameof(SocialPanel_OpenOn);
    public const string SocialPanel_OpenOff = nameof(SocialPanel_OpenOff);
    // Leader toggle for showing a member's rank in the overhead cluster.
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
    // Vault sub-view.
    public const string SocialPanel_VaultFormat = nameof(SocialPanel_VaultFormat);
    // Weekly financial-health dashboard on the vault page (discrete per-type running totals).
    public const string SocialPanel_WeeklyHeader = nameof(SocialPanel_WeeklyHeader);
    public const string SocialPanel_WeeklyIncomeFormat = nameof(SocialPanel_WeeklyIncomeFormat);
    public const string SocialPanel_WeeklyDonationsFormat = nameof(SocialPanel_WeeklyDonationsFormat);
    // Vault donor log (recent donations + the donor account).
    public const string SocialPanel_DonorLogEmpty = nameof(SocialPanel_DonorLogEmpty);
    public const string SocialPanel_DonorRowGold = nameof(SocialPanel_DonorRowGold);
    public const string SocialPanel_DonationsTab = nameof(SocialPanel_DonationsTab);
    public const string SocialPanel_SpendingTab = nameof(SocialPanel_SpendingTab);
    public const string SocialPanel_SpendingLogEmpty = nameof(SocialPanel_SpendingLogEmpty);
    public const string SocialPanel_SpendingRow = nameof(SocialPanel_SpendingRow);
    public const string SocialPanel_DonateButton = nameof(SocialPanel_DonateButton);
    public const string SocialPanel_DonatePrompt = nameof(SocialPanel_DonatePrompt);

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
