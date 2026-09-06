namespace Mirage.Shared.Records;

/// <summary>One message in an account's mailbox. Net-new primitive (only a global MOTD existed
/// before); the guild layer is the first sender (application / notification mail from "System"), and
/// it is deliberately shaped to grow into a full player-to-player mail system with attachments later.
/// Carried whole on the wire (shared with the client), so keep it a plain POCO.</summary>
public sealed class MailMessage
{
    /// <summary>Stable per-mailbox id (1-based), used to mark-read / delete a specific message.</summary>
    public int Id { get; set; }
    /// <summary>Sender label. "System" for engine-generated mail (guild invites, results, ...).</summary>
    public string Sender { get; set; } = "";
    /// <summary>Short line shown in the inbox list.</summary>
    public string Subject { get; set; } = "";
    /// <summary>Full text shown when the message is opened.</summary>
    public string Body { get; set; } = "";
    /// <summary>UTC-seconds the message was posted (sender's send time).</summary>
    public long TimeUtc { get; set; }
    /// <summary>UTC-seconds this message matures (becomes claimable). Equals <see cref="TimeUtc"/> for instant
    /// (system) mail; player-origin mail (P2P / marketplace) sets it 10-15 min ahead so the message rides
    /// "in transit" — grayed and claim-blocked — on both ends until it matures. 0 on legacy mail reads as
    /// already delivered.</summary>
    public long DeliverAt { get; set; }
    /// <summary>UTC-seconds this message auto-deletes (30 days after it matures — see
    /// <c>Constants.MailRetentionSeconds</c>). A normal message is removed by the expiry sweep; a CoD message
    /// returns to sender instead. 0 on legacy mail reads as "never expires".</summary>
    public long DeleteAt { get; set; }
    /// <summary>Collect-on-Delivery price in gold: >0 marks this a CoD whose attachments are LOCKED until the
    /// recipient pays. On the recipient's (unclaimed) inbox copy this also shortens the lifetime to a 3-day
    /// RETURN clock (<c>Constants.CodLifetimeSeconds</c>) — the expiry sweep mails the items back to the sender
    /// instead of deleting. Paying clears it to 0 (the message becomes a normal claimed mail). The sender's
    /// outbox receipt carries the price for display but keeps the normal 30-day retention. 0 = ordinary mail.</summary>
    public int CodPrice { get; set; }
    /// <summary>Account this message was addressed TO. Set on OUTBOX copies (shown as the "To" party); left
    /// empty on received inbox mail, which shows <see cref="Sender"/> instead.</summary>
    public string Recipient { get; set; } = "";
    public bool IsRead { get; set; }
    /// <summary>Item/gold stacks attached to this message. Gold rides as a currency attachment
    /// (<c>ItemNum == Constants.GoldItemIndex</c>), so claiming flows through the one ItemSystem.GiveItem
    /// chokepoint. Empty = nothing attached.</summary>
    public List<MailAttachment> Attachments { get; set; } = new();

    /// <summary>Deep copy: the <see cref="Attachments"/> list is cloned, not shared, because a mailbox is
    /// snapshotted for an off-thread account write.</summary>
    public MailMessage Clone()
    {
        var copy = (MailMessage)MemberwiseClone();
        copy.Attachments = Attachments.Select(a => a.Clone()).ToList();
        return copy;
    }
}
