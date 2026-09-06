using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.Platform;
using Mirage.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A capture zone is a circle in the world, not a circle clipped to one map.
///
/// <para>🔴 A point placed near a map edge spills its radius onto the neighbor. Testing
/// <c>player.Map == point.Map</c> cuts the zone off at a border the player cannot see and hands anyone who
/// stepped over it a safe tile INSIDE the ring they are standing in — visibly holding the point, scoring
/// nothing. The reach is measured in world coordinates across the 3x3 instead.</para>
/// </summary>
[TestFixture]
public class CaptureRadiusCrossesSeamsTests
{
    private const int West = 1, East = 2, Guild = 7, OtherGuild = 8;
    private static readonly int R = Constants.TerritoryCapturePointRadius;

    /// <summary>Two maps side by side, seam-linked. The point sits on the WEST map's right edge, so its
    /// radius reaches across onto the east map.</summary>
    private static (GuildTerritorySystem Sys, PlayerManager Pm, ContestPoint Point) Border()
    {
        var world = new GameWorld();
        foreach (int m in new[] { West, East })
        {
            var map = world.Maps[m];
            for (int x = 0; x <= Constants.MaxMapX; x++)
                for (int y = 0; y <= Constants.MaxMapY; y++)
                    map.EditTile(x, y, t => t with { Type = TileType.Walkable });
        }
        world.Maps[West].Right = East;
        world.Maps[East].Left = West;

        var sys = new GuildTerritorySystem(world, new PlayerManager(), new SilentDispatcher(),
            guilds: null!, spawn: null!, persistence: null!, bg: null!,
            NullLogger<GuildTerritorySystem>.Instance);

        var pm = (PlayerManager)typeof(GuildTerritorySystem)
            .GetField("_pm", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sys)!;

        var point = new ContestPoint
        {
            Label = "Alpha", Map = West, X = Constants.MaxMapX, Y = 5, Layer = WorldLayer.Ground,
        };
        return (sys, pm, point);
    }

    /// <summary>Stands one LIVING player at (map, x, y) in the participating guild. The HP matters: a player
    /// at 0 is a corpse, and a corpse holds nothing.</summary>
    private static void Stand(PlayerManager pm, int slot, int map, int x, int y)
    {
        var sp = pm[slot];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Guild = Guild;
        sp.Char.Map = map;
        sp.Char.X = x;
        sp.Char.Y = y;
        sp.Char.Layer = WorldLayer.Ground;
        sp.Char.MaxHp = 100;
        sp.Char.Hp = 100;
    }

    private static int Majority(GuildTerritorySystem sys, ContestPoint pt) =>
        (int)typeof(GuildTerritorySystem)
            .GetMethod("MajorityGuildInRadius", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sys, new object?[] { pt, new HashSet<int> { Guild } })!;

    [Test]
    public void APlayerJustOverTheSeamHoldsThePoint()
    {
        var (sys, pm, pt) = Border();
        Stand(pm, 1, East, 0, 5);   // one tile past the point, on the far side of the border

        Assert.That(Majority(sys, pt), Is.EqualTo(Guild),
            "the tile is inside the ring the player can see — it has to score");
    }

    /// <summary>The far edge of the spill still counts, right up to the radius.</summary>
    [Test]
    public void TheSpillCountsForItsWholeReach()
    {
        var (sys, pm, pt) = Border();
        Stand(pm, 1, East, R - 1, 5);   // R tiles from the point, measured through the seam

        Assert.That(Majority(sys, pt), Is.EqualTo(Guild));
    }

    /// <summary>The control half: crossing the seam does not make the radius infinite.</summary>
    [Test]
    public void PastTheRadiusStillCountsForNothing()
    {
        var (sys, pm, pt) = Border();
        Stand(pm, 1, East, R, 5);   // R + 1 tiles from the point

        Assert.That(Majority(sys, pt), Is.Zero);
    }

    /// <summary>Layer is unchanged by any of this: a player under a bridge-top point still holds nothing.</summary>
    [Test]
    public void TheOtherLayerAcrossTheSeamHoldsNothing()
    {
        var (sys, pm, pt) = Border();
        Stand(pm, 1, East, 0, 5);
        pm[1].Char.Layer = WorldLayer.Fringe;

        Assert.That(Majority(sys, pt), Is.Zero);
    }

    /// <summary>
    /// 🔴 A corpse holds nothing.
    ///
    /// <para>A dead player stays connected and lies where they fell until they respawn, so every gate this
    /// count applies — guild, layer, radius — passes for a body. And the meter turns on a strict PLURALITY,
    /// so counting the dead does not merely inflate a number: a team that was wiped ON the point goes on
    /// holding it, and a team wiped while taking one goes on taking it, with nobody alive standing there.</para>
    /// </summary>
    [Test]
    public void ACorpseInTheRadiusHoldsNothing()
    {
        var (sys, pm, pt) = Border();
        Stand(pm, 1, East, 0, 5);
        pm[1].Char.Hp = 0;                 // killed where they stood

        Assert.That(Majority(sys, pt), Is.Zero);
    }

    /// <summary>The half that decides a fight: the living side wins the point even when outnumbered by the
    /// dead. A count that included corpses would hand it to whoever lost the engagement.</summary>
    [Test]
    public void TheLivingOutweighTheDeadHoweverManyFell()
    {
        var (sys, pm, pt) = Border();
        for (int slot = 1; slot <= 3; slot++)
        {
            Stand(pm, slot, East, 0, 5);
            pm[slot].Guild = Guild;
            pm[slot].Char.Hp = 0;          // three of this guild fell on the point
        }
        Stand(pm, 4, East, 1, 5);
        pm[4].Guild = OtherGuild;          // one of theirs is still standing

        Assert.That(MajorityOf(sys, pt, Guild, OtherGuild), Is.EqualTo(OtherGuild));
    }

    private static int MajorityOf(GuildTerritorySystem sys, ContestPoint pt, params int[] participants) =>
        (int)typeof(GuildTerritorySystem)
            .GetMethod("MajorityGuildInRadius", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sys, new object?[] { pt, new HashSet<int>(participants) })!;
}
