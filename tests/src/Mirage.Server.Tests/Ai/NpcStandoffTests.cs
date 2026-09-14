using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// A body that keeps its distance.
///
/// <para>🔴 <b>The third answer, and the one neither of the other two gives.</b> A pursuer walks into
/// arm's reach and a fleeing body runs until it has forgotten you. Anything that wants a gap and MEANS
/// TO KEEP IT — an archer, a caster, a heckler, a bodyguard holding a perimeter — had neither, so a
/// world could author one and watch it do the opposite of what it was for.</para>
///
/// <para>What is pinned here is the whole band: it closes when it is too far, gives ground when
/// something walks into it, holds when it is where it meant to be, and tells the game once it has
/// arrived — which is the same event a body that closed all the way in raises, on the same terms.</para>
/// </summary>
[TestFixture]
public class NpcStandoffTests
{
    private const int Map = 1, Slot = 1, Kind = 1, Index = 1;

    private sealed class Watcher : IWorldObserver
    {
        public string Name => "watcher";

        public List<EntityHandle> Contacts { get; } = [];

        public void OnContact(EntityHandle npc, EntityHandle target) => Contacts.Add(target);
    }

    private sealed class Module(IWorldObserver observer) : ICoreModule
    {
        public string Name => "Test";

        public void Configure(ICoreBuilder builder) => builder.AddObserver(observer);
    }

    private sealed class Harness
    {
        public required GameWorld World { get; init; }
        public required NpcAiSystem Ai { get; init; }
        public required MapNpcRecord Body { get; init; }
        public required PlayerRecord Target { get; init; }
        public required Watcher Game { get; init; }
        public required ServerWorld Seam { get; init; }
    }

    /// <summary>One open map, one creature holding one player. A fresh <see cref="GameWorld"/> gives a
    /// walkable 16x12 with no neighbors, so an edge is a hard wall.</summary>
    private static Harness Build(NpcBehavior behavior, int range, int standoff, int npcX, int playerX)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var spawn = new SpawnSystem(world, pm, dispatcher, items: null!);
        var game = new Watcher();
        var events = new WorldEvents(CoreRegistry.Build([new Module(game)]));
        var ai = new NpcAiSystem(world, pm, dispatcher, movement, spawn, items: null!, events: events);

        var npc = world.Npcs[Kind];
        npc.Name = "Archer";
        npc.Behavior = behavior;
        npc.Range = range;
        npc.Standoff = standoff;

        var body = world.MapNpcs[Map, Slot];
        body.Num = Kind;
        body.X = npcX;
        body.Y = 6;
        body.Target = Index;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Matt";
        sp.Char.Map = Map;
        sp.Char.X = playerX;
        sp.Char.Y = 6;
        world.MapObservers[Map].Add(Index);

        var seam = new ServerWorld(world, pm, new AttributeSystem(world, pm, dispatcher),
                                   deaths: null!, movement: movement, items: null!, joinLeave: null!,
                                   decals: null!, ai: ai, guilds: null!, dispatcher: dispatcher);

        return new Harness
        {
            World = world,
            Ai = ai,
            Body = body,
            Target = sp.Char,
            Game = game,
            Seam = seam,
        };
    }

    /// <summary>One legs pass, with the step-clock already due.</summary>
    private static void Step(Harness h, long now = 1_000_000)
    {
        h.Body.NextMoveMs = 0;
        h.Ai.RunMovement(now);
    }

    // ── What the record says ──────────────────────────────────────────────────

    /// <summary>A standoff the record names is the standoff, as long as it is a gap it can see across.
    /// Nothing named falls back to half its reach, which is what a world authored before the field
    /// existed carries.</summary>
    [TestCase(8, 4, ExpectedResult = 4)]
    [TestCase(8, 0, ExpectedResult = 4, Description = "half of what it can see")]
    [TestCase(8, 1, ExpectedResult = Constants.MinStandoffTiles, Description = "one tile is melee reach")]
    [TestCase(8, 99, ExpectedResult = 8, Description = "never farther than it can notice")]
    [TestCase(1, 0, ExpectedResult = Constants.MinStandoffTiles, Description = "the floor wins over a tiny reach")]
    public int TheStandoffIsReadOffTheRecord(int range, int standoff) =>
        new NpcRecord { Range = range, Standoff = standoff }.EffectiveStandoff;

    /// <summary>And it is answered to a game only for the behavior that keeps one, because no other
    /// reads it.</summary>
    [Test]
    public void OnlyABodyThatKeepsADistanceReportsOne()
    {
        var shadow = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 2, playerX: 10);
        var pursuer = Build(NpcBehavior.Pursue, range: 8, standoff: 4, npcX: 2, playerX: 10);

        Assert.Multiple(() =>
        {
            Assert.That(shadow.Seam.StandoffOf(EntityHandle.ForNpc(Map, Slot)), Is.EqualTo(4));
            Assert.That(pursuer.Seam.StandoffOf(EntityHandle.ForNpc(Map, Slot)), Is.Zero);
        });
    }

    // ── The band ──────────────────────────────────────────────────────────────

    [Test]
    public void TooFar_ItCloses()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 2, playerX: 12);

        Step(h);

        Assert.That(h.Body.X, Is.EqualTo(3), "a step toward, exactly as a pursuer would take");
    }

    [Test]
    public void TooClose_ItGivesGround()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 10, playerX: 12);

        Step(h);

        Assert.That(h.Body.X, Is.EqualTo(9), "a step back, away from what walked into it");
    }

    /// <summary>Standing where it meant to stand, it stands there. The neutral band either side is what
    /// keeps it from stepping on every beat its target does.</summary>
    [TestCase(4, Description = "dead on")]
    [TestCase(5, Description = "one inside the band")]
    [TestCase(3, Description = "one outside the band")]
    public void AtItsStandoff_ItHolds(int gap)
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 12 - gap, playerX: 12);
        int wasX = h.Body.X;

        Step(h);

        Assert.That(h.Body.X, Is.EqualTo(wasX));
    }

    /// <summary>And it faces what it is minding while it holds, because a body standing still looking
    /// the wrong way reads as one that has lost interest.</summary>
    [Test]
    public void WhileItHolds_ItFacesWhatItIsMinding()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);
        h.Body.Dir = Direction.Left;

        Step(h);

        Assert.That(h.Body.Dir, Is.EqualTo(Direction.Right));
    }

    /// <summary>A pursuer walks all the way in, which is the whole difference between the two.</summary>
    [Test]
    public void APursuerIsUnaffected()
    {
        var h = Build(NpcBehavior.Pursue, range: 8, standoff: 4, npcX: 8, playerX: 12);

        Step(h);

        Assert.That(h.Body.X, Is.EqualTo(9), "it closes through the gap a shadow would have kept");
    }

    // ── What the game is told ─────────────────────────────────────────────────

    /// <summary>Reaching the distance it wanted IS reaching its target, so it raises the same event a
    /// body that closed all the way in raises — and raises it once, not on every beat it holds.</summary>
    [Test]
    public void ArrivingAtTheDistanceItWantedRaisesContactOnce()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);

        Step(h);
        Step(h, now: 2_000_000);
        Step(h, now: 3_000_000);

        Assert.That(h.Game.Contacts, Is.EqualTo(new[] { EntityHandle.ForPlayer(Index) }));
    }

    /// <summary>Nothing is raised while it is still on its way, so a game acting on contact is acting on
    /// a body that has actually arrived.</summary>
    [Test]
    public void ClosingRaisesNothing()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 2, playerX: 12);

        Step(h);

        Assert.That(h.Game.Contacts, Is.Empty);
    }

    /// <summary>Holding station keeps the give-up clock alive. Without that a body doing exactly what it
    /// was authored to do would time out and walk home.</summary>
    [Test]
    public void HoldingStationCountsAsReachingIt()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);

        Step(h, now: 5_000_000);

        Assert.That(h.Body.LastReachedTargetMs, Is.EqualTo(5_000_000));
    }

    // ── Who it is minding ─────────────────────────────────────────────────────

    /// <summary>🔴 Whether a body is after somebody was askable and WHO was not — and a bolt has to be
    /// aimed at something.</summary>
    [Test]
    public void AGameCanAskWhatACreatureIsMinding()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);

        Assert.That(h.Seam.TargetOf(EntityHandle.ForNpc(Map, Slot)), Is.EqualTo(EntityHandle.ForPlayer(Index)));
    }

    [Test]
    public void ABodyMindingNobodyAnswersWithNobody()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);
        h.Body.Target = 0;

        Assert.That(h.Seam.TargetOf(EntityHandle.ForNpc(Map, Slot)).IsSet, Is.False);
    }

    /// <summary>And a creature it noticed comes back as a creature, so a rule can tell the two apart the
    /// way the two contact handlers do.</summary>
    [Test]
    public void ACreatureItNoticedComesBackAsACreature()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 8, playerX: 12);
        h.Body.Target = 0;
        h.Body.NpcTargetSpawnMap = Map;
        h.Body.NpcTargetSpawnSlot = 2;

        var target = h.Seam.TargetOf(EntityHandle.ForNpc(Map, Slot));

        Assert.Multiple(() =>
        {
            Assert.That(target.IsNpc, Is.True);
            Assert.That(target, Is.EqualTo(EntityHandle.ForNpc(Map, 2)));
        });
    }

    /// <summary>A body backed against a wall by something walking into it holds where it is and faces
    /// what is coming, which is what a cornered animal does.</summary>
    [Test]
    public void Cornered_ItStandsAndFaces()
    {
        var h = Build(NpcBehavior.Shadow, range: 8, standoff: 4, npcX: 0, playerX: 1);
        h.Body.Y = 0;
        h.Target.Y = 0;
        h.Body.Dir = Direction.Left;

        // Wall it in on the two tiles a retreat could reach from the top-left corner.
        h.World.Maps[Map].Tile[0, 1] = new TileRecord { Type = TileType.Blocked };
        h.World.Maps[Map].Tile[1, 1] = new TileRecord { Type = TileType.Blocked };

        Step(h);

        Assert.Multiple(() =>
        {
            Assert.That((h.Body.X, h.Body.Y), Is.EqualTo((0, 0)), "nowhere to give ground to");
            Assert.That(h.Body.Dir, Is.EqualTo(Direction.Right), "so it faces what walked in");
            Assert.That(h.Game.Contacts, Is.Not.Empty, "and the game hears about it");
        });
    }

    private sealed class NoOpDispatcher : IPacketDispatcher
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
