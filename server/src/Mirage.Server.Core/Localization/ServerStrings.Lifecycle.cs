using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Startup, shutdown, networking, console commands, and the handful of common words
/// reused across every other group.</summary>
public static partial class ServerStrings
{
    // ── Server lifecycle ──────────────────────────────────────────────────────
    public const string Server_Starting = nameof(Server_Starting);
    public const string Server_RunningInLanguage = nameof(Server_RunningInLanguage);
    public const string Server_LoadingGameData = nameof(Server_LoadingGameData);
    public const string Server_LoadingItems = nameof(Server_LoadingItems);
    public const string Server_LoadingNpcs = nameof(Server_LoadingNpcs);
    public const string Server_LoadingShops = nameof(Server_LoadingShops);
    public const string Server_LoadingSpells = nameof(Server_LoadingSpells);
    public const string Server_LoadingClasses = nameof(Server_LoadingClasses);
    public const string Server_LoadingQuests = nameof(Server_LoadingQuests);
    public const string Server_LoadingConversations = nameof(Server_LoadingConversations);
    public const string Server_LoadingMaps = nameof(Server_LoadingMaps);
    // Which set of records is loaded, from world.json. Operator-facing; a player never sees it.
    public const string Server_WorldName = nameof(Server_WorldName);   // "{WorldName}"
    public const string Server_DefaultMotd = nameof(Server_DefaultMotd);   // "{GameName}"
    public const string Server_NoMotdHint = nameof(Server_NoMotdHint);
    public const string Server_LoadingMotd = nameof(Server_LoadingMotd);
    public const string Server_LoadedSummary = nameof(Server_LoadedSummary);
    public const string Server_RuntimeDataSummary = nameof(Server_RuntimeDataSummary);
    public const string Server_PaddedSummary = nameof(Server_PaddedSummary);
    public const string Server_SpawningMapItems = nameof(Server_SpawningMapItems);
    public const string Server_LoadingDroppedItems = nameof(Server_LoadingDroppedItems);
    public const string Server_SpawningNpcs = nameof(Server_SpawningNpcs);
    public const string Server_Ready = nameof(Server_Ready);
    public const string Server_ShuttingDown = nameof(Server_ShuttingDown);
    public const string Server_Stopped = nameof(Server_Stopped);
    public const string Server_SavedPlayersOnShutdown = nameof(Server_SavedPlayersOnShutdown);
    public const string Server_SavedDropsOnShutdown = nameof(Server_SavedDropsOnShutdown);
    public const string Server_GameThreadStarted = nameof(Server_GameThreadStarted);
    public const string Server_GameThreadStopped = nameof(Server_GameThreadStopped);

    // ── Network ───────────────────────────────────────────────────────────────
    public const string Net_ListeningOnPort = nameof(Net_ListeningOnPort);
    public const string Net_NewConnection = nameof(Net_NewConnection);
    public const string Net_PlayerDisconnected = nameof(Net_PlayerDisconnected);
    public const string Net_EditorDisconnected = nameof(Net_EditorDisconnected);
    public const string Net_PlayerQueued = nameof(Net_PlayerQueued);
    public const string Net_PlayerRefusedFull = nameof(Net_PlayerRefusedFull);
    public const string Net_EditorRefusedFull = nameof(Net_EditorRefusedFull);

    // Said to the PLAYER, in the player's language — the two above go to the operator's log, in the
    // operator's. The locale comes off the packet the connection opened with, because there is no session
    // to read one from yet.
    public const string Net_ServerFullAlert = nameof(Net_ServerFullAlert);
    public const string Net_EditorFullAlert = nameof(Net_EditorFullAlert);

    // ── Remote management ─────────────────────────────────────────────────────
    public const string Management_Listening = nameof(Management_Listening);
    public const string Management_TokenMissing = nameof(Management_TokenMissing);
    public const string Management_OperatorAttached = nameof(Management_OperatorAttached);
    public const string Management_OperatorDetached = nameof(Management_OperatorDetached);
    public const string Management_AuthFailed = nameof(Management_AuthFailed);
    public const string Management_AuthLockedOut = nameof(Management_AuthLockedOut);
    public const string Management_RemoteCommand = nameof(Management_RemoteCommand);
    public const string Management_LinesDropped = nameof(Management_LinesDropped);

    // ── Console commands ──────────────────────────────────────────────────────
    public const string Console_Prompt = nameof(Console_Prompt);
    public const string Console_Help = nameof(Console_Help);
    public const string Credits_CreatorDeveloper = nameof(Credits_CreatorDeveloper);
    public const string Console_UpdateChecking = nameof(Console_UpdateChecking);
    public const string Console_UpdateNone = nameof(Console_UpdateNone);
    public const string Console_UpdateAvailable = nameof(Console_UpdateAvailable);
    public const string Console_Shutdown = nameof(Console_Shutdown);
    public const string Console_UnknownCommand = nameof(Console_UnknownCommand);
    public const string Console_WhoTotal = nameof(Console_WhoTotal);
    public const string Console_KickUsage = nameof(Console_KickUsage);
    public const string Console_BanUsage = nameof(Console_BanUsage);
    public const string Console_MuteUsage = nameof(Console_MuteUsage);
    public const string Console_PlayerNotOnline = nameof(Console_PlayerNotOnline);
    public const string Console_Kicked = nameof(Console_Kicked);
    public const string Console_Banned = nameof(Console_Banned);
    public const string Console_Muted = nameof(Console_Muted);
    // Lifting a punishment. These target an ACCOUNT rather than an online character, because the point
    // of a lift is that the person is locked out — so each has its own not-found line.
    public const string Console_LiftUsage = nameof(Console_LiftUsage);
    public const string Console_AccountNotFound = nameof(Console_AccountNotFound);
    public const string Console_Unbanned = nameof(Console_Unbanned);
    public const string Console_NotBanned = nameof(Console_NotBanned);
    public const string Console_Unkicked = nameof(Console_Unkicked);
    public const string Console_NotKicked = nameof(Console_NotKicked);
    public const string Console_Unmuted = nameof(Console_Unmuted);
    public const string Console_NotMuted = nameof(Console_NotMuted);
    public const string Console_ModerationBans = nameof(Console_ModerationBans);
    public const string Console_ModerationBanLine = nameof(Console_ModerationBanLine);
    public const string Console_ModerationPenalties = nameof(Console_ModerationPenalties);
    public const string Console_ModerationPenaltyLine = nameof(Console_ModerationPenaltyLine);
    public const string Console_ModerationNone = nameof(Console_ModerationNone);
    public const string Console_ModerationScanned = nameof(Console_ModerationScanned);
    // Machine bans, from the console. HwBanOffline and HwBanNoKey are both refusals to half-do the job:
    // the key lives on a live session, so an offline target has nothing to ban.
    public const string Console_HwBanUsage = nameof(Console_HwBanUsage);
    public const string Console_HwBanned = nameof(Console_HwBanned);
    public const string Console_HwBanOffline = nameof(Console_HwBanOffline);
    public const string Console_HwBanNoKey = nameof(Console_HwBanNoKey);
    public const string Console_HwUnbanned = nameof(Console_HwUnbanned);
    public const string Console_NotHwBanned = nameof(Console_NotHwBanned);
    public const string Console_ModerationHwBans = nameof(Console_ModerationHwBans);
    public const string Console_ModerationHwBanLine = nameof(Console_ModerationHwBanLine);
    public const string Console_ModerationHwMode = nameof(Console_ModerationHwMode);
    // World-level admin commands. Every usage line lists its valid values from the enum itself, so the
    // console can never advertise a phase or weather the server would then refuse.
    public const string Console_TodUsage = nameof(Console_TodUsage);
    public const string Console_TodSet = nameof(Console_TodSet);
    public const string Console_WeatherUsage = nameof(Console_WeatherUsage);
    public const string Console_WeatherSet = nameof(Console_WeatherSet);
    public const string Console_MotdCleared = nameof(Console_MotdCleared);

    // /management — remote operator access, set from the console rather than only from the shell.
    public const string Console_ManagementUsage = nameof(Console_ManagementUsage);
    public const string Console_ManagementPort = nameof(Console_ManagementPort);
    public const string Console_ManagementToken = nameof(Console_ManagementToken);
    public const string Console_ManagementTokenSet = nameof(Console_ManagementTokenSet);
    public const string Console_ManagementTokenUnset = nameof(Console_ManagementTokenUnset);
    public const string Console_ManagementTokenGenerated = nameof(Console_ManagementTokenGenerated);
    public const string Console_ManagementListening = nameof(Console_ManagementListening);
    public const string Console_ManagementBound = nameof(Console_ManagementBound);
    public const string Console_ManagementNotBound = nameof(Console_ManagementNotBound);
    public const string Console_ManagementOperators = nameof(Console_ManagementOperators);
    public const string Console_ManagementNeedsToken = nameof(Console_ManagementNeedsToken);
    public const string Console_ManagementRefused = nameof(Console_ManagementRefused);
    public const string Console_ManagementOff = nameof(Console_ManagementOff);
    public const string Console_ManagementSaveFailed = nameof(Console_ManagementSaveFailed);
    public const string Console_ManagementPortUsage = nameof(Console_ManagementPortUsage);
    public const string Console_MotdSet = nameof(Console_MotdSet);
    public const string Console_SetAccessUsage = nameof(Console_SetAccessUsage);
    public const string Console_AccessSet = nameof(Console_AccessSet);
    public const string Console_RespawnUsage = nameof(Console_RespawnUsage);
    public const string Console_MapRespawned = nameof(Console_MapRespawned);
    public const string Console_MapReport = nameof(Console_MapReport);
    public const string Console_WarStarted = nameof(Console_WarStarted);
    public const string Console_WarAdvanced = nameof(Console_WarAdvanced);
    public const string Console_WarEnded = nameof(Console_WarEnded);
    public const string Console_NoWarInProgress = nameof(Console_NoWarInProgress);
    public const string Console_GuildResetUsage = nameof(Console_GuildResetUsage);
    public const string Console_GuildReset = nameof(Console_GuildReset);

    // ── Common ────────────────────────────────────────────────────────────────
    public const string Common_InventoryFull = nameof(Common_InventoryFull);
    public const string Common_DoorUnlocked = nameof(Common_DoorUnlocked);
}
