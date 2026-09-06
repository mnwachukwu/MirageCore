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

/// <summary>Account bank deposit/withdraw/sort against a live banking Inn. Locks the invariants from the bank
/// design: depositing EQUIPPED gear is refused (must unequip first), currency STACKS onto an existing pile
/// (deposit and withdraw), a partial currency move splits the stack, banking away from a banking Inn is a
/// no-op, and SortBank uses the shared canonical ordering.</summary>
[TestFixture]
public class BankSystemTests
{
    const int Map = 1, ShopNum = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10, Potion = 16;

    static (GameWorld world, PlayerManager pm, BankSystem bank, PlayerRecord p, ServerPlayer sp) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var bank = new BankSystem(world, pm, dispatcher, items);

        // A banking Inn reachable via its keeper NPC (banking resolves through the active shop
        // now, not the map). OpenKeeperShop mirrors OpenNpcShop: keeper on the player's tile + observed + in range.
        world.Shops[ShopNum].ShopType = ShopType.Inn;
        world.Shops[ShopNum].AllowBanking = true;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        OpenKeeperShop(world, sp, Idx);
        return (world, pm, bank, p, sp);
    }

    const int KeeperNpc = 1, KeeperSlot = 1;
    static void OpenKeeperShop(GameWorld world, ServerPlayer sp, int idx)
    {
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var mn = world.MapNpcs[sp.Char.Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = sp.Char.X;
        mn.Y = sp.Char.Y;
        world.MapObservers[sp.Char.Map].Add(idx);
        sp.SetActiveShop(ShopNum, sp.Char.Map, KeeperSlot);
    }

    [Test]
    public void Deposit_UnequippedGear_MovesToBank_CarryingDurability()
    {
        var (world, _, bank, p, sp) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        p.Inv[3].Num = Sword;
        p.Inv[3].Dur = 55;

        bank.Deposit(Idx, invSlot: 3, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[3].Num, Is.EqualTo(0), "the inventory slot is cleared");
            Assert.That(sp.Bank[1].Num, Is.EqualTo(Sword), "gear lands in the first open bank slot");
            Assert.That(sp.Bank[1].Dur, Is.EqualTo(55), "worn durability is carried across");
        });
    }

    // The headline invariant: worn gear must be taken off before it can be banked.
    [Test]
    public void Deposit_EquippedGear_Refused()
    {
        var (world, _, bank, p, sp) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        p.Inv[3].Num = Sword;
        p.WeaponSlot = 3;  // equipped

        bank.Deposit(Idx, invSlot: 3, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[3].Num, Is.EqualTo(Sword), "equipped gear stays in the bag");
            Assert.That(p.WeaponSlot, Is.EqualTo(3), "and stays equipped");
            Assert.That(sp.Bank[1].Num, Is.EqualTo(0), "nothing was deposited");
        });
    }

    [Test]
    public void Deposit_Currency_StacksOntoExistingPile_PartialSplits()
    {
        var (world, _, bank, p, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        p.Inv[2].Num = Gold;
        p.Inv[2].Quantity = 1000;
        sp.Bank[5].Num = Gold;
        sp.Bank[5].Quantity = 200;  // an existing gold pile in the bank

        bank.Deposit(Idx, invSlot: 2, amount: 300);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Bank[5].Quantity, Is.EqualTo(500), "deposit stacks onto the existing pile");
            Assert.That(p.Inv[2].Num, Is.EqualTo(Gold), "a partial deposit keeps the inventory stack");
            Assert.That(p.Inv[2].Quantity, Is.EqualTo(700), "with the remainder");
        });
    }

    [Test]
    public void Withdraw_Currency_FullMove_ClearsBankSlot()
    {
        var (world, _, bank, p, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        sp.Bank[5].Num = Gold;
        sp.Bank[5].Quantity = 400;

        bank.Withdraw(Idx, bankSlot: 5, amount: 0);   // amount <= 0 => withdraw all

        Assert.Multiple(() =>
        {
            Assert.That(sp.Bank[5].Num, Is.EqualTo(0), "the bank slot is emptied");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(400), "the gold is now in the bag");
        });
    }

    [Test]
    public void Deposit_AwayFromBankingInn_NoOp()
    {
        var (world, _, bank, p, sp) = Setup();
        world.Shops[ShopNum].AllowBanking = false;   // the Inn no longer offers banking
        world.Items[Sword].Type = ItemType.Weapon;
        p.Inv[3].Num = Sword;

        bank.Deposit(Idx, invSlot: 3, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[3].Num, Is.EqualTo(Sword), "the item is untouched");
            Assert.That(sp.Bank[1].Num, Is.EqualTo(0));
        });
    }

    // Gold, then gear, then potion — the shared SortKey ordering, with empty slots falling to the tail.
    [Test]
    public void SortBank_AppliesCanonicalOrder()
    {
        var (world, _, bank, _, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Power = 5;
        world.Items[Potion].Type = ItemType.PotionAddHp;
        world.Items[Potion].VitalAmount = 50;

        sp.Bank[1].Num = Potion;                          // deliberately out of order
        sp.Bank[2].Num = Gold;
        sp.Bank[2].Quantity = 100;
        sp.Bank[3].Num = Sword;
        sp.Bank[3].Dur = 100;

        bank.SortBank(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Bank[1].Num, Is.EqualTo(Gold), "gold pins to the top");
            Assert.That(sp.Bank[2].Num, Is.EqualTo(Sword), "gear next");
            Assert.That(sp.Bank[3].Num, Is.EqualTo(Potion), "consumables last");
            Assert.That(sp.Bank[4].Num, Is.EqualTo(0), "empty slots fall to the tail");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // No-op packet dispatcher (per-file convention; the bank paths only fan out to it, never read from it).
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
