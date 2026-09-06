using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// ServerPlayer.ActiveShop resolution: reach opens a session, and nothing after it closes one.
///
/// <para>Every panel a keeper opens locks the player where they stand, so a session that began within reach
/// can only leave it because the KEEPER walked off — and a wandering shopkeeper cancelling a half-finished
/// withdrawal is the shop breaking, not a rule working. Distance is checked once, by the interact spine that
/// opens the panel, and never again.</para>
///
/// <para>A session resolves to nothing in two cases: none was opened, or the slot it was opened against
/// holds something other than that keeper — an empty slot, or a different NPC respawned into it. A session
/// must never resolve into a stranger's inventory, which is what the identity check is for.</para>
///
/// <para>This gate fronts every shop, inn, bank, market and set-spawn op, so it is exercised indirectly
/// everywhere and pinned directly here.</para>
/// </summary>
[TestFixture]
public class ActiveShopResolutionTests
{
    const int Map = 1, ShopNum = 3, KeeperNpc = 5, KeeperSlot = 1, Index = 1;

    static (GameWorld world, ServerPlayer sp) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Shops[ShopNum].ShopType = ShopType.Inn;
        world.Shops[ShopNum].Keeper = KeeperNpc;      // shop #3 is kept by NPC #5
        var mn = world.MapNpcs[Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = 5;
        mn.Y = 5;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 5;  // standing on the keeper's tile
        world.MapObservers[Map].Add(Index);
        sp.SetActiveShop(ShopNum, Map, KeeperSlot);
        return (world, sp);
    }

    [Test]
    public void ActiveShop_KeeperInRange_ResolvesTheShop()
    {
        var (world, sp) = Setup();
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(ShopNum));
    }

    [Test]
    public void ActiveShop_NoOpenShop_ReturnsZero()
    {
        var (world, sp) = Setup();
        sp.ClearActiveShop();
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0));
    }

    /// <summary>The case the whole rule exists for. Keepers wander, and a shopkeeper who takes a step while
    /// somebody is halfway through a withdrawal must not cancel it — the panel has the player pinned where
    /// they stand, so the distance between them is the NPC's doing and not theirs.</summary>
    [Test]
    public void ActiveShop_KeeperWandersOff_KeepsTheShopOpen()
    {
        var (world, sp) = Setup();
        world.MapNpcs[Map, KeeperSlot].X = 20;
        world.MapNpcs[Map, KeeperSlot].Y = 20;  // well beyond interact range
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(ShopNum));
    }

    /// <summary>Observing the keeper's map is part of being able to OPEN a session, not part of holding one.
    /// A player who has stopped observing has left the map, and leaving the map clears the session outright
    /// (MovementSystem.PlayerWarp) rather than leaving one that resolves to nothing.</summary>
    [Test]
    public void ActiveShop_PlayerNotObservingMap_StillResolves()
    {
        var (world, sp) = Setup();
        world.MapObservers[Map].Remove(Index);
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(ShopNum));
    }

    [Test]
    public void ActiveShop_KeeperVanished_ReturnsZero()
    {
        var (world, sp) = Setup();
        world.MapNpcs[Map, KeeperSlot].Num = 0;   // the keeper NPC died / despawned
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0), "a vanished keeper closes the shop");
    }

    // The NPC in that slot keeps a different shop than the one this session was opened against.
    [Test]
    public void ActiveShop_KeeperKeepsADifferentShop_ReturnsZero()
    {
        var (world, sp) = Setup();
        world.Shops[ShopNum].Keeper = 99;   // this shop belongs to a different keeper NPC
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0));
    }

    /// <summary>Leaving the character has to end the session outright. Reach guards the opening only, so a
    /// session left behind on a recycled slot would be a live shop nobody opened.</summary>
    [Test]
    public void LeavingTheCharacter_EndsTheSession()
    {
        var (world, sp) = Setup();
        Assume.That(sp.ActiveShop(world, Index), Is.EqualTo(ShopNum));

        sp.ClearActiveShop();

        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0));
    }
}
