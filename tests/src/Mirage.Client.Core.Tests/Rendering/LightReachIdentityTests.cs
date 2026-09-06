using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// A reach mask array is a stable identity for the reach it holds.
///
/// <para>That is what lets the renderer key a GPU texture on the array itself and upload a mask once rather
/// than once per light per light-map pass. The guarantee has two halves. While
/// <see cref="RenderCommandBuilder.ReachGeneration"/> holds still, the same array is the same reach; when
/// anything the trace reads moves, that number moves with it, and the renderer lets go of every texture it
/// holds.</para>
///
/// <para>Lose the first half and a wall that never moved starts occluding the wrong light. Lose the second
/// and a door opens onto a mask that still says it is shut.</para>
/// </summary>
[TestFixture]
public class LightReachIdentityTests
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
        me.X = W / 2;
        me.Y = H / 2;

        var camera = new Camera();
        camera.Update(me.X, me.Y, 0f, 0f, state.NeighborMapNums, W, H);
        return (state, camera);
    }

    // The local player's torch, which is the one light this scene emits with a mask on it.
    private static LightSourceCmd Torch(RenderFrame frame)
    {
        foreach (var cmd in frame.Lights)
        {
            if (cmd.Reach is not null) return cmd;
        }

        Assert.Fail("the scene emitted no masked light");
        return default;
    }

    [Test]
    public void StandingStill_HandsBackTheSameMaskEveryFrame()
    {
        var (state, camera) = Scene();
        var frame = new RenderFrame();

        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        var first = Torch(frame).Reach;
        int gen = RenderCommandBuilder.ReachGeneration;

        for (int f = 0; f < 5; f++)
        {
            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
            Assert.That(Torch(frame).Reach, Is.SameAs(first),
                "a standing emitter was handed a different mask array, so its texture would re-upload every frame");
        }

        Assert.That(RenderCommandBuilder.ReachGeneration, Is.EqualTo(gen),
            "the generation moved with nothing to move it, which drops every mask texture");
    }

    [Test]
    public void MidStep_TheTwoMasksAreDistinct()
    {
        var (state, camera) = Scene();
        var frame = new RenderFrame();
        state.Players[1].XOffset = -Constants.PicX / 2f;

        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        var cmd = Torch(frame);

        // Both are bound at once while the halo cross-fades between them, so one texture cannot serve both.
        Assert.That(cmd.ReachInto, Is.Not.Null, "a mid-step emitter traces the tile it is entering too");
        Assert.That(cmd.ReachInto, Is.Not.SameAs(cmd.Reach));
    }

    [Test]
    public void OutgrowingTheCache_NeverGivesTwoLightsOneMask()
    {
        var (state, camera) = Scene();
        var frame = new RenderFrame();

        // A fixed camera over a party that scatters to fresh ground every frame. Several emitters therefore
        // MISS the cache within one build, which is what puts the cap boundary between two of them.
        const int Company = 5;
        for (int i = 0; i < Company; i++)
        {
            var p = state.Players[i + 2];
            p.Name = $"Companion{i}";
            p.Sprite = 1;
            p.Map = state.CenterMapNum;
        }

        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        int gen = RenderCommandBuilder.ReachGeneration;
        var tileOf = new Dictionary<byte[], (float X, float Y, int R)>(ReferenceEqualityComparer.Instance);
        int drops = 0;

        // One lap of the map's tiles, five at a time — comfortably more distinct reaches than the cap holds.
        for (int f = 0; f * Company + Company <= W * H; f++)
        {
            for (int i = 0; i < Company; i++)
            {
                int cell = f * Company + i;
                state.Players[i + 2].X = cell % W;
                state.Players[i + 2].Y = cell / W;
            }

            frame.Clear();
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);

            if (RenderCommandBuilder.ReachGeneration != gen)
            {
                drops++;
                gen = RenderCommandBuilder.ReachGeneration;
            }

            // Within one frame, two emitters standing on different tiles must hold different masks. A cache
            // dropped partway through a build recycles the mask an earlier light is holding and hands it
            // straight to a later one.
            tileOf.Clear();
            foreach (var cmd in frame.Lights)
            {
                if (cmd.Reach is null) continue;
                (float X, float Y, int R) here = (cmd.TileScreenX, cmd.TileScreenY, cmd.ReachRadius);
                if (tileOf.TryGetValue(cmd.Reach, out var already))
                {
                    Assert.That(already, Is.EqualTo(here),
                        $"two lights at ({already.X},{already.Y}) and ({here.X},{here.Y}) share one mask array");
                }

                tileOf[cmd.Reach] = here;
            }
        }

        Assert.That(drops, Is.GreaterThan(0),
            "the cache never outgrew its cap, so this lap proves nothing — lengthen it");
    }

    [Test]
    public void AChangedMap_MovesTheGeneration()
    {
        var (state, camera) = Scene();
        var frame = new RenderFrame();

        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
        int gen = RenderCommandBuilder.ReachGeneration;

        state.NeighborMaps[1, 1]!.Revision++;
        frame.Clear();
        RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);

        Assert.That(RenderCommandBuilder.ReachGeneration, Is.Not.EqualTo(gen),
            "the world changed and the generation did not, so stale mask textures would stay bound");
    }
}
