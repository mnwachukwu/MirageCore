using Mirage.Shared.Records;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// What the engine's own conveniences cost, as the loaded game declared them.
///
/// <para><b>Core derives no price.</b> A shop charges what its author wrote on the item, and everything
/// else here is a flat figure or a percentage of one. An engine that modelled an economy — fitting a
/// curve to a tier, pricing a repair off an item's power — would be inventing numbers for a world whose
/// money it cannot see, and it would be wrong for every world that authors its own.</para>
///
/// <para><b>Declaring nothing is free.</b> Every figure defaults to zero, and zero means the engine
/// charges nothing and imposes nothing: free postage, no sale tax, a shop that pays nothing for what a
/// player brings in, repairs that cost nothing, and no wait on the way home. That is a coherent world —
/// the one an engine with no game loaded runs.</para>
///
/// <para>The arithmetic lives here rather than at the call sites because both ends do it: the server
/// charges and the client previews, and two copies of a percentage drift into a preview that lies.</para>
/// </summary>
public sealed record GamePrices
{
    /// <summary>What an engine with no game loaded charges, which is nothing.</summary>
    public static readonly GamePrices Free = new();

    /// <summary>Gold to found a guild.</summary>
    public int GuildCost { get; init; }

    /// <summary>Gold to move your respawn point to an inn.</summary>
    public int InnSpawnCost { get; init; }

    /// <summary>Gold a letter costs before anything is attached to it.</summary>
    public int MailBaseCost { get; init; }

    /// <summary>Gold each attachment adds.</summary>
    public int MailAttachmentCost { get; init; }

    /// <summary>A percentage of what is IN the parcel, added to the postage. Keyed on the shipment rather
    /// than the sender, so handing the job to an alt saves nothing.</summary>
    public int MailValuePercent { get; init; }

    /// <summary>A percentage of a marketplace sale, taken from the seller.</summary>
    public int MarketTaxPercent { get; init; }

    /// <summary>A percentage of an item's authored price, which is what a shop pays for one a player
    /// brings in. Well under half in most worlds, so vendoring drops supplements income rather than
    /// becoming the way to earn.</summary>
    public int SellBackPercent { get; init; }

    /// <summary>A percentage of an item's authored price, which is what restoring it from broken to whole
    /// costs. Part of a repair costs that share of it.</summary>
    public int RepairPercent { get; init; }

    /// <summary>Seconds between one trip home and the next.</summary>
    public int HomeCooldownSeconds { get; init; }

    private const double Percent = 100.0;

    /// <summary>Postage on one letter: the base, each attachment, and a share of what is inside.</summary>
    public long MailSendCost(int attachments, long attachedValue = 0) =>
        MailBaseCost
        + (long)MailAttachmentCost * Math.Max(attachments, 0)
        + (long)(Math.Max(attachedValue, 0) * MailValuePercent / Percent);

    /// <summary>What is in a parcel, for the postage above: an item's authored price times how many of
    /// it. Nothing to do with a declared rate, so it is the same answer in every world.</summary>
    public static long AttachmentValue(int quantity, int unitPrice) =>
        (long)Math.Max(quantity, 0) * Math.Max(unitPrice, 0);

    /// <summary>The tax on a marketplace sale at <paramref name="price"/>.</summary>
    public int MarketTax(int price) =>
        (int)((long)Math.Max(price, 0) * MarketTaxPercent / 100);

    /// <summary>What a shop pays for <paramref name="item"/> at <paramref name="currentDurability"/> —
    /// <see cref="SellBackPercent"/>% of its authored price, scaled by condition. A pristine piece fetches
    /// the whole fraction, one at half durability half of it, and a broken one is scrap.
    ///
    /// <para><paramref name="currentDurability"/> is required rather than defaulted: every real sell path
    /// holds an inventory slot and knows the wear, and a default would let one quietly pay full price for
    /// a ruined item. An item with no durability budget ignores it.</para></summary>
    public int SellValue(ItemRecord item, int currentDurability)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (SellBackPercent <= 0 || item.Price <= 0) return 0;

        int full = (int)(item.Price * SellBackPercent / Percent);
        if (full <= 0) return 0;

        int maxDur = item.Durability;
        if (maxDur <= 0) return Math.Max(1, full);

        int dur = Math.Clamp(currentDurability, 0, maxDur);
        if (dur <= 0) return 0;   // broken: a shop buys scrap for nothing rather than paying for a repair job
        return Math.Max(1, (int)((long)full * dur / maxDur));
    }

    /// <summary>Gold to put <paramref name="durabilityPoints"/> of condition back into
    /// <paramref name="item"/>. A whole repair costs <see cref="RepairPercent"/>% of the item's authored
    /// price; part of one costs that share. Points past the item's maximum are ignored rather than
    /// charged.</summary>
    public int RepairCost(int durabilityPoints, ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (RepairPercent <= 0 || item.Price <= 0) return 0;

        int maxDur = Math.Max((int)item.Durability, 1);
        int points = Math.Clamp(durabilityPoints, 0, maxDur);
        if (points <= 0) return 0;

        double whole = item.Price * RepairPercent / Percent;
        return Math.Max(1, (int)Math.Round(whole * points / maxDur, MidpointRounding.AwayFromZero));
    }

    /// <summary>How many points of condition <paramref name="gold"/> buys on <paramref name="item"/> —
    /// what the shop's partial-repair offer is worth to somebody who cannot afford the whole job.</summary>
    public int RepairPointsAffordable(long gold, ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        int maxDur = Math.Max((int)item.Durability, 1);
        if (RepairPercent <= 0 || item.Price <= 0) return maxDur;   // nothing to charge: the whole job is free
        if (gold <= 0) return 0;

        double perPoint = item.Price * RepairPercent / Percent / maxDur;
        if (perPoint <= 0) return maxDur;

        // Start from the exact proportion, then walk down the rounding. The estimate is never more than a
        // point or two high, so this settles immediately rather than scanning.
        int points = (int)Math.Clamp(Math.Floor(gold / perPoint), 0, maxDur);
        while (points > 0 && RepairCost(points, item) > gold) points--;
        return points;
    }
}
