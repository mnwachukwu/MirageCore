using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>
/// 🔴 Only a BLOCKING map load charges the arrival beat. A seamless crossing must not.
///
/// <para>The two meet in one place. A crossing asks the server for a region re-sync, and that carries the
/// new centre map's NPC snapshot — the very packet that ends a blocking load. Charging the beat there
/// stalls the player at every seam.</para>
///
/// <para><b>How long the stall is decides whether anyone sees it</b>, which is why this is worth a test
/// rather than an eye. A walk is 400 ms a tile, so a 200 ms beat disappears inside the step the player is
/// already taking. A sprint is nearer 138 ms, so the beat is LONGER than a step and the player visibly
/// stops at the border. The bug is invisible at the pace most testing happens at.</para>
/// </summary>
[TestFixture]
public class SeamCrossingOwesNoBeatTests
{
    /// <summary>Handling a map-NPC snapshot touches neither the sender nor the cache, which is why both
    /// are null here — the same shape the other packet-handling fixtures use.</summary>
    private static (ClientState State, ClientPacketHandler Handler) Playing()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 7 };
        state.NeighborMapNums[1, 1] = 7;
        state.Me.Name = "Me";
        state.Me.Map = 7;

        return (state, new ClientPacketHandler(state, null!, null!));
    }

    /// <summary>The centre map's snapshot, which both paths end on.</summary>
    private static string CentreSnapshot(int mapNum) =>
        PacketSerializer.Serialize(new MapNpcsPacket { MapNum = mapNum });

    [Test]
    public void ASeamCrossingDoesNotChargeTheBeat()
    {
        var (state, handler) = Playing();
        state.GettingMap = false;   // a crossing never blocks input

        handler.Handle(CentreSnapshot(7));

        Assert.That(state.ArrivedAtMs, Is.Zero,
            "a crossing is one continuous walk — charging it the beat stalls the player at every seam");
    }

    [Test]
    public void ABlockingLoadStillChargesIt()
    {
        var (state, handler) = Playing();
        state.GettingMap = true;    // a warp, a teleport, or the join handshake

        handler.Handle(CentreSnapshot(7));

        Assert.Multiple(() =>
        {
            Assert.That(state.ArrivedAtMs, Is.Not.Zero, "landing from a warp still owes a step's beat");
            Assert.That(state.GettingMap, Is.False, "and the load is over either way");
        });
    }

    /// <summary>A NEIGHBOUR's snapshot is a pre-load and settles nothing — it must not end the load or
    /// charge anything, however many of them arrive.</summary>
    [Test]
    public void ANeighbourSnapshotChargesNothingAndEndsNothing()
    {
        var (state, handler) = Playing();
        state.GettingMap = true;

        handler.Handle(CentreSnapshot(9));   // not the centre map

        Assert.Multiple(() =>
        {
            Assert.That(state.ArrivedAtMs, Is.Zero);
            Assert.That(state.GettingMap, Is.True, "still waiting on the centre");
        });
    }
}
