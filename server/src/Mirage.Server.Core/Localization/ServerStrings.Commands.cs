using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Player slash-commands and the operator/admin command set.</summary>
public static partial class ServerStrings
{
    // ── Player commands ───────────────────────────────────────────────────────
    // /played + the /info playtime line: current character + account total.
    public const string Command_Played = nameof(Command_Played);
    // /home — the warp itself, and the four refusals: the player is dead, the cooldown still has time on
    // it, the player is in combat, or the home point names a tile that does not exist.
    public const string Command_HomeWarped = nameof(Command_HomeWarped);
    public const string Command_HomeCooldown = nameof(Command_HomeCooldown);   // "{Remaining}"
    public const string Command_HomeInCombat = nameof(Command_HomeInCombat);
    public const string Command_HomeWhileDead = nameof(Command_HomeWhileDead);
    // The generic refusal for any other TYPED command a corpse may not run (/trade, /join, /leave).
    public const string Command_WhileDead = nameof(Command_WhileDead);
    public const string Command_HomeDestinationMissing = nameof(Command_HomeDestinationMissing);
    // /homecd — the same timer, asked about rather than hit.
    public const string Command_HomeCooldownLeft = nameof(Command_HomeCooldownLeft);   // "{Remaining}"
    public const string Command_HomeReady = nameof(Command_HomeReady);

    // ── Admin commands ────────────────────────────────────────────────────────
    public const string AdminCommand_PlayerInfo = nameof(AdminCommand_PlayerInfo);
    public const string AdminCommand_StatsHeader = nameof(AdminCommand_StatsHeader);
    public const string AdminCommand_StatsLevel = nameof(AdminCommand_StatsLevel);
    public const string AdminCommand_StatsVitals = nameof(AdminCommand_StatsVitals);
    public const string AdminCommand_StatsAttributes = nameof(AdminCommand_StatsAttributes);
    public const string AdminCommand_StatsChances = nameof(AdminCommand_StatsChances);
    public const string AdminCommand_Location = nameof(AdminCommand_Location);
    public const string AdminCommand_GuildReset = nameof(AdminCommand_GuildReset);
    public const string AdminCommand_WarStarted = nameof(AdminCommand_WarStarted);
    public const string AdminCommand_WarAdvanced = nameof(AdminCommand_WarAdvanced);
    public const string AdminCommand_WarEnded = nameof(AdminCommand_WarEnded);
    public const string AdminCommand_NoWarInProgress = nameof(AdminCommand_NoWarInProgress);
    public const string AdminCommand_GodModeOn = nameof(AdminCommand_GodModeOn);
    public const string AdminCommand_GodModeOff = nameof(AdminCommand_GodModeOff);
    public const string AdminCommand_WarpedToPlayer = nameof(AdminCommand_WarpedToPlayer);
    public const string AdminCommand_WarpedToTarget = nameof(AdminCommand_WarpedToTarget);
    public const string AdminCommand_CannotWarpSelf = nameof(AdminCommand_CannotWarpSelf);
    public const string AdminCommand_SummonedYou = nameof(AdminCommand_SummonedYou);
    public const string AdminCommand_PlayerSummoned = nameof(AdminCommand_PlayerSummoned);
    public const string AdminCommand_CannotWarpSelfToSelf = nameof(AdminCommand_CannotWarpSelfToSelf);
    public const string AdminCommand_TargetIsDead = nameof(AdminCommand_TargetIsDead);   // "{Target}"
    public const string AdminCommand_WarpWhileDead = nameof(AdminCommand_WarpWhileDead);
    public const string AdminCommand_WarpedToMap = nameof(AdminCommand_WarpedToMap);
    public const string AdminCommand_MapRespawned = nameof(AdminCommand_MapRespawned);
    public const string AdminCommand_CannotKickSelf = nameof(AdminCommand_CannotKickSelf);
    public const string AdminCommand_CannotBanSelf = nameof(AdminCommand_CannotBanSelf);
    public const string AdminCommand_CannotMuteSelf = nameof(AdminCommand_CannotMuteSelf);
    public const string AdminCommand_CannotTargetAdmin = nameof(AdminCommand_CannotTargetAdmin);
    public const string AdminCommand_InvalidMinutes = nameof(AdminCommand_InvalidMinutes);
    public const string AdminCommand_ConsoleOperatorName = nameof(AdminCommand_ConsoleOperatorName);
    public const string AdminCommand_KickBroadcast = nameof(AdminCommand_KickBroadcast);
    public const string AdminCommand_Kicked = nameof(AdminCommand_Kicked);
    public const string AdminCommand_BanBroadcast = nameof(AdminCommand_BanBroadcast);
    public const string AdminCommand_BanListRefreshed = nameof(AdminCommand_BanListRefreshed);
    // Lifting a punishment in game (Creator only). Each names the ACCOUNT, because that is what the
    // punishment is on and what the Creator has to be sure they lifted.
    public const string AdminCommand_AccountNotFound = nameof(AdminCommand_AccountNotFound);
    public const string AdminCommand_Unbanned = nameof(AdminCommand_Unbanned);
    public const string AdminCommand_NotBanned = nameof(AdminCommand_NotBanned);
    public const string AdminCommand_Unkicked = nameof(AdminCommand_Unkicked);
    public const string AdminCommand_NotKicked = nameof(AdminCommand_NotKicked);
    public const string AdminCommand_Unmuted = nameof(AdminCommand_Unmuted);
    public const string AdminCommand_NotMuted = nameof(AdminCommand_NotMuted);
    // Machine bans. HwBanNoKey is the one that matters: it reports a PARTIAL success, so nobody walks
    // away believing a machine was blocked when only the account was.
    public const string AdminCommand_HwUnbanned = nameof(AdminCommand_HwUnbanned);
    public const string AdminCommand_NotHwBanned = nameof(AdminCommand_NotHwBanned);
    public const string AdminCommand_HwBanNoKey = nameof(AdminCommand_HwBanNoKey);
    /// <summary>Signal mode only: what every Monitor and above is told when a banned machine gets in.</summary>
    public const string AdminCommand_MachineBanHit = nameof(AdminCommand_MachineBanHit);
    public const string AdminCommand_ModerationBans = nameof(AdminCommand_ModerationBans);
    public const string AdminCommand_ModerationBanLine = nameof(AdminCommand_ModerationBanLine);
    public const string AdminCommand_ModerationPenalties = nameof(AdminCommand_ModerationPenalties);
    public const string AdminCommand_ModerationPenaltyLine = nameof(AdminCommand_ModerationPenaltyLine);
    public const string AdminCommand_MuteBroadcast = nameof(AdminCommand_MuteBroadcast);
    public const string AdminCommand_YouAreMuted = nameof(AdminCommand_YouAreMuted);
    public const string AdminCommand_CannotModifyAccess = nameof(AdminCommand_CannotModifyAccess);
    public const string AdminCommand_PlayerGrantedAccess = nameof(AdminCommand_PlayerGrantedAccess);
    public const string AdminCommand_InvalidAccessLevel = nameof(AdminCommand_InvalidAccessLevel);
    public const string AdminCommand_MotdChanged = nameof(AdminCommand_MotdChanged);
    public const string AdminCommand_BootedFor = nameof(AdminCommand_BootedFor);
    public const string AdminCommand_ConnectionLost = nameof(AdminCommand_ConnectionLost);
}
