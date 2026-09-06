using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A territory war clears the map of PvE, but a guard is not PvE — it is the map's standing defence, and it
/// holds its post for the whole war state.
///
/// <para>🔴 The despawn and the respawn read ONE rule, <see cref="GameWorld.IsContestSuppressedNpc"/>. Split
/// them and the bug is invisible at war start and arrives later: guards survive the opening despawn, then the
/// first one to die never comes back, so the map quietly empties of guards over a twenty-minute contest. Each
/// half is asserted separately below for exactly that reason.</para>
/// </summary>
[TestFixture]
public class ContestSparesGuardsTests
{
    private const int Map = 1, Territory = 3, DefendingGuild = 9;
    private const int GuardNum = 4, BeastNum = 5;
    private const int GuardPost = 1, BeastPost = 2;

    /// <summary>A map holding one guard and one ordinary hostile, both spawned, with a contest live over it.</summary>
    private static (GameWorld world, SpawnSystem spawn) Warring()
    {
        var world = new GameWorld();
        var spawn = new SpawnSystem(world, new PlayerManager(), new SilentDispatcher());

        world.Npcs[GuardNum].Behavior = NpcBehavior.Guard;
        world.Npcs[GuardNum].Name = "Watchman";
        world.Npcs[BeastNum].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[BeastNum].Name = "Wolf";

        var map = world.Maps[Map];
        map.Npcs = [new MapNpcEntry(GuardNum, null, null), new MapNpcEntry(BeastNum, null, null)];
        world.MapNpcs[Map, GuardPost].Num = GuardNum;
        world.MapNpcs[Map, BeastPost].Num = BeastNum;

        world.ContestZones.Add(new ContestZone
        {
            TerritoryIndex = Territory,
            Name = "Ashfall",
            Participants = [DefendingGuild],
            Maps = [Map],
        });
        return (world, spawn);
    }

    [Test]
    public void TheWarStartDespawnLeavesGuardsStanding()
    {
        var (world, spawn) = Warring();

        spawn.DespawnMapNpcs(Map, keepGuards: true);

        Assert.Multiple(() =>
        {
            Assert.That(world.MapNpcs[Map, GuardPost].Num, Is.EqualTo(GuardNum),
                "a guard holds its post through the war");
            Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.Zero,
                "everything contestable still clears out");
        });
    }

    /// <summary>The half that only breaks later: a guard killed mid-contest has to come back.</summary>
    [Test]
    public void AGuardKilledMidContestRespawns()
    {
        var (world, spawn) = Warring();
        world.MapNpcs[Map, GuardPost].Num = 0;   // killed during the contest

        spawn.SpawnNpc(GuardPost, Map);

        Assert.That(world.MapNpcs[Map, GuardPost].Num, Is.EqualTo(GuardNum));
    }

    [Test]
    public void EverythingElseStaysSuppressed()
    {
        var (world, spawn) = Warring();
        world.MapNpcs[Map, BeastPost].Num = 0;

        spawn.SpawnNpc(BeastPost, Map);

        Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.Zero,
            "no PvE returns while the contest runs");
    }

    /// <summary>The control half: with no contest running, nothing is exempt from a plain despawn.</summary>
    [Test]
    public void OutsideAWarThePlainDespawnTakesGuardsToo()
    {
        var (world, spawn) = Warring();
        world.ContestZones.Clear();

        spawn.DespawnMapNpcs(Map, keepGuards: false);

        Assert.Multiple(() =>
        {
            Assert.That(world.MapNpcs[Map, GuardPost].Num, Is.Zero);
            Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.Zero);
        });
    }
}
