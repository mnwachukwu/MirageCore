using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>
/// How long a client keeps what it was told about an NPC.
///
/// <para>🔴 Two failures, and neither one throws. Keeping values too long hands them to whatever spawns
/// into that identity next — a fresh mob wearing a dead one's health, which reads as a server bug. Not
/// keeping them long enough empties a bar because the camera moved: an NPC's values are keyed by the
/// identity it SPAWNED at, so a chaser that walks onto the next map is the same body and must not
/// reset.</para>
/// </summary>
[TestFixture]
public class NpcAttributeLifetimeTests
{
    private const int Map = 7, Slot = 3;

    private static (ClientState State, ClientPacketHandler Handler) Playing()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = Map };
        state.NeighborMapNums[1, 1] = Map;
        state.Me.Name = "Me";
        state.Me.Map = Map;

        return (state, new ClientPacketHandler(state, null!, null!));
    }

    private static EntityHandle Npc => EntityHandle.ForNpc(Map, Slot);

    private static void GiveItValues(ClientState state)
        => state.BagFor(Npc)!.Set("hull", 5).Set("hullMax", 10);

    [Test]
    public void ADeadNpcTakesItsValuesWithIt()
    {
        var (state, handler) = Playing();
        state.MapNpcs[Slot].Num = 1;
        GiveItValues(state);

        handler.Handle(PacketSerializer.Serialize(new NpcDeadPacket { MapNum = Map, NpcSlot = Slot }));

        Assert.That(state.AttributesOf(Npc), Is.Null, "the slot respawns as a new body");
    }

    /// <summary>A visitor going home is replaced by a fresh native respawn at the same identity, so the
    /// values it was carrying must not be inherited.</summary>
    [Test]
    public void AVisitorLeavingForGoodTakesItsValuesWithIt()
    {
        var (state, handler) = Playing();
        GiveItValues(state);

        handler.Handle(PacketSerializer.Serialize(
            new NpcDespawnPacket { SpawnMapNum = Map, SpawnSlot = Slot }));

        Assert.That(state.AttributesOf(Npc), Is.Null);
    }

    [Test]
    public void AWarpDropsEveryNpcsValues()
    {
        var (state, _) = Playing();
        GiveItValues(state);

        state.ClearMapState();

        Assert.That(state.AttributesOf(Npc), Is.Null, "the whole picture is about to be rebuilt from the server");
    }

    /// <summary>🔴 The one case that must NOT forget. A chaser crossing a seam is the same body under the
    /// same handle, and clearing on a camera move is how a bar ends up flickering empty every border.</summary>
    [Test]
    public void AChaserCrossingIntoViewKeepsWhatItWasCarrying()
    {
        var (state, handler) = Playing();
        GiveItValues(state);

        handler.Handle(PacketSerializer.Serialize(new TraversalNpcPacket
        {
            SpawnMapNum = Map,
            SpawnSlot = Slot,
            CurrentMapNum = Map,
            Num = 1,
        }));

        Assert.That(state.AttributesOf(Npc)?["hull"].AsLong(), Is.EqualTo(5));
    }

    /// <summary>🔴 A sync writes, so it creates the bag; a read does not. The reader here runs once per
    /// visible body per frame, and a get-or-create in it would leave an empty bag behind every NPC that
    /// ever crossed the screen — nothing clears one that holds nothing.</summary>
    [Test]
    public void ASyncCreatesABag_AndAReadNeverDoes()
    {
        var (state, _) = Playing();

        Assert.Multiple(() =>
        {
            Assert.That(state.AttributesOf(Npc), Is.Null, "before anything arrives");
            Assert.That(state.BagFor(Npc), Is.Not.Null, "the sync handler has somewhere to write");
            Assert.That(state.AttributesOf(Npc), Is.Not.Null, "and the reader finds it afterwards");
        });
    }
}
