using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>Regression: a dying large NPC's delayed-death sprite (held in place until a killing spell bolt
/// lands, per the cast-animation deferral) must keep its footprint SIZE instead of shrinking to a 32x32
/// sprite mid-death. Pins that the death FX carries the def's EffectiveSize for both native slot NPCs and
/// chasing traversal guests, and that size-1 stays size-1. This is the finnicky wiring the fix guards.</summary>
[TestFixture]
public class NpcDeathSizeTests
{
    static EntityDeathFx CaptureDeath(ClientState state, string handler, object packet)
    {
        var h = new ClientPacketHandler(state, null!, null!);   // death handlers touch neither sender nor mapCache
        EntityDeathFx? captured = null;
        h.EntityDied += fx => captured = fx;
        typeof(ClientPacketHandler).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(h, new[] { packet });
        Assert.That(captured, Is.Not.Null, "the death must raise an EntityDied FX so the shell can hold the dying sprite");
        return captured!.Value;
    }

    [Test]
    public void NativeNpcDeath_CarriesFootprintSize()
    {
        var state = new ClientState { CenterMapNum = 1 };
        state.NpcDefs[5] = new NpcRecord { Sprite = 7, Size = 3 };
        var n = state.MapNpcs[2];
        n.Num = 5;
        n.X = 4;
        n.Y = 6;

        var fx = CaptureDeath(state, "HandleNpcDead", new NpcDeadPacket { MapNum = 1, NpcSlot = 2 });

        Assert.That(fx.Size, Is.EqualTo(3), "a dying 3x3 NPC's death sprite must keep its size, not shrink to 32x32");
        Assert.That(fx.SpriteRow, Is.EqualTo(7), "the death sprite uses the NPC's own sprite row");
    }

    [Test]
    public void GuestNpcDeath_CarriesFootprintSize()
    {
        var state = new ClientState { CenterMapNum = 1 };
        state.NpcDefs[5] = new NpcRecord { Sprite = 7, Size = 2 };
        state.TraversalNpcs[(9, 1)] = new ClientTraversalNpc { Num = 5, CurrentMapNum = 1, X = 4, Y = 6, SpawnMapNum = 9, SpawnSlot = 1 };

        var fx = CaptureDeath(state, "HandleTraversalNpc",
            new TraversalNpcPacket { SpawnMapNum = 9, SpawnSlot = 1, CurrentMapNum = 1, Num = 5, X = 4, Y = 6, Dead = true });

        Assert.That(fx.Size, Is.EqualTo(2), "a dying 2x2 chasing guest's death sprite must keep its size too");
    }

    [Test]
    public void Size1NpcDeath_StaysSize1()
    {
        var state = new ClientState { CenterMapNum = 1 };
        state.NpcDefs[5] = new NpcRecord { Sprite = 7, Size = 1 };
        var n = state.MapNpcs[2];
        n.Num = 5;
        n.X = 4;
        n.Y = 6;

        var fx = CaptureDeath(state, "HandleNpcDead", new NpcDeadPacket { MapNum = 1, NpcSlot = 2 });

        Assert.That(fx.Size, Is.EqualTo(1));
    }
}
