using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// How a walking emitter's shadows get from one tile to the next.
///
/// <para>Reach is answered per TILE, so a single mask can only change in one jump as an emitter crosses a
/// border — the halo slides sub-pixel and its shadows arrive all at once, which reads as squares of light
/// popping in and out. So both tiles are traced and the light blends between them across the step.</para>
///
/// <para>An entity's X/Y is already the DESTINATION the instant a step begins, with the offset counting a
/// whole tile back to zero. The tile being LEFT is therefore the one the offset points at, and it is the
/// one the blend starts on.</para>
/// </summary>
[TestFixture]
public class LightTraceTileTests
{
    private const int W = 24;
    private const int H = 20;

    // A player mid-step east: X is already the destination, XOffset counts back up to 0.
    private static LightSourceCmd TorchAt(float xOffset)
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++) state.NeighborMaps[c, r] = new MapRecord(W, H);
        }

        state.MyIndex = 1;
        var me = state.Players[1];
        me.Name = "Vandestelka";
        me.Sprite = 1;
        me.X = 6;
        me.Y = 5;
        me.XOffset = xOffset;

        var grid = new int[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++) grid[c, r] = c * 3 + r + 1;
        }

        var camera = new Camera();
        camera.Update(0, 0, 0f, 0f, grid, W, H);

        return RenderCommandBuilder.Build(state, new RenderFrame(), camera).Lights.Single(l => l.Id == 1);
    }

    [Test]
    public void StandingStill_TracesOnceAndBlendsNothing()
    {
        var cmd = TorchAt(0f);

        Assert.Multiple(() =>
        {
            Assert.That(cmd.Reach, Is.Not.Null);
            Assert.That(cmd.ReachInto, Is.Null, "the second trace is only paid for by things that move");
            Assert.That(cmd.ReachBlend, Is.Zero);
        });
    }

    [Test]
    public void MidStep_BothTilesAreTracedAndTheBlendSaysWhere()
    {
        var start = TorchAt(-Constants.PicX);            // a whole tile still to travel
        var nearlyThere = TorchAt(-Constants.PicX * 0.1f);

        Assert.Multiple(() =>
        {
            Assert.That(start.ReachInto, Is.Not.Null, "the tile being entered is traced too");
            Assert.That(start.ReachBlend, Is.EqualTo(0f).Within(0.01f), "and counts for nothing yet");
            Assert.That(nearlyThere.ReachBlend, Is.EqualTo(0.9f).Within(0.01f), "but almost everything here");
        });
    }

    [Test]
    public void TheBlendStartsOnTheTileBeingLeftAndEndsOnTheOneBeingEntered()
    {
        var cmd = TorchAt(-Constants.PicX * 0.5f);
        var standing = TorchAt(0f);

        // Standing on tile 6, the anchor IS tile 6; mid-step it is tile 5, one tile west.
        Assert.That(cmd.TileScreenX, Is.EqualTo(standing.TileScreenX - Constants.PicX).Within(0.01f),
            "the first mask is the tile being left");
        Assert.That(cmd.IntoScreenX, Is.EqualTo(standing.TileScreenX).Within(0.01f),
            "and the second is the one being entered");
    }

    /// <summary>The whole point: nothing jumps. The blend has to sweep the step rather than flip somewhere
    /// in the middle of it, or the shadows still change in one frame — just at a different moment.</summary>
    [Test]
    public void AcrossTheStep_TheBlendMovesContinuously()
    {
        float previous = 0f;
        for (int i = 0; i <= 8; i++)
        {
            float blend = TorchAt(-Constants.PicX * (1f - i / 9f)).ReachBlend;
            Assert.That(blend, Is.GreaterThanOrEqualTo(previous), $"step {i} went backwards");
            Assert.That(blend - previous, Is.LessThan(0.2f), $"step {i} jumped");
            previous = blend;
        }

        // The last frame has no offset left, so it collapses to a single mask. That is not a jump away from
        // where the blend was heading — it is the very tile it was converging on.
        Assert.That(TorchAt(0f).TileScreenX,
            Is.EqualTo(TorchAt(-0.01f).IntoScreenX).Within(0.01f));
    }
}
