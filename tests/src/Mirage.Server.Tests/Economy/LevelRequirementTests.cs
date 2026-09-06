using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// The level requirement on items and spells — the gate that paces the tier ladder.
///
/// <para>Every test here keeps the player's STAT comfortably above the item's requirement, so a refusal
/// can only be the level gate. That isolation is the point: the two gates answer different questions
/// (stat = who may use this, level = when), and a test that let both fail at once would pass for the
/// wrong reason.</para>
/// </summary>
[TestFixture]
public class LevelRequirementTests
{
    const int Map = 1, Idx = 1;
    const int Sword = 10, Armor = 11, Potion = 12, Scroll = 13, Coin = 14;

    static (GameWorld world, ItemSystem items, PlayerRecord p) Setup(int level)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var items = new ItemSystem(world, pm, new NoOpDispatcher(), persistence: null!, bg: null!);
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.Level = level;
        // Far above anything asked for below, so no STAT gate ever fires and the level gate is the only
        // thing under test. INT included: a spell's VitalAmount doubles as its INT requirement and floors
        // at 1, so even a trivial spell refuses a 0-INT character — which would fail these tests for the
        // wrong reason.
        p.Str = 200;
        p.Def = 200;
        p.Int = 200;
        p.MaxHp = 100;
        p.Hp = 50;
        return (world, items, p);
    }

    // ── Applicability: which kinds of item carry a level at all ──────────────

    [Test]
    public void UsesLevelReq_CoversWhatIsWornOrDrunk()
    {
        Assert.Multiple(() =>
        {
            foreach (var t in new[] { ItemType.Weapon, ItemType.Armor, ItemType.Helmet, ItemType.Shield })
                Assert.That(ItemRecord.UsesLevelReq(t), Is.True, $"{t} is worn, so it can carry a level");
            foreach (var t in new[] { ItemType.PotionAddHp, ItemType.PotionSubSp })
                Assert.That(ItemRecord.UsesLevelReq(t), Is.True, $"{t} is drunk, so it can carry a level");

            // A scroll's gate is on the SPELL it teaches — that is where learning is refused. A level on the
            // paper as well would be a second number nothing reads, and an editor field always showing zero.
            Assert.That(ItemRecord.UsesLevelReq(ItemType.Spell), Is.False);

            // Gold is not something you qualify for, and a door that refuses its own key because the
            // holder is under-leveled is a puzzle nobody asked for.
            Assert.That(ItemRecord.UsesLevelReq(ItemType.Currency), Is.False);
            Assert.That(ItemRecord.UsesLevelReq(ItemType.Key), Is.False);
        });
    }

    // ── What a buyer is shown before paying ──────────────────────────────────
    // The shop confirm and any other acquisition screen ask ItemRecord for one number. A scroll's own is
    // always zero, so asking the item alone reports "no requirement" on the single purchase whose
    // requirement is easiest to miss — and the buyer finds out from a refusal after the gold is gone.

    [Test]
    public void EffectiveLevelReq_OfEquipment_IsTheItemsOwn()
    {
        var sword = new ItemRecord { Type = ItemType.Weapon, LevelReq = 40 };

        Assert.That(ItemRecord.EffectiveLevelReq(sword, null), Is.EqualTo(40));
    }

    [Test]
    public void EffectiveLevelReq_OfAScroll_ComesFromTheSpellItTeaches()
    {
        var scroll = new ItemRecord { Type = ItemType.Spell, SpellNum = 7 };
        var taught = new SpellRecord { LevelReq = 100 };

        Assert.Multiple(() =>
        {
            Assert.That(scroll.LevelReq, Is.Zero, "a scroll carries no level of its own");
            Assert.That(ItemRecord.EffectiveLevelReq(scroll, taught), Is.EqualTo(100));
        });
    }

    /// <summary>A scroll whose spell is unknown to the reader (an out-of-range number, or a client that has
    /// not been sent that definition) reports no gate rather than guessing one.</summary>
    [Test]
    public void EffectiveLevelReq_OfAScrollWithNoSpell_IsZero()
    {
        var scroll = new ItemRecord { Type = ItemType.Spell, SpellNum = 7 };

        Assert.That(ItemRecord.EffectiveLevelReq(scroll, null), Is.Zero);
    }

    /// <summary>The spell is consulted only for a scroll. A potion handed the wrong spell must not start
    /// reporting that spell's level.</summary>
    [Test]
    public void EffectiveLevelReq_IgnoresASpellOnAnythingButAScroll()
    {
        var potion = new ItemRecord { Type = ItemType.PotionAddHp, LevelReq = 0 };
        var unrelated = new SpellRecord { LevelReq = 200 };

        Assert.That(ItemRecord.EffectiveLevelReq(potion, unrelated), Is.Zero);
    }

    [Test]
    public void EffectiveLevelReq_OfNothing_IsZero()
    {
        Assert.That(ItemRecord.EffectiveLevelReq(null, null), Is.Zero);
    }

    /// <summary>Dropping the scroll case must not loosen the real gate: learning is still refused below the
    /// SPELL's level, which is enforced where the scroll is used rather than on the item record.</summary>
    [Test]
    public void AScrollsGateIsTheSpellsLevel_NotTheItems()
    {
        var (world, items, p) = Setup(level: 5);
        world.Items[9].Type = ItemType.Spell;
        world.Items[9].SpellNum = 4;
        world.Items[9].LevelReq = 0;             // Normalize() zeroes it; the paper carries no level
        world.Spells[4].Name = "Firebolt";
        world.Spells[4].LevelReq = 30;
        p.Inv[1].Num = 9;

        items.UseItem(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Spell.Count(s => s == 4), Is.Zero, "under the spell's level, so nothing is learned");
            Assert.That(p.Inv[1].Num, Is.EqualTo(9), "and the scroll is not consumed");
        });
    }

    [Test]
    public void Normalize_ClearsTheLevelOnTypesThatCannotUseIt()
    {
        var currency = new ItemRecord { Type = ItemType.Currency, LevelReq = 20 };
        var key = new ItemRecord { Type = ItemType.Key, LevelReq = 20 };
        var sword = new ItemRecord { Type = ItemType.Weapon, LevelReq = 20 };
        currency.Normalize();
        key.Normalize();
        sword.Normalize();
        Assert.Multiple(() =>
        {
            Assert.That(currency.LevelReq, Is.EqualTo(0));
            Assert.That(key.LevelReq, Is.EqualTo(0));
            Assert.That(sword.LevelReq, Is.EqualTo(20), "a weapon keeps its level");
        });
    }

    /// <summary>Normalize and the predicate must agree across EVERY type. They drive different things — the
    /// editor shows the field from the predicate, Normalize decides what a save keeps — so a type where they
    /// disagree is a field that offers to be set and then silently discards what was typed.</summary>
    [Test]
    public void Normalize_AndUsesLevelReq_AgreeOnEveryType()
    {
        foreach (ItemType t in Enum.GetValues<ItemType>())
        {
            var r = new ItemRecord { Type = t, LevelReq = 40 };
            r.Normalize();
            Assert.That(r.LevelReq, Is.EqualTo(ItemRecord.UsesLevelReq(t) ? (short)40 : (short)0),
                $"{t}: Normalize and UsesLevelReq disagree, so the editor and the saved file do too");
        }
    }

    // Unlike the item fields, a spell's level applies to every type, so Normalize must never clear it —
    // retyping a spell keeps its tier.
    [Test]
    public void SpellNormalize_KeepsTheLevelOnEveryType()
    {
        foreach (SpellType t in Enum.GetValues<SpellType>())
        {
            var s = new SpellRecord { Type = t, VitalAmount = 5, IntReq = 5, LevelReq = 12 };
            s.Normalize();
            Assert.That(s.LevelReq, Is.EqualTo(12), $"{t} should keep its level requirement");
        }
    }

    // ── Equip ────────────────────────────────────────────────────────────────

    [Test]
    public void Equip_BelowLevel_Refused()
    {
        var (world, items, p) = Setup(level: 4);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Power = 10;
        world.Items[Sword].LevelReq = 5;
        items.GiveItem(Idx, Sword, 0);

        items.UseItem(Idx, 1);

        Assert.That(p.WeaponSlot, Is.EqualTo(0), "a level-5 weapon must not go on a level-4 character");
    }

    [Test]
    public void Equip_AtExactlyTheRequiredLevel_Allowed()
    {
        var (world, items, p) = Setup(level: 5);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Power = 10;
        world.Items[Sword].LevelReq = 5;
        items.GiveItem(Idx, Sword, 0);

        items.UseItem(Idx, 1);

        Assert.That(p.WeaponSlot, Is.EqualTo(1), "the requirement is a minimum, not a threshold to exceed");
    }

    [Test]
    public void Equip_NoLevelRequirement_Allowed()
    {
        var (world, items, p) = Setup(level: 1);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Power = 10;
        items.GiveItem(Idx, Sword, 0);

        items.UseItem(Idx, 1);

        Assert.That(p.WeaponSlot, Is.EqualTo(1), "0 means no level gate");
    }

    // Taking a piece OFF is never blocked. Otherwise a player who dropped a level while wearing something
    // would be stuck in it — the one state the gate must not create.
    [Test]
    public void Unequip_IsAllowedEvenWhenBelowTheLevel()
    {
        var (world, items, p) = Setup(level: 10);
        world.Items[Armor].Type = ItemType.Armor;
        world.Items[Armor].Power = 10;
        world.Items[Armor].LevelReq = 10;
        items.GiveItem(Idx, Armor, 0);
        items.UseItem(Idx, 1);
        Assert.That(p.ArmorSlot, Is.EqualTo(1), "precondition: it is on");

        p.Level = 3;                 // deleveled below it
        items.UseItem(Idx, 1);       // toggle off

        Assert.That(p.ArmorSlot, Is.EqualTo(0), "you can always take a piece off");
    }

    // ── Consumables ──────────────────────────────────────────────────────────

    [Test]
    public void Potion_BelowLevel_Refused()
    {
        var (world, items, p) = Setup(level: 2);
        world.Items[Potion].Type = ItemType.PotionAddHp;
        world.Items[Potion].VitalAmount = 20;
        world.Items[Potion].LevelReq = 10;
        items.GiveItem(Idx, Potion, 1);
        int before = p.Hp;

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(before), "an under-level potion must not heal");
            Assert.That(ItemSystem.CountItem(p, world.Items, Potion), Is.GreaterThan(0), "nor be consumed");
        });
    }

    [Test]
    public void Potion_AtLevel_Works()
    {
        var (world, items, p) = Setup(level: 10);
        world.Items[Potion].Type = ItemType.PotionAddHp;
        world.Items[Potion].VitalAmount = 20;
        world.Items[Potion].LevelReq = 10;
        items.GiveItem(Idx, Potion, 1);

        items.UseItem(Idx, 1);

        Assert.That(p.Hp, Is.GreaterThan(50), "at level, the potion heals");
    }

    // Currency carries no level gate at all, so it can never be blocked by one — this is the applicability
    // rule reaching the live path rather than just the record.
    [Test]
    public void Currency_IsNeverLevelGated()
    {
        var (world, items, p) = Setup(level: 1);
        world.Items[Coin].Type = ItemType.Currency;
        world.Items[Coin].LevelReq = 99;   // authored in error; Normalize would strip it
        world.Items[Coin].Normalize();
        items.GiveItem(Idx, Coin, 500);

        Assert.That(world.Items[Coin].LevelReq, Is.EqualTo(0));
        Assert.That(ItemSystem.CountItem(p, world.Items, Coin), Is.EqualTo(500));
    }

    // ── Learning from a scroll ───────────────────────────────────────────────
    // Two independent gates meet here: the SCROLL's level (it is an item) and the SPELL's own. A scroll is
    // only a delivery mechanism and could reasonably be handed out early, so the spell it teaches carries
    // its own tier.

    [Test]
    public void Scroll_SpellAboveLevel_NotLearnedAndScrollKept()
    {
        var (world, items, p) = Setup(level: 3);
        world.Spells[1].Name = "Firebolt";
        world.Spells[1].Type = SpellType.SubHp;
        world.Spells[1].VitalAmount = 1;      // trivial INT gate, so only the level can refuse
        world.Spells[1].LevelReq = 10;
        world.Items[Scroll].Type = ItemType.Spell;
        world.Items[Scroll].SpellNum = 1;
        items.GiveItem(Idx, Scroll, 1);

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(SpellSystem.HasSpell(p, 1), Is.False, "an under-level spell is not learned");
            Assert.That(ItemSystem.CountItem(p, world.Items, Scroll), Is.GreaterThan(0), "and the scroll survives");
        });
    }

    [Test]
    public void Scroll_SpellAtLevel_Learned()
    {
        var (world, items, p) = Setup(level: 10);
        world.Spells[1].Name = "Firebolt";
        world.Spells[1].Type = SpellType.SubHp;
        world.Spells[1].VitalAmount = 1;
        world.Spells[1].LevelReq = 10;
        world.Items[Scroll].Type = ItemType.Spell;
        world.Items[Scroll].SpellNum = 1;
        items.GiveItem(Idx, Scroll, 1);

        items.UseItem(Idx, 1);

        Assert.That(SpellSystem.HasSpell(p, 1), Is.True);
    }

    // The scroll's own level and the spell's are separate numbers, and BOTH must be met. This is the case
    // that would silently pass if one gate were reading the other's field.
    [Test]
    public void Scroll_FreelyGivenScrollStillRespectsTheSpellsLevel()
    {
        var (world, items, p) = Setup(level: 3);
        world.Spells[1].Name = "Firebolt";
        world.Spells[1].Type = SpellType.SubHp;
        world.Spells[1].VitalAmount = 1;
        world.Spells[1].LevelReq = 10;        // the SPELL is tier-gated
        world.Items[Scroll].Type = ItemType.Spell;
        world.Items[Scroll].SpellNum = 1;
        world.Items[Scroll].LevelReq = 0;     // the SCROLL is not
        items.GiveItem(Idx, Scroll, 1);

        items.UseItem(Idx, 1);

        Assert.That(SpellSystem.HasSpell(p, 1), Is.False,
            "an ungated scroll must not launder a level-gated spell");
    }

    // ── The delevel sweep ────────────────────────────────────────────────────
    // A delevel drains stats AND drops the level. The sweep already handled the stat half; without the
    // level half a player could die below a piece's tier and keep wearing it, unable to put it back on
    // if they ever took it off.

    [Test]
    public void DelevelSweep_TakesOffGearAboveTheNewLevel()
    {
        var (world, items, p) = Setup(level: 15);
        world.Items[Armor].Type = ItemType.Armor;
        world.Items[Armor].Power = 10;
        world.Items[Armor].LevelReq = 15;
        items.GiveItem(Idx, Armor, 0);
        items.UseItem(Idx, 1);
        Assert.That(p.ArmorSlot, Is.EqualTo(1), "precondition: it is on");

        p.Level = 9;
        items.RevalidateEquipmentRequirements(Idx);

        Assert.That(p.ArmorSlot, Is.EqualTo(0), "falling below the level takes the piece off");
    }

    [Test]
    public void DelevelSweep_LeavesGearThatStillQualifies()
    {
        var (world, items, p) = Setup(level: 15);
        world.Items[Armor].Type = ItemType.Armor;
        world.Items[Armor].Power = 10;
        world.Items[Armor].LevelReq = 5;
        items.GiveItem(Idx, Armor, 0);
        items.UseItem(Idx, 1);

        p.Level = 9;                 // still above the piece's 5
        items.RevalidateEquipmentRequirements(Idx);

        Assert.That(p.ArmorSlot, Is.EqualTo(1), "a piece still within reach stays on");
    }

    // Regression: the sweep was renamed from UnequipIfBelowStatRequirement when the level check joined it.
    // The stat half must still work — that was the whole reason the sweep existed.
    [Test]
    public void DelevelSweep_StillTakesOffGearAboveTheNewStat()
    {
        var (world, items, p) = Setup(level: 20);
        world.Items[Armor].Type = ItemType.Armor;
        world.Items[Armor].Power = 100;
        items.GiveItem(Idx, Armor, 0);
        items.UseItem(Idx, 1);
        Assert.That(p.ArmorSlot, Is.EqualTo(1), "precondition: it is on");

        p.Def = 3;                   // respec/delevel drained the stat
        items.RevalidateEquipmentRequirements(Idx);

        Assert.That(p.ArmorSlot, Is.EqualTo(0), "the stat half of the sweep survived the rename");
    }

    // ── Harness ──────────────────────────────────────────────────────────────
    // No-op packet dispatcher, per the per-file convention used by the other ItemSystem suites.

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
