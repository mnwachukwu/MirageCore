using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Formulas;

/// <summary>Pure inventory helpers on <see cref="ItemSystem"/> — no world/dispatcher needed. Locks the two
/// rules everything else builds on: currency STACKS onto an existing slot while gear takes the first empty
/// slot (<see cref="ItemSystem.FindOpenInvSlot"/>), and the canonical item ordering shared by the inventory
/// and bank sorts (<c>SortKey</c>): Gold, other currency, equipped gear, unequipped gear (strongest first),
/// keys, scrolls, Add then Sub potions.</summary>
[TestFixture]
public class ItemFormulaTests
{
    // Item ids used across the fixture (all <= RecordLimits.Default.Items = 255).
    const int Gold = Constants.GoldItemIndex;   // 1, Currency
    const int Cur = 5;                           // a non-gold currency
    const int Wep = 10, Arm = 11, Key = 12, Hlm = 13, Shd = 14, Spl = 15;
    const int PAdHp = 16, PAdMp = 17, PAdSp = 18, PSuHp = 19, PSuMp = 20, PSuSp = 21;

    static ItemRecord[] BuildItems()
    {
        var items = new ItemRecord[RecordLimits.Default.Items + 1];
        for (int i = 0; i <= RecordLimits.Default.Items; i++) items[i] = new ItemRecord();
        items[Gold].Type = ItemType.Currency;
        items[Cur].Type = ItemType.Currency;
        items[Wep].Type = ItemType.Weapon;
        items[Arm].Type = ItemType.Armor;
        items[Hlm].Type = ItemType.Helmet;
        items[Shd].Type = ItemType.Shield;
        items[Key].Type = ItemType.Key;
        items[Spl].Type = ItemType.Spell;
        items[PAdHp].Type = ItemType.PotionAddHp;
        items[PAdMp].Type = ItemType.PotionAddMp;
        items[PAdSp].Type = ItemType.PotionAddSp;
        items[PSuHp].Type = ItemType.PotionSubHp;
        items[PSuMp].Type = ItemType.PotionSubMp;
        items[PSuSp].Type = ItemType.PotionSubSp;
        return items;
    }

    // ── FindOpenInvSlot ────────────────────────────────────────────────────────

    [Test]
    public void FindOpenInvSlot_InvalidItemNum_ReturnsZero()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.FindOpenInvSlot(p, items, 0), Is.EqualTo(0));
            Assert.That(ItemSystem.FindOpenInvSlot(p, items, RecordLimits.Default.Items + 1), Is.EqualTo(0));
        });
    }

    [Test]
    public void FindOpenInvSlot_Gear_TakesFirstEmptySlot()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        p.Inv[1].Num = Wep;
        p.Inv[2].Num = Arm;  // 1 and 2 occupied
        Assert.That(ItemSystem.FindOpenInvSlot(p, items, Hlm), Is.EqualTo(3));
    }

    [Test]
    public void FindOpenInvSlot_GearFullBag_ReturnsZero()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        for (int i = 1; i <= Constants.MaxInv; i++) p.Inv[i].Num = Wep;
        Assert.That(ItemSystem.FindOpenInvSlot(p, items, Hlm), Is.EqualTo(0));
    }

    // Currency prefers an existing stack over an empty slot, so gold never fragments across slots.
    [Test]
    public void FindOpenInvSlot_Currency_StacksOntoExistingSlot()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        p.Inv[4].Num = Gold;   // an existing gold stack sits at slot 4 (1-3 empty)
        Assert.That(ItemSystem.FindOpenInvSlot(p, items, Gold), Is.EqualTo(4), "stacks onto the existing pile, not slot 1");
    }

    [Test]
    public void FindOpenInvSlot_Currency_NoStack_TakesFirstEmpty()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        Assert.That(ItemSystem.FindOpenInvSlot(p, items, Gold), Is.EqualTo(1));
    }

    // Even a completely full bag can still receive currency if a matching stack exists.
    [Test]
    public void FindOpenInvSlot_CurrencyFullBag_StillStacks()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        for (int i = 1; i <= Constants.MaxInv; i++) p.Inv[i].Num = Wep;
        p.Inv[7].Num = Gold;
        Assert.That(ItemSystem.FindOpenInvSlot(p, items, Gold), Is.EqualTo(7));
    }

    // ── HasItem ────────────────────────────────────────────────────────────────

    [Test]
    public void HasItem_Currency_ReturnsStackValue()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        p.Inv[3].Num = Gold;
        p.Inv[3].Quantity = 250;
        Assert.That(ItemSystem.CountItem(p, items, Gold), Is.EqualTo(250));
    }

    [Test]
    public void HasItem_NonCurrency_ReturnsOne()
    {
        var p = new PlayerRecord();
        var items = BuildItems();
        p.Inv[3].Num = Wep;
        p.Inv[3].Quantity = 999;  // Value ignored for non-currency
        Assert.That(ItemSystem.CountItem(p, items, Wep), Is.EqualTo(1));
    }

    [Test]
    public void HasItem_Absent_ReturnsZero()
        => Assert.That(ItemSystem.CountItem(new PlayerRecord(), BuildItems(), Wep), Is.EqualTo(0));

    // ── SortKey (canonical ordering; internal static → reflection) ─────────────

    static readonly MethodInfo SortKeyMethod =
        typeof(ItemSystem).GetMethod("SortKey", BindingFlags.NonPublic | BindingFlags.Static)!;

    static (int Cat, int Sub, int Mag) SortKey(int itemNum, ItemRecord item, bool equipped)
        => ((int, int, int))SortKeyMethod.Invoke(null, new object[] { itemNum, item, equipped })!;

    [Test]
    public void SortKey_Gold_PinsAboveEverything()
    {
        var items = BuildItems();
        Assert.That(SortKey(Gold, items[Gold], equipped: false), Is.EqualTo((0, 0, 0)));
    }

    [Test]
    public void SortKey_OtherCurrency_RanksBelowGoldAboveGear()
    {
        var items = BuildItems();
        Assert.That(SortKey(Cur, items[Cur], equipped: false), Is.EqualTo((1, 0, 0)));
    }

    // Equipped gear is category 2 (leads the bag below currency), ordered Weapon/Armor/Helmet/Shield.
    [Test]
    public void SortKey_EquippedGear_Category2_InTypeOrder()
    {
        var items = BuildItems();
        Assert.Multiple(() =>
        {
            Assert.That(SortKey(Wep, items[Wep], equipped: true), Is.EqualTo((2, 0, 0)));
            Assert.That(SortKey(Arm, items[Arm], equipped: true), Is.EqualTo((2, 1, 0)));
            Assert.That(SortKey(Hlm, items[Hlm], equipped: true), Is.EqualTo((2, 2, 0)));
            Assert.That(SortKey(Shd, items[Shd], equipped: true), Is.EqualTo((2, 3, 0)));
        });
    }

    // Unequipped gear is category 3; magnitude carries the item's Power so the OrderByDescending
    // in the sort surfaces the strongest piece first.
    [Test]
    public void SortKey_UnequippedGear_Category3_CarriesPowerMagnitude()
    {
        var items = BuildItems();
        items[Wep].Power =50;
        items[Arm].Power =30;
        items[Hlm].Power =20;
        items[Shd].Power =10;
        Assert.Multiple(() =>
        {
            Assert.That(SortKey(Wep, items[Wep], equipped: false), Is.EqualTo((3, 0, 50)));
            Assert.That(SortKey(Arm, items[Arm], equipped: false), Is.EqualTo((3, 1, 30)));
            Assert.That(SortKey(Hlm, items[Hlm], equipped: false), Is.EqualTo((3, 2, 20)));
            Assert.That(SortKey(Shd, items[Shd], equipped: false), Is.EqualTo((3, 3, 10)));
        });
    }

    [Test]
    public void SortKey_KeysAndScrolls()
    {
        var items = BuildItems();
        Assert.Multiple(() =>
        {
            Assert.That(SortKey(Key, items[Key], equipped: false), Is.EqualTo((4, 0, 0)));
            Assert.That(SortKey(Spl, items[Spl], equipped: false), Is.EqualTo((5, 0, 0)));
        });
    }

    // Add potions (cat 6) sort above Sub potions (cat 7); each groups by vital HP/MP/SP and carries the
    // potion's VitalAmount as the magnitude so bigger potions rise within a group.
    [Test]
    public void SortKey_Potions_AddBeforeSub_ByVital_WithAmountMagnitude()
    {
        var items = BuildItems();
        items[PAdHp].VitalAmount =100;
        items[PAdMp].VitalAmount =80;
        items[PAdSp].VitalAmount =60;
        items[PSuHp].VitalAmount =40;
        items[PSuMp].VitalAmount =20;
        items[PSuSp].VitalAmount =10;
        Assert.Multiple(() =>
        {
            Assert.That(SortKey(PAdHp, items[PAdHp], false), Is.EqualTo((6, 0, 100)));
            Assert.That(SortKey(PAdMp, items[PAdMp], false), Is.EqualTo((6, 1, 80)));
            Assert.That(SortKey(PAdSp, items[PAdSp], false), Is.EqualTo((6, 2, 60)));
            Assert.That(SortKey(PSuHp, items[PSuHp], false), Is.EqualTo((7, 0, 40)));
            Assert.That(SortKey(PSuMp, items[PSuMp], false), Is.EqualTo((7, 1, 20)));
            Assert.That(SortKey(PSuSp, items[PSuSp], false), Is.EqualTo((7, 2, 10)));
        });
    }

    // The whole point of the key: category order is strictly increasing across the tiers, so a sort by
    // (Cat, Sub, -Mag) yields Gold < currency < equipped < unequipped gear < keys < scrolls < add < sub.
    [Test]
    public void SortKey_CategoriesAreStrictlyOrdered()
    {
        var items = BuildItems();
        int gold = SortKey(Gold, items[Gold], false).Cat;
        int cur = SortKey(Cur, items[Cur], false).Cat;
        int equip = SortKey(Wep, items[Wep], true).Cat;
        int gear = SortKey(Wep, items[Wep], false).Cat;
        int key = SortKey(Key, items[Key], false).Cat;
        int scroll = SortKey(Spl, items[Spl], false).Cat;
        int add = SortKey(PAdHp, items[PAdHp], false).Cat;
        int sub = SortKey(PSuHp, items[PSuHp], false).Cat;
        Assert.That(gold, Is.LessThan(cur));
        Assert.That(cur, Is.LessThan(equip));
        Assert.That(equip, Is.LessThan(gear));
        Assert.That(gear, Is.LessThan(key));
        Assert.That(key, Is.LessThan(scroll));
        Assert.That(scroll, Is.LessThan(add));
        Assert.That(add, Is.LessThan(sub));
    }
}
