using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Locks the rule that a REFRESH is not an author edit.
///
/// <para>Every item-picker row (NPC drop, quest reward, shop trade) normalizes its quantity against the
/// picked item's currency-ness, and re-runs that normalization whenever the item list changes. The trap is
/// that the arriving item list is what makes currency-ness KNOWABLE at all: a row built from disk cannot
/// answer "is this currency?" until the providers are attached, which happens on SELECTION. So the first
/// normalization of a legitimately-authored record fires the moment the designer clicks it — and if that
/// write marks dirty, every such record shows the unsaved-changes dot on sight.</para>
///
/// <para>This is not hypothetical: the seeded bestiary carries a quantity on non-currency drop lines
/// (treasure and the like), so it dirtied 108 drop lines across the roster. A dot that is always on makes a
/// real unsaved edit indistinguishable from a file read off disk, which is the whole value of the dot.</para>
///
/// <para>The other half of each test matters just as much: the refresh must still NORMALIZE. Suppressing
/// the dirty flag by skipping the coercion would leave the spinner showing a number the game discards.</para>
/// </summary>
[TestFixture]
public class RowRefreshDirtyTests
{
    // Item 1 is currency (gold); everything else is not — the same shape the live world has.
    private const int Gold = 1;
    private const int Sword = 2;
    private static NamedEntry[] Entries() =>
        [new NamedEntry(0, ""), new NamedEntry(Gold, "Gold"), new NamedEntry(Sword, "Sword")];
    private static bool IsCurrency(int id) => id == Gold;

    // ── NPC drop rows ─────────────────────────────────────────────────────────
    // Rule: no item → 0; non-currency → 0 (the game reads no quantity off a non-stacking drop);
    // currency → at least 1.

    [Test]
    public void NpcDrop_RefreshNormalizingAuthoredQuantity_DoesNotDirty()
    {
        // Exactly what the seed holds: a non-currency drop carrying quantity 1.
        var npc = new NpcRowViewModel(1, new NpcRecord
        {
            Name = "Orc Raider",
            Drops = [new NpcDrop { ItemNum = Sword, Quantity = 1, Chance = 2 }],
        });
        Assert.That(npc.IsDirty, Is.False, "a row built straight from disk is not an edit");

        // Selection wires the pickers, which is the first moment currency-ness is knowable.
        npc.AttachItemProviders(Entries, IsCurrency);

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops[0].Value, Is.EqualTo(0), "the refresh must still normalize");
            Assert.That(npc.Drops[0].IsDirty, Is.False, "normalizing is not an author edit");
            Assert.That(npc.IsDirty, Is.False, "opening an NPC must not mark it modified");
        });
    }

    [Test]
    public void NpcDrop_AuthorEditingQuantity_StillDirties()
    {
        var npc = new NpcRowViewModel(1, new NpcRecord
        {
            Name = "Orc Raider",
            Drops = [new NpcDrop { ItemNum = Gold, Quantity = 10, Chance = 60 }],
        });
        npc.AttachItemProviders(Entries, IsCurrency);
        Assert.That(npc.IsDirty, Is.False, "precondition: opening is clean");

        npc.Drops[0].Value = 25;

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops[0].IsDirty, Is.True);
            Assert.That(npc.IsDirty, Is.True, "a real edit must still raise the dot");
        });
    }

    // ── Quest reward rows ─────────────────────────────────────────────────────
    // Rule: no item → 0; non-currency → exactly 1; currency → at least 1.

    [Test]
    public void QuestReward_RefreshNormalizingAuthoredQuantity_DoesNotDirty()
    {
        var row = new QuestRewardRowViewModel(1, new QuestReward { ItemNum = Sword, Quantity = 5 }, Entries, IsCurrency);
        Assert.That(row.IsDirty, Is.False);

        row.NotifyEntriesChanged();

        Assert.Multiple(() =>
        {
            Assert.That(row.Value, Is.EqualTo(1), "a non-currency reward never stacks");
            Assert.That(row.IsDirty, Is.False);
        });
    }

    [Test]
    public void QuestReward_AuthorEditingQuantity_StillDirties()
    {
        var row = new QuestRewardRowViewModel(1, new QuestReward { ItemNum = Gold, Quantity = 100 }, Entries, IsCurrency);
        row.NotifyEntriesChanged();
        Assert.That(row.IsDirty, Is.False, "precondition: opening is clean");

        row.Value = 250;

        Assert.That(row.IsDirty, Is.True);
    }

    // ── Shop trade rows ───────────────────────────────────────────────────────
    // Same rule as a quest reward, applied to both sides of the trade.

    [Test]
    public void Trade_RefreshNormalizingAuthoredQuantity_DoesNotDirty()
    {
        var row = new ShopBarterRowViewModel(1,
            new BarterItemRecord { GiveItem = Gold, GiveQuantity = 200, GetItem = Sword, GetQuantity = 7 },
            Entries, IsCurrency);
        Assert.That(row.IsDirty, Is.False);

        row.NotifyEntriesChanged();

        Assert.Multiple(() =>
        {
            Assert.That(row.GiveQuantity, Is.EqualTo(200), "currency keeps its authored stack");
            Assert.That(row.GetQuantity, Is.EqualTo(1), "the non-currency side normalizes to one");
            Assert.That(row.IsDirty, Is.False);
        });
    }

    [Test]
    public void Trade_AuthorEditingQuantity_StillDirties()
    {
        var row = new ShopBarterRowViewModel(1,
            new BarterItemRecord { GiveItem = Gold, GiveQuantity = 200, GetItem = Sword, GetQuantity = 1 },
            Entries, IsCurrency);
        row.NotifyEntriesChanged();
        Assert.That(row.IsDirty, Is.False, "precondition: opening is clean");

        row.GiveQuantity = 300;

        Assert.That(row.IsDirty, Is.True);
    }
}
