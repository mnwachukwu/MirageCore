using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

// Keeper resolution for the NPC-interaction spine: shops shifting off maps are assigned to an
// NPC via ShopRecord.Keeper, and GameWorld.ShopAssignedToNpc is the resolver the interact handler + the client's
// $-glyph/keeper-shop kind both key on. GameWorld.KeeperShopKind maps that to the wire value (0 none/1 store/2 inn).
[TestFixture]
public class NpcInteractTests
{
    [Test]
    public void ShopAssignedToNpc_FindsTheKeepersShop()
    {
        var world = new GameWorld();
        world.Shops[3].Keeper = 5;   // shop #3 is kept by NPC #5
        Assert.That(world.ShopAssignedToNpc(5), Is.EqualTo(3));
    }

    [Test]
    public void ShopAssignedToNpc_NoneAssigned_ReturnsZero()
    {
        var world = new GameWorld();
        world.Shops[3].Keeper = 5;
        Assert.That(world.ShopAssignedToNpc(6), Is.EqualTo(0), "NPC with no assigned shop");
        Assert.That(world.ShopAssignedToNpc(0), Is.EqualTo(0), "npcNum 0 is never a keeper");
    }

    [Test]
    public void ShopAssignedToNpc_LowestShopIndexWins()
    {
        var world = new GameWorld();
        world.Shops[7].Keeper = 5;
        world.Shops[2].Keeper = 5;   // two shops keyed to the same NPC (a misconfig) — the scan takes the first
        Assert.That(world.ShopAssignedToNpc(5), Is.EqualTo(2));
    }

    [Test]
    public void KeeperShopKind_StoreKeeper_ReturnsOne()
    {
        var world = new GameWorld();
        world.Shops[3].Keeper = 5;
        world.Shops[3].ShopType = ShopType.Store;
        Assert.That(world.KeeperShopKind(5), Is.EqualTo(1));
    }

    [Test]
    public void KeeperShopKind_InnKeeper_ReturnsTwo()
    {
        var world = new GameWorld();
        world.Shops[3].Keeper = 5;
        world.Shops[3].ShopType = ShopType.Inn;
        Assert.That(world.KeeperShopKind(5), Is.EqualTo(2));
    }

    [Test]
    public void KeeperShopKind_NoKeeper_ReturnsZero()
    {
        var world = new GameWorld();
        Assert.That(world.KeeperShopKind(5), Is.EqualTo(0), "no shop assigned -> kind 0");
        Assert.That(world.KeeperShopKind(0), Is.EqualTo(0), "npcNum 0 is never a keeper");
    }

    // ── Interact range: the authoritative gate behind every NPC interaction ────────

    // Two-layer world: a keeper on the bridge deck and a player on the ground beneath it are adjacent on screen
    // but not on the same plane, so neither is reachable. This is the backstop a modified client can't skip.
    [Test]
    public void IsNpcInInteractRange_DifferentLayerNoRamp_IsOutOfReach()
    {
        var world = InteractWorld(npcLayer: WorldLayer.Fringe);
        var pc = new PlayerRecord { Map = 1, X = 5, Y = 5, Layer = WorldLayer.Ground };

        Assert.That(world.IsNpcInInteractRange(1, pc, 1, 2, out int npcNum), Is.False,
            "a keeper on the bridge is not reachable from plain ground beneath it");
        Assert.That(npcNum, Is.EqualTo(0), "no NPC resolved when the planes don't connect");
    }

    [Test]
    public void IsNpcInInteractRange_SameLayer_IsInReach()
    {
        var world = InteractWorld(npcLayer: WorldLayer.Fringe);
        var pc = new PlayerRecord { Map = 1, X = 5, Y = 5, Layer = WorldLayer.Fringe };   // walked up onto the deck

        Assert.That(world.IsNpcInInteractRange(1, pc, 1, 2, out int npcNum), Is.True);
        Assert.That(npcNum, Is.EqualTo(5), "same plane and within r=5 -> the keeper resolves");
    }

    // The ramp carve-out, matching melee and spell targeting: a ramp bridges the planes down its MOUNT axis, so a
    // fringe keeper standing on one is reachable from the ground at its foot without stepping up first.
    [Test]
    public void IsNpcInInteractRange_CrossLayerOntoRamp_IsInReach()
    {
        var world = InteractWorld(npcLayer: WorldLayer.Fringe);
        // The keeper's own tile (5,6) is a ramp whose ground side faces Up — i.e. toward the player at (5,5).
        world.Maps[1].EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Up } });
        var pc = new PlayerRecord { Map = 1, X = 5, Y = 5, Layer = WorldLayer.Ground };

        Assert.That(world.IsNpcInInteractRange(1, pc, 1, 2, out int npcNum), Is.True,
            "standing at the ramp's foot connects to the fringe keeper on it");
        Assert.That(npcNum, Is.EqualTo(5));
    }

    // ...but only down the mount axis. Approaching the same ramp from its side does not connect.
    [Test]
    public void IsNpcInInteractRange_CrossLayerOffRampMountAxis_IsOutOfReach()
    {
        var world = InteractWorld(npcLayer: WorldLayer.Fringe);
        world.Maps[1].EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Up } });
        var pc = new PlayerRecord { Map = 1, X = 3, Y = 6, Layer = WorldLayer.Ground };   // beside the ramp, not at its foot

        Assert.That(world.IsNpcInInteractRange(1, pc, 1, 2, out _), Is.False,
            "a ramp bridges the planes only the way you climb it, not across its side");
    }

    // One observed map (1), one NPC template (5) in slot 2 at (5,6) on the given layer.
    static GameWorld InteractWorld(WorldLayer npcLayer)
    {
        var world = new GameWorld();
        world.MapObservers[1].Add(1);
        var mn = world.MapNpcs[1, 2];
        mn.Num = 5;
        mn.X = 5;
        mn.Y = 6;
        mn.Layer = npcLayer;
        return world;
    }
}
