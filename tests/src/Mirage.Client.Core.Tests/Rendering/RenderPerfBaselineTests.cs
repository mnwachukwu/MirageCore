using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Diagnostics;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// Client render-path performance baseline. <b>[Explicit] — run manually.</b>
///
/// <para><see cref="RenderCommandBuilder.Build"/> runs once per frame and walks the whole observable
/// 3x3 region: every visible tile on three layers, every NPC and traversal guest, every player, blood
/// decals, lights, and map items. It is the one client function where a per-frame allocation
/// translates directly into GC pressure at frame rate — 60 allocations a second per byte-source, which
/// is what produces the periodic hitches players notice rather than a lower average frame time.</para>
///
/// <para>So the number that matters here is <b>bytes per frame</b>, not microseconds. The builder is
/// deliberately written to reuse its buffers (the <c>RenderFrame</c> is cleared and refilled, the
/// per-tile corpse counter is a reused static); this measures whether that actually holds under a
/// populated world, and gives a figure to compare against after any change.</para>
/// </summary>
[TestFixture]
[Explicit, Category("Benchmark")]
public class RenderPerfBaselineTests
{
    const int CenterMap = 1;

    // A populated observable region: a full center map plus its eight neighbors, NPCs spread across
    // them, players on the center map, and ground items — roughly what a busy town looks like.
    static ClientState BusyState(int npcsPerMap, int players, int itemsPerMap)
    {
        var state = new ClientState { MyIndex = 1, CenterMapNum = CenterMap };

        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                int mapNum = col * 3 + row + 1;
                var map = new MapRecord();
                for (int x = 0; x <= Constants.MaxMapX; x++)
                {
                    for (int y = 0; y <= Constants.MaxMapY; y++)
                    {
                        map.EditTile(x, y, t => t with { Type = TileType.Walkable });
                        // Layered art: each layer cell is a packed sheet+tile value, 0 = unused.
                        map.EditTile(x, y, t => t.WithCell(LayerType.Ground, 0, 1 + ((x + y) % 4)));        // every tile has ground art
                        if (x % 3 == 0) map.EditTile(x, y, t => t.WithCell(LayerType.Fringe, 0, 2));        // partial fringe coverage
                    }
                }

                state.NeighborMaps[col, row] = map;
                state.NeighborMapNums[col, row] = mapNum;
            }
        }

        state.CenterMapNum = state.NeighborMapNums[1, 1];

        // NPC definitions the emitters read for size/name/behavior.
        for (int i = 1; i <= 20; i++)
            state.NpcDefs[i] = new NpcRecord { Name = $"npc{i}", Behavior = NpcBehavior.AttackOnSight };

        // Native NPCs on the center map.
        for (int slot = 1; slot <= npcsPerMap && slot < state.MapNpcs.Length; slot++)
        {
            var n = state.MapNpcs[slot];
            n.Num = 1 + (slot % 20);
            n.X = slot % (Constants.MaxMapX + 1);
            n.Y = slot % (Constants.MaxMapY + 1);
            n.Hp = 50;
            n.MaxHp = 100;
        }

        // Players spread over the center map.
        for (int i = 1; i <= players && i < state.Players.Length; i++)
        {
            var p = state.Players[i];
            p.Name = $"player{i}";
            p.Map = CenterMap;
            p.X = i % (Constants.MaxMapX + 1);
            p.Y = i % (Constants.MaxMapY + 1);
            p.Hp = 60;
            p.MaxHp = 100;
            p.Mp = 30;
            p.MaxMp = 100;
            p.Sp = 80;
            p.MaxSp = 100;
        }

        // Ground items.
        for (int i = 1; i <= 10; i++)
            state.Items[i] = new ItemRecord { Name = $"item{i}", Pic = (short)i };
        for (int i = 1; i <= itemsPerMap; i++)
        {
            state.MapItems[i] = new MapItemRecord
            {
                Num = 1 + (i % 10), X = i % (Constants.MaxMapX + 1), Y = i % (Constants.MaxMapY + 1),
            };
        }

        return state;
    }

    static (double usPerFrame, double bytesPerFrame) MeasureFrames(int frames, ClientState state)
    {
        var frame = new RenderFrame();
        var camera = new Camera();
        camera.Update(8, 6, 0f, 0f, state.NeighborMapNums, state.MapTilesX, state.MapTilesY);

        void OneFrame()
        {
            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        }

        for (int i = 0; i < 200; i++) OneFrame();      // JIT + let the reused buffers reach steady size

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++) OneFrame();
        sw.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();

        return (sw.Elapsed.TotalMicroseconds / frames, (after - before) / (double)frames);
    }

    [Test]
    public void Benchmark_RenderCommandBuilder_PerFrameCostAndAllocation()
    {
        TestContext.WriteLine("RenderCommandBuilder.Build — per frame, over a populated 3x3 region:");
        TestContext.WriteLine("");

        foreach (var (label, npcs, players, items) in new[]
        {
            ("quiet   (5 npc,  1 player,  5 items)", 5, 1, 5),
            ("busy    (30 npc, 10 players, 25 items)", 30, 10, 25),
            ("crowded (60 npc, 30 players, 60 items)", 60, 30, 60),
        })
        {
            var r = MeasureFrames(3_000, BusyState(npcs, players, items));
            TestContext.WriteLine($"  {label,-42} {r.usPerFrame,8:F2} us   {r.bytesPerFrame,9:F0} B/frame");
            // Context: at 60fps a byte-per-frame figure becomes B/s x60.
            TestContext.WriteLine($"  {"",-42} {"",8}     -> {r.bytesPerFrame * 60 / 1024.0,7:F1} KB/s at 60fps");
        }

        TestContext.WriteLine("");
        TestContext.WriteLine("  A steady figure near zero means the reused-buffer design is holding.");
        TestContext.WriteLine("  A figure that grows with entity count is a per-entity allocation worth chasing.");
    }

    // The frame buffer is meant to be reused, not reallocated. If Clear() left capacity behind, the
    // second and later frames would allocate nothing extra — this checks that directly by comparing a
    // cold first frame against warm steady-state frames.
    [Test]
    public void Benchmark_RenderFrame_ReuseHoldsAcrossFrames()
    {
        var state = BusyState(30, 10, 25);
        var frame = new RenderFrame();
        var camera = new Camera();
        camera.Update(8, 6, 0f, 0f, state.NeighborMapNums, state.MapTilesX, state.MapTilesY);

        // Cold: the very first frame grows the command lists from empty.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        frame.Clear();
        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        long cold = GC.GetAllocatedBytesForCurrentThread() - b0;

        // Warm: lists are at size, so a frame should cost close to nothing.
        for (int i = 0; i < 300; i++)
        {
            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        }
        long warm = (GC.GetAllocatedBytesForCurrentThread() - b1) / 500;

        TestContext.WriteLine($"  first (cold) frame: {cold,9:F0} B");
        TestContext.WriteLine($"  warm steady frame:  {warm,9:F0} B");
        TestContext.WriteLine($"  ratio: warm is {(cold == 0 ? 0 : (double)warm / cold * 100):F1}% of cold");
    }
}
