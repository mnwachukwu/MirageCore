using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Gold: what the world pays, what things cost, and what wear costs to undo.  Drop-chance percentages
/// live in <see cref="Constants"/> since they're standalone game rules, not formula coefficients.
///
/// <para>EVERYTHING HERE IS QUOTED AGAINST ONE CURVE — <see cref="ExpectedGoldPerLevel"/>.  That is the
/// whole point of the file.  Prices, repair, inn rest and the guild sinks were each a standalone constant
/// or a rule of their own before, and they drifted apart the moment the drop tables were authored: income
/// across the level range spans about 137,000x, while item <c>Power</c> spans 65x and a flat constant
/// spans 1x.  Anything priced off Power or off nothing is therefore meaningful at level 1 and free by
/// level 100, which is exactly what measurement found.  Quoting every sink as a share of the same curve
/// is what keeps them in step when any one of them is retuned.</para>
/// </summary>
public static class EconomyFormulas
{
    private const double PercentDenominator = 100.0;
    private const int EquipmentDamageFloor = 1;

    // ── The backbone ─────────────────────────────────────────────────────────
    // Gold earned crossing one level, fitted to the AUTHORED drop tables rather than chosen.
    //
    //     goldPerLevel = GoldCurveConstant x level^GoldCurveExponent
    //
    // Log-log least squares over the three content bands gives 4.112 x L^2.675 at R2 = 0.9886 — the shape
    // is a clean power law even though no one designed it to be. The constant is rounded to 4.0 (3% under
    // the fit, far inside the noise) because the curve is a DESIGN TARGET, not a prediction: actual income
    // wobbles roughly 0.5x-2.7x around it inside a band as the mob mix changes, and nothing should chase
    // that. Re-derive both numbers with .Tools/Simulations/GoldEconomy/gold-income.cs after any change to
    // the drop tables, the bestiary levels, or the EXP curve — all three feed it.
    //
    // WHY IT IS STEEPER THAN THE EXP CURVE. TnlForLevel is 500 x L^2, so kills/level grows about linearly;
    // gold per kill then grows about L^1.3 on top of that, and the product is L^2.675. This is why nothing
    // can be priced off item Power: Power tracks the stat budget, which is LINEAR in level (~1.2L), so a
    // Power-based price falls behind income by L^1.675 — a factor of ~11,000 across the range.
    private const double GoldCurveConstant = 4.0;
    private const double GoldCurveExponent = 2.675;

    /// <summary>Gold a player is expected to earn crossing <paramref name="level"/> — the reference every
    /// price and sink in the game is quoted against.  Level 1 pays 4; level 255 pays ~11.3M.</summary>
    public static long ExpectedGoldPerLevel(int level) =>
        (long)Math.Round(Math.Pow(Math.Max(level, 1), GoldCurveExponent) * GoldCurveConstant,
            MidpointRounding.AwayFromZero);

    /// <summary>Gold earned across the whole <see cref="Constants.GearTierLevels"/>-level rung a gear tier
    /// covers — the natural unit for pricing equipment, since a tier is bought once and worn for the whole
    /// rung.  Summed rather than approximated as 5x because the curve bends steeply at low levels, where
    /// 5 x goldPerLevel(1) would understate the rung by a third.</summary>
    public static long ExpectedGoldForTier(int level)
    {
        long total = 0;
        for (int L = Math.Max(level, 1); L < Math.Max(level, 1) + Constants.GearTierLevels; L++)
            total += ExpectedGoldPerLevel(Math.Min(L, Constants.MaxLevel));
        return total;
    }

    // ── Item pricing ─────────────────────────────────────────────────────────
    // The engine stores no price anywhere: ItemRecord carries Name/Pic/Type/Durability/VitalAmount/
    // SpellNum/Power/LevelReq and nothing else, and every price in the game is a BarterItemRecord line on a
    // shop. So a price has to be DERIVED, or the armory's 471 items become ~900 hand-typed numbers with
    // nothing keeping them consistent with each other or with income.
    //
    // GEAR IS CHEAP; SINKS BITE. A full four-piece tier upgrade costs about a tenth of the gold earned
    // across the rung it covers, so buying up is never the thing a player is saving for. The drains that
    // actually consume income are repair, consumables and the guild-scale sinks below.

    /// <summary>Share of a tier's rung income that one piece of equipment costs.  Four slots at
    /// <c>0.025</c> puts a full kit at a tenth of the rung.</summary>
    private const double EquipmentTierShare = 0.025;

    /// <summary>Share of a tier's rung income that a spell scroll costs.  Dearer than a single piece of
    /// gear because a scroll is permanent — it teaches the spell and is consumed, where armor wears out
    /// and is replaced every rung anyway.</summary>
    private const double ScrollTierShare = 0.05;

    /// <summary>Share of ONE level's income that a potion costs.  Consumables are priced per level rather
    /// than per rung because they are bought continuously rather than once a tier.</summary>
    private const double PotionLevelShare = 0.002;

    /// <summary>What a shop pays for an item a player brings in, as a percent of its
    /// <see cref="ItemValue"/>.  Well under half, so vendoring drops supplements income without becoming
    /// the main way to earn — the drop tables already pay ~22,000 items across the max band.</summary>
    public const int SellBackPercent = 25;

    // The medium-bulk Power an on-level piece carries, from the armory generator's own rule:
    // Power = round(0.40 x statBudget(level) x bulkMul), bulk 0.75 / 1.00 / 1.25. Restated here so pricing
    // can ask "how strong is this piece FOR its tier" without loading the generator — a heavy piece costs
    // 1.25x a medium one at the same tier, a light piece 0.75x, which falls out of the ratio directly.
    private const double ReferencePowerShare = 0.40;

    /// <summary>Power a medium-bulk piece carries at <paramref name="level"/> — the divisor that turns an
    /// item's Power into "how strong for its tier", so bulk prices itself.</summary>
    public static int ReferencePower(int level) =>
        Math.Max(1, (int)Math.Round(
            (Constants.PlayerBaseStatTotal + Constants.PointsPerLevel * (Math.Max(level, 1) - 1)) * ReferencePowerShare,
            MidpointRounding.AwayFromZero));

    /// <summary>What a shop charges for <paramref name="item"/>, in gold.
    ///
    /// <para><paramref name="spell"/> is required only for a <see cref="ItemType.Spell"/> scroll and is
    /// ignored otherwise: a scroll carries no <c>LevelReq</c> of its own — the gate lives on the spell it
    /// teaches — so its tier has to come from there.  A scroll passed without its spell falls back to the
    /// floor rather than pricing at zero.</para>
    ///
    /// <para>Currency and keys return 0: gold has no price in gold, and a key is quest furniture rather
    /// than stock.  A shop CAN still trade either — a BarterItemRecord names both sides explicitly — this
    /// only says the derivation declines to invent a number for them.</para></summary>
    public static int ItemValue(ItemRecord item, SpellRecord? spell = null)
    {
        // None is in this list for the OPPOSITE reason to the other two. Currency and Key have no worth to
        // derive; None is what TREASURE is typed as, and its worth is the entire point — it is simply
        // authored rather than derived, which is exactly the gap Price exists to fill.
        if (item.Type is ItemType.Currency or ItemType.Key or ItemType.None) return 0;

        if (ItemRecord.IsEquipment(item.Type))
        {
            double forTier = ExpectedGoldForTier(item.LevelReq) * EquipmentTierShare;
            double bulk = (double)Math.Max((int)item.Power, 1) / ReferencePower(item.LevelReq);
            return Clamp(forTier * bulk);
        }

        if (item.Type == ItemType.Spell)
            return Clamp(ExpectedGoldForTier(spell?.LevelReq ?? 0) * ScrollTierShare);

        // The six potion types.
        return Clamp(ExpectedGoldPerLevel(item.LevelReq) * PotionLevelShare);
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
    /// prevent. Items with no durability budget — potions, scrolls, keys — ignore it entirely.</para></summary>
    public static int ItemSellValue(ItemRecord item, int currentDurability, SpellRecord? spell = null)
    {
        int value = ItemValue(item, spell);
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
    // about L^1.3, so a value-priced repair is wrong by an exponent — 22% of a level's income at tier 20
    // against 5,433% at tier 235, which no choice of percentage fixes. Power grows about linearly in
    // level. At 40 a full kit costs 36-51% of a level's income in the mid and max bands and 19-26% in
    // the low band, so upkeep climbs as the game gets harder and always leaves at least half the take.
    //
    // TUNING: raise the divisor to make repair cheaper. Re-measure with .Tools/Simulations/FightSim, whose
    // last section prices a full kit against income at a sweep of candidate divisors.
    //
    // TWO TRAPS when re-measuring, both of which have already produced a wrong answer:
    //   Compare like for like. Repair per POINT against income per LEVEL makes Power look hopelessly
    //   behind, but durability lost per level and income both scale with kills per level and cancel.
    //   Enumerate SLOTS, not events. GetPlayerProtection calls DegradeArmor once per equipped defensive
    //   slot on every incoming blow, so armor, helmet and shield all chip on the same event and a
    //   successful block wears the shield again on top.
    private const double RepairPowerDivisor = 40.0;

    /// <summary>A full repair may never cost more than this percent of a new piece.  Repairing something
    /// for more than it costs to replace is a trap: the shop offers a service nobody should take, and a
    /// player who takes it is worse off purely for not having done the arithmetic.</summary>
    private const int RepairCapPercentOfPrice = 50;

    /// <summary>Gold per durability point on a piece of the given <paramref name="power"/>, BEFORE the
    /// replacement-cost cap — the one place the repair rate is stated.  Also the anchor a caster's reagent
    /// bill is matched against (<see cref="CombatFormulas.SubHpReagentCostExact"/>), so the two cannot drift.</summary>
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
    // Every one of these was a flat constant sized for the early game: 1,000 to found a guild, 1,000 to
    // declare a war, 1,000 to challenge a territory, 10 to send mail. Measured against income they are a
    // meaningful commitment at level 20 (a guild cost a fifth of the whole low band) and free by level
    // 100 — a guild at level 255 cost 0.0001 of a single level's earnings. Quoting each as a share of
    // ExpectedGoldPerLevel keeps its INTENT — "a guild is a real commitment" — true at every level, which
    // a constant cannot do against a curve that spans 137,000x.
    //
    // Each share below is a fraction of ONE level's income at the acting player's level. The player's
    // level is the right axis even for guild-scale costs: guild level is 0-5 and says nothing about how
    // wealthy the members are, so a level-5 guild of level-20 players and one of level-255 players would
    // otherwise pay the same for a war.

    /// <summary>Setting your spawn point at an inn.  A convenience bought repeatedly, so it stays small.</summary>
    private const double InnSpawnShare = 0.02;

    // ── Why the guild and mail costs are NOT scaled ──────────────────────────
    // They were, briefly, and it was a hole. Every one of them is paid by whoever CLICKS: a guild has its
    // level-1 alt declare the war, or mails the goods through a mule, and BandScale floors at 1.0 — so a
    // 906,226-gold declaration costs 1,000 and a 230,026-gold parcel costs 10. Scaling a cost by the actor's
    // level only works when the actor cannot be chosen, and for anything paid from a shared vault, or on
    // behalf of someone else, it always can be.
    //
    // Guild costs are also collective in a second sense: the vault is filled by the whole roster, so pinning
    // its price to one member's level is arbitrary even without the exploit. They stay flat, and the guild
    // economy stays an unscaled sub-economy — which is why the vault INCOME side is flat too, rather than
    // one half of it scaling away from the other.
    //
    // The inn's set-spawn cost DOES still scale, and that is not an inconsistency: you can only set your
    // own spawn point, so there is no one else to route it through.

    private static long Sink(int level, double share, long floor) =>
        Math.Max(floor, (long)Math.Round(ExpectedGoldPerLevel(level) * share, MidpointRounding.AwayFromZero));

    /// <summary>Gold to set your spawn point at an inn, for a player of <paramref name="level"/>.  The one
    /// sink still keyed on level, because it is the one nobody else can pay on your behalf.</summary>
    public static long InnSpawnCost(int level) => Sink(level, InnSpawnShare, Constants.SpawnCostMinimum);

    // ── Postage ──────────────────────────────────────────────────────────────
    // Two flat parts plus a share of what is in the parcel.
    //
    // The flat parts stay flat for the mule reason above, and stay SMALL because mail is a level-1
    // feature: any flat fee big enough to matter at level 255 (income ~10.9M a level) would be
    // unaffordable at level 5 (income 296). That tension is unresolvable with a constant, which is why
    // the scaling part is keyed on the PARCEL instead of the payer — a shipment's worth cannot be
    // minimized by handing it to an alt, so the exploit that killed level-scaling does not apply.
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

    /// <summary>Gold a warrior burns repairing one point of durability on on-level gear at
    /// <paramref name="level"/> — the reference a caster's per-cast reagent bill is matched to.
    ///
    /// <para>Priced against a synthetic reference piece (the tier's medium bulk at
    /// <see cref="ReferencePower"/>) rather than whatever the player happens to be holding, so the two
    /// classes are compared on the same footing and a caster's costs do not move when a warrior swaps
    /// weapons.</para></summary>
    public static double RepairGoldPerDurabilityPoint(int level) =>
        RepairGoldPerPoint(ReferencePower(level));
}
