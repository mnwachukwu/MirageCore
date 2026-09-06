using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// What giving and taking mean to a character RECORD — the half of <see cref="ItemSystem"/> with no player
/// slot, no packet and no message behind it.
///
/// <para>It exists because the account browser edits characters who are nowhere near a player slot: half the
/// time the target is offline and the only copy is a line in an account file. Re-implementing "give" over
/// there would have been a second answer to stacking, durability and a full bag.</para>
/// </summary>
[TestFixture]
public class EditorBagEditTests
{
    const int Gold = Constants.GoldItemIndex, Sword = 10, Potion = 16;

    static (ItemRecord[] Items, PlayerRecord P) Setup()
    {
        var items = new ItemRecord[64];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemRecord();
        items[Gold].Type = ItemType.Currency;
        items[Sword].Type = ItemType.Weapon;
        items[Sword].Durability = 60;
        items[Potion].Type = ItemType.PotionAddHp;

        var p = new PlayerRecord();
        for (int i = 0; i <= Constants.MaxInv; i++) p.Inv[i] = new PlayerInvSlot();
        return (items, p);
    }

    // ── Giving ────────────────────────────────────────────────────────────────

    [Test]
    public void GivingAPiece_TakesTheFirstFreeSlot_AtFullDurability()
    {
        var (items, p) = Setup();

        int slot = ItemSystem.PlaceInInventory(p, items, Sword, 1);

        Assert.Multiple(() =>
        {
            Assert.That(slot, Is.EqualTo(1));
            Assert.That(p.Inv[1].Num, Is.EqualTo(Sword));
            Assert.That(p.Inv[1].Dur, Is.EqualTo(60), "equipment arrives unworn");
        });
    }

    [Test]
    public void GivingCurrency_StacksOntoWhatIsAlreadyThere()
    {
        var (items, p) = Setup();
        p.Inv[3].Num = Gold;
        p.Inv[3].Quantity = 500;

        int slot = ItemSystem.PlaceInInventory(p, items, Gold, 250);

        Assert.Multiple(() =>
        {
            Assert.That(slot, Is.EqualTo(3), "the existing pile, not a second one");
            Assert.That(p.Inv[3].Quantity, Is.EqualTo(750));
        });
    }

    [Test]
    public void GivingIntoAFullBag_PlacesNothing()
    {
        var (items, p) = Setup();
        for (int i = 1; i <= Constants.MaxInv; i++) { p.Inv[i].Num = Potion; p.Inv[i].Quantity = 1; }

        Assert.That(ItemSystem.PlaceInInventory(p, items, Sword, 1), Is.Zero);
    }

    [Test]
    public void GivingAnItemThatDoesNotExist_PlacesNothing()
    {
        var (items, p) = Setup();
        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.PlaceInInventory(p, items, 0, 1), Is.Zero);
            Assert.That(ItemSystem.PlaceInInventory(p, items, items.Length + 5, 1), Is.Zero);
        });
    }

    // ── Taking ────────────────────────────────────────────────────────────────

    [Test]
    public void TakingAPiece_EmptiesTheSlot()
    {
        var (items, p) = Setup();
        p.Inv[2].Num = Sword;
        p.Inv[2].Quantity = 1;
        p.Inv[2].Dur = 40;

        var taken = ItemSystem.TakeFromInventory(p, items, 2, 0);

        Assert.Multiple(() =>
        {
            Assert.That(taken.ItemNum, Is.EqualTo(Sword));
            Assert.That(p.Inv[2].Num, Is.Zero);
            Assert.That(p.Inv[2].Dur, Is.Zero);
        });
    }

    [Test]
    public void TakingPartOfAStack_LeavesTheRest()
    {
        var (items, p) = Setup();
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 900;

        var taken = ItemSystem.TakeFromInventory(p, items, 1, 300);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo((Gold, 300)));
            Assert.That(p.Inv[1].Num, Is.EqualTo(Gold), "the pile is still there");
            Assert.That(p.Inv[1].Quantity, Is.EqualTo(600));
        });
    }

    [TestCase(0)]
    [TestCase(9999)]
    public void TakingAllOfAStack_ClearsTheSlot(int amount)
    {
        var (items, p) = Setup();
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 900;

        var taken = ItemSystem.TakeFromInventory(p, items, 1, amount);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo((Gold, 900)));
            Assert.That(p.Inv[1].Num, Is.Zero);
        });
    }

    /// <summary>An amount means nothing to a sword: it is one piece with its own durability, so a partial
    /// take would have to leave half a sword behind.</summary>
    [Test]
    public void TakingPartOfSomethingThatDoesNotStack_TakesTheWholeThing()
    {
        var (items, p) = Setup();
        p.Inv[1].Num = Sword;
        p.Inv[1].Quantity = 1;

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.TakeFromInventory(p, items, 1, 1).ItemNum, Is.EqualTo(Sword));
            Assert.That(p.Inv[1].Num, Is.Zero);
        });
    }

    /// <summary>🔴 The in-game paths REFUSE a worn piece and tell the player to unequip first. An operator
    /// reaching into somebody else's bag cannot do that, and a gear pointer left naming an emptied slot is a
    /// corrupt character sheet — so this one strips the piece on the way out.</summary>
    [Test]
    public void TakingAWornPiece_StripsIt()
    {
        var (items, p) = Setup();
        p.Inv[4].Num = Sword;
        p.Inv[4].Quantity = 1;
        p.WeaponSlot = 4;

        var taken = ItemSystem.TakeFromInventory(p, items, 4, 0);

        Assert.Multiple(() =>
        {
            Assert.That(taken.ItemNum, Is.EqualTo(Sword));
            Assert.That(p.Inv[4].Num, Is.Zero);
            Assert.That(p.WeaponSlot, Is.Zero, "the pointer would otherwise name an empty slot");
        });
    }

    [Test]
    public void TakingASlotThatIsNotWorn_LeavesTheWornOneAlone()
    {
        var (items, p) = Setup();
        p.Inv[1].Num = Sword; p.Inv[1].Quantity = 1;
        p.Inv[2].Num = Sword; p.Inv[2].Quantity = 1;
        p.WeaponSlot = 1;

        ItemSystem.TakeFromInventory(p, items, 2, 0);

        Assert.That(p.WeaponSlot, Is.EqualTo(1));
    }

    [Test]
    public void TakingFromAnEmptySlot_TakesNothing()
    {
        var (items, p) = Setup();
        Assert.That(ItemSystem.TakeFromInventory(p, items, 5, 0), Is.EqualTo((0, 0)));
    }

    [TestCase(0)]
    [TestCase(Constants.MaxInv + 1)]
    public void TakingFromASlotThatIsNotABagSlot_TakesNothing(int invSlot)
    {
        var (items, p) = Setup();
        Assert.That(ItemSystem.TakeFromInventory(p, items, invSlot, 0), Is.EqualTo((0, 0)));
    }

    // ── The pair ──────────────────────────────────────────────────────────────

    [Test]
    public void WhatIsGivenComesBackOut()
    {
        var (items, p) = Setup();
        ItemSystem.PlaceInInventory(p, items, Potion, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(p, items, Potion), Is.EqualTo(1));
            Assert.That(ItemSystem.TakeFromInventory(p, items, 1, 0).ItemNum, Is.EqualTo(Potion));
            Assert.That(ItemSystem.CountItem(p, items, Potion), Is.Zero);
        });
    }
}
