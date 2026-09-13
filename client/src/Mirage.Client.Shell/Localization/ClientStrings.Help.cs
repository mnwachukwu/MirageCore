using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>The shared help text, the right-click context menu, quest dialogs, NPC conversation
/// UI, and the party overlay.</summary>
public static partial class ClientStrings
{
    // ── Help text (shared: HelpPanel.Populate and ChatPanel /help output) ─────
    public const string HelpText_SocialHeader = nameof(HelpText_SocialHeader);
    public const string HelpText_EnterKey = nameof(HelpText_EnterKey);
    public const string HelpText_Say = nameof(HelpText_Say);
    public const string HelpText_Yell = nameof(HelpText_Yell);
    public const string HelpText_Tell = nameof(HelpText_Tell);
    public const string HelpText_Emote = nameof(HelpText_Emote);
    public const string HelpText_Broadcast = nameof(HelpText_Broadcast);
    public const string HelpText_GuildChat = nameof(HelpText_GuildChat);
    public const string HelpText_OfficerChat = nameof(HelpText_OfficerChat);
    // Admin-only social channels (shown in their own "Admin Social Commands" section).
    public const string HelpText_AdminSocialHeader = nameof(HelpText_AdminSocialHeader);
    public const string HelpText_AdminNotice = nameof(HelpText_AdminNotice);
    public const string HelpText_AdminMsg = nameof(HelpText_AdminMsg);
    // Chat-tab how-to section.
    public const string HelpText_TabsHeader = nameof(HelpText_TabsHeader);
    public const string HelpText_TabAdd = nameof(HelpText_TabAdd);
    public const string HelpText_TabRemove = nameof(HelpText_TabRemove);
    public const string HelpText_TabConfig = nameof(HelpText_TabConfig);
    public const string HelpText_PlayerCommandsHeader = nameof(HelpText_PlayerCommandsHeader);
    public const string HelpText_AdminCommandsHeader = nameof(HelpText_AdminCommandsHeader);
    // Per-command help lines, filtered by viewer's AdminLevel in HelpPanel.Populate.
    public const string HelpText_Cmd_Help = nameof(HelpText_Cmd_Help);
    public const string HelpText_Cmd_Info = nameof(HelpText_Cmd_Info);
    public const string HelpText_Cmd_Played = nameof(HelpText_Cmd_Played);
    public const string HelpText_Cmd_Home = nameof(HelpText_Cmd_Home);
    public const string HelpText_Cmd_HomeCd = nameof(HelpText_Cmd_HomeCd);
    public const string HelpText_Cmd_Who = nameof(HelpText_Cmd_Who);
    public const string HelpText_Cmd_Fps = nameof(HelpText_Cmd_Fps);
    public const string HelpText_Cmd_Inv = nameof(HelpText_Cmd_Inv);
    public const string HelpText_Cmd_Join = nameof(HelpText_Cmd_Join);
    public const string HelpText_Cmd_Leave = nameof(HelpText_Cmd_Leave);
    public const string HelpText_Cmd_Trade = nameof(HelpText_Cmd_Trade);
    public const string HelpText_Cmd_Roll = nameof(HelpText_Cmd_Roll);
    public const string HelpText_Cmd_Reply = nameof(HelpText_Cmd_Reply);
    public const string HelpText_Cmd_Kick = nameof(HelpText_Cmd_Kick);
    public const string HelpText_Cmd_Ban = nameof(HelpText_Cmd_Ban);
    public const string HelpText_Cmd_HwBan = nameof(HelpText_Cmd_HwBan);
    public const string HelpText_Cmd_HwUnban = nameof(HelpText_Cmd_HwUnban);
    public const string HelpText_Cmd_Mute = nameof(HelpText_Cmd_Mute);
    public const string HelpText_Cmd_Loc = nameof(HelpText_Cmd_Loc);
    public const string HelpText_Cmd_Debug = nameof(HelpText_Cmd_Debug);
    public const string HelpText_Cmd_WarpTo = nameof(HelpText_Cmd_WarpTo);
    public const string HelpText_Cmd_SetSprite = nameof(HelpText_Cmd_SetSprite);
    public const string HelpText_Cmd_MapReport = nameof(HelpText_Cmd_MapReport);
    public const string HelpText_Cmd_Respawn = nameof(HelpText_Cmd_Respawn);
    public const string HelpText_Cmd_Motd = nameof(HelpText_Cmd_Motd);
    public const string HelpText_Cmd_RefreshBanList = nameof(HelpText_Cmd_RefreshBanList);
    public const string HelpText_Cmd_GodMode = nameof(HelpText_Cmd_GodMode);
    public const string HelpText_Cmd_WarpMeTo = nameof(HelpText_Cmd_WarpMeTo);
    public const string HelpText_Cmd_WarpToMe = nameof(HelpText_Cmd_WarpToMe);
    public const string HelpText_Cmd_Tod = nameof(HelpText_Cmd_Tod);
    public const string HelpText_Cmd_Weather = nameof(HelpText_Cmd_Weather);
    public const string HelpText_Cmd_SetAccess = nameof(HelpText_Cmd_SetAccess);
    public const string HelpText_Cmd_StartWar = nameof(HelpText_Cmd_StartWar);
    public const string HelpText_Cmd_AdvanceWar = nameof(HelpText_Cmd_AdvanceWar);
    public const string HelpText_Cmd_EndWar = nameof(HelpText_Cmd_EndWar);
    public const string HelpText_Cmd_GuildReset = nameof(HelpText_Cmd_GuildReset);

    // ── Context menu (right-click on player) ────────────────────────────────
    public const string ContextMenu_Info = nameof(ContextMenu_Info);
    public const string ContextMenu_PartyInvite = nameof(ContextMenu_PartyInvite);
    public const string ContextMenu_Whisper = nameof(ContextMenu_Whisper);
    public const string ContextMenu_Shop = nameof(ContextMenu_Shop);
    public const string ContextMenu_Inn = nameof(ContextMenu_Inn);
    public const string ContextMenu_Talk = nameof(ContextMenu_Talk);
    public const string ContextMenu_PickUp = nameof(ContextMenu_PickUp);
    public const string ContextMenu_PickUpMenu = nameof(ContextMenu_PickUpMenu);
    public const string ContextMenu_PickUpAll = nameof(ContextMenu_PickUpAll);
    public const string ContextMenu_LootHeldBy = nameof(ContextMenu_LootHeldBy);
    public const string ContextMenu_TileGround = nameof(ContextMenu_TileGround);
    public const string ContextMenu_Trade = nameof(ContextMenu_Trade);
    public const string ContextMenu_GuildInvite = nameof(ContextMenu_GuildInvite);
    public const string ContextMenu_GuildRequest = nameof(ContextMenu_GuildRequest);
    public const string ContextMenu_AddFriend = nameof(ContextMenu_AddFriend);
    public const string ContextMenu_Ignore = nameof(ContextMenu_Ignore);
    public const string ContextMenu_Mute = nameof(ContextMenu_Mute);
    public const string ContextMenu_Kick = nameof(ContextMenu_Kick);
    public const string ContextMenu_Ban = nameof(ContextMenu_Ban);
    public const string ContextMenu_TeleportTo = nameof(ContextMenu_TeleportTo);
    public const string ContextMenu_BringHere = nameof(ContextMenu_BringHere);
    public const string ContextMenu_SetAccess = nameof(ContextMenu_SetAccess);
    public const string ContextMenu_Access_Player = nameof(ContextMenu_Access_Player);
    public const string ContextMenu_Access_Monitor = nameof(ContextMenu_Access_Monitor);
    public const string ContextMenu_Access_Mapper = nameof(ContextMenu_Access_Mapper);
    public const string ContextMenu_Access_Developer = nameof(ContextMenu_Access_Developer);
    public const string ContextMenu_Access_Creator = nameof(ContextMenu_Access_Creator);
    public const string ContextMenu_Deposit1 = nameof(ContextMenu_Deposit1);
    public const string ContextMenu_DepositX = nameof(ContextMenu_DepositX);
    public const string ContextMenu_DepositAll = nameof(ContextMenu_DepositAll);
    public const string ContextMenu_Withdraw1 = nameof(ContextMenu_Withdraw1);
    public const string ContextMenu_WithdrawX = nameof(ContextMenu_WithdrawX);
    public const string ContextMenu_WithdrawAll = nameof(ContextMenu_WithdrawAll);
    public const string ContextMenu_Drop1 = nameof(ContextMenu_Drop1);
    public const string ContextMenu_DropX = nameof(ContextMenu_DropX);
    public const string ContextMenu_DropAll = nameof(ContextMenu_DropAll);
    public const string ContextMenu_Unequip = nameof(ContextMenu_Unequip);

    // ── Quests: accept/turn-in dialog + quest log ───────────────────────────────
    // A repeatable quest already finished this period — one line per cadence, so each language words its own
    // "already done this <period>" naturally rather than interpolating a period noun.

    // ── NPC conversations (dialogue panel) ─────────────────────────────────────
    public const string ConversationPanel_Title = nameof(ConversationPanel_Title);
    public const string ConversationPanel_Leave = nameof(ConversationPanel_Leave);

    // ── Party overlay confirmation ────────────────────────────────────────────
    public const string PartyOverlay_ConfirmTitle = nameof(PartyOverlay_ConfirmTitle);
    public const string PartyOverlay_ConfirmBody = nameof(PartyOverlay_ConfirmBody);
    // Territory-contest in-world HUD.
    public const string Common_Yes = nameof(Common_Yes);
    public const string Common_No = nameof(Common_No);
    public const string GuildOffer_Invite = nameof(GuildOffer_Invite);
    public const string GuildOffer_Request = nameof(GuildOffer_Request);
    public const string GuildOffer_Accept = nameof(GuildOffer_Accept);
    public const string GuildOffer_Decline = nameof(GuildOffer_Decline);
    public const string GuildOffer_Transfer = nameof(GuildOffer_Transfer);
}
