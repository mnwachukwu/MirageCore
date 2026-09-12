using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// The stain seam: <c>DecalSystem.Deposit</c> is the whole entry point a game module has, so what it accepts,
/// what it merges, what it evicts, and what reaches the wire is the contract this turn published.
///
/// <para>Core itself never calls Deposit — an engine with no module loaded has ground that can be stained and
/// nothing that stains it — so these tests are the only exercise the system gets.</para>
/// </summary>
[TestFixture]
public class DecalSystemTests
{
    private const int Map = 1, Idx = 1;

    // ── What a deposit does ──────────────────────────────────────────────────────

    [Test]
    public void Deposit_PutsAStainDown()
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, 3, 4, size: 2, WorldLayer.Ground, amount: 1.0f);

        var field = world.MapDecals[Map];
        Assert.That(field.Decals, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That((field.Decals[0].X, field.Decals[0].Y), Is.EqualTo((3, 4)));
            Assert.That(field.Decals[0].Size, Is.EqualTo(2));
            Assert.That(field.Decals[0].Amount, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(field.Decals[0].Peak, Is.EqualTo(1.0f).Within(1e-4f), "a fresh stain's peak is what it holds");
        });
    }

    // Why this merges rather than stacks: something bleeding on one tile for ten seconds would otherwise pile up
    // a hundred rectangles that draw identically, and the wire would carry every one of them.
    [Test]
    public void Deposit_OntoTheSameRectangle_FeedsThatStainRatherThanAddingASecond()
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, 3, 4, size: 2, WorldLayer.Ground, amount: 0.5f);
        decals.Deposit(Map, 3, 4, size: 2, WorldLayer.Ground, amount: 0.5f);

        Assert.That(world.MapDecals[Map].Decals, Has.Count.EqualTo(1));
        Assert.That(world.MapDecals[Map].Decals[0].Amount, Is.EqualTo(1.0f).Within(1e-4f));
    }

    [Test]
    public void Deposit_OnTheOtherLayer_IsItsOwnStain()
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, 3, 4, size: 1, WorldLayer.Ground, amount: 0.5f);
        decals.Deposit(Map, 3, 4, size: 1, WorldLayer.Fringe, amount: 0.5f);

        Assert.That(world.MapDecals[Map].Decals, Has.Count.EqualTo(2),
                    "a bridge deck and the ground under it stain independently");
    }

    [Test]
    public void Deposit_IsCappedAtTheMaximumAmount()
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, 1, 1, size: 1, WorldLayer.Ground, amount: Constants.DecalMaxAmount * 10f);
        decals.Deposit(Map, 1, 1, size: 1, WorldLayer.Ground, amount: Constants.DecalMaxAmount * 10f);

        Assert.That(world.MapDecals[Map].Decals[0].Amount, Is.EqualTo(Constants.DecalMaxAmount).Within(1e-4f));
    }

    // ── What a deposit refuses ───────────────────────────────────────────────────

    [TestCase(-1, 5, TestName = "Deposit_OffTheWestEdge_IsIgnored")]
    [TestCase(5, -1, TestName = "Deposit_OffTheNorthEdge_IsIgnored")]
    [TestCase(9999, 5, TestName = "Deposit_OffTheEastEdge_IsIgnored")]
    [TestCase(5, 9999, TestName = "Deposit_OffTheSouthEdge_IsIgnored")]
    public void Deposit_OffTheMap_IsIgnored(int x, int y)
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, x, y, size: 1, WorldLayer.Ground, amount: 1f);

        Assert.That(world.MapDecals, Is.Empty, "a map nobody successfully stained carries no field at all");
    }

    [TestCase(0f, TestName = "Deposit_OfNothing_IsIgnored")]
    [TestCase(-1f, TestName = "Deposit_OfANegativeAmount_IsIgnored")]
    public void Deposit_WithoutAPositiveAmount_IsIgnored(float amount)
    {
        var (world, decals, _) = Setup();

        decals.Deposit(Map, 5, 5, size: 1, WorldLayer.Ground, amount);

        Assert.That(world.MapDecals, Is.Empty);
    }

    [Test]
    public void Deposit_OnAMapThatDoesNotExist_IsIgnored()
    {
        var (world, decals, _) = Setup();

        decals.Deposit(mapNum: 99999, 5, 5, size: 1, WorldLayer.Ground, amount: 1f);

        Assert.That(world.MapDecals, Is.Empty);
    }

    // A stain is clipped to the map rather than refused: a big body dying against the east wall should still
    // leave what fits, not nothing at all.
    [Test]
    public void Deposit_OverhangingTheEdge_IsClippedToWhatFits()
    {
        var (world, decals, _) = Setup();
        var map = world.Maps[Map];

        decals.Deposit(Map, map.Width - 2, map.Height - 2, size: 8, WorldLayer.Ground, amount: 1f);

        Assert.That(world.MapDecals[Map].Decals[0].Size, Is.EqualTo(2));
    }

    // ── The per-map cap ──────────────────────────────────────────────────────────

    // The faintest goes rather than the oldest: what a viewer misses least is what is nearly dry, and an
    // oldest-first rule would drop the big stain under a body that is still standing there.
    [Test]
    public void PastTheCap_TheFaintestStainIsEvicted()
    {
        var (world, decals, _) = Setup();
        int w = world.Maps[Map].Width;

        for (int i = 0; i < Constants.MaxMapDecals; i++)
            decals.Deposit(Map, i % w, i / w, size: 1, WorldLayer.Ground, amount: 2f);

        decals.Deposit(Map, 0, 0, size: 1, WorldLayer.Ground, amount: 2f);   // feeds, so still exactly at the cap
        world.MapDecals[Map].Decals[7].Amount = 0.05f;                       // one has nearly dried out

        decals.Deposit(Map, 0, Constants.MaxMapDecals / w + 1, size: 3, WorldLayer.Ground, amount: 2f);

        var field = world.MapDecals[Map];
        Assert.Multiple(() =>
        {
            Assert.That(field.Decals, Has.Count.EqualTo(Constants.MaxMapDecals), "the cap holds");
            Assert.That(field.Decals.Any(d => d.Amount < 0.1f), Is.False, "the faintest is what left");
            Assert.That(field.Decals.Any(d => d.Size == 3), Is.True, "the new stain is what arrived");
        });
    }

    // ── Drying, and what that costs the wire ─────────────────────────────────────

    // Decay alone never broadcasts: the client replays the same linear fade from the same constant, so a drying
    // map converges without a byte. A stain that dries OUT is a change the client cannot derive, so it is sent.
    [Test]
    public void Tick_DryingAlone_SaysNothingOnTheWire()
    {
        var (world, decals, dispatcher) = Setup();
        decals.Deposit(Map, 5, 5, size: 1, WorldLayer.Ground, amount: Constants.DecalMaxAmount);
        decals.Tick();              // the deposit's own dirty flag
        dispatcher.ToMap.Clear();

        decals.Tick();
        decals.Tick();

        Assert.That(dispatcher.ToMap, Is.Empty);
        Assert.That(world.MapDecals[Map].Decals[0].Amount, Is.LessThan(Constants.DecalMaxAmount));
    }

    [Test]
    public void Tick_AStainThatDriesOut_LeavesTheListAndIsBroadcast()
    {
        var (world, decals, dispatcher) = Setup();
        decals.Deposit(Map, 5, 5, size: 1, WorldLayer.Ground, amount: 0.025f);   // survives one tick, not two
        decals.Tick();
        dispatcher.ToMap.Clear();

        decals.Tick();

        Assert.That(world.MapDecals, Is.Empty, "a map with nothing left on it holds no state");
        Assert.That(dispatcher.ToMap.OfType<DecalUpdatePacket>().Single().Decals, Is.Empty,
                    "the client is told the map is clean; it cannot derive that from its own fade");
    }

    [Test]
    public void Tick_ADeposit_IsBroadcastToTheMap()
    {
        var (_, decals, dispatcher) = Setup();

        decals.Deposit(Map, 6, 7, size: 2, WorldLayer.Fringe, amount: 1f);
        decals.Tick();

        var sent = dispatcher.ToMap.OfType<DecalUpdatePacket>().Single();
        Assert.That(sent.MapNum, Is.EqualTo(Map));
        var e = sent.Decals.Single();
        Assert.Multiple(() =>
        {
            Assert.That((e.X, e.Y, e.Size), Is.EqualTo((6, 7, 2)));
            Assert.That(e.Layer, Is.EqualTo(WorldLayer.Fringe));
            Assert.That(DecalUpdatePacket.Dequantize(e.Amount), Is.EqualTo(1f).Within(0.02f));
        });
    }

    [Test]
    public void Tick_WithNobodyWatching_SendsNothing()
    {
        var (world, decals, dispatcher) = Setup();
        world.MapObservers[Map].Clear();

        decals.Deposit(Map, 5, 5, size: 1, WorldLayer.Ground, amount: 1f);
        decals.Tick();

        Assert.That(dispatcher.ToMap, Is.Empty);
    }

    // ── The join handshake ───────────────────────────────────────────────────────

    // Without this a joiner sees a stained map as clean until something spills on it again, because the tick
    // broadcasts only what CHANGED.
    [Test]
    public void SendSnapshot_GivesAJoinerTheStainsAlreadyThere()
    {
        var (_, decals, dispatcher) = Setup();
        decals.Deposit(Map, 2, 2, size: 1, WorldLayer.Ground, amount: 1f);
        decals.Tick();

        decals.SendSnapshot(Idx, Map);

        var sent = dispatcher.ToOne.OfType<DecalUpdatePacket>().Single();
        Assert.That(sent.MapNum, Is.EqualTo(Map));
        Assert.That(sent.Decals, Has.Count.EqualTo(1));
    }

    [Test]
    public void SendSnapshot_OfACleanMap_SendsNothing()
    {
        var (_, decals, dispatcher) = Setup();

        decals.SendSnapshot(Idx, Map);

        Assert.That(dispatcher.ToOne, Is.Empty, "an empty list is not worth a packet on every map crossing");
    }

    // ── The wire's own resolution ────────────────────────────────────────────────

    // Amounts travel as a byte, so a round trip is lossy by design — within half a step, which is invisible.
    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1.75f)]
    [TestCase(3.0f)]
    public void AnAmount_SurvivesTheWireWithinAStep(float amount)
    {
        float back = DecalUpdatePacket.Dequantize(DecalUpdatePacket.Quantize(amount));

        Assert.That(back, Is.EqualTo(amount).Within(Constants.DecalMaxAmount / 255f));
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    private static (GameWorld world, DecalSystem decals, CapturingDispatcher dispatcher) Setup()
    {
        var world = new GameWorld();
        var dispatcher = new CapturingDispatcher();
        world.MapObservers[Map].Add(Idx);
        return (world, new DecalSystem(world, dispatcher), dispatcher);
    }

    private sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> ToMap = new();
        public readonly List<IPacket> ToOne = new();

        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) => ToMap.Add(packet);
        public void SendTo(int index, IPacket packet) => ToOne.Add(packet);

        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
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
