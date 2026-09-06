using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Guild membership and progression, and the guild-war lifecycle from declaration
/// through peace terms.</summary>
public static partial class ServerStrings
{
    // ── Guild ─────────────────────────────────────────────────────────────────
    public const string Guild_Founded = nameof(Guild_Founded);
    public const string Guild_Disbanded = nameof(Guild_Disbanded);
    public const string Guild_NotInOne = nameof(Guild_NotInOne);
    public const string Guild_AlreadyInOne = nameof(Guild_AlreadyInOne);
    public const string Guild_NameTaken = nameof(Guild_NameTaken);
    public const string Guild_NameLength = nameof(Guild_NameLength);
    public const string Guild_NameNeedsAlnum = nameof(Guild_NameNeedsAlnum);
    public const string Guild_NeedGold = nameof(Guild_NeedGold);
    public const string Guild_AdminCannotJoin = nameof(Guild_AdminCannotJoin);
    public const string Guild_DisbandNotLeader = nameof(Guild_DisbandNotLeader);
    public const string Guild_DisbandHasMembers = nameof(Guild_DisbandHasMembers);
    public const string Guild_NeedOfficer = nameof(Guild_NeedOfficer);
    public const string Guild_PlayerNotOnline = nameof(Guild_PlayerNotOnline);
    public const string Guild_TargetInGuild = nameof(Guild_TargetInGuild);
    public const string Guild_TargetNotOfficer = nameof(Guild_TargetNotOfficer);
    public const string Guild_NotOpen = nameof(Guild_NotOpen);
    public const string Guild_InviteSent = nameof(Guild_InviteSent);
    public const string Guild_RequestSent = nameof(Guild_RequestSent);
    public const string Guild_NoOffer = nameof(Guild_NoOffer);
    public const string Guild_OfferGone = nameof(Guild_OfferGone);
    public const string Guild_RequesterGone = nameof(Guild_RequesterGone);
    public const string Guild_MemberJoined = nameof(Guild_MemberJoined);
    public const string Guild_NeedLeader = nameof(Guild_NeedLeader);
    public const string Guild_OpenedForMembership = nameof(Guild_OpenedForMembership);
    public const string Guild_ClosedForMembership = nameof(Guild_ClosedForMembership);
    public const string Guild_LeaderCantLeave = nameof(Guild_LeaderCantLeave);
    public const string Guild_YouLeft = nameof(Guild_YouLeft);
    public const string Guild_MemberLeft = nameof(Guild_MemberLeft);
    public const string Guild_NotAMember = nameof(Guild_NotAMember);
    public const string Guild_CantKickSelf = nameof(Guild_CantKickSelf);
    public const string Guild_CantKickRank = nameof(Guild_CantKickRank);
    public const string Guild_MemberKicked = nameof(Guild_MemberKicked);
    public const string Guild_YouWereKicked = nameof(Guild_YouWereKicked);
    public const string Guild_CantPromote = nameof(Guild_CantPromote);
    public const string Guild_MemberPromoted = nameof(Guild_MemberPromoted);
    public const string Guild_YouWerePromoted = nameof(Guild_YouWerePromoted);
    public const string Guild_CantDemote = nameof(Guild_CantDemote);
    public const string Guild_MemberDemoted = nameof(Guild_MemberDemoted);
    public const string Guild_YouWereDemoted = nameof(Guild_YouWereDemoted);
    public const string Guild_TransferNeedsOfficer = nameof(Guild_TransferNeedsOfficer);
    public const string Guild_TransferOffered = nameof(Guild_TransferOffered);
    public const string Guild_LeadershipTransferred = nameof(Guild_LeadershipTransferred);
    public const string Guild_MotdSet = nameof(Guild_MotdSet);
    public const string Guild_LabelsSet = nameof(Guild_LabelsSet);
    public const string Guild_ColorSet = nameof(Guild_ColorSet);
    public const string Guild_StandingOverheadOn = nameof(Guild_StandingOverheadOn);
    public const string Guild_StandingOverheadOff = nameof(Guild_StandingOverheadOff);
    public const string Guild_ColorReserved = nameof(Guild_ColorReserved);
    // Daily 00:00 settlement notices, carried on the Guild channel.
    public const string GuildSchedule_TaxPaid = nameof(GuildSchedule_TaxPaid);
    public const string GuildSchedule_TaxMissed = nameof(GuildSchedule_TaxMissed);
    public const string GuildSchedule_PerksRestored = nameof(GuildSchedule_PerksRestored);
    // Guild leveled up (guild XP crossed a level threshold), on the Guild channel.
    public const string Guild_LeveledUp = nameof(Guild_LeveledUp);
    // Vault gold donations + manual late tax payment.
    public const string Guild_DonateOk = nameof(Guild_DonateOk);
    public const string Guild_DonateNeedGold = nameof(Guild_DonateNeedGold);
    public const string Guild_DonateAnnounce = nameof(Guild_DonateAnnounce);
    public const string Guild_DonateValorOk = nameof(Guild_DonateValorOk);
    public const string Guild_DonateNeedValor = nameof(Guild_DonateNeedValor);
    public const string Guild_DonateValorAnnounce = nameof(Guild_DonateValorAnnounce);
    public const string Guild_TaxNothingDue = nameof(Guild_TaxNothingDue);
    public const string Guild_TaxUnaffordable = nameof(Guild_TaxUnaffordable);
    // Daily income (L5 perk gold; territory income later) credited at the 00:00 settlement.
    public const string GuildSchedule_IncomeCredited = nameof(GuildSchedule_IncomeCredited);
    // Territory income credited to the controlling guild's vault at the 00:00 settlement.
    public const string GuildSchedule_TerritoryIncome = nameof(GuildSchedule_TerritoryIncome);
    public const string GuildSchedule_SeasonPlaced = nameof(GuildSchedule_SeasonPlaced);
    public const string GuildSchedule_SeasonMemberSubject = nameof(GuildSchedule_SeasonMemberSubject);
    public const string GuildSchedule_SeasonMemberBody = nameof(GuildSchedule_SeasonMemberBody);
    public const string GuildSchedule_SeasonChampion = nameof(GuildSchedule_SeasonChampion);
    // Territory war night: challenge registration + war-night resolution.
    public const string GuildTerritory_NotATerritory = nameof(GuildTerritory_NotATerritory);
    public const string GuildTerritory_CantChallengeOwn = nameof(GuildTerritory_CantChallengeOwn);
    public const string GuildTerritory_ChallengersFull = nameof(GuildTerritory_ChallengersFull);
    public const string GuildTerritory_AlreadyChallenging = nameof(GuildTerritory_AlreadyChallenging);
    public const string GuildTerritory_CantAfford = nameof(GuildTerritory_CantAfford);
    public const string GuildTerritory_ChallengeOk = nameof(GuildTerritory_ChallengeOk);
    public const string GuildTerritory_Abandoned = nameof(GuildTerritory_Abandoned);
    public const string GuildTerritory_NoChallenge = nameof(GuildTerritory_NoChallenge);
    public const string GuildTerritory_WithdrawnOk = nameof(GuildTerritory_WithdrawnOk);
    public const string GuildTerritory_ContestOwned = nameof(GuildTerritory_ContestOwned);
    public const string GuildTerritory_LayClaim = nameof(GuildTerritory_LayClaim);
    public const string GuildTerritory_WarNightStart = nameof(GuildTerritory_WarNightStart);
    public const string GuildTerritory_WarNightEnd = nameof(GuildTerritory_WarNightEnd);
    public const string GuildTerritory_ResultWon = nameof(GuildTerritory_ResultWon);
    public const string GuildTerritory_ResultDefended = nameof(GuildTerritory_ResultDefended);
    public const string GuildTerritory_ResultAbandonedLost = nameof(GuildTerritory_ResultAbandonedLost);
    public const string GuildTerritory_ResultLost = nameof(GuildTerritory_ResultLost);
    public const string GuildTerritory_OfficerReqChallenge = nameof(GuildTerritory_OfficerReqChallenge);
    // Live contest (KotH) phase notices to participants + the challenge-during-contest guard.
    public const string GuildTerritory_ContestActive = nameof(GuildTerritory_ContestActive);
    public const string GuildTerritory_SetupBegun = nameof(GuildTerritory_SetupBegun);
    public const string GuildTerritory_ContestBegun = nameof(GuildTerritory_ContestBegun);
    public const string GuildTerritory_CooldownBegun = nameof(GuildTerritory_CooldownBegun);
    // Non-participant courtesy warning (setup-present + on entering a contested territory).
    public const string GuildTerritory_NonParticipantWarning = nameof(GuildTerritory_NonParticipantWarning);
    public const string GuildTerritory_ContestSettling = nameof(GuildTerritory_ContestSettling);
    // Guild quests.
    public const string Guild_QuestActive = nameof(Guild_QuestActive);
    public const string Guild_QuestDailyCap = nameof(Guild_QuestDailyCap);
    public const string Guild_QuestNeedGold = nameof(Guild_QuestNeedGold);
    public const string Guild_QuestNoTargets = nameof(Guild_QuestNoTargets);
    public const string Guild_QuestAcquired = nameof(Guild_QuestAcquired);
    public const string Guild_QuestNoneToAbandon = nameof(Guild_QuestNoneToAbandon);
    public const string Guild_QuestAbandoned = nameof(Guild_QuestAbandoned);
    public const string Guild_QuestComplete = nameof(Guild_QuestComplete);
    public const string Guild_QuestProgress = nameof(Guild_QuestProgress);
    public const string Guild_QuestExpired = nameof(Guild_QuestExpired);
    // ── Guild wars ────────────
    // Rejections / confirmations to the acting player.
    public const string GuildWar_NoTarget = nameof(GuildWar_NoTarget);
    public const string GuildWar_AlreadyAtWar = nameof(GuildWar_AlreadyAtWar);
    public const string GuildWar_NeedLevel = nameof(GuildWar_NeedLevel);
    public const string GuildWar_NeedLevelReturn = nameof(GuildWar_NeedLevelReturn);
    public const string GuildWar_TooMany = nameof(GuildWar_TooMany);
    public const string GuildWar_VaultCantAfford = nameof(GuildWar_VaultCantAfford);
    public const string GuildWar_NotDeclaredByYou = nameof(GuildWar_NotDeclaredByYou);
    public const string GuildWar_CantRetractMutual = nameof(GuildWar_CantRetractMutual);
    public const string GuildWar_RetractLocked = nameof(GuildWar_RetractLocked);
    public const string GuildWar_RequestSent = nameof(GuildWar_RequestSent);
    public const string GuildWar_RequestAlreadyPending = nameof(GuildWar_RequestAlreadyPending);
    public const string GuildWar_RequestsFull = nameof(GuildWar_RequestsFull);
    public const string GuildWar_NoSuchRequest = nameof(GuildWar_NoSuchRequest);
    // Officer-request nudges + Leader accept/deny outcomes on the Guild Officer channel (leadership only).
    public const string GuildWar_OfficerReqDeclare = nameof(GuildWar_OfficerReqDeclare);
    public const string GuildWar_OfficerReqReturn = nameof(GuildWar_OfficerReqReturn);
    public const string GuildWar_OfficerReqRetract = nameof(GuildWar_OfficerReqRetract);
    public const string GuildWar_RequestAccepted = nameof(GuildWar_RequestAccepted);
    public const string GuildWar_RequestDenied = nameof(GuildWar_RequestDenied);
    // Guild-channel notices (declaration made / received, daily upkeep, dropped for non-payment).
    public const string GuildWar_YouDeclared = nameof(GuildWar_YouDeclared);
    public const string GuildWar_DeclaredOnYou = nameof(GuildWar_DeclaredOnYou);
    public const string GuildWar_MaintenancePaid = nameof(GuildWar_MaintenancePaid);
    public const string GuildWar_MaintenanceDropped = nameof(GuildWar_MaintenanceDropped);
    // Public announcements (grudge declarations / retractions / reciprocation-to-mutual).
    public const string GuildWar_Declared = nameof(GuildWar_Declared);
    public const string GuildWar_Retracted = nameof(GuildWar_Retracted);
    public const string GuildWar_UpkeepLapsed = nameof(GuildWar_UpkeepLapsed);
    public const string GuildWar_ReachesElevatedPitch = nameof(GuildWar_ReachesElevatedPitch);
    // Attrition / resolution (mutual wars): bankruptcy warning (guild notice) + the decisive/cold end lines.
    public const string GuildWar_UncoveredDeath = nameof(GuildWar_UncoveredDeath);
    public const string GuildWar_WonAttrition = nameof(GuildWar_WonAttrition);
    public const string GuildWar_WonBankruptcy = nameof(GuildWar_WonBankruptcy);
    public const string GuildWar_ColdEnd = nameof(GuildWar_ColdEnd);
    // Re-declare cooldown + "not at war" rejections.
    public const string GuildWar_OnCooldown = nameof(GuildWar_OnCooldown);
    public const string GuildWar_NotAtWar = nameof(GuildWar_NotAtWar);
    // Peace (concession): rejections + officer request + the private pleas/outcomes + public accept.
    public const string GuildWar_PeaceNeedsMutual = nameof(GuildWar_PeaceNeedsMutual);
    public const string GuildWar_PeaceAlreadyOffered = nameof(GuildWar_PeaceAlreadyOffered);
    public const string GuildWar_NoPendingPeace = nameof(GuildWar_NoPendingPeace);
    public const string GuildWar_OfficerReqPeace = nameof(GuildWar_OfficerReqPeace);
    public const string GuildWar_PeaceSought = nameof(GuildWar_PeaceSought);
    public const string GuildWar_PeaceSoughtByThem = nameof(GuildWar_PeaceSoughtByThem);
    public const string GuildWar_PeaceRejected = nameof(GuildWar_PeaceRejected);
    public const string GuildWar_PeaceWithdrawn = nameof(GuildWar_PeaceWithdrawn);
    public const string GuildWar_PeaceWithdrawnByThem = nameof(GuildWar_PeaceWithdrawnByThem);
    public const string GuildWar_PeaceAccepted = nameof(GuildWar_PeaceAccepted);
    public const string GuildWar_PeaceNeedsOffering = nameof(GuildWar_PeaceNeedsOffering);
    public const string GuildWar_OfferingTooLarge = nameof(GuildWar_OfferingTooLarge);
    // Wagers: the matched-ante negotiation notices + rejections, plus the pot payout + forfeit.
    public const string GuildWar_WagerNeedLeader = nameof(GuildWar_WagerNeedLeader);
    public const string GuildWar_WagerNeedsMutual = nameof(GuildWar_WagerNeedsMutual);
    public const string GuildWar_WagerActive = nameof(GuildWar_WagerActive);
    public const string GuildWar_WagerWindowClosed = nameof(GuildWar_WagerWindowClosed);
    public const string GuildWar_WagerTooLarge = nameof(GuildWar_WagerTooLarge);
    public const string GuildWar_WagerNonePending = nameof(GuildWar_WagerNonePending);
    public const string GuildWar_WagerNoneToWithdraw = nameof(GuildWar_WagerNoneToWithdraw);
    public const string GuildWar_WagerCantAfford = nameof(GuildWar_WagerCantAfford);
    public const string GuildWar_WagerOppCantAfford = nameof(GuildWar_WagerOppCantAfford);
    public const string GuildWar_WagerProposed = nameof(GuildWar_WagerProposed);
    public const string GuildWar_WagerProposedByThem = nameof(GuildWar_WagerProposedByThem);
    public const string GuildWar_WagerAccepted = nameof(GuildWar_WagerAccepted);
    public const string GuildWar_WagerRejected = nameof(GuildWar_WagerRejected);
    public const string GuildWar_WagerRejectedByThem = nameof(GuildWar_WagerRejectedByThem);
    public const string GuildWar_WagerWithdrawn = nameof(GuildWar_WagerWithdrawn);
    public const string GuildWar_WagerWithdrawnByThem = nameof(GuildWar_WagerWithdrawnByThem);
    public const string GuildWar_WonPot = nameof(GuildWar_WonPot);
    public const string GuildWar_WonForfeit = nameof(GuildWar_WonForfeit);
    // Post-death standing readout: both guilds' level + current war score.
    public const string GuildWar_DeathScoreReadout = nameof(GuildWar_DeathScoreReadout);
    // Guild chat decorators. The *Ranked variants prepend the speaker's rank word (Leader/Officer);
    // the plain variants are for a rank-less Member speaking in the guild channel.
    public const string Guild_ChatSay = nameof(Guild_ChatSay);
    public const string Guild_ChatSayRanked = nameof(Guild_ChatSayRanked);
    public const string GuildOfficer_ChatSay = nameof(GuildOfficer_ChatSay);
    public const string GuildOfficer_ChatSayRanked = nameof(GuildOfficer_ChatSayRanked);
    public const string Guild_RankLeader = nameof(Guild_RankLeader);
    public const string Guild_RankOfficer = nameof(Guild_RankOfficer);
    // Access-rank words, prefaced onto the speaker's name in non-guild channels (above Player only).
    public const string Access_Monitor = nameof(Access_Monitor);
    public const string Access_Mapper = nameof(Access_Mapper);
    public const string Access_Developer = nameof(Access_Developer);
    public const string Access_Creator = nameof(Access_Creator);
    // Discovery — open-guild applications.
    public const string Guild_AlreadyApplied = nameof(Guild_AlreadyApplied);
    public const string Guild_ApplicationsFull = nameof(Guild_ApplicationsFull);
    public const string Guild_ApplicationSent = nameof(Guild_ApplicationSent);
    public const string Guild_ApplicationReceived = nameof(Guild_ApplicationReceived);
    public const string Guild_MailApprovedSubject = nameof(Guild_MailApprovedSubject);
    public const string Guild_MailApprovedBody = nameof(Guild_MailApprovedBody);
    public const string Guild_MailRejectedSubject = nameof(Guild_MailRejectedSubject);
    public const string Guild_MailRejectedBody = nameof(Guild_MailRejectedBody);
}
