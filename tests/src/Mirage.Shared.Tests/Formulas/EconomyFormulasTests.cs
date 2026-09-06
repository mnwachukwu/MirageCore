using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The gold economy: the income backbone every price is quoted against, the derived item prices,
/// the repair rule (shared by the shop repair path and the guild-war vault-repair sink) and the on-death
/// equipment wear.
///
/// <para>Most of these pin RELATIONSHIPS rather than magic numbers. The backbone is a fit to the authored
/// drop tables and is expected to move when the bestiary or the EXP curve does; what must not move is that
/// a price keeps pace with income, since falling behind is the exact defect this file exists to prevent.</para></summary>
[TestFixture]
public class EconomyFormulasTests
{
    private static ItemRecord Gear(ItemType type, short power, short levelReq, short durability = 50) =>
        new() { Name = "test", Type = type, Power = power, LevelReq = levelReq, Durability = durability };

    // ── The backbone ─────────────────────────────────────────────────────────

    [Test]
    public void ExpectedGoldPerLevel_RisesMonotonically()
    {
        for (int L = 1; L < Constants.MaxLevel; L++)
            Assert.That(EconomyFormulas.ExpectedGoldPerLevel(L + 1),
                Is.GreaterThan(EconomyFormulas.ExpectedGoldPerLevel(L)), $"level {L} -> {L + 1}");
    }

    [Test]
    public void ExpectedGoldPerLevel_OutrunsTheStatBudget()
    {
        // The defect this whole file guards against: item Power tracks the stat budget, which is LINEAR in
        // level, while income is superlinear. Anything priced off Power alone therefore decays to nothing.
        // Pinning the gap keeps that reasoning honest if either curve is ever retuned.
        const int Ref = 20;   // the level the pre-rework flat constants were sized against
        double incomeRatio = (double)EconomyFormulas.ExpectedGoldPerLevel(Constants.MaxLevel)
                           / EconomyFormulas.ExpectedGoldPerLevel(Ref);
        double powerRatio = (double)EconomyFormulas.ReferencePower(Constants.MaxLevel)
                          / EconomyFormulas.ReferencePower(Ref);
        Assert.That(incomeRatio, Is.GreaterThan(powerRatio * 50),
            "income must outrun Power by enough that a Power-based price cannot keep up");
    }

    [Test]
    public void ExpectedGoldPerLevel_ClampsBelowLevelOne()
    {
        Assert.That(EconomyFormulas.ExpectedGoldPerLevel(0), Is.EqualTo(EconomyFormulas.ExpectedGoldPerLevel(1)));
        Assert.That(EconomyFormulas.ExpectedGoldPerLevel(-5), Is.EqualTo(EconomyFormulas.ExpectedGoldPerLevel(1)));
    }

    [Test]
    public void ExpectedGoldForTier_IsTheSumOfItsRung_NotFiveTimesTheFirstLevel()
    {
        // The curve bends steeply at the bottom, so approximating the rung as 5x its first level would
        // understate tier 1 badly. Summing is the whole reason the method exists.
        long summed = 0;
        for (int L = 1; L <= Constants.GearTierLevels; L++) summed += EconomyFormulas.ExpectedGoldPerLevel(L);
        Assert.That(EconomyFormulas.ExpectedGoldForTier(1), Is.EqualTo(summed));
        Assert.That(EconomyFormulas.ExpectedGoldForTier(1),
            Is.GreaterThan(EconomyFormulas.ExpectedGoldPerLevel(1) * Constants.GearTierLevels * 2));
    }

    // ── Item pricing ─────────────────────────────────────────────────────────

    [Test]
    public void ItemValue_AFullKitIsAboutATenthOfItsRung()
    {
        // "Gear is cheap; sinks bite" — four slots at 2.5% of the rung each. Checked at every gear tier so
        // the share cannot hold at one band and drift at another.
        foreach (short tier in new short[] { 1, 5, 10, 15, 20, 100, 110, 120, 235, 245, 255 })
        {
            long rung = EconomyFormulas.ExpectedGoldForTier(tier);
            short power = (short)EconomyFormulas.ReferencePower(tier);
            long kit = EconomyFormulas.ItemValue(Gear(ItemType.Weapon, power, tier))
                     + EconomyFormulas.ItemValue(Gear(ItemType.Armor, power, tier))
                     + EconomyFormulas.ItemValue(Gear(ItemType.Helmet, power, tier))
                     + EconomyFormulas.ItemValue(Gear(ItemType.Shield, power, tier));
            Assert.That(100.0 * kit / rung, Is.EqualTo(10.0).Within(1.0), $"tier {tier}");
        }
    }

    [Test]
    public void ItemValue_ScalesWithBulkWithinATier()
    {
        // Bulk prices itself off the ratio to the tier's medium Power: light 0.75x, heavy 1.25x.
        const short tier = 100;
        short medium = (short)EconomyFormulas.ReferencePower(tier);
        short light = (short)(medium * 0.75), heavy = (short)(medium * 1.25);
        int lightValue = EconomyFormulas.ItemValue(Gear(ItemType.Armor, light, tier));
        int mediumValue = EconomyFormulas.ItemValue(Gear(ItemType.Armor, medium, tier));
        int heavyValue = EconomyFormulas.ItemValue(Gear(ItemType.Armor, heavy, tier));
        Assert.That(lightValue, Is.LessThan(mediumValue));
        Assert.That(heavyValue, Is.GreaterThan(mediumValue));
        Assert.That((double)heavyValue / mediumValue, Is.EqualTo(1.25).Within(0.02));
    }

    [Test]
    public void ItemValue_CurrencyAndKeysHaveNoDerivedPrice()
    {
        Assert.That(EconomyFormulas.ItemValue(new ItemRecord { Type = ItemType.Currency }), Is.Zero);
        Assert.That(EconomyFormulas.ItemValue(new ItemRecord { Type = ItemType.Key }), Is.Zero);
        Assert.That(EconomyFormulas.ItemValue(new ItemRecord { Type = ItemType.None }), Is.Zero);
    }

    [Test]
    public void ItemValue_ScrollPricesOffItsSpellNotItself()
    {
        // A scroll carries no LevelReq of its own — the gate lives on the spell it teaches — so passing the
        // spell is what gives it a tier. Without one it must not price at zero.
        var scroll = new ItemRecord { Name = "scroll", Type = ItemType.Spell, SpellNum = 1 };
        int low = EconomyFormulas.ItemValue(scroll, new SpellRecord { LevelReq = 1 });
        int high = EconomyFormulas.ItemValue(scroll, new SpellRecord { LevelReq = 235 });
        Assert.That(high, Is.GreaterThan(low * 1000), "a max-band scroll must cost orders more than a starter one");
        Assert.That(EconomyFormulas.ItemValue(scroll), Is.GreaterThan(0), "a scroll with no spell still prices at the floor");
    }

    [Test]
    public void ItemSellValue_IsTheConfiguredFractionOfValue()
    {
        var item = Gear(ItemType.Weapon, 200, 120);
        Assert.That(EconomyFormulas.ItemSellValue(item, item.Durability),
            Is.EqualTo(EconomyFormulas.ItemValue(item) * EconomyFormulas.SellBackPercent / 100).Within(1));
        Assert.That(EconomyFormulas.ItemSellValue(new ItemRecord { Type = ItemType.Currency }, 0), Is.Zero);
    }

    [Test]
    public void ItemSellValue_ScalesWithCondition()
    {
        // A shop pays for what it actually receives. Without this, a player wears a piece to nothing and
        // still vendors it at the pristine rate, handing the shop a repair bill it never charged for.
        var item = Gear(ItemType.Weapon, 200, 120, durability: 100);
        int pristine = EconomyFormulas.ItemSellValue(item, 100);

        Assert.Multiple(() =>
        {
            Assert.That(EconomyFormulas.ItemSellValue(item, 50), Is.EqualTo(pristine / 2).Within(1),
                "half worn fetches half of the sell-back fraction");
            Assert.That(EconomyFormulas.ItemSellValue(item, 25), Is.EqualTo(pristine / 4).Within(1));
            Assert.That(EconomyFormulas.ItemSellValue(item, 0), Is.Zero, "a broken piece is scrap");
            Assert.That(EconomyFormulas.ItemSellValue(item, 500), Is.EqualTo(pristine),
                "durability past the maximum cannot pay more than pristine");
        });

        // Condition is meaningless for anything with no durability budget.
        var potion = new ItemRecord { Type = ItemType.PotionAddHp, LevelReq = 120, VitalAmount = 10 };
        Assert.That(EconomyFormulas.ItemSellValue(potion, 0),
            Is.EqualTo(EconomyFormulas.ItemSellValue(potion, 999)), "a potion has no wear to price");
    }

    // ── Repair ───────────────────────────────────────────────────────────────

    [Test]
    public void RepairCost_IsTheePowerRate_ExceptWhereTheCapBinds()
    {
        // Gold per point is the Power rate. Checked well up the ladder, where prices are large enough that
        // the replacement-cost cap never engages and the raw rate is what you pay.
        //
        // Quoted against RepairGoldPerPoint rather than a literal: the divisor is a tuning knob, and this
        // test is about the SHAPE — linear in points, floored, clamped at a full repair. Pinning the
        // literal would only prove the knob had not moved.
        var item = Gear(ItemType.Weapon, power: 200, levelReq: 120, durability: 100);
        int full = (int)Math.Round(100 * EconomyFormulas.RepairGoldPerPoint(200), MidpointRounding.AwayFromZero);
        Assert.That(EconomyFormulas.RepairCost(100, item), Is.EqualTo(full), "100 points at the Power rate");
        Assert.That(EconomyFormulas.RepairCost(50, item), Is.EqualTo(full / 2), "pro-rata");
        Assert.That(EconomyFormulas.RepairCost(0, item), Is.EqualTo(1), "floored at 1");
        Assert.That(EconomyFormulas.RepairCost(500, item), Is.EqualTo(full), "clamped at a full repair");
    }

    [Test]
    public void RepairCost_CapBindsAtTheBottomOfTheLadder()
    {
        // A tier-1 shield carries 100 durability and costs about 11 gold. The raw Power rate would charge
        // 60 to restore something replaceable for 11 — so the cap has to engage here, and only here.
        var cheap = Gear(ItemType.Shield, (short)EconomyFormulas.ReferencePower(1), levelReq: 1, durability: 100);
        int price = EconomyFormulas.ItemValue(cheap);
        int full = EconomyFormulas.RepairCost(100, cheap);
        Assert.That(full, Is.LessThan(price), "the cap must bite before repair beats replacement");
        Assert.That(full, Is.LessThan(100 * EconomyFormulas.RepairGoldPerPoint(cheap.Power)),
            "and it must actually be lower than the raw Power rate");
    }

    [Test]
    public void RepairPointsAffordable_NeverOverchargesThePurse()
    {
        // The rate is a floored display figure, so gold/rate can name a point count costing a gold more
        // than the player holds — which is exactly what a partial repair would then charge. This is the
        // guard on doing the division exactly instead.
        foreach (short tier in new short[] { 1, 20, 100, 120, 235, 255 })
        {
            var item = Gear(ItemType.Weapon, (short)EconomyFormulas.ReferencePower(tier), tier, durability: 100);
            int full = EconomyFormulas.RepairCost(100, item);
            foreach (long purse in new[] { 0L, 1L, full / 3, full / 2, full - 1, full, full * 2 })
            {
                int points = EconomyFormulas.RepairPointsAffordable(purse, item);
                Assert.That(points, Is.InRange(0, 100), $"tier {tier}, purse {purse}");
                if (points > 0)
                    Assert.That(EconomyFormulas.RepairCost(points, item), Is.LessThanOrEqualTo(purse),
                        $"tier {tier}, purse {purse}: bought {points} points it cannot pay for");
            }
        }
    }

    [Test]
    public void RepairingIsAlwaysCheaperThanReplacing()
    {
        // Otherwise repair is a trap: the shop offers a service nobody should ever take, and a player who
        // takes it is simply worse off for not knowing. NOT automatic under the Power rate — a tier-1
        // shield carries 100 durability against an 11-gold price, so the raw rate would charge 60 — which
        // is why RepairCost caps against the price. Swept across durabilities because the cap binds on the
        // RATIO of durability to price, not on tier alone.
        //
        // The sweep runs to 2,000 because that is what the armory ships: durability is sqrt(level) x bulk,
        // so a tier-255 Tower Shield carries 2,000. Raising durability raises the RAW cost of a full
        // repair in direct proportion (more points to buy) while the price stays put, so it drives items
        // INTO the cap — this invariant is exactly the one a durability change can break, so the sweep has
        // to reach what the armory actually ships.
        foreach (short tier in new short[] { 1, 5, 10, 15, 20, 100, 120, 235, 255 })
            foreach (short dur in new short[] { 20, 50, 100, 200, 500, 1_000, 2_000 })
            {
                var item = Gear(ItemType.Weapon, (short)EconomyFormulas.ReferencePower(tier), tier, dur);
                int price = EconomyFormulas.ItemValue(item);
                int fullRepair = EconomyFormulas.RepairCost(item.Durability, item);
                Assert.That(fullRepair, Is.LessThan(price),
                    $"tier {tier}, durability {dur}: repairing from zero must beat buying new");
            }
    }

    // ── Sinks ────────────────────────────────────────────────────────────────

    [Test]
    public void InnSpawnCost_ScalesWithLevel_AndHoldsItsFloor()
    {
        // The ONE sink still keyed on level, because you can only set your own spawn — there is nobody else
        // to route it through. Everything else a third party can pay is flat for exactly that reason.
        Assert.That(EconomyFormulas.InnSpawnCost(1), Is.EqualTo(Constants.SpawnCostMinimum));
        Assert.That(EconomyFormulas.InnSpawnCost(Constants.MaxLevel),
            Is.GreaterThan(EconomyFormulas.InnSpawnCost(20) * 100));
    }

    [Test]
    public void MailSendCost_HasFlatPartsPlusAShareOfTheParcel()
    {
        // The flat parts are flat on purpose: a level-scaled fee is defeated by handing the parcel to a
        // level-1 mule. They also stay small, because mail is a level-1 feature.
        long bare = EconomyFormulas.MailSendCost(0);
        Assert.That(bare, Is.EqualTo(Constants.MailBaseSendCost));
        Assert.That(EconomyFormulas.MailSendCost(Constants.MaxMailAttachments),
            Is.EqualTo(Constants.MailBaseSendCost + Constants.MaxMailAttachments * Constants.MailAttachmentSendCost));
        Assert.That(EconomyFormulas.MailSendCost(-1), Is.EqualTo(bare), "a negative count cannot refund");
        Assert.That(EconomyFormulas.MailSendCost(0, -5_000), Is.EqualTo(bare), "nor can a negative value");

        // The part that scales, and the reason it is exploit-proof: it reads the PARCEL, not the payer, so
        // routing the send through an alt changes nothing about what it costs.
        Assert.That(EconomyFormulas.MailSendCost(1, 1_000_000) - EconomyFormulas.MailSendCost(1),
            Is.EqualTo(1_000_000L * Constants.MailAttachedValuePercent / 100));
    }

    [Test]
    public void MailPostage_StaysCheaperThanTheEscrowedChannels()
    {
        // The marketplace and CoD mail both charge MarketSaleTaxPercent and both guarantee payment. Plain
        // mail guarantees nothing, so it must stay strictly cheaper — otherwise the trusted channel is
        // priced above the untrusted one and nobody uses it. The old flat 10 gold made this gap absurd:
        // a 1.7M-gold item moved for 10 gold instead of 5%.
        Assert.That(Constants.MailAttachedValuePercent, Is.GreaterThan(0));
        Assert.That(Constants.MailAttachedValuePercent, Is.LessThan(Constants.MarketSaleTaxPercent));
    }

    [Test]
    public void MailAttachmentValue_PricesGoldAsItsAmount()
    {
        // Gold rides as a currency attachment and carries NO Price (ItemRecord.Normalize clears it), so a
        // naive price-times-quantity would value a fortune at zero and make gold the untaxed channel.
        Assert.That(EconomyFormulas.MailAttachmentValue(Constants.GoldItemIndex, 5_000, unitPrice: 0),
            Is.EqualTo(5_000));
        Assert.That(EconomyFormulas.MailAttachmentValue(itemNum: 42, quantity: 3, unitPrice: 100),
            Is.EqualTo(300));
        Assert.That(EconomyFormulas.MailAttachmentValue(itemNum: 42, quantity: 0, unitPrice: 100), Is.Zero);
    }

    [Test]
    public void CasterAndWarriorUpkeepStayInStep()
    {
        // Reagents are 1 gold each, so a cast's reagent cost must equal the gold a warrior burns per swing.
        // The parity has to be DERIVED from the live repair rule: priced off a copy of it, the two drift to
        // ~87x apart at max level in the caster's favor and nothing fails.
        //
        // EXACT, and the whole ladder. A +/-1 gold tolerance is wider than the entire quantity below level 40,
        // so it admitted a level-1 cast costing ten times a level-1 swing; and starting at 20 never looked.
        foreach (int level in new[] { 1, 2, 5, 10, 20, 40, 100, 120, 235, 255 })
        {
            double warriorPerSwing = EconomyFormulas.RepairGoldPerDurabilityPoint(level) * 0.48;   // avg chip per hit
            double casterPerCast = CombatFormulas.SubHpReagentCostExact(level);
            Assert.That(casterPerCast, Is.EqualTo(warriorPerSwing).Within(0.0001),
                $"level {level}: a cast must cost what a swing costs, in gold");
        }
    }

    [Test]
    public void RepairGoldPerDurabilityPoint_RisesWithTier()
    {
        Assert.That(EconomyFormulas.RepairGoldPerDurabilityPoint(Constants.MaxLevel),
            Is.GreaterThan(EconomyFormulas.RepairGoldPerDurabilityPoint(20) * 10),
            "upkeep has to track the gear it maintains, or it decays into a rounding error");
    }

    // ── Wear ─────────────────────────────────────────────────────────────────

    [Test]
    public void EquipmentDamageOnDeath_IsPercentOfMax_FlooredAt1()
    {
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(maxDur: 100, percentOfMax: 20), Is.EqualTo(20));
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(100, 10), Is.EqualTo(10));
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(1, 10), Is.EqualTo(1));   // 0.1 → floor 1
    }
}
