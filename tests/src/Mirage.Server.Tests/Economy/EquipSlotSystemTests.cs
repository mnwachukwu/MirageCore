using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// Putting something on and taking it off, against the slots the loaded game declared.
///
/// <para>The whole of Core's rule: one item per slot, the item stays in the bag, and a piece naming a slot
/// this world does not have cannot be worn at all. What a worn piece then DOES belongs to a game.</para>
/// </summary>
[TestFixture]
public class EquipSlotSystemTests
{
    const int Map = 1, Idx = 1;
    const int Blade = 10, Spare = 11, Cap = 12, Ration = 13;

    static (GameWorld world, ItemSystem items, PlayerRecord p) Setup()
    {
        var world = new GameWorld
        {
            EquipSlots = new EquipSlotSet(
            [
                new EquipSlot { Key = "head", Ordinal = 0 },
                new EquipSlot { Key = "hand", Ordinal = 1 },
            ]),
        };
        var pm = new PlayerManager();
        var items = new ItemSystem(world, pm, new NoOpDispatcher(), persistence: null!, bg: null!);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;

        Equip(world, Blade, "hand");
        Equip(world, Spare, "hand");
        Equip(world, Cap, "head");
        world.Items[Ration].Type = ItemType.Consumable;

        return (world, items, sp.Char);
    }

    static void Equip(GameWorld world, int num, string slot)
    {
        world.Items[num].Type = ItemType.Equipment;
        world.Items[num].EquipSlot = slot;
        world.Items[num].Durability = 40;
    }

    [Test]
    public void UsingAPiece_WearsItWithoutMovingItOutOfTheBag()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);

        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.EquippedIn("hand"), Is.EqualTo(1));
            Assert.That(p.Inv[1].Num, Is.EqualTo(Blade), "the item itself never leaves the bag slot");
        });
    }

    [Test]
    public void UsingAWornPiece_TakesItOff()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);
        items.UseItem(Idx, 1);

        items.UseItem(Idx, 1);

        Assert.That(p.Equipped, Is.Empty);
    }

    // One item per slot is the only rule Core has about wearing, so the piece already there comes off.
    [Test]
    public void WearingASecondPieceForOneSlot_ReplacesTheFirst()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);
        items.GiveItem(Idx, Spare, 0);
        items.UseItem(Idx, 1);

        items.UseItem(Idx, 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.EquippedIn("hand"), Is.EqualTo(2));
            Assert.That(p.IsEquipped(1), Is.False, "and the first one is simply carried again");
        });
    }

    [Test]
    public void DifferentSlots_AreWornAtTheSameTime()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);
        items.GiveItem(Idx, Cap, 0);

        items.UseItem(Idx, 1);
        items.UseItem(Idx, 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.EquippedIn("hand"), Is.EqualTo(1));
            Assert.That(p.EquippedIn("head"), Is.EqualTo(2));
        });
    }

    /// <summary>A world authored against another game's slots opens and plays; the pieces it cannot place
    /// are carried. Refusing the wear is the honest answer, and it is the only one that does not invent a
    /// slot the game never declared.</summary>
    [Test]
    public void APieceNamingAnUndeclaredSlot_CannotBeWorn()
    {
        var (world, items, p) = Setup();
        world.Items[Blade].EquipSlot = "saddle";
        items.GiveItem(Idx, Blade, 0);

        items.UseItem(Idx, 1);

        Assert.That(p.Equipped, Is.Empty);
    }

    [Test]
    public void AGameThatDeclaresNoSlots_WearsNothing()
    {
        var (world, items, p) = Setup();
        world.EquipSlots = EquipSlotSet.Empty;
        items.GiveItem(Idx, Blade, 0);

        items.UseItem(Idx, 1);

        Assert.That(p.Equipped, Is.Empty);
    }

    // ── What leaving the bag has to clear ─────────────────────────────────────

    [Test]
    public void UnequipSlot_TakesOffAWornPieceAndLeavesItInTheBag()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);
        items.UseItem(Idx, 1);

        items.UnequipSlot(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Equipped, Is.Empty);
            Assert.That(p.Inv[1].Num, Is.EqualTo(Blade));
        });
    }

    [Test]
    public void TakingTheItemOutOfTheBag_LeavesNoWornSlotPointingAtIt()
    {
        var (world, items, p) = Setup();
        items.GiveItem(Idx, Blade, 0);
        items.UseItem(Idx, 1);

        ItemSystem.TakeFromInventory(p, world.Items, 1, amount: 0);

        Assert.That(p.Equipped, Is.Empty, "a worn slot naming an emptied bag slot would never come off");
    }

    // ── The bag tidy ──────────────────────────────────────────────────────────

    // The tidy moves the slot objects, so a worn index that was not re-pointed would name whatever
    // landed there instead.
    [Test]
    public void SortingTheBag_KeepsWornPiecesWorn()
    {
        var (_, items, p) = Setup();
        items.GiveItem(Idx, Ration, 0);
        items.GiveItem(Idx, Blade, 0);
        items.UseItem(Idx, 2);

        items.SortInventory(Idx);

        Assert.That(p.Inv[p.EquippedIn("hand")].Num, Is.EqualTo(Blade));
    }

    // ── Harness ────────────────────────────────────────────────────────

    // No-op packet dispatcher (per-file convention). Item ops only fan out to it.
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
