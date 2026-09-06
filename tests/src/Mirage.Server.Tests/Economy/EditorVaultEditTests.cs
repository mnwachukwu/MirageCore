using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// What depositing and withdrawing mean to a vault ARRAY — the half of <see cref="BankSystem"/> with no
/// player slot behind it, which is what the account browser reaches for when the vault belongs to nobody who
/// is logged in.
///
/// <para>The vault is ACCOUNT-shared rather than per character, so it is edited beside the access and guild
/// lines rather than on a character card, and what decides which copy is authoritative is whether anybody on
/// the account is online — not which character is on screen.</para>
/// </summary>
[TestFixture]
public class EditorVaultEditTests
{
    const int Gold = Constants.GoldItemIndex, Sword = 10, Potion = 16;

    static (ItemRecord[] Items, PlayerInvSlot[] Bank) Setup()
    {
        var items = new ItemRecord[64];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemRecord();
        items[Gold].Type = ItemType.Currency;
        items[Sword].Type = ItemType.Weapon;
        items[Sword].Durability = 60;
        items[Potion].Type = ItemType.PotionAddHp;

        return (items, AccountRecord.NewBank());
    }

    // ── Putting in ────────────────────────────────────────────────────────────

    [Test]
    public void APieceTakesTheFirstFreeSlot_AtFullDurability()
    {
        var (items, bank) = Setup();

        int slot = BankSystem.PlaceInBank(bank, items, Sword, 1);

        Assert.Multiple(() =>
        {
            Assert.That(slot, Is.EqualTo(1));
            Assert.That(bank[1].Num, Is.EqualTo(Sword));
            Assert.That(bank[1].Dur, Is.EqualTo(60));
        });
    }

    [Test]
    public void CurrencyStacksOntoWhatIsAlreadyThere()
    {
        var (items, bank) = Setup();
        bank[4].Num = Gold;
        bank[4].Quantity = 1_000;

        int slot = BankSystem.PlaceInBank(bank, items, Gold, 500);

        Assert.Multiple(() =>
        {
            Assert.That(slot, Is.EqualTo(4), "the existing pile, not a second one");
            Assert.That(bank[4].Quantity, Is.EqualTo(1_500));
        });
    }

    /// <summary>Two of the same sword are two vault slots. Only currency stacks — everything else carries its
    /// own durability, and merging them would have to pick one.</summary>
    [Test]
    public void TwoOfTheSamePieceTakeTwoSlots()
    {
        var (items, bank) = Setup();

        Assert.Multiple(() =>
        {
            Assert.That(BankSystem.PlaceInBank(bank, items, Sword, 1), Is.EqualTo(1));
            Assert.That(BankSystem.PlaceInBank(bank, items, Sword, 1), Is.EqualTo(2));
        });
    }

    [Test]
    public void AFullVaultTakesNothingMore()
    {
        var (items, bank) = Setup();
        for (int i = 1; i <= Constants.MaxBankSlots; i++) { bank[i].Num = Potion; bank[i].Quantity = 1; }

        Assert.That(BankSystem.PlaceInBank(bank, items, Sword, 1), Is.Zero);
    }

    [Test]
    public void CurrencyStillStacksIntoAnOtherwiseFullVault()
    {
        var (items, bank) = Setup();
        for (int i = 1; i <= Constants.MaxBankSlots; i++) { bank[i].Num = Potion; bank[i].Quantity = 1; }
        bank[7].Num = Gold;
        bank[7].Quantity = 10;

        Assert.Multiple(() =>
        {
            Assert.That(BankSystem.PlaceInBank(bank, items, Gold, 90), Is.EqualTo(7));
            Assert.That(bank[7].Quantity, Is.EqualTo(100));
        });
    }

    [Test]
    public void AnItemThatDoesNotExistGoesNowhere()
    {
        var (items, bank) = Setup();
        Assert.Multiple(() =>
        {
            Assert.That(BankSystem.PlaceInBank(bank, items, 0, 1), Is.Zero);
            Assert.That(BankSystem.PlaceInBank(bank, items, items.Length + 3, 1), Is.Zero);
        });
    }

    // ── Taking out ────────────────────────────────────────────────────────────

    [Test]
    public void TakingAPieceEmptiesTheSlot()
    {
        var (items, bank) = Setup();
        bank[2].Num = Sword;
        bank[2].Quantity = 1;
        bank[2].Dur = 25;

        var taken = BankSystem.TakeFromBank(bank, items, 2, 0);

        Assert.Multiple(() =>
        {
            Assert.That(taken.ItemNum, Is.EqualTo(Sword));
            Assert.That(bank[2].Num, Is.Zero);
            Assert.That(bank[2].Dur, Is.Zero);
        });
    }

    [Test]
    public void TakingPartOfAStackLeavesTheRest()
    {
        var (items, bank) = Setup();
        bank[1].Num = Gold;
        bank[1].Quantity = 900;

        var taken = BankSystem.TakeFromBank(bank, items, 1, 400);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo((Gold, 400)));
            Assert.That(bank[1].Quantity, Is.EqualTo(500));
        });
    }

    [TestCase(0)]
    [TestCase(99_999)]
    public void TakingAllOfAStackClearsTheSlot(int amount)
    {
        var (items, bank) = Setup();
        bank[1].Num = Gold;
        bank[1].Quantity = 900;

        Assert.Multiple(() =>
        {
            Assert.That(BankSystem.TakeFromBank(bank, items, 1, amount), Is.EqualTo((Gold, 900)));
            Assert.That(bank[1].Num, Is.Zero);
        });
    }

    [Test]
    public void TakingPartOfSomethingThatDoesNotStackTakesTheWholeThing()
    {
        var (items, bank) = Setup();
        bank[1].Num = Sword;
        bank[1].Quantity = 1;

        Assert.Multiple(() =>
        {
            Assert.That(BankSystem.TakeFromBank(bank, items, 1, 1).ItemNum, Is.EqualTo(Sword));
            Assert.That(bank[1].Num, Is.Zero);
        });
    }

    [Test]
    public void TakingFromAnEmptySlotTakesNothing()
    {
        var (items, bank) = Setup();
        Assert.That(BankSystem.TakeFromBank(bank, items, 3, 0), Is.EqualTo((0, 0)));
    }

    [TestCase(0)]
    [TestCase(Constants.MaxBankSlots + 1)]
    public void TakingFromASlotThatIsNotAVaultSlotTakesNothing(int bankSlot)
    {
        var (items, bank) = Setup();
        Assert.That(BankSystem.TakeFromBank(bank, items, bankSlot, 0), Is.EqualTo((0, 0)));
    }

    [Test]
    public void WhatIsPutInComesBackOut()
    {
        var (items, bank) = Setup();
        int slot = BankSystem.PlaceInBank(bank, items, Potion, 1);

        Assert.That(BankSystem.TakeFromBank(bank, items, slot, 0), Is.EqualTo((Potion, 1)));
    }
}
