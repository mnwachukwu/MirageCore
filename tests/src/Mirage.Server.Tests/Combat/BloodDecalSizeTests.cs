using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.World;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>The blood POOL model: blood is a per-map list of size×size rectangles that overlap freely.  A bleed
/// merges into the list by rectangle math — ENVELOPED (a bigger pool already covers the footprint) feeds that
/// pool; otherwise a new pool drops, ABSORBS every pool it fully contains, and adds to partial overlaps.  Pins
/// the size stamping + the merge rules + the invariant that no pool is ever fully contained inside another.</summary>
[TestFixture]
public class BloodDecalSizeTests
{
    static (GameWorld world, BloodSystem blood) NewBlood()
    {
        var world = new GameWorld();
        return (world, new BloodSystem(world, null!));   // Deposit never touches the dispatcher
    }

    static System.Collections.Generic.List<BloodPool> Pools(GameWorld w) => w.MapBlood[1].Pools;

    [Test]
    public void BigNpcDeposit_MakesOnePoolAtItsFootprint()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 6, 1.0f, size: 3);
        Assert.That(Pools(world).Count, Is.EqualTo(1));
        Assert.That(Pools(world)[0].Size, Is.EqualTo(3), "a 3x3 NPC drops one body-sized pool");
        Assert.That(Pools(world)[0].X, Is.EqualTo(5));
        Assert.That(Pools(world)[0].Y, Is.EqualTo(6));
        Assert.That(Pools(world)[0].Amount, Is.GreaterThan(0f));
    }

    [Test]
    public void Size1Deposit_MakesSize1Pool()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 6, 1.0f);
        Assert.That(Pools(world).Count, Is.EqualTo(1));
        Assert.That(Pools(world)[0].Size, Is.EqualTo(1));
    }

    [Test]
    public void OutOfRangeSize_ClampsToMaxNpcSize()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 6, 1.0f, size: 99);
        Assert.That(Pools(world)[0].Size, Is.EqualTo(Mirage.Shared.Constants.MaxNpcSize));
    }

    [Test]
    public void Size1InsideBigPool_FeedsIt_NoNewPool()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // 3x3 pool, footprint [5,7]
        float before = Pools(world)[0].Amount;
        blood.Deposit(1, 7, 7, 1.0f);                    // a size-1 bleed on the far corner tile (enveloped)
        Assert.That(Pools(world).Count, Is.EqualTo(1), "a bleed inside the pool must NOT drop a second pool");
        Assert.That(Pools(world)[0].Size, Is.EqualTo(3));
        Assert.That(Pools(world)[0].Amount, Is.GreaterThan(before), "it feeds the enveloping pool");
    }

    [Test]
    public void Size1OutsideAnyPool_MakesItsOwn()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // footprint [5,7]
        blood.Deposit(1, 9, 9, 1.0f);                    // clear of the footprint
        Assert.That(Pools(world).Count, Is.EqualTo(2));
    }

    [Test]
    public void Size2InsideSize3_FeedsIt_KeepsSize3()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // footprint [5,7]
        float before = Pools(world)[0].Amount;
        blood.Deposit(1, 6, 6, 1.0f, size: 2);          // 2x2 footprint [6,7] fully inside the 3x3 → enveloped
        Assert.That(Pools(world).Count, Is.EqualTo(1), "a size-2 fully inside a size-3 feeds it, no new pool");
        Assert.That(Pools(world)[0].Size, Is.EqualTo(3));
        Assert.That(Pools(world)[0].Amount, Is.GreaterThan(before));
    }

    [Test]
    public void BiggerBleederOntoSmaller_AbsorbsIt()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 2);          // existing 2x2 pool [5,6]
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // 3x3 [5,7] fully contains the 2x2 → absorb it
        Assert.That(Pools(world).Count, Is.EqualTo(1), "the bigger footprint absorbs the smaller pool it covers");
        Assert.That(Pools(world)[0].Size, Is.EqualTo(3));
    }

    [Test]
    public void NineSize1_UnderSize3_MergeIntoOnePool()
    {
        var (world, blood) = NewBlood();
        for (int x = 5; x <= 7; x++)
        {
            for (int y = 5; y <= 7; y++)
                blood.Deposit(1, x, y, 1.0f);            // 9 separate size-1 pools tiling [5,7]x[5,7]
        }

        Assert.That(Pools(world).Count, Is.EqualTo(9), "adjacent size-1 pools don't merge with each other");
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // a size-3 covering all 9 → absorb them all
        Assert.That(Pools(world).Count, Is.EqualTo(1), "the size-3 absorbs every fully-covered size-1 pool");
        Assert.That(Pools(world)[0].Size, Is.EqualTo(3));
    }

    [Test]
    public void PartialOverlap_KeepsBothPools()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 2);          // [5,6]x[5,6]
        float before = Pools(world)[0].Amount;
        blood.Deposit(1, 6, 6, 1.0f, size: 2);          // [6,7]x[6,7] - overlaps but neither contains the other
        Assert.That(Pools(world).Count, Is.EqualTo(2), "partial overlap renders both pools");
        Assert.That(Pools(world)[0].Amount, Is.GreaterThan(before), "the overlapped pool is still fed");
    }

    [Test]
    public void Invariant_NoPoolIsFullyContainedInAnother()
    {
        var (world, blood) = NewBlood();
        // A messy sequence of mixed-size bleeds in overlapping spots.
        blood.Deposit(1, 4, 4, 1.0f);
        blood.Deposit(1, 5, 5, 1.0f, size: 2);
        blood.Deposit(1, 6, 6, 1.0f, size: 3);
        blood.Deposit(1, 5, 5, 1.0f);
        blood.Deposit(1, 7, 7, 1.0f, size: 2);
        blood.Deposit(1, 4, 4, 1.0f, size: 3);
        var pools = Pools(world);
        for (int i = 0; i < pools.Count; i++)
        {
            for (int j = 0; j < pools.Count; j++)
            {
                if (i == j) continue;
                bool contained = pools[j].X >= pools[i].X && pools[j].Right <= pools[i].Right
                              && pools[j].Y >= pools[i].Y && pools[j].Bottom <= pools[i].Bottom;
                Assert.That(contained, Is.False, $"pool {j} is fully inside pool {i} - invariant broken");
            }
        }
    }

    [Test]
    public void TrailDrip_OnBloodyTile_IsSuppressed()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 3);          // pool covers [5,7]
        int count = Pools(world).Count;
        blood.DepositTrail(1, 6, 6, 1);                  // (6,6) is already under the pool → no drip
        Assert.That(Pools(world).Count, Is.EqualTo(count), "a trail drip on a tile already under a pool is suppressed");
    }

    // Two-layer world: pools on different layers never merge, even when they overlap the same tiles — the ground
    // beneath a bridge and the deck on top bleed independently.
    [Test]
    public void DifferentLayers_DoNotMerge()
    {
        var (world, blood) = NewBlood();
        blood.Deposit(1, 5, 5, 1.0f, size: 3, layer: Mirage.Shared.WorldLayer.Ground);   // 3x3 on the ground
        blood.Deposit(1, 6, 6, 1.0f, size: 1, layer: Mirage.Shared.WorldLayer.Fringe);    // a fringe drip over it — enveloped only if SAME layer

        Assert.That(Pools(world).Count, Is.EqualTo(2), "a fringe pool over a ground pool stays separate");
        Assert.That(Pools(world)[0].Layer, Is.EqualTo(Mirage.Shared.WorldLayer.Ground));
        Assert.That(Pools(world)[1].Layer, Is.EqualTo(Mirage.Shared.WorldLayer.Fringe));
    }
}
