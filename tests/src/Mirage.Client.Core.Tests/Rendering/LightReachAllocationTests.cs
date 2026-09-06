using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The render path does not allocate per frame, and an emitter walking does not make it start.
///
/// <para>Reach masks are the one thing here big enough to matter — a few kilobytes each, and a wandering
/// emitter needs a new one every time it enters a tile. Left to the collector that is a steady drip of
/// garbage in the one place the client is otherwise allocation-free, which shows up as Gen0 collections
/// ticking up during ordinary play and nothing to point at.</para>
///
/// <para>So discarded masks are recycled, and this is what says they still are. The number to watch is not
/// zero — the first pass through a stretch of ground has to build its masks — but a SECOND pass over the
/// same ground, where every mask has already been made once, must cost nothing.</para>
/// </summary>
[TestFixture]
public class LightReachAllocationTests
{
    private const int W = 24, H = 20;

    private static (ClientState State, Camera Camera) Scene()
    {
        var state = new ClientState { MyIndex = 1 };
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                var map = new MapRecord(W, H);
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                        if ((x * 7 + y * 3) % 11 == 0) map.EditTile(x, y, t => t with { Type = TileType.Blocked });
                }

                state.NeighborMaps[col, row] = map;
                state.NeighborMapNums[col, row] = col * 3 + row + 1;
            }
        }

        state.CenterMapNum = state.NeighborMapNums[1, 1];
        var me = state.Players[1];
        me.Name = "Vandestelka";
        me.Sprite = 1;
        me.Map = state.CenterMapNum;
        me.Y = H / 2;

        var camera = new Camera();
        camera.Update(8, 6, 0f, 0f, state.NeighborMapNums, W, H);
        return (state, camera);
    }

    // One lap of the emitter along a row of tiles, a frame per tile.
    private static void Walk(ClientState state, Camera camera, RenderFrame frame)
    {
        var me = state.Players[1];
        for (int x = 2; x < W - 2; x++)
        {
            me.X = x;
            me.XOffset = -Constants.PicX / 2f;    // mid-step, so both tiles are traced
            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        }
    }

    [Test]
    public void WalkingGroundItHasAlreadyCovered_AllocatesNothing()
    {
        var (state, camera) = Scene();
        var frame = new RenderFrame();

        // Two laps to warm: the first builds every mask, the second proves the pool has them and lets the
        // dictionary and the sorted buffers reach their steady sizes.
        Walk(state, camera, frame);
        Walk(state, camera, frame);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int lap = 0; lap < 20; lap++) Walk(state, camera, frame);
        long perLap = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;

        // A mask at the torch's radius is several kilobytes, and a lap crosses twenty tiles. Anything in
        // that range means masks are being built rather than reused; a few hundred bytes of incidental
        // churn is not what this is guarding against.
        Assert.That(perLap, Is.LessThan(2048),
            $"a lap over known ground allocated {perLap} B — the reach masks are not being recycled");
    }
}
