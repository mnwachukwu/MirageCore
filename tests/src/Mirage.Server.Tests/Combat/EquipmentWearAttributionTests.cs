using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>How gear wears: WHICH slots a combat event touches, and HOW the sliding chip scale behaves.
/// Both are load-bearing for the repair economy.
///
/// <para><b>Slots.</b> <see cref="EconomyFormulas"/> quotes a full kit's upkeep as (wear events per kill) x
/// (kills per level) x (gold per point). The first term depends entirely on how many slots each event
/// touches, and the answer is not one: <c>GetPlayerProtection</c> calls <c>DegradeArmor</c> once per
/// equipped defensive slot every time it prices an incoming blow, so armor, helmet AND shield chip on the
/// same hit, and a block wears the shield again on top. Getting this wrong once understated a kit's upkeep
/// by about half, and nothing failed — the number was simply wrong in a spreadsheet.</para>
///
/// <para><b>The sliding scale.</b> A hit does not always chip: the chance rises as the piece wears, 25% →
/// 50% → 75% → 100% across four condition bands. Two properties of that fall out and the economy leans
/// on both. It is SCALE-FREE (the bands are percentages of max, so a 2,000-durability piece behaves
/// exactly like a 100-durability one in relative terms) which is why scaling durability moves the repair
/// CADENCE and not the repair GOLD. And it is CONVEX (wear accelerates as a piece degrades), which is why
/// the full-cycle average is 0.48 rather than the 0.25 of fresh gear.</para></summary>
[TestFixture]
public class EquipmentWearAttributionTests
{
    const int Map = 1, Idx = 1;
    const int ArmorNum = 10, HelmetNum = 11, ShieldNum = 12;
    const short MaxDur = 100;
    const int CriticalDur = 20;   // 20% of max — inside the always-chips band, so wear is deterministic

    // ── Which slots an event wears ────────────────────────────────────────────

    [Test]
    public void OneLandedBlow_WearsAllThreeDefensiveSlots_NotJustOne()
    {
        var (combat, p) = Setup();

        combat.GetPlayerProtection(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.EqualTo(CriticalDur - 1), "armor chips on a blow that lands");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(CriticalDur - 1), "so does the helmet");
            Assert.That(p.Inv[3].Dur, Is.EqualTo(CriticalDur - 1),
                "and so does the shield — THREE slots on one event, which is what doubles a kit's upkeep");
        });
    }

    [Test]
    public void WearIsProportionalToEquippedSlots_SoAKitDoesNotCostOnePiece()
    {
        // The economy claim in one assertion: N blows cost N points on EACH worn slot, so a four-piece
        // player pays for four pieces. Ten blows stays well inside the always-chips band.
        var (combat, p) = Setup();
        const int blows = 10;
        for (int i = 0; i < blows; i++) combat.GetPlayerProtection(Idx);

        int worn = (CriticalDur - p.Inv[1].Dur) + (CriticalDur - p.Inv[2].Dur) + (CriticalDur - p.Inv[3].Dur);
        Assert.That(worn, Is.EqualTo(blows * 3),
            "durability lost across the kit is blows x slots, not blows");
    }

    [Test]
    public void NoShieldEquipped_LeavesTheOtherTwoUnaffected()
    {
        // Guards the slot indexing: dropping a slot must cost that slot's wear and nothing else. A bug
        // that wore the wrong index shows up here as armor or helmet chipping twice, or not at all.
        var (combat, p) = Setup(withShield: false);

        combat.GetPlayerProtection(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.EqualTo(CriticalDur - 1), "armor still chips exactly once");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(CriticalDur - 1), "helmet still chips exactly once");
            Assert.That(p.Inv[3].Dur, Is.Zero, "the empty shield slot is untouched");
        });
    }

    [Test]
    public void ABlock_WearsTheShieldAlone()
    {
        // The second half of the shield's wear, and why it is the fastest-wearing defensive slot: a
        // successful block returns BEFORE mitigation is priced, so it wears the shield without touching
        // armor or helmet.
        //
        // Blocking is a roll, so the roll is pinned: one call, one block, no batch to sample through.
        var (combat, p) = Setup(rng: PinnedRolls.Always);
        p.Sp = 100_000;   // a block drains stamina, and the drain must not be what ends the test

        combat.TryPlayerNegateMagic(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[3].Dur, Is.LessThan(CriticalDur), "a block wears the shield");
            Assert.That(p.Inv[1].Dur, Is.EqualTo(CriticalDur), "a block never wears armor");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(CriticalDur), "a block never wears the helmet");
        });
    }

    // ── The sliding chip scale ────────────────────────────────────────────────

    [Test]
    public void ChipChance_RisesAsConditionFalls_AcrossAllFourBands()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CombatFormulas.DurabilityDegradeChancePercent(100, 100),
                Is.EqualTo(CombatFormulas.DurDegradeHealthyChancePct), "fresh gear shrugs off most hits");
            Assert.That(CombatFormulas.DurabilityDegradeChancePercent(75, 100),
                Is.EqualTo(CombatFormulas.DurDegradeHealthyChancePct), "the healthy band is inclusive at 75%");
            Assert.That(CombatFormulas.DurabilityDegradeChancePercent(74, 100),
                Is.EqualTo(CombatFormulas.DurDegradeWornChancePct));
            Assert.That(CombatFormulas.DurabilityDegradeChancePercent(49, 100),
                Is.EqualTo(CombatFormulas.DurDegradeDamagedChancePct));
            Assert.That(CombatFormulas.DurabilityDegradeChancePercent(24, 100),
                Is.EqualTo(CombatFormulas.DurDegradeCriticalChancePct), "below 25% every hit chips");
        });

        // Monotonic the whole way down — no band may be gentler than the one above it.
        int previous = 0;
        for (int dur = 100; dur >= 0; dur--)
        {
            int chance = CombatFormulas.DurabilityDegradeChancePercent(dur, 100);
            Assert.That(chance, Is.GreaterThanOrEqualTo(previous), $"chance dipped at {dur}% condition");
            previous = chance;
        }
    }

    [Test]
    public void ChipChance_IsScaleFree_SoDurabilityScalingCannotMoveTheGold()
    {
        // THE INVARIANT BEHIND #63. Durability went from a flat 100 to sqrt(level) x bulk — up to 2,000 —
        // and that was allowed to happen without retuning repair ONLY because the bands are percentages of
        // max. Equal CONDITION must mean equal chance at any pool size; if it ever stops being true,
        // raising durability starts silently changing what a level of repair costs.
        foreach (short maxDur in new short[] { 80, 100, 450, 1_000, 2_000 })
            foreach (int conditionPct in new[] { 100, 80, 60, 40, 20, 5 })
            {
                int dur = maxDur * conditionPct / 100;
                Assert.That(CombatFormulas.DurabilityDegradeChancePercent(dur, maxDur),
                    Is.EqualTo(CombatFormulas.DurabilityDegradeChancePercent(conditionPct, 100)),
                    $"{conditionPct}% condition must chip alike at maxDur {maxDur} and 100");
            }
    }

    [Test]
    public void FullCycleAverage_IsTheNumberTheEconomyIsQuotedOn()
    {
        // EconomyFormulas and CombatFormulas.SubHpReagentCostExact both price upkeep on ~0.48 durability lost
        // per wear event. That is not a chosen constant — it is the four bands integrated over a full
        // 100 → 0 cycle, so this recomputes it from the band constants rather than restating it.
        (int Width, int Chance)[] bands =
        [
            (100 - CombatFormulas.DurDegradeHealthyPct, CombatFormulas.DurDegradeHealthyChancePct),
            (CombatFormulas.DurDegradeHealthyPct - CombatFormulas.DurDegradeWornPct, CombatFormulas.DurDegradeWornChancePct),
            (CombatFormulas.DurDegradeWornPct - CombatFormulas.DurDegradeDamagedPct, CombatFormulas.DurDegradeDamagedChancePct),
            (CombatFormulas.DurDegradeDamagedPct, CombatFormulas.DurDegradeCriticalChancePct),
        ];
        double hits = 0;
        foreach (var (width, chance) in bands) hits += width * 100.0 / chance;
        double perEvent = 100.0 / hits;

        Assert.That(perEvent, Is.EqualTo(0.48).Within(0.01),
            "the 0.48 the repair share and the reagent bill are both computed from");

        // CONVEXITY, and it is a real strategy consequence rather than trivia: fresh gear loses a quarter
        // of a point per event, so a player who tops up before dropping out of the healthy band pays about
        // HALF the upkeep of one who runs every piece to zero. The measured 36-51% of income is therefore
        // the worst case, not the typical one.
        double freshPerEvent = CombatFormulas.DurDegradeHealthyChancePct / 100.0;
        Assert.That(freshPerEvent, Is.LessThan(perEvent * 0.6),
            "keeping gear healthy must be materially cheaper per event than running it to zero");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    static (CombatSystem Combat, PlayerRecord Player) Setup(bool withShield = true, IRandomSource? rng = null)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        // A REAL ItemSystem, unlike the other combat fixtures: the wear path calls SendInventoryUpdate on
        // every chip, so a null one throws before any assertion can run.
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var combat = new CombatSystem(world, pm, dispatcher, items, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!, rng: rng);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.Level = 50;
        p.Def = 100;
        p.Sp = 500;   // block and dodge both cost stamina; at 0 CanPlayerBlock is false

        foreach (var (itemNum, type) in new[]
                 { (ArmorNum, ItemType.Armor), (HelmetNum, ItemType.Helmet), (ShieldNum, ItemType.Shield) })
        {
            world.Items[itemNum].Name = type.ToString();
            world.Items[itemNum].Type = type;
            world.Items[itemNum].Durability = MaxDur;
            world.Items[itemNum].Power = 20;
        }

        p.Inv[1].Num = ArmorNum;  p.Inv[1].Dur = CriticalDur;  p.ArmorSlot = 1;
        p.Inv[2].Num = HelmetNum; p.Inv[2].Dur = CriticalDur;  p.HelmetSlot = 2;
        if (withShield) { p.Inv[3].Num = ShieldNum; p.Inv[3].Dur = CriticalDur; p.ShieldSlot = 3; }
        return (combat, p);
    }

    // No-op packet dispatcher (per-file convention). Wear only fans inventory updates out to it.
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
