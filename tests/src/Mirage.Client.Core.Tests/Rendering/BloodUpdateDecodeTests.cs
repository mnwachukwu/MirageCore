using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The blood-pool wire decode. A BloodUpdatePacket carries a map's WHOLE pool list,
/// <see cref="BloodUpdatePacket.BytesPerPool"/> each — x and y as little-endian 16-bit, then size, amount,
/// freshness, layer — and the client REPLACES its list for that map, so a merged-away pool drops out.
///
/// <para>The byte literals here are the contract, written out rather than generated: the server packs
/// against the same layout, and a slip on either side shears every pool or leaves ghosts. The coordinates
/// are 16-bit because a tile coordinate has to be able to name any tile on the map — a byte stops at 255,
/// which is one tile short of a 256-wide map.</para>
/// </summary>
[TestFixture]
public class BloodUpdateDecodeTests
{
    static readonly MethodInfo Handle = typeof(ClientPacketHandler)
        .GetMethod("HandleBloodUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;

    static void Apply(ClientState state, BloodUpdatePacket packet)
    {
        var handler = new ClientPacketHandler(state, null!, null!);
        Handle.Invoke(handler, new object[] { packet });
    }

    [Test]
    public void DecodesPoolList()
    {
        var state = new ClientState { CenterMapNum = 1 };
        // Two pools: (5,6) size 3, full amount and freshness, on GROUND; (2,3) size 1, half amount,
        // ~0.78 freshness, on FRINGE.
        Apply(state, new BloodUpdatePacket
        {
            MapNum = 1,
            Reset = false,
            Pools = [5, 0, 6, 0, 3, 255, 255, 0,
                     2, 0, 3, 0, 1, 128, 200, 1],
        });

        var pools = state.BloodByMap[1];
        Assert.That(pools, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(pools[0].X, Is.EqualTo(5));
            Assert.That(pools[0].Y, Is.EqualTo(6));
            Assert.That(pools[0].Size, Is.EqualTo(3), "a big NPC's pool must decode its body size so the decal scales");
            Assert.That(pools[0].Amount, Is.EqualTo(Constants.BloodMaxTileAmount).Within(1e-3f));
            Assert.That(pools[0].Freshness, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(pools[0].Layer, Is.EqualTo(WorldLayer.Ground));
            Assert.That(pools[1].Size, Is.EqualTo(1));
            Assert.That(pools[1].Freshness, Is.EqualTo(200 / 255f).Within(1e-3f));
            Assert.That(pools[1].Layer, Is.EqualTo(WorldLayer.Fringe), "the last byte decodes the layer");
        });
    }

    /// <summary>A tile past the 255th column or row. This is the whole reason the coordinates are two bytes:
    /// packed as one, x = 300 would arrive as 44 and the decal would land somewhere else entirely, with
    /// nothing to report it.</summary>
    [TestCase(300, 7, TestName = "past the 255th column")]
    [TestCase(7, 300, TestName = "past the 255th row")]
    [TestCase(1000, 999, TestName = "well past it on both axes")]
    public void DecodesACoordinateWiderThanAByte(int x, int y)
    {
        var state = new ClientState { CenterMapNum = 1 };
        Apply(state, new BloodUpdatePacket
        {
            MapNum = 1,
            Pools = [(byte)(x & 0xFF), (byte)(x >> 8), (byte)(y & 0xFF), (byte)(y >> 8), 1, 255, 255, 0],
        });

        var pool = state.BloodByMap[1][0];
        Assert.That((pool.X, pool.Y), Is.EqualTo((x, y)));
    }

    [Test]
    public void FullListReplace_SwapsNotAppends()
    {
        var state = new ClientState { CenterMapNum = 1 };
        Apply(state, new BloodUpdatePacket { MapNum = 1, Pools = [5, 0, 6, 0, 3, 255, 255, 0] });
        Apply(state, new BloodUpdatePacket { MapNum = 1, Pools = [2, 0, 3, 0, 1, 128, 200, 0] });

        Assert.That(state.BloodByMap[1], Has.Count.EqualTo(1), "a full-list update REPLACES the map's pools, not appends");
        Assert.That(state.BloodByMap[1][0].X, Is.EqualTo(2), "the surviving pool is from the latest packet");
    }

    /// <summary>A trailing partial pool is ignored rather than decoded from whatever follows it.</summary>
    [Test]
    public void ATruncatedPool_IsDropped()
    {
        var state = new ClientState { CenterMapNum = 1 };
        Apply(state, new BloodUpdatePacket { MapNum = 1, Pools = [5, 0, 6, 0, 3, 255, 255, 0, 2, 0, 3] });

        Assert.That(state.BloodByMap[1], Has.Count.EqualTo(1));
    }

    [Test]
    public void UnobservedMap_IsIgnored()
    {
        var state = new ClientState { CenterMapNum = 1 };   // map 9 is not the center nor a neighbor
        Apply(state, new BloodUpdatePacket { MapNum = 9, Pools = [5, 0, 6, 0, 3, 255, 255, 0] });

        Assert.That(state.BloodByMap.ContainsKey(9), Is.False, "blood for a map we aren't observing is dropped");
    }
}
