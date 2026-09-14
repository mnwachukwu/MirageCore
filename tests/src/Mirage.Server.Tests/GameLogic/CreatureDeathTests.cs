using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A creature stops, and what it was carrying reaches the ground.
///
/// <para>🔴 <b>Both halves of this failed silently, and for a long time.</b> <c>IWorld.Kill</c> is
/// offered on a creature and documented as working, and every game written against it read a refusal as
/// "nothing was there" — so a world could be authored, played, and tested with nothing in it able to
/// die. And <see cref="NpcDrop"/> is authored in the editor, carried on the wire, and stored on the
/// record, with nothing anywhere rolling it. A test double that answers yes to any handle hides the
/// first; a drop table nobody reads hides the second.</para>
///
/// <para>So what is pinned here is the whole path: the body leaves, the slot starts counting, the table
/// rolls line by line, and a game gets its say over every line before the roll happens.</para>
/// </summary>
[TestFixture]
public class CreatureDeathTests
{
    private const int Map = 1, Slot = 1, Kind = 1, Coin = 2, Sword = 3, Index = 1;

    /// <summary>An open map, one creature standing on it, and two items to drop. The creature's table is
    /// the caller's to write, because it is the thing under test.</summary>
    private static (SpawnSystem Spawns, GameWorld World, ItemSystem Items, PlayerManager Pm) Build(
        IRandomSource? rng = null, params ILootPolicy[] loot)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);

        world.Maps[Map] = new MapRecord(16, 12);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 12; y++)
                world.Maps[Map].Tile[x, y] = new TileRecord { Type = TileType.Walkable };

        world.Items[Coin].Name = "Gold";
        world.Items[Coin].Type = ItemType.Currency;
        world.Items[Sword].Name = "Sword";

        var npc = world.Npcs[Kind];
        npc.Name = "Bandit";
        npc.SpawnSecs = 30;

        var mn = world.MapNpcs[Map, Slot];
        mn.Num = Kind;
        mn.X = 5;
        mn.Y = 5;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Matt";
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 6;
        world.MapObservers[Map].Add(Index);

        return (new SpawnSystem(world, pm, dispatcher, items, rng, loot), world, items, pm);
    }

    private static EntityHandle TheCreature => EntityHandle.ForNpc(Map, Slot);

    private static List<MapItemRecord> OnTheGround(GameWorld world) =>
        [.. world.MapItems[Map].Where(mi => mi.Num > 0)];

    // ── The body ──────────────────────────────────────────────────────────────

    [Test]
    public void ACreatureCanBeKilled()
    {
        var (spawns, world, _, _) = Build();

        bool died = spawns.KillNpc(TheCreature);

        Assert.Multiple(() =>
        {
            Assert.That(died, Is.True, "a game asking a creature to die is answered yes");
            Assert.That(world.MapNpcs[Map, Slot].Num, Is.Zero, "and the slot is empty afterwards");
        });
    }

    /// <summary>⚠ The one answer a game keys off: a verb aimed at an empty square must not pay out.</summary>
    [Test]
    public void KillingNothing_IsAnsweredNo()
    {
        var (spawns, world, _, _) = Build();
        world.MapNpcs[Map, Slot].Num = 0;

        Assert.That(spawns.KillNpc(TheCreature), Is.False);
    }

    [Test]
    public void TheSlotStartsCounting_SoTheCreatureComesBack()
    {
        var (spawns, world, _, _) = Build();

        spawns.KillNpc(TheCreature);

        Assert.That(world.MapNpcs[Map, Slot].SpawnWait, Is.GreaterThan(0),
            "the respawn clock is stamped, or the slot stays empty for good");
    }

    // ── The table ─────────────────────────────────────────────────────────────

    [Test]
    public void ACertainLine_LandsOnTheTileItFellOn()
    {
        var (spawns, world, _, _) = Build();
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        var ground = OnTheGround(world);

        Assert.Multiple(() =>
        {
            Assert.That(ground, Has.Count.EqualTo(1));
            Assert.That(ground[0].Num, Is.EqualTo(Sword));
            Assert.That((ground[0].X, ground[0].Y), Is.EqualTo((5, 5)), "where the body was, not where its slot is");
            Assert.That(ground[0].Source, Is.EqualTo(ItemSource.NpcDropped));
        });
    }

    [Test]
    public void ALineThatNeverLands_DropsNothing()
    {
        var (spawns, world, _, _) = Build();
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 0 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Is.Empty);
    }

    /// <summary>🔴 Every line rolls on its own, so one death yields nothing, one thing, or several. A
    /// table read as "pick one of these" would make the third line below unreachable whenever the first
    /// two landed, and nothing would say so.</summary>
    [Test]
    public void EveryLineRollsOnItsOwn()
    {
        var (spawns, world, _, _) = Build();
        world.Npcs[Kind].Drops =
        [
            new NpcDrop { ItemNum = Coin, Quantity = 10, Chance = 100 },
            new NpcDrop { ItemNum = Sword, Chance = 100 },
            new NpcDrop { ItemNum = Sword, Chance = 100 },
        ];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Has.Count.EqualTo(3), "three lines, three drops");
    }

    [Test]
    public void AStackingItemCarriesItsCount()
    {
        var (spawns, world, _, _) = Build();
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Coin, Quantity = 10, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world)[0].Quantity, Is.EqualTo(10));
    }

    /// <summary>⚠ An inert line is an ordinary state for a half-authored row, not an error. It is skipped
    /// rather than spawning item zero.</summary>
    [Test]
    public void AHalfAuthoredLine_IsPassedOver()
    {
        var (spawns, world, _, _) = Build();
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = 0, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Is.Empty);
    }

    [Test]
    public void ACreatureWithNoTable_DropsNothingAndStillDies()
    {
        var (spawns, world, _, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(spawns.KillNpc(TheCreature), Is.True);
            Assert.That(OnTheGround(world), Is.Empty);
        });
    }

    // ── The seam ──────────────────────────────────────────────────────────────

    /// <summary>🔴 With no game loaded the table lands exactly as it was authored. Every seam's "declare
    /// nothing" case has to be a coherent world rather than an empty one.</summary>
    [Test]
    public void WithNoPolicy_TheTableLandsAsAuthored()
    {
        var (spawns, world, _, _) = Build(new AlwaysRolls(50));
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 60 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Has.Count.EqualTo(1), "a roll of 50 is below a chance of 60");
    }

    [Test]
    public void APolicyCanLiftTheRate()
    {
        var lifts = new Weighing(spoil => spoil.ChancePercent *= 2);
        var (spawns, world, _, _) = Build(new AlwaysRolls(50), lifts);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 30 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Has.Count.EqualTo(1),
            "30 would have missed a roll of 50; doubled, it lands");
    }

    [Test]
    public void APolicyCanChangeHowMuchThereIs()
    {
        var doubles = new Weighing(spoil => spoil.Quantity *= 2);
        var (spawns, world, _, _) = Build(null, doubles);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Coin, Quantity = 10, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world)[0].Quantity, Is.EqualTo(20));
    }

    /// <summary>A chance of nothing takes the line off the table without rolling it — which is both how a
    /// rule says a creature owes this player nothing, and how a game takes the line over and does its own
    /// spawning.</summary>
    [Test]
    public void APolicyCanTakeALineOffTheTable()
    {
        var refuses = new Weighing(spoil => spoil.ChancePercent = 0);
        var (spawns, world, _, _) = Build(null, refuses);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world), Is.Empty);
    }

    [Test]
    public void APolicyCanHoldADropForSomebody()
    {
        var claims = new Weighing(spoil =>
        {
            spoil.ClaimedBy = EntityHandle.ForPlayer(Index);
            spoil.ClaimSeconds = 30;
        });
        var (spawns, world, _, _) = Build(null, claims);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world)[0].TaggedToPlayer, Is.EqualTo(Index));
    }

    /// <summary>What the policy is shown is the line as authored, plus who did it and what it was. The
    /// body still resolves while this runs, which is the only moment it does.</summary>
    [Test]
    public void APolicyIsShownTheKillAndTheLine()
    {
        Spoil? seen = null;
        var watches = new Weighing(spoil => seen = spoil);
        var (spawns, world, _, _) = Build(null, watches);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Sword, Quantity = 4, Chance = 25 }];

        spawns.KillNpc(TheCreature, EntityHandle.ForPlayer(Index));

        Assert.That(seen, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(seen!.ItemNum, Is.EqualTo(Sword));
            Assert.That(seen.Quantity, Is.EqualTo(4));
            Assert.That(seen.ChancePercent, Is.EqualTo(25));
            Assert.That(seen.Kind, Is.EqualTo(Kind), "what it was, which outlives the body");
            Assert.That(seen.Killer, Is.EqualTo(EntityHandle.ForPlayer(Index)));
            Assert.That(seen.Body, Is.EqualTo(TheCreature));
        });
    }

    /// <summary>Every policy sees every line, in the order their modules were configured, and each writes
    /// on what the one before it left.</summary>
    [Test]
    public void EveryPolicySeesEveryLine_InOrder()
    {
        var first = new Weighing(spoil => spoil.Quantity += 1);
        var second = new Weighing(spoil => spoil.Quantity *= 10);
        var (spawns, world, _, _) = Build(null, first, second);
        world.Npcs[Kind].Drops = [new NpcDrop { ItemNum = Coin, Quantity = 1, Chance = 100 }];

        spawns.KillNpc(TheCreature);

        Assert.That(OnTheGround(world)[0].Quantity, Is.EqualTo(20),
            "(1 + 1) x 10, not 1 + (1 x 10) — the second writes on what the first left");
    }

    // ── The doubles ───────────────────────────────────────────────────────────

    private sealed class Weighing(Action<Spoil> what) : ILootPolicy
    {
        public void Weigh(Spoil spoil) => what(spoil);
    }

    /// <summary>Every percent roll comes back the same, so a chance is either above it or below it and a
    /// test about the gate is not a test about luck.</summary>
    private sealed class AlwaysRolls(int percent) : IRandomSource
    {
        public int Next(int maxExclusive) => percent;
        public int Next(int minInclusive, int maxExclusive) => percent;
        public long NextInt64(long minInclusive, long maxExclusive) => percent;
        public double NextDouble() => percent / 100.0;
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
