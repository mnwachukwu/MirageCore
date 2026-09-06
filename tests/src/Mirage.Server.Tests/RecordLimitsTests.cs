using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>
/// The record families are per-server settings now, not compiled-in constants. These lock the two things
/// that have to stay true for that to be safe: a world is cut from the operator's numbers, and the numbers
/// themselves cannot be set to something that breaks the server.
/// </summary>
[TestFixture]
public sealed class RecordLimitsTests
{
    private static ServerConfig Config(RecordLimits records) =>
        ServerConfig.Default with { MaxPlayers = 2, Records = records };

    [Test]
    public void AWorldIsCutFromTheConfiguredNumbers()
    {
        var world = new GameWorld(Config(new RecordLimits
        {
            Items = 40, Npcs = 30, Shops = 12, Spells = 25,
            Quests = 8, Conversations = 9, Maps = 20, MapGroups = 6,
        }));

        Assert.Multiple(() =>
        {
            // 1-based with an unused index 0, so every array is one longer than its limit.
            Assert.That(world.Items, Has.Length.EqualTo(41));
            Assert.That(world.Npcs, Has.Length.EqualTo(31));
            Assert.That(world.Shops, Has.Length.EqualTo(13));
            Assert.That(world.Spells, Has.Length.EqualTo(26));
            Assert.That(world.Quests, Has.Length.EqualTo(9));
            Assert.That(world.Conversations, Has.Length.EqualTo(10));
            Assert.That(world.Maps, Has.Length.EqualTo(21));
        });
    }

    [Test]
    public void EveryMapKeyedArrayFollowsTheMapCount()
    {
        // These are indexed by map number everywhere, so one of them lagging behind is an exception on a
        // map the operator legitimately configured.
        var world = new GameWorld(Config(RecordLimits.Default with { Maps = 15 }));

        Assert.Multiple(() =>
        {
            Assert.That(world.TempTiles, Has.Length.EqualTo(16));
            Assert.That(world.MapItems, Has.Length.EqualTo(16));
            Assert.That(world.MapObservers, Has.Length.EqualTo(16));
            Assert.That(world.MapTraversalNpcs, Has.Length.EqualTo(16));
            Assert.That(world.PlayersOnMap, Has.Length.EqualTo(16));
            Assert.That(world.MapNpcs.GetLength(0), Is.EqualTo(16));
        });
    }

    [Test]
    public void EverySlotIsConstructed()
    {
        // Index 0 is the unused dummy every read site relies on being non-null.
        var world = new GameWorld(Config(RecordLimits.Default with { Items = 5, Maps = 3 }));

        Assert.Multiple(() =>
        {
            for (int i = 0; i <= 5; i++) Assert.That(world.Items[i], Is.Not.Null, $"item {i}");
            for (int m = 0; m <= 3; m++) Assert.That(world.MapObservers[m], Is.Not.Null, $"map {m}");
        });
    }

    [Test]
    public void TheWorldAndItsLimitsAgree()
    {
        // Bounds checks read Limits and the arrays are cut from it, so a check can never guard an array of
        // a different size. Assert the relationship rather than the numbers.
        var world = new GameWorld(Config(RecordLimits.Default with { Items = 77, Spells = 13 }));

        Assert.That(world.Items, Has.Length.EqualTo(world.Limits.Items + 1));
        Assert.That(world.Spells, Has.Length.EqualTo(world.Limits.Spells + 1));
    }

    [Test]
    public void AnAbsurdNumberIsClampedRatherThanAllocated()
    {
        var config = ServerConfig.Default with { Records = RecordLimits.Default with { Items = int.MaxValue } };

        Assert.That(config.Records.Items, Is.EqualTo(RecordLimits.Ceiling));
    }

    [Test]
    public void AnEmptyFamilyIsClampedToOne()
    {
        var config = ServerConfig.Default with { Records = RecordLimits.Default with { Maps = 0 } };

        Assert.That(config.Records.Maps, Is.EqualTo(1), "a world with no maps at all is not a world");
    }

    [Test]
    public void ConfiguringNothingLeavesTheStockNumbers()
    {
        Assert.That(ServerConfig.Default.Records, Is.EqualTo(RecordLimits.Default));
    }
}
