using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>Pure guild-war math + status derivation + mirror-list maintenance:
/// the declare-cost curve (punch-down/up, level-0 doubling, floor), daily maintenance, the derived
/// <see cref="GuildWarStatus"/>, live/warmup gating, and Find/Unlink of the paired war entries.</summary>
[TestFixture]
public class GuildWarFormulasTests
{
    // ── Declare cost ─────────────────────────────────────────────────────────

    [Test]
    public void DeclareCost_SameLevel_IsBaseCost()
    {
        Assert.That(GuildWarFormulas.DeclareCost(declarerLevel: 2, targetLevel: 2),
            Is.EqualTo(Constants.GuildWarDeclareBaseCost));
    }

    // The three below are quoted against the constants, not literals. The guild gold family is rescaled
    // as a unit, and what these protect is the DIRECTION and SIZE of the level tilt, which a rescale
    // leaves alone. Literals here would only prove nobody had retuned the guild economy.
    [Test]
    public void DeclareCost_PunchingUp_CostsLess()   // L1 → L5 = base - 4*step
    {
        Assert.That(GuildWarFormulas.DeclareCost(1, 5),
            Is.EqualTo(Constants.GuildWarDeclareBaseCost - 4 * Constants.GuildWarDeclareLevelStep));
    }

    [Test]
    public void DeclareCost_PunchingDown_CostsMore()   // L5 → L1 = base + 4*step
    {
        Assert.That(GuildWarFormulas.DeclareCost(5, 1),
            Is.EqualTo(Constants.GuildWarDeclareBaseCost + 4 * Constants.GuildWarDeclareLevelStep));
    }

    [Test]
    public void DeclareCost_Level0Target_DoublesWholeCost()   // L5 → L0 = (base + 5*step) * 2
    {
        Assert.That(GuildWarFormulas.DeclareCost(5, 0),
            Is.EqualTo((Constants.GuildWarDeclareBaseCost + 5 * Constants.GuildWarDeclareLevelStep)
                       * Constants.GuildWarL0TargetCostMultiplier));
    }

    [Test]
    public void DeclareCost_HugeGap_FlooredAtMin()   // synthetic gap drives the base negative → floor
    {
        Assert.That(GuildWarFormulas.DeclareCost(declarerLevel: 5, targetLevel: 20),
            Is.EqualTo(Constants.GuildWarDeclareMinCost));
    }

    // ── Daily maintenance ────────────────────────────────────────────────────

    [Test]
    public void DailyMaintenance_IsHalfTheDeclareCost()
    {
        Assert.That(GuildWarFormulas.DailyMaintenance(1400), Is.EqualTo(700));
    }

    [Test]
    public void DailyMaintenance_RoundsAwayFromZero()   // 999 * 0.5 = 499.5 → 500
    {
        Assert.That(GuildWarFormulas.DailyMaintenance(999), Is.EqualTo(500));
    }

    // ── Status derivation ────────────────────────────────────────────────────

    private static GuildWar War(bool we, bool they, long goLive) =>
        new() { OpponentIndex = 2, WeDeclared = we, TheyDeclared = they, GoLiveUtc = goLive };

    [Test]
    public void Status_OneSidedBeforeGoLive_IsWarmup()
    {
        Assert.That(GuildWarFormulas.Status(War(we: true, they: false, goLive: 1000), nowUtc: 500),
            Is.EqualTo(GuildWarStatus.Warmup));
    }

    [Test]
    public void Status_DefenderBeforeGoLive_IsAlsoWarmup()
    {
        Assert.That(GuildWarFormulas.Status(War(we: false, they: true, goLive: 1000), nowUtc: 500),
            Is.EqualTo(GuildWarStatus.Warmup));
    }

    [Test]
    public void Status_LiveOneSided_SplitsAggressorVsDefender()
    {
        Assert.That(GuildWarFormulas.Status(War(we: true, they: false, goLive: 100), nowUtc: 500),
            Is.EqualTo(GuildWarStatus.OneSidedAggressor));
        Assert.That(GuildWarFormulas.Status(War(we: false, they: true, goLive: 100), nowUtc: 500),
            Is.EqualTo(GuildWarStatus.OneSidedDefender));
    }

    [Test]
    public void Status_BothDeclared_IsMutual_RegardlessOfWarmup()
    {
        Assert.That(GuildWarFormulas.Status(War(we: true, they: true, goLive: 10_000), nowUtc: 500),
            Is.EqualTo(GuildWarStatus.Mutual));
    }

    [Test]
    public void IsLive_TrueOnceGoLiveReached()
    {
        Assert.That(GuildWarFormulas.IsLive(War(true, false, goLive: 500), nowUtc: 499), Is.False);
        Assert.That(GuildWarFormulas.IsLive(War(true, false, goLive: 500), nowUtc: 500), Is.True);
    }

    // ── Mirror maintenance ───────────────────────────────────────────────────

    [Test]
    public void Find_ReturnsMatchingEntryOrNull()
    {
        var g = new GuildRecord { Index = 1 };
        g.Wars.Add(new GuildWar { OpponentIndex = 7 });
        Assert.That(GuildWarFormulas.Find(g, 7), Is.Not.Null);
        Assert.That(GuildWarFormulas.Find(g, 8), Is.Null);
    }

    [Test]
    public void Unlink_RemovesTheWarFromBothGuilds()
    {
        var a = new GuildRecord { Index = 1 };
        var b = new GuildRecord { Index = 2 };
        a.Wars.Add(new GuildWar { OpponentIndex = 2 });
        b.Wars.Add(new GuildWar { OpponentIndex = 1 });

        GuildWarFormulas.Unlink(a, b);

        Assert.That(a.Wars, Is.Empty);
        Assert.That(b.Wars, Is.Empty);
    }

    // ── Officer war-request queue ────────────────────────────────────────────

    private static WarRequestQueueResult Queue(GuildRecord g, GuildWarRequestKind kind, int target, int max = 10) =>
        GuildWarFormulas.TryQueueRequest(g, kind, target, "Foe", "off@acct", "Officer", nowUtc: 0, max);

    [Test]
    public void TryQueueRequest_Added_WhenNew()
    {
        var g = new GuildRecord { Index = 1 };
        Assert.That(Queue(g, GuildWarRequestKind.Declare, 2), Is.EqualTo(WarRequestQueueResult.Added));
        Assert.That(g.WarRequests, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryQueueRequest_DuplicateKindAndTarget_IsAlreadyPending()
    {
        var g = new GuildRecord { Index = 1 };
        Queue(g, GuildWarRequestKind.Declare, 2);
        Assert.That(Queue(g, GuildWarRequestKind.Declare, 2), Is.EqualTo(WarRequestQueueResult.AlreadyPending));
        Assert.That(g.WarRequests, Has.Count.EqualTo(1));   // not double-added
    }

    [Test]
    public void TryQueueRequest_SameTargetDifferentKind_BothQueue()
    {
        var g = new GuildRecord { Index = 1 };
        Queue(g, GuildWarRequestKind.Declare, 2);
        Assert.That(Queue(g, GuildWarRequestKind.Retract, 2), Is.EqualTo(WarRequestQueueResult.Added));
        Assert.That(g.WarRequests, Has.Count.EqualTo(2));
    }

    [Test]
    public void TryQueueRequest_AtCap_IsFull()
    {
        var g = new GuildRecord { Index = 1 };
        for (int t = 2; t <= 4; t++) Queue(g, GuildWarRequestKind.Declare, t, max: 3);
        Assert.That(Queue(g, GuildWarRequestKind.Declare, 5, max: 3), Is.EqualTo(WarRequestQueueResult.Full));
        Assert.That(g.WarRequests, Has.Count.EqualTo(3));
    }

    [Test]
    public void FindAndRemoveRequest_MatchByKindAndTarget()
    {
        var g = new GuildRecord { Index = 1 };
        Queue(g, GuildWarRequestKind.Declare, 2);
        Assert.That(GuildWarFormulas.FindRequest(g, GuildWarRequestKind.Declare, 2), Is.Not.Null);
        Assert.That(GuildWarFormulas.FindRequest(g, GuildWarRequestKind.Retract, 2), Is.Null);   // wrong kind

        Assert.That(GuildWarFormulas.RemoveRequest(g, GuildWarRequestKind.Declare, 2), Is.True);
        Assert.That(g.WarRequests, Is.Empty);
        Assert.That(GuildWarFormulas.RemoveRequest(g, GuildWarRequestKind.Declare, 2), Is.False);   // already gone
    }

    // ── War-death durability cost ────────────────────────────────────────────

    [Test]
    public void WarDeathVaultCost_Is75PercentRoundedAwayFromZero()
    {
        Assert.That(GuildWarFormulas.WarDeathVaultCost(1000), Is.EqualTo(750));
        Assert.That(GuildWarFormulas.WarDeathVaultCost(101), Is.EqualTo(76));   // 75.75 → 76
    }

    [Test]
    public void WarDeathVaultCovers_NeedsWearAndEnoughGold()
    {
        Assert.That(GuildWarFormulas.WarDeathVaultCovers(totalRepairCost: 0, vaultGold: 10_000), Is.False);   // no wear
        Assert.That(GuildWarFormulas.WarDeathVaultCovers(1000, 750), Is.True);    // exactly the 75% share
        Assert.That(GuildWarFormulas.WarDeathVaultCovers(1000, 749), Is.False);   // one short → whole-or-nothing
    }

    [Test]
    public void WarDeathItemWear_QuarterWhenCovered_FullOtherwise()
    {
        Assert.That(GuildWarFormulas.WarDeathItemWear(doubledWear: 20, vaultCovered: true), Is.EqualTo(5));    // 25%
        Assert.That(GuildWarFormulas.WarDeathItemWear(20, vaultCovered: false), Is.EqualTo(20));               // full
        Assert.That(GuildWarFormulas.WarDeathItemWear(1, vaultCovered: true), Is.EqualTo(0));                  // 0.25 → 0
    }

    // ── Attrition & per-target DR ────────────────────────────────────────────

    [Test]
    public void DecayedDrStage_RecoversOneStagePerPeriod_FlooredAt1()
    {
        Assert.That(GuildWarFormulas.DecayedDrStage(stage: 0, lastUtc: 0, nowUtc: 1000), Is.EqualTo(1));       // never killed → fresh
        Assert.That(GuildWarFormulas.DecayedDrStage(3, 1000, 1000), Is.EqualTo(3));                            // no time passed
        Assert.That(GuildWarFormulas.DecayedDrStage(3, 1000, 1000 + Constants.GuildWarDrRecoverySeconds), Is.EqualTo(2));
        Assert.That(GuildWarFormulas.DecayedDrStage(3, 1000, 1000 + 5 * Constants.GuildWarDrRecoverySeconds), Is.EqualTo(1));
    }

    [Test]
    public void AttritionScore_FullTreasuryNoDr_PlusDrScaledBaseDeath()
    {
        int baseDeath = Constants.GuildWarBaseDeathAttrition;
        int min = Constants.GuildWarDrStagePercents[^1];   // DR minimum percent (25)

        // Treasury "war spend" always counts in FULL (no DR); the base-death rate is DR-scaled.
        Assert.That(GuildWarFormulas.AttritionScore(treasuryDamage: 300, drStage: 1), Is.EqualTo(300 + baseDeath));

        // Base-death rate at stage 1 (full) vs stage 4 (min) vs beyond (still the min, never 0).
        Assert.That(GuildWarFormulas.AttritionScore(0, drStage: 1), Is.EqualTo(baseDeath));
        Assert.That(GuildWarFormulas.AttritionScore(0, drStage: 4), Is.EqualTo(baseDeath * min / 100));
        Assert.That(GuildWarFormulas.AttritionScore(0, drStage: 9), Is.EqualTo(baseDeath * min / 100));   // clamped, never 0
        Assert.That(GuildWarFormulas.AttritionScore(0, drStage: 9), Is.GreaterThan(0));                   // 1 death always > 0

        // Treasury has NO DR: the same drained gold adds identically at any stage.
        Assert.That(GuildWarFormulas.AttritionScore(300, 9) - GuildWarFormulas.AttritionScore(0, 9), Is.EqualTo(300));
    }

    [Test]
    public void IsCold_TrueOnceNoProgressForColdWindow()
    {
        Assert.That(GuildWarFormulas.IsCold(lastProgressUtc: 1000, nowUtc: 1000 + Constants.GuildWarColdSeconds - 1), Is.False);
        Assert.That(GuildWarFormulas.IsCold(1000, 1000 + Constants.GuildWarColdSeconds), Is.True);
    }

    [Test]
    public void InitMutualAttrition_SeedsFullMeter()
    {
        var war = new GuildWar();
        GuildWarFormulas.InitMutualAttrition(war, nowUtc: 5000);
        Assert.That(war.Attrition, Is.EqualTo(Constants.GuildWarAttritionPool));
        Assert.That(war.MinAttritionSeen, Is.EqualTo(Constants.GuildWarAttritionPool));
        Assert.That(war.LastProgressUtc, Is.EqualTo(5000));
        Assert.That(war.UncoveredDeathStreak, Is.EqualTo(0));
        Assert.That(war.MutualSinceUtc, Is.EqualTo(5000));   // anchors the wager window
    }

    // ── Re-declare cooldown ──────────────────────────────────────────────────

    [Test]
    public void Cooldown_SetThenRemainingReflectsIt_ZeroWhenExpiredOrAbsent()
    {
        var g = new GuildRecord { Index = 1 };
        long cd = Constants.GuildWarRedeclareCooldownSeconds;
        GuildWarFormulas.SetCooldown(g, opponentIndex: 2, untilUtc: 1000 + cd, nowUtc: 1000);
        Assert.That(GuildWarFormulas.RemainingCooldownSeconds(g, 2, nowUtc: 1000), Is.EqualTo(cd));
        Assert.That(GuildWarFormulas.RemainingCooldownSeconds(g, 2, nowUtc: 1000 + cd), Is.EqualTo(0));   // expired
        Assert.That(GuildWarFormulas.RemainingCooldownSeconds(g, 3, nowUtc: 1000), Is.EqualTo(0));         // none for guild 3
    }

    [Test]
    public void SetCooldown_ReplacesSameOpponent_AndPrunesExpired()
    {
        var g = new GuildRecord { Index = 1 };
        GuildWarFormulas.SetCooldown(g, opponentIndex: 2, untilUtc: 50, nowUtc: 10);     // expires before now=100
        GuildWarFormulas.SetCooldown(g, opponentIndex: 3, untilUtc: 200, nowUtc: 100);   // prunes the expired opp2
        Assert.That(g.WarCooldowns, Has.Count.EqualTo(1));
        Assert.That(GuildWarFormulas.RemainingCooldownSeconds(g, 3, 100), Is.EqualTo(100));
        GuildWarFormulas.SetCooldown(g, opponentIndex: 3, untilUtc: 300, nowUtc: 100);   // replaces, not duplicates
        Assert.That(g.WarCooldowns, Has.Count.EqualTo(1));
        Assert.That(GuildWarFormulas.RemainingCooldownSeconds(g, 3, 100), Is.EqualTo(200));
    }

    // ── Wagers ───────────────────────────────────────────────────────────────

    [Test]
    public void MaxWager_IsHalfTheVault_FlooredDown()
    {
        Assert.That(GuildWarFormulas.MaxWager(1000), Is.EqualTo(500));
        Assert.That(GuildWarFormulas.MaxWager(999), Is.EqualTo(499));   // floor, never over-stakes
        Assert.That(GuildWarFormulas.MaxWager(0), Is.EqualTo(0));
    }

    [Test]
    public void WagerWindowOpen_OpenWithinWindow_ClosedAfter_FalseWhenNotMutual()
    {
        long win = Constants.GuildWarWagerWindowSeconds;
        var mutual = new GuildWar { MutualSinceUtc = 1000 };
        Assert.That(GuildWarFormulas.WagerWindowOpen(mutual, 1000), Is.True);
        Assert.That(GuildWarFormulas.WagerWindowOpen(mutual, 1000 + win), Is.True);         // inclusive edge
        Assert.That(GuildWarFormulas.WagerWindowOpen(mutual, 1000 + win + 1), Is.False);     // closed
        Assert.That(GuildWarFormulas.WagerWindowOpen(new GuildWar(), 1000), Is.False);       // not mutual (stamp 0)
    }

    [Test]
    public void SettleWagerPot_DecisiveWin_WinnerTakesBothStakes()
    {
        var (a, b) = MutualPair(anteA: 200, anteB: 200);
        long pot = GuildWarFormulas.SettleWagerPot(a, b, a);   // a wins decisively
        Assert.That(pot, Is.EqualTo(400));
        Assert.That(a.VaultGold, Is.EqualTo(400));   // winner-take-all
        Assert.That(b.VaultGold, Is.EqualTo(0));
        Assert.That(GuildWarFormulas.Find(a, b.Index)!.AnteEscrow, Is.EqualTo(0));   // escrows cleared
        Assert.That(GuildWarFormulas.Find(b, a.Index)!.AnteEscrow, Is.EqualTo(0));
    }

    [Test]
    public void SettleWagerPot_Draw_ReturnsEachOwnStake()
    {
        var (a, b) = MutualPair(anteA: 200, anteB: 200);
        long pot = GuildWarFormulas.SettleWagerPot(a, b, winner: null);   // cold draw
        Assert.That(pot, Is.EqualTo(0));
        Assert.That(a.VaultGold, Is.EqualTo(200));   // each gets its own stake back
        Assert.That(b.VaultGold, Is.EqualTo(200));
    }

    [Test]
    public void SettleWagerPot_IncludesPeaceOffering_AndIsSafeWithoutEntries()
    {
        var (a, b) = MutualPair(anteA: 0, anteB: 0);
        GuildWarFormulas.Find(b, a.Index)!.PeaceEscrow = 150;   // b sued for peace with a 150-gold offering
        long pot = GuildWarFormulas.SettleWagerPot(a, b, a);    // a accepts → a wins the offering
        Assert.That(pot, Is.EqualTo(150));
        Assert.That(a.VaultGold, Is.EqualTo(150));
        // No war entries at all → no throw, no transfer.
        var x = new GuildRecord { Index = 10 };
        var y = new GuildRecord { Index = 11 };
        Assert.That(GuildWarFormulas.SettleWagerPot(x, y, x), Is.EqualTo(0));
    }

    // Two guilds linked into a mutual war, each holding the given ante in escrow (vaults start at 0).
    private static (GuildRecord A, GuildRecord B) MutualPair(long anteA, long anteB)
    {
        var a = new GuildRecord { Index = 1, Name = "A" };
        var b = new GuildRecord { Index = 2, Name = "B" };
        a.Wars.Add(new GuildWar { OpponentIndex = 2, WeDeclared = true, TheyDeclared = true, AnteEscrow = anteA });
        b.Wars.Add(new GuildWar { OpponentIndex = 1, WeDeclared = true, TheyDeclared = true, AnteEscrow = anteB });
        return (a, b);
    }
}
