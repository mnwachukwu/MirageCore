using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Gold: what the world pays, what things cost, and what wear costs to undo.  Drop-chance percentages
/// live in <see cref="Constants"/> since they're standalone game rules, not formula coefficients.
///
/// <para>EVERYTHING HERE IS QUOTED AGAINST ONE CURVE — <see cref="ExpectedGoldPerTier"/>.  That is the
/// whole point of the file.  A price fixed as a constant, or derived from item <c>Power</c>, cannot stay
/// meaningful across a progression: income over the tier range spans about 137,000x where <c>Power</c>
/// spans 65x and a constant spans 1x, so anything priced off either is significant at the bottom and free
/// by the middle.  Quoting every sink as a share of the same curve is what keeps them in step when any one
/// of them is retuned.</para>
///
/// <para><b>The curve's shape is a default, not a law.</b>  Its two coefficients were fitted to one set of
/// authored drop tables; a game that writes its own content should refit them, and one whose shops name
/// every price outright never calls this file at all.</para>
/// </summary>
public static class EconomyFormulas
{
    private const double PercentDenominator = 100.0;
    private const int EquipmentDamageFloor = 1;

    // ── The backbone ─────────────────────────────────────────────────────────
    // Gold a player is expected to earn crossing one tier, fitted to a set of AUTHORED drop tables rather
    // than chosen.
    //
    //     goldPerTier = GoldCurveConstant x tier^GoldCurveExponent
    //
    // Log-log least squares over three content bands gave 4.112 x T^2.675 at R2 = 0.9886 — a clean power
    // law even though nobody designed it to be. The constant is rounded to 4.0, 3% under the fit and far
    // inside the noise, because the curve is a DESIGN TARGET rather than a prediction: actual income wobbles
    // roughly 0.5x-2.7x around it inside a band as the mix of what is killed changes, and nothing should
    // chase that.
    //
    // WHY IT IS STEEPER THAN A POWER-BASED PRICE. Item Power tracks a stat budget, which grows about
    // linearly, so a Power-based price falls behind income by T^1.675 — a factor of ~11,000 across the
    // range. That is the whole reason a curve exists here instead of a multiplier on Power.
    private const double GoldCurveConstant = 4.0;
    private const double GoldCurveExponent = 2.675;

    /// <summary>Gold a player is expected to earn crossing <paramref name="tier"/> — the reference every
    /// price and sink here is quoted against.  Tier 1 pays 4; tier 255 pays ~11.3M.</summary>
    public static long ExpectedGoldPerTier(int tier) =>
        (long)Math.Round(Math.Pow(Math.Max(tier, 1), GoldCurveExponent) * GoldCurveConstant,
            MidpointRounding.AwayFromZero);

    /// <summary>Gold earned across the whole <see cref="Constants.GearTierSpan"/>-tier rung a piece of gear
    /// covers — the natural unit for pricing equipment, since a piece is bought once and worn for the whole
    /// rung.  Summed rather than approximated as 5x because the curve bends steeply at the bottom, where
    /// 5 x the tier-1 figure would understate the rung by a third.</summary>
    public static long ExpectedGoldForRung(int tier)
    {
        long total = 0;
        for (int t = Math.Max(tier, 1); t < Math.Max(tier, 1) + Constants.GearTierSpan; t++)
            total += ExpectedGoldPerTier(Math.Min(t, Constants.MaxItemTier));
        return total;
    }

    // ── Item pricing ─────────────────────────────────────────────────────────
    // The engine stores no price on an item: every price is a BarterItemRecord line on a shop. So a shop
    // that does not name one needs the number DERIVED, or a few hundred authored items become a thousand
    // hand-typed figures with nothing keeping them consistent with each other or with income.
    //
    // GEAR IS CHEAP; UPKEEP BITES. One piece costs a fortieth of the gold earned across the rung it covers,
    // so buying up is never the thing a player saves for. What actually consumes income is repair and
    // consumables.

    /// <summary>Share of a rung's income that ONE piece of equipment costs.
    ///
    /// <para>How many pieces a full kit is depends on how many slots the game declared, so the total is a
    /// game's to check rather than a number in here: at four slots this puts a kit at a tenth of the rung,
    /// which is the shape this figure was chosen against.</para></summary>
    private const double EquipmentTierShare = 0.025;

    /// <summary>Share of ONE tier's income that a potion costs.  Consumables are priced per tier rather
    /// than per rung because they are bought continuously rather than once a rung.</summary>
    private const double PotionTierShare = 0.002;

    /// <summary>What a shop pays for an item a player brings in, as a percent of its
    /// <see cref="ItemValue"/>.  Well under half, so vendoring drops supplements income without becoming
    /// the main way to earn — the drop tables already pay ~22,000 items across the max band.</summary>
    public const int SellBackPercent = 25;

    // The Power a medium-bulk piece carries at its own tier, so pricing can ask "how strong is this piece
    // FOR its tier" — a heavy piece costs 1.25x a medium one at the same tier and a light piece 0.75x, which
    // falls out of the ratio directly. The ramp below is the budget a tier is worth (a base at tier 1, a
    // fixed amount added each rung after) and the share of it a medium piece carries.
    private const double ReferencePowerShare = 0.40;
    private const int ReferencePowerBase = 20;
    private const int ReferencePowerPerTier = 3;

    /// <summary>Power a medium-bulk piece carries at <paramref name="tier"/> — the divisor that turns an
    /// item's Power into "how strong for its tier", so bulk prices itself.</summary>
    public static int ReferencePower(int tier) =>
        Math.Max(1, (int)Math.Round(
            (ReferencePowerBase + ReferencePowerPerTier * (Math.Max(tier, 1) - 1)) * ReferencePowerShare,
            MidpointRounding.AwayFromZero));

    /// <summary>What a shop charges for <paramref name="item"/>, in gold.
    ///
    /// <para>Currency and keys return 0: gold has no price in gold, and a key is quest furniture rather
    /// than stock.  A shop CAN still trade either — a BarterItemRecord names both sides explicitly — this
    /// only says the derivation declines to invent a number for them.</para></summary>
    public static int ItemValue(ItemRecord item)
    {
        // None is in this list for the OPPOSITE reason to the other two. Currency and Key have no worth to
        // derive; None is what TREASURE is typed as, and its worth is the entire point — it is simply
        // authored rather than derived, which is exactly the gap Price exists to fill.
        if (item.Type is ItemType.Currency or ItemType.Key or ItemType.None) return 0;

        if (ItemRecord.IsEquipment(item.Type))
        {
            double forTier = ExpectedGoldForRung(item.Tier) * EquipmentTierShare;
            double bulk = (double)Math.Max((int)item.Power, 1) / ReferencePower(item.Tier);
            return Clamp(forTier * bulk);
        }

        return Clamp(ExpectedGoldPerTier(item.Tier) * PotionTierShare);
    }

    /// <summary>What a shop pays for an item a player sells, in gold — <see cref="SellBackPercent"/>% of
    /// <see cref="ItemValue"/>, scaled by the piece's CONDITION.  A pristine piece fetches the full
    /// fraction; one at half durability fetches half of it; a broken one is scrap and fetches nothing.
    /// Floored at 1 for anything still intact, so a low-tier piece is worth carrying back rather than
    /// dropping.
    ///
    /// <para><paramref name="currentDurability"/> is REQUIRED rather than defaulted, deliberately. Every
    /// real sell path holds an inventory slot and therefore knows the wear; a default would let that path
    /// quietly pay full price for a ruined item, which is precisely the bug this parameter exists to
    /// prevent. Items with no durability budget — consumables and keys — ignore it entirely.</para></summary>
    public static int ItemSellValue(ItemRecord item, int currentDurability)
    {
        int value = ItemValue(item);
        if (value <= 0) return 0;
        int full = (int)(value * SellBackPercent / PercentDenominator);

        int maxDur = item.Durability;
        if (maxDur <= 0) return Math.Max(1, full);   // condition does not apply to this item type

        int dur = Math.Clamp(currentDurability, 0, maxDur);
        if (dur <= 0) return 0;   // broken: the shop buys scrap for nothing rather than paying for a repair job
        return Math.Max(1, (int)((long)full * dur / maxDur));
    }

    private static int Clamp(double gold) =>
        (int)Math.Clamp(Math.Round(gold, MidpointRounding.AwayFromZero), 1, int.MaxValue);

    // ── Wear and repair ──────────────────────────────────────────────────────

    /// <summary>Each equipped item loses <paramref name="percentOfMax"/>% of its max durability,
    /// floor 1.  Used for the normal (10%) and PK (20%) death penalties.</summary>
    public static int EquipmentDamageOnDeath(int maxDur, int percentOfMax) =>
        Math.Max((int)Math.Round(maxDur * percentOfMax / PercentDenominator, MidpointRounding.AwayFromZero), EquipmentDamageFloor);

    // ── The repair rate, and why it is keyed on Power ────────────────────────
    // Gold per durability point is Power / RepairPowerDivisor. Keyed on POWER, not the item's value:
    // value grows as L^2.675 (it is a share of a rung's income) while the gold a fight earns grows as
    // about T^1.3, so a value-priced repair is wrong by an exponent — 22% of a tier's income at tier 20
    // against 5,433% at tier 235, which no choice of percentage fixes. Power grows about linearly in
    // tier. At 40, upkeep climbs as the game gets harder and still leaves most of the take.
    //
    // TUNING: raise the divisor to make repair cheaper. Two traps when re-measuring, both of which have
    // already produced a wrong answer:
    //   Compare like for like. Repair per POINT against income per TIER makes Power look hopelessly
    //   behind, but durability lost per tier and income both scale with the same play and cancel.
    //   Count SLOTS, not events. Whatever a game spends durability on may spend it on several worn
    //   pieces at once, so a per-event figure understates the bill by however many slots it touches.
    private const double RepairPowerDivisor = 40.0;

    /// <summary>A full repair may never cost more than this percent of a new piece.  Repairing something
    /// for more than it costs to replace is a trap: the shop offers a service nobody should take, and a
    /// player who takes it is worse off purely for not having done the arithmetic.</summary>
    private const int RepairCapPercentOfPrice = 50;

    /// <summary>Gold per durability point on a piece of the given <paramref name="power"/>, BEFORE the
    /// replacement-cost cap — the one place the repair rate is stated.</summary>
    public static double RepairGoldPerPoint(int power) => Math.Max(power, 0) / RepairPowerDivisor;

    /// <summary>Gold to repair <paramref name="durabilityPoints"/> of durability on <paramref name="item"/>,
    /// floored at 1.  Points beyond the item's maximum are ignored rather than charged.
    ///
    /// <para>The Power rate is capped so a full repair stays under
    /// <see cref="RepairCapPercentOfPrice"/>% of the item's price. The cap only binds at the BOTTOM of the
    /// ladder, and it has to: a tier-1 shield carries 100 durability but costs 11 gold, so the raw rate
    /// would charge 60 to restore an item replaceable for 11. By the mid band a full repair is already a
    /// few percent of the price and the cap never touches it.</para>
    ///
    /// <para>The single source of truth for the repair formula — the shop repair path and the guild-war
    /// vault-repair sink both use it, so the war "vault pays 75% of the repair cost" is priced by the
    /// normal repair formula.</para></summary>
    public static int RepairCost(int durabilityPoints, ItemRecord item)
    {
        int maxDur = Math.Max((int)item.Durability, 1);
        int points = Math.Clamp(durabilityPoints, 0, maxDur);
        return Math.Max(1, (int)Math.Round(points * EffectiveRepairRate(item), MidpointRounding.AwayFromZero));
    }

    /// <summary>The Power rate, lowered where a full repair would otherwise beat the price of a new one.</summary>
    private static double EffectiveRepairRate(ItemRecord item)
    {
        double rate = RepairGoldPerPoint(item.Power);
        int price = ItemValue(item);
        if (price <= 0) return rate;   // nothing derivable to cap against
        int maxDur = Math.Max((int)item.Durability, 1);
        double capped = price * (RepairCapPercentOfPrice / PercentDenominator) / maxDur;
        return Math.Min(rate, capped);
    }

    /// <summary>Gold per durability point, FOR DISPLAY ONLY — the shop panel quotes a rate alongside the
    /// total.  Derived from <see cref="RepairCost"/> rather than the other way round, so there is still
    /// exactly one repair rule.
    ///
    /// <para>Do not buy with it.  It is an integer division of an exact pro-rata cost, so it rounds DOWN,
    /// and <c>gold / rate</c> therefore names a point count that can cost more than <c>gold</c> — one gold
    /// over, on a partial repair, which is enough to charge a player more than they have.  Use
    /// <see cref="RepairPointsAffordable"/> for anything that spends.</para></summary>
    public static int RepairRatePerPoint(ItemRecord item) =>
        Math.Max(1, RepairCost(Math.Max((int)item.Durability, 1), item) / Math.Max((int)item.Durability, 1));

    /// <summary>The most durability points <paramref name="gold"/> can actually buy on
    /// <paramref name="item"/> — the largest N whose <see cref="RepairCost"/> is still within budget, so a
    /// partial repair can never overcharge.  0 means not even one point is affordable.</summary>
    public static int RepairPointsAffordable(long gold, ItemRecord item)
    {
        if (gold <= 0) return 0;
        int maxDur = Math.Max((int)item.Durability, 1);
        double perPoint = EffectiveRepairRate(item);
        if (perPoint <= 0) return maxDur;   // nothing to charge: the whole repair is free

        // Start from the exact proportion, then walk down the rounding. The estimate is never more than a
        // point or two high, so this settles immediately rather than scanning.
        int points = (int)Math.Clamp(Math.Floor(gold / perPoint), 0, maxDur);
        while (points > 0 && RepairCost(points, item) > gold) points--;
        return points;
    }

    // ── Sinks ────────────────────────────────────────────────────────────────

    /// <summary>Gold to set your spawn point at an inn. Flat, like every other price here.</summary>
    public static long InnSpawnCost() => Constants.SpawnCostMinimum;

    // ── Postage ──────────────────────────────────────────────────────────────
    // Two flat parts plus a share of what is in the parcel.
    //
    // The scaling part is keyed on the PARCEL rather than on the payer, because every flat fee here is paid
    // by whoever CLICKS: a cost scaled to the actor is minimized by handing the job to an alt, and a
    // shipment's worth cannot be.
    //
    // Sits deliberately below the 5% that MarketSystem.SaleTax and MailSystem.CodTax both charge: those
    // two buy escrow (and, for the market, discovery), and plain mail buys neither. The 3-point spread is
    // what a guaranteed payment is worth.

    /// <summary>Gold value of one attached stack, for postage: gold rides as a currency attachment so its
    /// worth is simply the amount, and everything else is its stored unit price times the stack size.
    /// Stated once here because the server charges it and the client previews it.</summary>
    public static long MailAttachmentValue(int itemNum, int quantity, int unitPrice) =>
        itemNum == Constants.GoldItemIndex
            ? Math.Max(0, quantity)
            : (long)Math.Max(0, unitPrice) * Math.Max(0, quantity);

    /// <summary>Gold to send one piece of mail: a base fee, a per-stack handling fee, and
    /// <see cref="Constants.MailAttachedValuePercent"/>% of <paramref name="attachedValue"/> (the summed
    /// <see cref="MailAttachmentValue"/> of everything in the parcel).</summary>
    public static long MailSendCost(int attachments, long attachedValue = 0) =>
        Constants.MailBaseSendCost
        + Math.Max(0, attachments) * (long)Constants.MailAttachmentSendCost
        + Math.Max(0, attachedValue) * Constants.MailAttachedValuePercent / 100;

    // ── The caster/warrior upkeep anchor ─────────────────────────────────────
    // A warrior's cost of fighting is repair. A caster's is reagents, at 1 gold each. For the two to cost
    // the same to play, reagents-per-cast has to equal the gold a warrior burns per swing — and that is a
    // number only this class knows, since it falls out of RepairCost.
    //
    // Parity has to be DERIVED from the repair rule here, never restated next to it. A restated copy goes
    // stale the moment repair is retuned, and it fails silently — the two sides can drift from 1.3x apart
    // at tier 20 to 87x apart at 255, all in the caster's favor, with nothing throwing.

    /// <summary>Gold burned repairing one point of durability on on-tier gear at
    /// <paramref name="tier"/> — the reference an upkeep cost elsewhere can be matched to.
    ///
    /// <para>Priced against a synthetic reference piece (the tier's medium bulk at
    /// <see cref="ReferencePower"/>) rather than whatever the player happens to be holding, so the two
    /// classes are compared on the same footing and a caster's costs do not move when a warrior swaps
    /// weapons.</para></summary>
    public static double RepairGoldPerDurabilityPoint(int tier) =>
        RepairGoldPerPoint(ReferencePower(tier));
}
