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
/// A contest clears its maps of NPCs and keeps them clear for the whole war state.
///
/// <para>🔴 The despawn and the respawn read ONE rule, <see cref="GameWorld.IsContestSuppressedMap"/>.
/// Split them and the bug is invisible at war start and arrives later: something survives the opening
/// despawn, then the first one to die never comes back, so the map quietly empties over a twenty-minute
/// contest. Each half is asserted separately below for exactly that reason.</para>
/// </summary>
[TestFixture]
public class ContestSuppressionTests
{
    private const int Map = 1, Territory = 3, DefendingGuild = 9;
    private const int WardenNum = 4, BeastNum = 5;
    private const int WardenPost = 1, BeastPost = 2;

    /// <summary>A map holding two spawned NPCs with a contest live over it.</summary>
    private static (GameWorld world, SpawnSystem spawn) Warring()
    {
        var world = new GameWorld();
        var spawn = new SpawnSystem(world, new PlayerManager(), new SilentDispatcher());

        world.Npcs[WardenNum].Behavior = NpcBehavior.Pursue;
        world.Npcs[WardenNum].Name = "Watchman";
        world.Npcs[BeastNum].Behavior = NpcBehavior.Pursue;
        world.Npcs[BeastNum].Name = "Wolf";

        var map = world.Maps[Map];
        map.Npcs = [new MapNpcEntry(WardenNum, null, null), new MapNpcEntry(BeastNum, null, null)];
        world.MapNpcs[Map, WardenPost].Num = WardenNum;
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
    public void TheWarStartDespawnClearsTheWholeMap()
    {
        var (world, spawn) = Warring();

        spawn.DespawnMapNpcs(Map);

        Assert.Multiple(() =>
        {
            Assert.That(world.MapNpcs[Map, WardenPost].Num, Is.Zero);
            Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.Zero);
        });
    }

    /// <summary>The half that only breaks later: nothing comes back mid-contest.</summary>
    [Test]
    public void NothingRespawnsMidContest()
    {
        var (world, spawn) = Warring();
        world.MapNpcs[Map, WardenPost].Num = 0;
        world.MapNpcs[Map, BeastPost].Num = 0;

        spawn.SpawnNpc(WardenPost, Map);
        spawn.SpawnNpc(BeastPost, Map);

        Assert.Multiple(() =>
        {
            Assert.That(world.MapNpcs[Map, WardenPost].Num, Is.Zero);
            Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.Zero);
        });
    }

    /// <summary>The control half: with no contest running, a despawned NPC comes straight back.</summary>
    [Test]
    public void OutsideAWarASpawnGoesThrough()
    {
        var (world, spawn) = Warring();
        world.ContestZones.Clear();
        world.MapNpcs[Map, BeastPost].Num = 0;

        spawn.SpawnNpc(BeastPost, Map);

        Assert.That(world.MapNpcs[Map, BeastPost].Num, Is.EqualTo(BeastNum));
    }
}
