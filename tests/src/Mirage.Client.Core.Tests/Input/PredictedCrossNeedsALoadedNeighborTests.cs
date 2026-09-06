using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>
/// 🔴 A step off a map edge is predicted on the spot: the grid shifts and the cell in that direction
/// BECOMES the center map. So it may only be predicted into a cell that is actually loaded — an empty
/// one makes <see cref="ClientState.Map"/> null, and it is typed as though that cannot happen. Every
/// draw path dereferences it unchecked, so the next frame throws out of the draw loop, which on a
/// released client is a window that vanishes with nothing written down.
///
/// <para><b>A WARP is what makes this reachable, and warps are why it looks unrelated to edges.</b> A
/// warp empties the whole 3×3 grid and the server refills it a packet at a time, so for a few frames
/// the center map names neighbors that are not there yet. Land on a tile that is ALSO on an edge — the
/// ordinary shape of a doorway, where the tile you arrive on is the bottom row of the map outside —
/// with the movement key still held from walking into the door, and the step out is predicted into a
/// cell that is still empty.</para>
///
/// <para>Whether it lands is a race between a held key and the map packets, which is why it can be
/// reliable on one machine and unreproducible on another.</para>
///
/// <para>The server's confirmed cross carries the same rule already (it falls back to a blocking
/// reload rather than shifting an empty cell in). These two are the only places the center map changes
/// by shifting, and they now ask the same question of the same cell.</para>
/// </summary>
[TestFixture]
public class PredictedCrossNeedsALoadedNeighborTests
{
    /// <summary>A player standing on the bottom row of map 1, which declares map 2 below it — where a
    /// warp out of a building leaves you.</summary>
    private static (ClientState State, FakeTransport Transport, ClientPacketSender Sender) JustWarpedOntoTheEdge()
    {
        var state = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        state.NeighborMapNums[1, 1] = 1;
        state.Map.Down = 2;

        // What a warp leaves behind: the grid emptied, the server yet to refill it.
        state.ClearMapState();

        var me = state.Me;
        me.Name = "Me";
        me.Map = 1;
        me.X = 5;
        me.Y = state.Map.Height - 1;
        me.Dir = Direction.Down;

        var transport = new FakeTransport();
        return (state, transport, new ClientPacketSender(transport));
    }

    private static void TheNeighborArrives(ClientState state)
    {
        state.NeighborMaps[1, 2] = new MapRecord { Up = 1 };
        state.NeighborMapNums[1, 2] = 2;
    }

    /// <summary>The crash, as a test: warp onto an edge and keep walking before the grid refills.</summary>
    [Test]
    public void StillHoldingTheKeyWhenTheNeighborHasNotArrivedDoesNotNullTheMap()
    {
        var (state, transport, sender) = JustWarpedOntoTheEdge();
        int arrivedAt = state.Me.Y;

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, state, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.NeighborMaps[1, 1], Is.Not.Null,
                "the center map must survive the step — every draw path dereferences it unchecked");
            Assert.That(state.CenterMapNum, Is.EqualTo(1), "and the grid must not have shifted");
            Assert.That(state.Me.Y, Is.EqualTo(arrivedAt), "the player waits where they landed");
            Assert.That(transport.Sent.OfType<PlayerMovePacket>(), Is.Empty,
                "no move is sent for a cross that was refused");
        });
    }

    /// <summary>And once it has arrived, the same step crosses exactly as before. Without this, a guard
    /// that simply never crossed would satisfy the test above.</summary>
    [Test]
    public void OnceTheNeighborArrivesTheSameStepCrosses()
    {
        var (state, transport, sender) = JustWarpedOntoTheEdge();
        TheNeighborArrives(state);

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, state, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(state.CenterMapNum, Is.EqualTo(2), "the loaded neighbor becomes the center");
            Assert.That(state.Me.Y, Is.EqualTo(0), "and the player arrives on its top row");
            Assert.That(transport.Sent.OfType<PlayerMovePacket>().Count(), Is.EqualTo(1));
        });
    }

    /// <summary>A map with no neighbor that way still refuses, loaded cell or not — the declaration is
    /// still the first question.</summary>
    [Test]
    public void AMapDeclaringNoNeighborStillRefuses()
    {
        var (state, _, sender) = JustWarpedOntoTheEdge();
        state.Map.Down = 0;
        TheNeighborArrives(state);

        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, state, sender, 0);

        Assert.That(state.CenterMapNum, Is.EqualTo(1));
    }

    /// <summary>Both paths that make a cell the center read the same cell for a given direction.</summary>
    [Test]
    public void EveryDirectionReadsTheCellItWouldCrossInto()
    {
        var state = new ClientState();
        state.NeighborMaps[1, 0] = new MapRecord();   // up
        state.NeighborMaps[1, 2] = new MapRecord();   // down
        state.NeighborMaps[0, 1] = new MapRecord();   // left
        state.NeighborMaps[2, 1] = new MapRecord();   // right

        Assert.Multiple(() =>
        {
            Assert.That(ClientState.CellToward(Direction.Up), Is.EqualTo((1, 0)));
            Assert.That(ClientState.CellToward(Direction.Down), Is.EqualTo((1, 2)));
            Assert.That(ClientState.CellToward(Direction.Left), Is.EqualTo((0, 1)));
            Assert.That(ClientState.CellToward(Direction.Right), Is.EqualTo((2, 1)));

            foreach (var dir in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                Assert.That(state.NeighborToward(dir), Is.Not.Null, $"{dir} reads its own cell");

            state.NeighborMaps[1, 2] = null;
            Assert.That(state.NeighborToward(Direction.Down), Is.Null, "an empty cell reads as empty");
            Assert.That(state.NeighborToward(Direction.Up), Is.Not.Null, "and only that one");
        });
    }
}
