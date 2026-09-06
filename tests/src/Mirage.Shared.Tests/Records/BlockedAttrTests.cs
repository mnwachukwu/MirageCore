using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// What a wall stops.
///
/// <para>A Blocked tile carries two authored fields — whether light stops there and whether sight does —
/// and both default to stopping. That default is what a map holding neither field means, so it is the one
/// thing here that cannot be allowed to drift: flip it and every wall in every existing world turns
/// transparent at once, with nothing to report it.</para>
/// </summary>
[TestFixture]
public class BlockedAttrTests
{
    private static readonly JsonSerializerOptions Json = Serialization.RecordJson.Options;

    [Test]
    public void ANewWall_StopsEverything()
    {
        var tile = new TileRecord { Type = TileType.Blocked };

        Assert.Multiple(() =>
        {
            Assert.That(tile.BlocksLight, Is.True);
            Assert.That(tile.BlocksSight, Is.True);
            Assert.That(new FringeAttr { Type = TileType.Blocked }.BlocksLight, Is.True);
            Assert.That(new FringeAttr { Type = TileType.Blocked }.BlocksSight, Is.True);
        });
    }

    /// <summary>The guard that matters: a map authored before these fields existed holds neither key, and
    /// has to read back as a solid wall.</summary>
    [Test]
    public void AWallWithNeitherFieldOnDisk_ReadsBackSolid()
    {
        var tile = JsonSerializer.Deserialize<TileRecord>("""{"type":"Blocked","ground":[147]}""", Json)!;

        Assert.Multiple(() =>
        {
            Assert.That(tile.Type, Is.EqualTo(TileType.Blocked));
            Assert.That(tile.BlocksLight, Is.True, "a wall with nothing said about it stops light");
            Assert.That(tile.BlocksSight, Is.True, "and stops sight");
        });
    }

    /// <summary>Only what the wall lets through is written, so an ordinary wall costs nothing on disk.</summary>
    [Test]
    public void APlainWall_WritesNeitherField()
    {
        string json = JsonSerializer.Serialize(new TileRecord { Type = TileType.Blocked }, Json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("blocksLight"));
            Assert.That(json, Does.Not.Contain("blocksSight"));
        });
    }

    [Test]
    public void AWindow_RoundTripsThroughDisk()
    {
        var window = new TileRecord { Type = TileType.Blocked, BlocksLight = false, BlocksSight = false };

        string json = JsonSerializer.Serialize(window, Json);
        var back = JsonSerializer.Deserialize<TileRecord>(json, Json)!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("blocksLight"));
            Assert.That(back.BlocksLight, Is.False);
            Assert.That(back.BlocksSight, Is.False);
        });
    }

    [Test]
    public void ARailing_RoundTripsThroughTheWire()
    {
        var railing = new TileRecord { Type = TileType.Blocked, BlocksLight = false, BlocksSight = true };

        var back = SendMapPacket.TileData.From(3, 4, railing).ToTile();

        Assert.Multiple(() =>
        {
            Assert.That(back.Type, Is.EqualTo(TileType.Blocked));
            Assert.That(back.BlocksLight, Is.False);
            Assert.That(back.BlocksSight, Is.True);
        });
    }

    /// <summary>A plain wall sends no attribute fields at all, and the receiving record's own defaults are
    /// already solid.</summary>
    [Test]
    public void APlainWall_SendsNoAttributeFields_AndArrivesSolid()
    {
        var wall = new TileRecord { Type = TileType.Blocked };

        var data = SendMapPacket.TileData.From(3, 4, wall);
        var back = data.ToTile();

        Assert.Multiple(() =>
        {
            Assert.That(data.Fields, Is.Null, "nothing to say about a wall that stops everything");
            Assert.That(back.BlocksLight, Is.True);
            Assert.That(back.BlocksSight, Is.True);
        });
    }

    /// <summary>Repainting a wall as something else takes its permissions with it: a tile that is not a
    /// wall must not carry a "lets light through" it would honour the moment it became one.</summary>
    [Test]
    public void RepaintingAWall_ResetsWhatItStops()
    {
        var tile = new TileRecord { Type = TileType.Blocked, BlocksLight = false, BlocksSight = false };

        tile = TileAttrRules.Normalize(tile with { Type = TileType.Walkable });

        Assert.Multiple(() =>
        {
            Assert.That(tile.BlocksLight, Is.True);
            Assert.That(tile.BlocksSight, Is.True);
        });
    }

    [Test]
    public void TheResolvedAttribute_CarriesWhatTheWallStops()
    {
        var tile = new TileRecord { Type = TileType.Blocked, BlocksLight = false, BlocksSight = true };

        var attr = LayerLogic.AttrFor(tile, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(attr.Type, Is.EqualTo(TileType.Blocked));
            Assert.That(attr.BlocksLight, Is.False);
            Assert.That(attr.BlocksSight, Is.True);
        });
    }

    /// <summary>A ramp reads as solid understructure from below, and understructure stops everything.</summary>
    [Test]
    public void ARampReadFromBelow_StopsEverything()
    {
        var tile = new TileRecord { FringeAttr = new FringeAttr { Type = TileType.LayerRamp } };

        var attr = LayerLogic.AttrFor(tile, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(attr.Type, Is.EqualTo(TileType.Blocked));
            Assert.That(attr.BlocksLight, Is.True);
            Assert.That(attr.BlocksSight, Is.True);
        });
    }
}
