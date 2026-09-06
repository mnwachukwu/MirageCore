using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>Config, help, controls, options, chat and its per-tab options, and the quit dialog.</summary>
public static partial class ClientStrings
{
    // ── ConfigPanel ───────────────────────────────────────────────────────────
    public const string ConfigPanel_Title = nameof(ConfigPanel_Title);
    public const string ConfigPanel_HostLabel = nameof(ConfigPanel_HostLabel);
    public const string ConfigPanel_PortLabel = nameof(ConfigPanel_PortLabel);
    public const string ConfigPanel_HostEmptyError = nameof(ConfigPanel_HostEmptyError);
    public const string ConfigPanel_PortEmptyError = nameof(ConfigPanel_PortEmptyError);
    public const string ConfigPanel_PortNotNumberError = nameof(ConfigPanel_PortNotNumberError);
    public const string ConfigPanel_PortRangeError = nameof(ConfigPanel_PortRangeError);
    public const string ConfigPanel_TestButton = nameof(ConfigPanel_TestButton);
    public const string ConfigPanel_SaveButton = nameof(ConfigPanel_SaveButton);
    public const string ConfigPanel_ForgetButton = nameof(ConfigPanel_ForgetButton);
    public const string ConfigPanel_AddButton = nameof(ConfigPanel_AddButton);
    public const string ConfigPanel_ClearPinButton = nameof(ConfigPanel_ClearPinButton);
    public const string ConfigPanel_PinCleared = nameof(ConfigPanel_PinCleared);       // "{Server}"
    public const string ConfigPanel_NoPinStored = nameof(ConfigPanel_NoPinStored);     // "{Server}"
    public const string ConfigPanel_KnownServersLabel = nameof(ConfigPanel_KnownServersLabel);
    public const string ConfigPanel_NameLabel = nameof(ConfigPanel_NameLabel);
    public const string ConfigPanel_ServerAdded = nameof(ConfigPanel_ServerAdded);   // "{Server}"
    public const string ConfigPanel_TestingConnection = nameof(ConfigPanel_TestingConnection);
    public const string ConfigPanel_ConnectionTimedOut = nameof(ConfigPanel_ConnectionTimedOut);
    public const string ConfigPanel_ConnectionSucceeded = nameof(ConfigPanel_ConnectionSucceeded);
    public const string ConfigPanel_ConnectionFailed = nameof(ConfigPanel_ConnectionFailed);

    // ── HelpPanel ─────────────────────────────────────────────────────────────
    public const string HelpPanel_Title = nameof(HelpPanel_Title);

    // ── ControlsPanel ─────────────────────────────────────────────────────────
    // Common_ControlsHeader is shared with the HelpPanel's link label (same "Controls" string).
    public const string Common_ControlsHeader = nameof(Common_ControlsHeader);
    public const string ControlsPanel_KeyboardTab = nameof(ControlsPanel_KeyboardTab);
    public const string ControlsPanel_PlayStationTab = nameof(ControlsPanel_PlayStationTab);
    public const string ControlsPanel_XboxTab = nameof(ControlsPanel_XboxTab);

    // ── OptionsPanel ──────────────────────────────────────────────────────────
    public const string OptionsPanel_Title = nameof(OptionsPanel_Title);
    public const string OptionsPanel_MaintainAspectRatio = nameof(OptionsPanel_MaintainAspectRatio);
    public const string OptionsPanel_AlwaysShowBars = nameof(OptionsPanel_AlwaysShowBars);
    public const string OptionsPanel_ShowCombatNumbers = nameof(OptionsPanel_ShowCombatNumbers);
    public const string OptionsPanel_PlayMusic = nameof(OptionsPanel_PlayMusic);
    public const string OptionsPanel_MusicVolume = nameof(OptionsPanel_MusicVolume);
    public const string OptionsPanel_UseGamepad = nameof(OptionsPanel_UseGamepad);
    public const string OptionsPanel_SkipPlayersTabTarget = nameof(OptionsPanel_SkipPlayersTabTarget);
    public const string OptionsPanel_ShowNpcNames = nameof(OptionsPanel_ShowNpcNames);
    public const string OptionsPanel_ShowBlood = nameof(OptionsPanel_ShowBlood);
    public const string OptionsPanel_ShowOtherPlayerNames = nameof(OptionsPanel_ShowOtherPlayerNames);
    public const string OptionsPanel_ShowPlayerName = nameof(OptionsPanel_ShowPlayerName);
    public const string OptionsPanel_ShowCooldownBar = nameof(OptionsPanel_ShowCooldownBar);
    public const string OptionsPanel_ShowOtherCooldownBars = nameof(OptionsPanel_ShowOtherCooldownBars);
    public const string OptionsPanel_ShowChatTimestamps = nameof(OptionsPanel_ShowChatTimestamps);
    public const string OptionsPanel_Use24HourClock = nameof(OptionsPanel_Use24HourClock);
    public const string OptionsPanel_ShowChannelLabels = nameof(OptionsPanel_ShowChannelLabels);
    public const string OptionsPanel_RestoreDefaults = nameof(OptionsPanel_RestoreDefaults);
    public const string OptionsPanel_ResetPanels = nameof(OptionsPanel_ResetPanels);
    public const string OptionsPanel_Language = nameof(OptionsPanel_Language);

    // ── ChatPanel ─────────────────────────────────────────────────────────────
    public const string ChatPanel_Title = nameof(ChatPanel_Title);
    public const string ChatPanel_FpsDisplay = nameof(ChatPanel_FpsDisplay);
    public const string ChatPanel_FpsOn = nameof(ChatPanel_FpsOn);
    public const string ChatPanel_FpsOff = nameof(ChatPanel_FpsOff);
    // The frame readout over the action bar. The bands and the counters are Developer+ only; everyone
    // else gets the rate on its own.
    public const string Hud_FrameBands = nameof(Hud_FrameBands);
    public const string Hud_FrameHalves = nameof(Hud_FrameHalves);
    public const string Hud_FrameLight = nameof(Hud_FrameLight);
    public const string Hud_FrameCatchUp = nameof(Hud_FrameCatchUp);
    public const string Hud_FrameWorst = nameof(Hud_FrameWorst);
    public const string Hud_FrameWorstRest = nameof(Hud_FrameWorstRest);
    public const string Hud_FrameGc = nameof(Hud_FrameGc);
    public const string ChatPanel_HomeInCombat = nameof(ChatPanel_HomeInCombat);
    public const string ChatPanel_UsageTell = nameof(ChatPanel_UsageTell);
    public const string ChatPanel_InvalidMapNumber = nameof(ChatPanel_InvalidMapNumber);
    public const string ChatPanel_UsageRoll = nameof(ChatPanel_UsageRoll);
    public const string ChatPanel_UsageGuildReset = nameof(ChatPanel_UsageGuildReset);
    public const string ChatPanel_UnknownCommand = nameof(ChatPanel_UnknownCommand);
    public const string ChatPanel_NotInGuild = nameof(ChatPanel_NotInGuild);
    public const string ChatPanel_NotOfficer = nameof(ChatPanel_NotOfficer);
    public const string ChatPanel_DefaultTabName = nameof(ChatPanel_DefaultTabName);
    // Names for the two out-of-the-box tabs created on a fresh account.
    public const string ChatPanel_DefaultTab_General = nameof(ChatPanel_DefaultTab_General);
    public const string ChatPanel_DefaultTab_Combat = nameof(ChatPanel_DefaultTab_Combat);

    // ── ChatOptionsPanel (per-tab right-click options) ────────────────────────
    public const string ChatOptionsPanel_Title = nameof(ChatOptionsPanel_Title);
    public const string ChatOptionsPanel_TabName = nameof(ChatOptionsPanel_TabName);
    public const string ChatOptionsPanel_Notify = nameof(ChatOptionsPanel_Notify);
    public const string ChatOptionsPanel_Close = nameof(ChatOptionsPanel_Close);
    public const string ChatOptionsPanel_SectionGeneral = nameof(ChatOptionsPanel_SectionGeneral);
    public const string ChatOptionsPanel_SectionChat = nameof(ChatOptionsPanel_SectionChat);
    public const string ChatOptionsPanel_SectionSystem = nameof(ChatOptionsPanel_SectionSystem);
    public const string ChatOptionsPanel_SectionCombat = nameof(ChatOptionsPanel_SectionCombat);
    public const string ChatOptionsPanel_SectionGuild = nameof(ChatOptionsPanel_SectionGuild);
    public const string ChatOptionsPanel_Channel_Say = nameof(ChatOptionsPanel_Channel_Say);
    public const string ChatOptionsPanel_Channel_Yell = nameof(ChatOptionsPanel_Channel_Yell);
    public const string ChatOptionsPanel_Channel_Broadcast = nameof(ChatOptionsPanel_Channel_Broadcast);
    public const string ChatOptionsPanel_Channel_Tell = nameof(ChatOptionsPanel_Channel_Tell);
    public const string ChatOptionsPanel_Channel_AdminChat = nameof(ChatOptionsPanel_Channel_AdminChat);
    public const string ChatOptionsPanel_Channel_Notice = nameof(ChatOptionsPanel_Channel_Notice);
    public const string ChatOptionsPanel_Channel_JoinLeaveNotice = nameof(ChatOptionsPanel_Channel_JoinLeaveNotice);
    public const string ChatOptionsPanel_Channel_System = nameof(ChatOptionsPanel_Channel_System);
    public const string ChatOptionsPanel_Channel_Combat = nameof(ChatOptionsPanel_Channel_Combat);
    public const string ChatOptionsPanel_Channel_Rewards = nameof(ChatOptionsPanel_Channel_Rewards);
    public const string ChatOptionsPanel_Channel_Guild = nameof(ChatOptionsPanel_Channel_Guild);
    public const string ChatOptionsPanel_Channel_GuildOfficer = nameof(ChatOptionsPanel_Channel_GuildOfficer);
    public const string ChatOptionsPanel_Channel_GuildWar = nameof(ChatOptionsPanel_Channel_GuildWar);
    public const string ChatOptionsPanel_Channel_War = nameof(ChatOptionsPanel_Channel_War);

    // ── QuitConfirmDialog ────────────────────────────────────────────────────
    public const string QuitConfirm_Quit = nameof(QuitConfirm_Quit);
    public const string QuitConfirm_Logout = nameof(QuitConfirm_Logout);
    public const string QuitConfirm_Prompt = nameof(QuitConfirm_Prompt);
    public const string QuitConfirm_CombatWarnLine1 = nameof(QuitConfirm_CombatWarnLine1);
    public const string QuitConfirm_CombatWarnLine2 = nameof(QuitConfirm_CombatWarnLine2);

    // ── CreditsScreen (section headers — proper names + copyright lines stay verbatim) ──
    public const string Credits_SectionVB6 = nameof(Credits_SectionVB6);
    public const string Credits_Programming = nameof(Credits_Programming);
    public const string Credits_ArtMusic = nameof(Credits_ArtMusic);
    public const string Credits_GuiArt = nameof(Credits_GuiArt);
    public const string Credits_GuiArtNote = nameof(Credits_GuiArtNote);
    public const string Credits_SectionCSharp = nameof(Credits_SectionCSharp);
    public const string Credits_CreatorDeveloper = nameof(Credits_CreatorDeveloper);
}
