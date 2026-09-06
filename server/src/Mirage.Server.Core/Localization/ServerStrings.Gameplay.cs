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
    public const string SpellSystem_NoSpell = nameof(SpellSystem_NoSpell);
    public const string SpellSystem_NotEnoughMana = nameof(SpellSystem_NotEnoughMana);
    public const string SpellSystem_LevelRequired = nameof(SpellSystem_LevelRequired);
    public const string SpellSystem_NotEnoughReagents = nameof(SpellSystem_NotEnoughReagents);
    public const string SpellSystem_IntRequired = nameof(SpellSystem_IntRequired);
    public const string SpellSystem_CannotCastOnNpc = nameof(SpellSystem_CannotCastOnNpc);
    public const string SpellSystem_OutOfRange = nameof(SpellSystem_OutOfRange);
    public const string SpellSystem_NoLineOfSight = nameof(SpellSystem_NoLineOfSight);
    public const string SpellSystem_NoTarget = nameof(SpellSystem_NoTarget);
    public const string SpellSystem_CannotHarmSelf = nameof(SpellSystem_CannotHarmSelf);
    public const string SpellSystem_CannotHarmParty = nameof(SpellSystem_CannotHarmParty);
    public const string SpellSystem_CannotHarmGuild = nameof(SpellSystem_CannotHarmGuild);
    public const string SpellSystem_CannotHarmPlayer = nameof(SpellSystem_CannotHarmPlayer);
    public const string SpellSystem_CannotTargetDead = nameof(SpellSystem_CannotTargetDead);
    public const string SpellSystem_CannotCastOnFriendlyNpc = nameof(SpellSystem_CannotCastOnFriendlyNpc);
    public const string SpellSystem_TargetInventoryFull = nameof(SpellSystem_TargetInventoryFull);
    public const string SpellSystem_TargetInvalid = nameof(SpellSystem_TargetInvalid);

    // ── MovementSystem ────────────────────────────────────────────────────────
    public const string MovementSystem_EnterSafeBase = nameof(MovementSystem_EnterSafeBase);
    public const string MovementSystem_EnterSafePk = nameof(MovementSystem_EnterSafePk);
    public const string MovementSystem_EnterSafeNonPk = nameof(MovementSystem_EnterSafeNonPk);
    public const string MovementSystem_LeaveSafeBase = nameof(MovementSystem_LeaveSafeBase);
    public const string MovementSystem_LeaveSafeNonPk = nameof(MovementSystem_LeaveSafeNonPk);
    public const string MovementSystem_EnterArenaBase = nameof(MovementSystem_EnterArenaBase);
    public const string MovementSystem_EnterArenaPvp = nameof(MovementSystem_EnterArenaPvp);
    public const string MovementSystem_LeaveArena = nameof(MovementSystem_LeaveArena);
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
    public const string RegenerationSystem_CombatEnded = nameof(RegenerationSystem_CombatEnded);

    // ── PacketHandler ─────────────────────────────────────────────────────────
    public const string PacketHandler_NotNearShop = nameof(PacketHandler_NotNearShop);
    public const string PacketHandler_CannotLogoutCombat = nameof(PacketHandler_CannotLogoutCombat);
    public const string PacketHandler_StudyCombat = nameof(PacketHandler_StudyCombat);
    public const string PacketHandler_ForgotSpell = nameof(PacketHandler_ForgotSpell);
    public const string PacketHandler_TellFrom = nameof(PacketHandler_TellFrom);
    public const string PacketHandler_TellTo = nameof(PacketHandler_TellTo);
    public const string PacketHandler_PlayerNotOnline = nameof(PacketHandler_PlayerNotOnline);
    public const string PacketHandler_NoStatPoints = nameof(PacketHandler_NoStatPoints);
    public const string PacketHandler_GainedStr = nameof(PacketHandler_GainedStr);
    public const string PacketHandler_GainedDef = nameof(PacketHandler_GainedDef);
    public const string PacketHandler_GainedInt = nameof(PacketHandler_GainedInt);
    public const string PacketHandler_GainedSpd = nameof(PacketHandler_GainedSpd);
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
    public const string SearchSystem_WouldntStandChance = nameof(SearchSystem_WouldntStandChance);
    public const string SearchSystem_TheyHaveAdvantage = nameof(SearchSystem_TheyHaveAdvantage);
    public const string SearchSystem_EvenFight = nameof(SearchSystem_EvenFight);
    public const string SearchSystem_YouHaveAdvantage = nameof(SearchSystem_YouHaveAdvantage);
    public const string SearchSystem_TheyWouldntChance = nameof(SearchSystem_TheyWouldntChance);
    public const string SearchSystem_YouHaveAdvantageNpc = nameof(SearchSystem_YouHaveAdvantageNpc);
    public const string SearchSystem_NpcWouldntChance = nameof(SearchSystem_NpcWouldntChance);
    public const string SearchSystem_TargetNow = nameof(SearchSystem_TargetNow);
    public const string SearchSystem_TargetNowNpc = nameof(SearchSystem_TargetNowNpc);
    public const string SearchSystem_TargetSelf = nameof(SearchSystem_TargetSelf);
    public const string SearchSystem_SeeCurrency = nameof(SearchSystem_SeeCurrency);
    public const string SearchSystem_SeeEquipment = nameof(SearchSystem_SeeEquipment);
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

    public const string Quest_Sender = nameof(Quest_Sender);
    public const string Quest_Accepted = nameof(Quest_Accepted);
    public const string Quest_ReadyToTurnIn = nameof(Quest_ReadyToTurnIn);
    public const string Quest_Complete = nameof(Quest_Complete);
    public const string Quest_Abandoned = nameof(Quest_Abandoned);
    public const string Quest_ObjectiveProgress = nameof(Quest_ObjectiveProgress);
    public const string Quest_AnyTarget = nameof(Quest_AnyTarget);
    public const string Quest_RewardExp = nameof(Quest_RewardExp);
    public const string Quest_NotEligible = nameof(Quest_NotEligible);
    public const string Quest_NoGiverOpen = nameof(Quest_NoGiverOpen);
    public const string Quest_NotComplete = nameof(Quest_NotComplete);
    public const string Quest_TooMany = nameof(Quest_TooMany);
    public const string Quest_RewardMailSubject = nameof(Quest_RewardMailSubject);
    public const string Quest_RewardMailBody = nameof(Quest_RewardMailBody);
}
