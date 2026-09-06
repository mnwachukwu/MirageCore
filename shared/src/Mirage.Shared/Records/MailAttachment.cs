namespace Mirage.Shared.Records;

/// <summary>One item stack attached to a mail message. Gold rides as a currency attachment
/// (<c>ItemNum == Constants.GoldItemIndex</c>) so claiming an attachment always flows through the one
/// <c>ItemSystem.GiveItem</c> chokepoint. Carried whole on the wire (shared with the client), so keep
/// it a plain POCO.</summary>
public sealed class MailAttachment
{
    /// <summary>1-based item number (0 = none). Gold uses <c>Constants.GoldItemIndex</c>.</summary>
    public int ItemNum { get; set; }
    /// <summary>Stack size for items, or the gold/currency amount.</summary>
    public int Quantity { get; set; }
    /// <summary>Durability carried from the sender so a worn item does not reset to max when claimed.
    /// 0 for currency and fresh items (claim then applies the item's default durability).</summary>
    public int Dur { get; set; }
    /// <summary>True once this stack has been collected into the recipient's inventory.</summary>
    public bool Claimed { get; set; }

    public MailAttachment Clone() => (MailAttachment)MemberwiseClone();
}
