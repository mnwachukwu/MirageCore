using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// Marks on the ground.
///
/// <para>🔴 <b>Core draws over bodies and had nothing for a place.</b> An overhead bar is a row over
/// somebody's head; this is its twin for a square, and it is what a game needs to draw a flag on a
/// capture point, a ring around somewhere dangerous, or a name over a doorway. None of those has a body
/// to hang on.</para>
///
/// <para>Two things fail silently without a test. A mark placed again under a name already used has to
/// REPLACE rather than stack, or a counting meter piles up a hundred marks that draw on top of each
/// other. And the ring a player is shown has to be the ring a rule scores, or the line on the screen
/// sits somewhere other than the line that counts and every complaint about it is about the wrong
/// thing.</para>
/// </summary>
[TestFixture]
public class WorldMarkerTests
{
    private const int Map = 1, Next = 2, Ann = 1, Bob = 2;

    private static (MarkerSystem Marks, GameWorld World, Recorder Sent) Build()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var sent = new Recorder();

        foreach (int m in (int[])[Map, Next])
        {
            world.Maps[m] = new MapRecord(16, 12);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 12; y++)
                    world.Maps[m].Tile[x, y] = new TileRecord { Type = TileType.Walkable };
        }

        foreach (int index in (int[])[Ann, Bob])
        {
            var sp = pm[index];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            sp.Char.Map = Map;
            world.MapObservers[Map].Add(index);
        }

        return (new MarkerSystem(world, pm, sent), world, sent);
    }

    private static WorldMarker A(string id, int map = Map, int x = 5, int y = 5, int radius = 0,
                                 params EntityHandle[] seenBy) =>
        new()
        {
            Id = id,
            At = new WorldPlace(map, x, y),
            Label = "North Gate",
            Rgb = 0xC03030,
            Radius = radius,
            SeenBy = seenBy,
        };

    private static List<WorldMarker> On(GameWorld world, int map) =>
        world.Markers.TryGetValue(map, out var here) ? here : [];

    // ── Putting one down ──────────────────────────────────────────────────────

    [Test]
    public void AMarkGoesWhereItIsPut()
    {
        var (marks, world, _) = Build();

        Assert.That(marks.Mark(A("gate")), Is.True);
        Assert.That(On(world, Map), Has.Count.EqualTo(1));
        Assert.That(On(world, Map)[0].Label, Is.EqualTo("North Gate"));
    }

    [Test]
    public void AMarkOnASquareThatIsNotThere_IsRefused()
    {
        var (marks, world, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(marks.Mark(A("gate", map: 999_999)), Is.False, "past this world's ceiling");
            Assert.That(marks.Mark(A("gate", x: 99)), Is.False, "past the edge of a map that is there");
            Assert.That(marks.Mark(A("gate", y: -1)), Is.False, "and off the other end of it");
            Assert.That(world.Markers, Is.Empty);
        });
    }

    /// <summary>🔴 Marking again under a name already used REPLACES. It is what makes a mark that moves,
    /// or whose meter is counting, one call — and without it a contest that redraws every five seconds
    /// would leave a pile of identical flags behind it.</summary>
    [Test]
    public void MarkingAgainUnderOneName_Replaces()
    {
        var (marks, world, _) = Build();

        marks.Mark(A("gate", x: 5, y: 5));
        marks.Mark(A("gate", x: 8, y: 9));

        Assert.That(On(world, Map), Has.Count.EqualTo(1));
        Assert.That((On(world, Map)[0].At.X, On(world, Map)[0].At.Y), Is.EqualTo((8, 9)));
    }

    /// <summary>⚠ A mark that crosses maps leaves one list and joins another, and the map it left is owed
    /// the news as much as the map it arrived on — otherwise it is drawn in both places at once.</summary>
    [Test]
    public void AMarkThatCrossesMaps_LeavesTheOneItWasOn()
    {
        var (marks, world, sent) = Build();

        marks.Mark(A("gate", map: Map));
        sent.Packets.Clear();
        marks.Mark(A("gate", map: Next));

        Assert.Multiple(() =>
        {
            Assert.That(world.Markers.ContainsKey(Map), Is.False, "nothing left on the map it came from");
            Assert.That(On(world, Next), Has.Count.EqualTo(1));
            Assert.That(sent.Maps(), Does.Contain(Map), "and the map it came from was told");
        });
    }

    [Test]
    public void OneIsTakenAwayByName()
    {
        var (marks, world, _) = Build();

        marks.Mark(A("gate"));

        Assert.Multiple(() =>
        {
            Assert.That(marks.Unmark("gate"), Is.True);
            Assert.That(world.Markers, Is.Empty, "and the map holds no empty list either");
            Assert.That(marks.Unmark("gate"), Is.False, "taking away what is not there is an ordinary no");
        });
    }

    // ── The ring ──────────────────────────────────────────────────────────────

    /// <summary>🔴 The ring is measured center to center and INCLUSIVE, so what this answers yes for is
    /// exactly the staircase of tiles the client outlines. A radius of two reaches two tiles straight out
    /// and one-and-one diagonally, and does not reach two-and-two.</summary>
    [TestCase(5, 5, ExpectedResult = true)]
    [TestCase(7, 5, ExpectedResult = true)]
    [TestCase(8, 5, ExpectedResult = false)]
    [TestCase(6, 6, ExpectedResult = true)]
    [TestCase(7, 7, ExpectedResult = false)]
    [TestCase(3, 5, ExpectedResult = true)]
    [TestCase(5, 3, ExpectedResult = true)]
    public bool TheRingIsTheStaircaseOfTilesInsideIt(int x, int y)
    {
        var (marks, _, _) = Build();
        marks.Mark(A("gate", x: 5, y: 5, radius: 2));

        return marks.Inside("gate", new WorldPlace(Map, x, y));
    }

    [Test]
    public void AMarkWithNoRing_IsInsideNothing()
    {
        var (marks, _, _) = Build();
        marks.Mark(A("gate", x: 5, y: 5));

        Assert.That(marks.Inside("gate", new WorldPlace(Map, 5, 5)), Is.False);
    }

    /// <summary>⚠ A ring is drawn on the ground it is on. The world scrolls contiguously, so a square one
    /// tile over a border is close and is still on another map.</summary>
    [Test]
    public void ARingReachesNoFurtherThanItsOwnMap()
    {
        var (marks, _, _) = Build();
        marks.Mark(A("gate", map: Map, x: 5, y: 5, radius: 4));

        Assert.That(marks.Inside("gate", new WorldPlace(Next, 5, 5)), Is.False);
    }

    [Test]
    public void AMarkThatIsNotThere_IsInsideNothing()
    {
        var (marks, _, _) = Build();

        Assert.That(marks.Inside("gate", new WorldPlace(Map, 5, 5)), Is.False);
    }

    // ── Who sees it ───────────────────────────────────────────────────────────

    [Test]
    public void WithNoAudience_EverybodyWatchingIsTold()
    {
        var (marks, _, sent) = Build();

        marks.Mark(A("gate"));

        Assert.Multiple(() =>
        {
            Assert.That(sent.For(Ann).Markers, Has.Count.EqualTo(1));
            Assert.That(sent.For(Bob).Markers, Has.Count.EqualTo(1));
        });
    }

    /// <summary>🔴 Two people standing on one square can be owed different lists. That is the whole point
    /// for anything a side holds privately, and it is why the list is built per client rather than
    /// broadcast.</summary>
    [Test]
    public void NamingBodies_MakesItPrivateToThem()
    {
        var (marks, _, sent) = Build();

        marks.Mark(A("gate", seenBy: EntityHandle.ForPlayer(Ann)));

        Assert.Multiple(() =>
        {
            Assert.That(sent.For(Ann).Markers, Has.Count.EqualTo(1));
            Assert.That(sent.For(Bob).Markers, Is.Empty, "and the other is told the square holds nothing");
        });
    }

    /// <summary>⚠ An empty list is sent, not skipped. It is the only way a client that was shown a mark
    /// learns it has been taken away.</summary>
    [Test]
    public void TakingOneAway_SendsAnEmptyList()
    {
        var (marks, _, sent) = Build();

        marks.Mark(A("gate"));
        sent.Packets.Clear();
        marks.Unmark("gate");

        Assert.That(sent.For(Ann).Markers, Is.Empty);
        Assert.That(sent.Packets, Is.Not.Empty, "and something really was sent");
    }

    // ── Catching somebody up ──────────────────────────────────────────────────

    /// <summary>Without this a joiner sees an unmarked map until something else changes, because a change
    /// is the only other thing that sends one.</summary>
    [Test]
    public void SomebodyWhoHasJustArrived_IsToldWhatIsThere()
    {
        var (marks, _, sent) = Build();

        marks.Mark(A("gate"));
        sent.Packets.Clear();
        marks.SendSnapshot(Bob, Map);

        Assert.That(sent.For(Bob).Markers, Has.Count.EqualTo(1));
    }

    [Test]
    public void AJoinerIsToldOnlyWhatIsTheirs()
    {
        var (marks, _, sent) = Build();

        marks.Mark(A("gate", seenBy: EntityHandle.ForPlayer(Ann)));
        sent.Packets.Clear();
        marks.SendSnapshot(Bob, Map);

        Assert.That(sent.Packets, Is.Empty, "nothing of theirs, so nothing to say");
    }

    // ── The double ────────────────────────────────────────────────────────────

    /// <summary>What went to whom. A mark can name who sees it, so which client a packet went to is half
    /// of what these tests are about.</summary>
    private sealed class Recorder : IPacketDispatcher
    {
        public List<(int Index, MarkerUpdatePacket Packet)> Packets { get; } = [];

        /// <summary>The last list this client was sent, or an empty one when they were sent none.</summary>
        public MarkerUpdatePacket For(int index)
        {
            for (int i = Packets.Count - 1; i >= 0; i--)
                if (Packets[i].Index == index) return Packets[i].Packet;

            return new MarkerUpdatePacket();
        }

        public IReadOnlyList<int> Maps() => [.. Packets.Select(p => p.Packet.MapNum).Distinct()];

        public void SendTo(int index, IPacket packet)
        {
            if (packet is MarkerUpdatePacket marks) Packets.Add((index, marks));
        }

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
