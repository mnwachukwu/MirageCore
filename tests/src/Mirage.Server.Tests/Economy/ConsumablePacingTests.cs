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
/// How often something may be used up.
///
/// <para>🔴 <b>Both halves, because the gate alone is nothing.</b> Core paces a consumable on its own
/// clock and cannot decide for itself whether one was spent — what using a draught DOES is a game's rule,
/// and a use the game refused must cost neither the item nor the beat. So the clock starts on the item
/// leaving the bag, which is the only evidence Core has. Without that half the gate compares against a
/// timer nothing ever sets, and a client may drink as fast as it can ask.</para>
///
/// <para>Equipment and a key are outside it on purpose: gear is refused in combat already, and opening a
/// door mid-fight is a legitimate move that must not cost the swing after it.</para>
/// </summary>
[TestFixture]
public class ConsumablePacingTests
{
    private const int Map = 1, Idx = 1, Draught = 10, Blade = 11;

    /// <summary>Stands in for the game: takes the item when told to, and not otherwise. Which is exactly
    /// the question Core is reading the answer to.</summary>
    private sealed class Drinker(bool spends) : IWorldObserver
    {
        public string Name => "drinker";

        public ItemSystem? Items { get; set; }

        public int Offered { get; private set; }

        public void OnItemUsed(EntityHandle who, int itemNum, int invSlot)
        {
            Offered++;
            if (spends) Items!.TakeItem(who.PlayerIndex, itemNum, 0);
        }
    }

    private sealed class Module(IWorldObserver observer) : ICoreModule
    {
        public string Name => "Test";

        public void Configure(ICoreBuilder builder) => builder.AddObserver(observer);
    }

    private static (ItemSystem Items, PlayerRecord P, Drinker Game) Setup(bool spends)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        world.Maps[Map] = new MapRecord(8, 8);
        world.Items[Draught].Name = "Draught";
        world.Items[Draught].Type = ItemType.Consumable;
        world.Items[Blade].Name = "Blade";
        world.Items[Blade].Type = ItemType.Equipment;
        world.Items[Blade].EquipSlot = "hand";
        world.EquipSlots = new EquipSlotSet([new EquipSlot { Key = "hand", Ordinal = 0 }]);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;

        var game = new Drinker(spends);
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!,
                                   events: new WorldEvents(CoreRegistry.Build([new Module(game)])));
        game.Items = items;

        return (items, sp.Char, game);
    }

    [Test]
    public void ADraughtTheGameSpent_StartsTheClock()
    {
        var (items, _, game) = Setup(spends: true);
        items.GiveItems(Idx, Draught, 3);

        items.UseItem(Idx, 1);
        items.UseItem(Idx, 2);

        Assert.That(game.Offered, Is.EqualTo(1),
            "the second ask fell inside the clock and never reached the game");
    }

    /// <summary>A use the game refused costs nothing, so the next one goes straight through. That is
    /// how a full health bar reads from Core's side.</summary>
    [Test]
    public void ADraughtTheGameRefused_CostsNothing()
    {
        var (items, _, game) = Setup(spends: false);
        items.GiveItems(Idx, Draught, 3);

        items.UseItem(Idx, 1);
        items.UseItem(Idx, 1);

        Assert.That(game.Offered, Is.EqualTo(2));
    }

    /// <summary>Wearing something answers to the combat rule rather than to this clock, so two in a row
    /// both land.</summary>
    [Test]
    public void GearIsOutsideTheClock()
    {
        var (items, p, game) = Setup(spends: false);
        items.GiveItem(Idx, Blade, 0);

        items.UseItem(Idx, 1);
        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(game.Offered, Is.EqualTo(2));
            Assert.That(p.EquippedIn("hand"), Is.EqualTo(0), "worn, then taken off again");
        });
    }

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
