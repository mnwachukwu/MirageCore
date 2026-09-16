using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Which tiles make up one door.
///
/// <para>🔴 <b>A gate is wider than a tile.</b> A plate names one square and a key is used on the one
/// square somebody faces, so without this a three-tile gate opens a third of the way and the barrier
/// still stands — and a single plate has no way to point at the other two.</para>
/// </summary>
[TestFixture]
public sealed class DoorSpanTests
{
    private static MapRecord MapWith(params (int X, int Y)[] doors)
    {
        var map = new MapRecord(12, 12);

        foreach (var (x, y) in doors) map.EditTile(x, y, t => t with { Type = TileType.Door });

        return map;
    }

    [Test]
    public void OneDoorTileIsItsOwnGate()
    {
        var map = MapWith((4, 4));

        Assert.That(DoorSpan.From(map, 4, 4, WorldLayer.Ground), Is.EquivalentTo(new[] { (4, 4) }));
    }

    /// <summary>The case this exists for: a gate three tiles wide opens as one gate, whichever of its
    /// squares was pointed at.</summary>
    [Test]
    public void AWideGateOpensWhole()
    {
        var map = MapWith((4, 4), (5, 4), (6, 4));

        Assert.Multiple(() =>
        {
            Assert.That(DoorSpan.From(map, 4, 4, WorldLayer.Ground),
                        Is.EquivalentTo(new[] { (4, 4), (5, 4), (6, 4) }));
            Assert.That(DoorSpan.From(map, 5, 4, WorldLayer.Ground), Has.Count.EqualTo(3),
                        "pointed at the middle rather than an end");
        });
    }

    /// <summary>⚠ Orthogonally connected, so two gates meeting at a corner stay two gates. A plate that
    /// opened both would be opening one the author never pointed at.</summary>
    [Test]
    public void ACornerDoesNotJoinTwoGates()
    {
        var map = MapWith((4, 4), (5, 5));

        Assert.That(DoorSpan.From(map, 4, 4, WorldLayer.Ground), Is.EquivalentTo(new[] { (4, 4) }));
    }

    [Test]
    public void ATileThatIsNotADoorSpansNothing()
    {
        var map = MapWith((4, 4));

        Assert.That(DoorSpan.From(map, 7, 7, WorldLayer.Ground), Is.Empty);
    }

    /// <summary>A gate running off the edge of the map stops at the edge rather than reading past it.</summary>
    [Test]
    public void TheEdgeOfTheMapEndsAGate()
    {
        var map = MapWith((0, 0), (1, 0));

        Assert.That(DoorSpan.From(map, 0, 0, WorldLayer.Ground), Is.EquivalentTo(new[] { (0, 0), (1, 0) }));
    }

    /// <summary>⚠ A map authored as a wall of doors is an authoring mistake, and it opens what it opens
    /// rather than walking the whole map on one step.</summary>
    [Test]
    public void AWallOfDoorsStopsAtTheCeiling()
    {
        var everywhere = new List<(int X, int Y)>();
        for (int x = 0; x < 12; x++)
            for (int y = 0; y < 12; y++)
                everywhere.Add((x, y));

        var map = MapWith([.. everywhere]);

        Assert.That(DoorSpan.From(map, 0, 0, WorldLayer.Ground), Has.Count.EqualTo(DoorSpan.Most));
    }
}
