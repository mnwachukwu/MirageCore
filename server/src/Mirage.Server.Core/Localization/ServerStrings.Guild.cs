using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Guild membership: founding, joining, leaving, rank, and the words a guild speaks in.</summary>
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
    // Vault gold donations + manual late tax payment.
    public const string Guild_DonateOk = nameof(Guild_DonateOk);
    public const string Guild_DonateNeedGold = nameof(Guild_DonateNeedGold);
    public const string Guild_DonateAnnounce = nameof(Guild_DonateAnnounce);
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
