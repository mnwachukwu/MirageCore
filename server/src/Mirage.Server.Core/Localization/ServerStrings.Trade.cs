using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Player-to-player exchange: marketplace listings, direct trade, mail and its
/// cash-on-delivery terms, and the friends/ignore lists.</summary>
public static partial class ServerStrings
{
    // ── Marketplace ─────────────────────────────────────────────────────────────
    public const string Market_Sender = nameof(Market_Sender);
    public const string Market_NotNearInn = nameof(Market_NotNearInn);
    public const string Market_BadPrice = nameof(Market_BadPrice);
    public const string Market_TooManyListings = nameof(Market_TooManyListings);
    public const string Market_CannotList = nameof(Market_CannotList);
    public const string Market_CannotListItem = nameof(Market_CannotListItem);
    public const string Market_Listed = nameof(Market_Listed);
    public const string Market_ListingGone = nameof(Market_ListingGone);
    public const string Market_CannotBuyOwn = nameof(Market_CannotBuyOwn);
    public const string Market_NotEnoughGold = nameof(Market_NotEnoughGold);
    public const string Market_Bought = nameof(Market_Bought);
    public const string Market_Canceled = nameof(Market_Canceled);
    public const string Market_BoughtSubject = nameof(Market_BoughtSubject);
    public const string Market_BoughtBody = nameof(Market_BoughtBody);
    public const string Market_SoldSubject = nameof(Market_SoldSubject);
    public const string Market_SoldBody = nameof(Market_SoldBody);
    public const string Market_ReturnSubject = nameof(Market_ReturnSubject);
    public const string Market_ReturnBody = nameof(Market_ReturnBody);
    public const string Market_ExpiredSubject = nameof(Market_ExpiredSubject);
    public const string Market_ExpiredBody = nameof(Market_ExpiredBody);

    // ── Direct trade ────────────────────────────────────────────────────────────
    public const string Trade_Sender = nameof(Trade_Sender);
    public const string Trade_TargetNotOnline = nameof(Trade_TargetNotOnline);
    public const string Trade_CannotTradeSelf = nameof(Trade_CannotTradeSelf);
    /// <summary>The target is a corpse. Named separately from the living refusals so the asker knows to wait
    /// rather than to move closer.</summary>
    public const string Trade_TargetIsDead = nameof(Trade_TargetIsDead);
    public const string Trade_AlreadyTrading = nameof(Trade_AlreadyTrading);
    public const string Trade_OutOfRange = nameof(Trade_OutOfRange);
    public const string Trade_RequestSent = nameof(Trade_RequestSent);
    public const string Trade_RequestReceived = nameof(Trade_RequestReceived);
    public const string Trade_NoRequest = nameof(Trade_NoRequest);
    public const string Trade_Declined = nameof(Trade_Declined);
    public const string Trade_OfferFull = nameof(Trade_OfferFull);
    public const string Trade_CannotOfferItem = nameof(Trade_CannotOfferItem);
    public const string Trade_NoSpace = nameof(Trade_NoSpace);
    public const string Trade_Failed = nameof(Trade_Failed);
    public const string Trade_Complete = nameof(Trade_Complete);
    public const string Trade_Canceled = nameof(Trade_Canceled);
    public const string Trade_ReturnedSubject = nameof(Trade_ReturnedSubject);
    public const string Trade_ReturnedBody = nameof(Trade_ReturnedBody);

    // "System" mail sender label (engine-generated mail).
    public const string Mail_SystemSender = nameof(Mail_SystemSender);
    public const string Mail_Sent = nameof(Mail_Sent);
    public const string Mail_CannotMailItem = nameof(Mail_CannotMailItem);
    public const string Mail_NoRecipient = nameof(Mail_NoRecipient);
    public const string Mail_ReturnedSubject = nameof(Mail_ReturnedSubject);
    public const string Mail_ReturnedBody = nameof(Mail_ReturnedBody);
    public const string Mail_CannotMailSelf = nameof(Mail_CannotMailSelf);
    public const string Mail_MultiNoAttachments = nameof(Mail_MultiNoAttachments);
    public const string Mail_CannotAfford = nameof(Mail_CannotAfford);
    public const string Mail_NoSubjectDefault = nameof(Mail_NoSubjectDefault);
    public const string Mail_SentToMany = nameof(Mail_SentToMany);
    public const string Mail_TooManyRecipients = nameof(Mail_TooManyRecipients);
    public const string Mail_CannotAffordCod = nameof(Mail_CannotAffordCod);
    public const string Mail_CodNoRoom = nameof(Mail_CodNoRoom);
    public const string Mail_CodPaid = nameof(Mail_CodPaid);
    public const string Mail_CodNeedsItem = nameof(Mail_CodNeedsItem);
    public const string Mail_CodSingleOnly = nameof(Mail_CodSingleOnly);
    public const string Mail_CodPaidSubject = nameof(Mail_CodPaidSubject);
    public const string Mail_CodPaidBody = nameof(Mail_CodPaidBody);
    public const string Mail_CodReturnedSubject = nameof(Mail_CodReturnedSubject);
    public const string Mail_CodReturnedBody = nameof(Mail_CodReturnedBody);

    // ── Social (friends / ignore) ─────────────────────────────────────────────
    public const string Social_FriendAdded = nameof(Social_FriendAdded);
    public const string Social_FriendRemoved = nameof(Social_FriendRemoved);
    public const string Social_AlreadyFriend = nameof(Social_AlreadyFriend);
    public const string Social_IgnoreAdded = nameof(Social_IgnoreAdded);
    public const string Social_IgnoreRemoved = nameof(Social_IgnoreRemoved);
    public const string Social_AlreadyIgnored = nameof(Social_AlreadyIgnored);
    public const string Social_TargetOffline = nameof(Social_TargetOffline);
    public const string Social_CantAddSelf = nameof(Social_CantAddSelf);
}
