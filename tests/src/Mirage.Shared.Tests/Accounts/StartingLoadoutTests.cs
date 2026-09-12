using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>The world's starting loadout — <see cref="WorldManifest.StartingItems"/>, its
/// canonicalization, and the resolver that decides what a new character actually receives.
///
/// <para>The resolver is shared by the grant path and anything that previews it, precisely so those two
/// cannot answer differently: gear that silently vanishes at creation, and a screen promising a sword the
/// server then withholds, are the same bug seen from two ends.</para></summary>
[TestFixture]
public class StartingLoadoutTests
{
    // 1 gold, 2 a sword, 3 a potion.
    private static ItemRecord[] Items() =>
    [
        new(),
        new() { Name = "Gold", Type = ItemType.Currency },
        new() { Name = "Light Sword", Type = ItemType.Equipment, EquipSlot = "hand", Power = 6, Durability = 40 },
        new() { Name = "Elixir", Type = ItemType.Consumable, VitalAmount = 20 },
    ];

    private static List<StartingItem> Authored(params int[] itemNums) =>
        [.. itemNums.Select(n => new StartingItem { ItemNum = n, Quantity = 200 })];

    // ── Normalize ────────────────────────────────────────────────────────────

    [Test]
    public void Normalize_DropsLinesNamingNoItem()
    {
        var kept = StartingLoadout.Normalize([new StartingItem { ItemNum = 4 }, new StartingItem { ItemNum = 0 }]);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Has.Count.EqualTo(1));
            Assert.That(kept[0].ItemNum, Is.EqualTo(4));
        });
    }

    [Test]
    public void Normalize_CapsToWhatACharacterCanHold()
    {
        var authored = Authored([.. Enumerable.Range(1, Constants.MaxInv + 10)]);

        Assert.That(StartingLoadout.Normalize(authored), Has.Count.EqualTo(Constants.MaxInv));
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        var once = StartingLoadout.Normalize([new StartingItem { ItemNum = 4 }, new StartingItem { ItemNum = 0 }]);

        Assert.That(StartingLoadout.Normalize(once), Has.Count.EqualTo(1));
    }

    [Test]
    public void Normalize_TakesNullAsNothingAuthored()
    {
        Assert.That(StartingLoadout.Normalize(null), Is.Empty);
    }

    /// <summary>The manifest normalizes on the way in, so nothing downstream has to wonder whether the
    /// list it holds was canonicalized.</summary>
    [Test]
    public void TheManifest_NormalizesWhatItIsGiven()
    {
        var manifest = new WorldManifest
        {
            StartingItems = [new StartingItem { ItemNum = 2 }, new StartingItem { ItemNum = 0 }],
        };

        Assert.That(manifest.StartingItems, Has.Count.EqualTo(1));
    }

    // ── The resolver ─────────────────────────────────────────────────────────

    [Test]
    public void Resolve_SkipsBlankReferencesAndLeavesNoGapInTheBag()
    {
        // Item 9 is off the end of the table and index 0 is the unused dummy. The potion behind them must
        // still land in slot 2 — a hole would be a bag that looks half-empty for no reason a player sees.
        var granted = StartingLoadout.ResolveItems(Authored(2, 9, 0, 3), Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted.Select(g => g.Num), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(granted.Select(g => g.Slot), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void Resolve_EquipmentIsWornAndCarriesFullDurability()
    {
        var granted = StartingLoadout.ResolveItems(Authored(2, 3), Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted[0].Worn, Is.True, "a weapon arrives equipped");
            Assert.That(granted[0].Durability, Is.EqualTo(40), "and pristine");
            Assert.That(granted[1].Worn, Is.False, "a potion is carried");
        });
    }

    [Test]
    public void Resolve_CurrencyKeepsItsStackAndEverythingElseIsExactlyOne()
    {
        var granted = StartingLoadout.ResolveItems(Authored(1, 3), Items());

        Assert.Multiple(() =>
        {
            Assert.That(granted[0].Value, Is.EqualTo(200));
            Assert.That(granted[1].Value, Is.EqualTo(0), "the engine reads Value only for currency");
        });
    }

    [Test]
    public void Resolve_StopsAtWhatTheBagHolds()
    {
        var granted = StartingLoadout.ResolveItems(Authored([.. Enumerable.Repeat(3, Constants.MaxInv + 5)]), Items());

        Assert.That(granted, Has.Count.EqualTo(Constants.MaxInv));
    }

    // ── The grant ────────────────────────────────────────────────────────────

    [Test]
    public void Grant_FillsTheBagAndWearsWhatNamesASlot()
    {
        var chr = new PlayerRecord();

        StartingLoadout.Grant(chr, Authored(3, 2), Items());

        Assert.Multiple(() =>
        {
            Assert.That(chr.Inv[1].Num, Is.EqualTo(3), "the potion was authored first, so it takes slot 1");
            Assert.That(chr.Inv[2].Num, Is.EqualTo(2));
            Assert.That(chr.Inv[2].Dur, Is.EqualTo(40));
            Assert.That(chr.EquippedIn("hand"), Is.EqualTo(2), "the sword arrives worn, pointing at its bag slot");
            Assert.That(chr.Equipped, Has.Count.EqualTo(1), "and nothing else is worn by accident");
        });
    }

    [Test]
    public void Grant_OfNothingLeavesACharacterEmptyHanded()
    {
        var chr = new PlayerRecord();

        StartingLoadout.Grant(chr, [], Items());

        Assert.Multiple(() =>
        {
            Assert.That(chr.Inv[1].Num, Is.Zero);
            Assert.That(chr.Equipped, Is.Empty);
        });
    }
}
