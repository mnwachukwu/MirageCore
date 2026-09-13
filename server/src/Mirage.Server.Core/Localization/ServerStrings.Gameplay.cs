using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>The remaining per-system lines: spells, movement, parties, spawning, PK expiry,
/// regeneration, packet validation, search, quests, time of day, and weather.</summary>
public static partial class ServerStrings
{
    // ── Map greeting ──────────────────────────────────────────────────────────
    // Spoken on entering or leaving a MAP, by that map's GreetingSpeaker. It belongs to the map (and its
    // MapGroup), not to any shop standing on it — a map cannot know whether it holds a store or an inn.
    public const string MapGreeting_JoinSay = nameof(MapGreeting_JoinSay);
    public const string MapGreeting_LeaveSay = nameof(MapGreeting_LeaveSay);

    // ── SpellSystem ───────────────────────────────────────────────────────────

    // ── MovementSystem ────────────────────────────────────────────────────────
    public const string MovementSystem_WarpDestinationMissing = nameof(MovementSystem_WarpDestinationMissing);

    // ── PartySystem ───────────────────────────────────────────────────────────
    public const string PartySystem_TargetNotOnline = nameof(PartySystem_TargetNotOnline);
    public const string PartySystem_AdminCannotParty = nameof(PartySystem_AdminCannotParty);
    public const string PartySystem_TargetIsAdmin = nameof(PartySystem_TargetIsAdmin);
    public const string PartySystem_AlreadyInParty = nameof(PartySystem_AlreadyInParty);
    public const string PartySystem_TargetAlreadyInParty = nameof(PartySystem_TargetAlreadyInParty);
    public const string PartySystem_InviteReceived = nameof(PartySystem_InviteReceived);
    public const string PartySystem_InviteSent = nameof(PartySystem_InviteSent);
    public const string PartySystem_NoInvite = nameof(PartySystem_NoInvite);
    public const string PartySystem_NoInvitePending = nameof(PartySystem_NoInvitePending);
    public const string PartySystem_Failed = nameof(PartySystem_Failed);
    public const string PartySystem_YouJoined = nameof(PartySystem_YouJoined);
    public const string PartySystem_TheyJoined = nameof(PartySystem_TheyJoined);
    public const string PartySystem_NotInParty = nameof(PartySystem_NotInParty);
    public const string PartySystem_YouLeft = nameof(PartySystem_YouLeft);
    public const string PartySystem_TheyLeft = nameof(PartySystem_TheyLeft);
    public const string PartySystem_Declined = nameof(PartySystem_Declined);
    public const string PartySystem_TheyDeclined = nameof(PartySystem_TheyDeclined);
    public const string PartySystem_InviteExpiredSelf = nameof(PartySystem_InviteExpiredSelf);
    public const string PartySystem_InviteExpiredOther = nameof(PartySystem_InviteExpiredOther);

    // ── PlayerSpawnSystem ─────────────────────────────────────────────────────
    public const string PlayerSpawnSystem_NotNearInn = nameof(PlayerSpawnSystem_NotNearInn);
    public const string PlayerSpawnSystem_InsufficientGold = nameof(PlayerSpawnSystem_InsufficientGold);
    public const string PlayerSpawnSystem_SpawnSet = nameof(PlayerSpawnSystem_SpawnSet);

    // ── PkExpirySystem ────────────────────────────────────────────────────────
    public const string PkExpirySystem_CrimesFaded = nameof(PkExpirySystem_CrimesFaded);

    // ── RegenerationSystem ────────────────────────────────────────────────────

    // ── PacketHandler ─────────────────────────────────────────────────────────
    public const string PacketHandler_NotNearShop = nameof(PacketHandler_NotNearShop);
    public const string PacketHandler_CannotLogoutCombat = nameof(PacketHandler_CannotLogoutCombat);
    public const string PacketHandler_TellFrom = nameof(PacketHandler_TellFrom);
    public const string PacketHandler_TellTo = nameof(PacketHandler_TellTo);
    public const string PacketHandler_PlayerNotOnline = nameof(PacketHandler_PlayerNotOnline);
    public const string PacketHandler_RollCoin = nameof(PacketHandler_RollCoin);
    public const string PacketHandler_RollDice = nameof(PacketHandler_RollDice);
    public const string PacketHandler_SelfMumble = nameof(PacketHandler_SelfMumble);
    public const string PacketHandler_Say = nameof(PacketHandler_Say);
    public const string PacketHandler_Emote = nameof(PacketHandler_Emote);
    public const string PacketHandler_Yell = nameof(PacketHandler_Yell);
    public const string PacketHandler_Broadcast = nameof(PacketHandler_Broadcast);
    public const string PacketHandler_Notice = nameof(PacketHandler_Notice);
    public const string PacketHandler_Admin = nameof(PacketHandler_Admin);

    // ── SearchSystem ──────────────────────────────────────────────────────────
    public const string SearchSystem_TargetNow = nameof(SearchSystem_TargetNow);
    public const string SearchSystem_TargetNowNpc = nameof(SearchSystem_TargetNowNpc);
    public const string SearchSystem_TargetSelf = nameof(SearchSystem_TargetSelf);
    public const string SearchSystem_SeeCurrency = nameof(SearchSystem_SeeCurrency);
    public const string SearchSystem_SeeItem = nameof(SearchSystem_SeeItem);

    // ── TimeOfDaySystem ───────────────────────────────────────────────────────
    public const string TimeOfDay_NightFalls = nameof(TimeOfDay_NightFalls);
    public const string TimeOfDay_NightWarning = nameof(TimeOfDay_NightWarning);
    public const string TimeOfDay_DawnBreaks = nameof(TimeOfDay_DawnBreaks);
    public const string TimeOfDay_DayReturns = nameof(TimeOfDay_DayReturns);
    public const string TimeOfDay_DuskFalls = nameof(TimeOfDay_DuskFalls);
    public const string TimeOfDay_UnnaturalShift = nameof(TimeOfDay_UnnaturalShift);
    public const string TimeOfDay_UnnaturalShiftBy = nameof(TimeOfDay_UnnaturalShiftBy);
    public const string TimeOfDay_WelcomeDay = nameof(TimeOfDay_WelcomeDay);
    public const string TimeOfDay_WelcomeDusk = nameof(TimeOfDay_WelcomeDusk);
    public const string TimeOfDay_WelcomeNight = nameof(TimeOfDay_WelcomeNight);
    public const string TimeOfDay_WelcomeDawn = nameof(TimeOfDay_WelcomeDawn);

    // ── WeatherSystem ─────────────────────────────────────────────────────────
    public const string Weather_RainBegins = nameof(Weather_RainBegins);
    public const string Weather_SnowBegins = nameof(Weather_SnowBegins);
    public const string Weather_HeatWaveBegins = nameof(Weather_HeatWaveBegins);
    public const string Weather_HeavyWindBegins = nameof(Weather_HeavyWindBegins);
    public const string Weather_Clears = nameof(Weather_Clears);
    public const string Weather_RainEffect = nameof(Weather_RainEffect);
    public const string Weather_SnowEffect = nameof(Weather_SnowEffect);
    public const string Weather_HeatWaveEffect = nameof(Weather_HeatWaveEffect);
    public const string Weather_HeavyWindEffect = nameof(Weather_HeavyWindEffect);
    public const string Weather_WelcomeClear = nameof(Weather_WelcomeClear);
    public const string Weather_WelcomeRain = nameof(Weather_WelcomeRain);
    public const string Weather_WelcomeSnow = nameof(Weather_WelcomeSnow);
    public const string Weather_WelcomeHeatWave = nameof(Weather_WelcomeHeatWave);
    public const string Weather_WelcomeHeavyWind = nameof(Weather_WelcomeHeavyWind);
    public const string Weather_UnnaturalShift = nameof(Weather_UnnaturalShift);
    public const string Weather_UnnaturalShiftBy = nameof(Weather_UnnaturalShiftBy);

}
