using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;
using System.Linq;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// Two-layer ("bridge") world render frame (Stage 1g-B): RenderFrame gained the Canopy tile stack (drawn over
/// everything, after the fringe-layer entity pass) and every per-entity draw command carries a WorldLayer, so
/// GameplayScreen.DrawWorld can split entities into the ground pass (under the bridge) and the fringe pass (on it).
/// </summary>
[TestFixture]
public class RenderFrameLayerTests
{
    [Test]
    public void Canopy_IsAllocatedToMaxCanopyLayers_AndClearedEachFrame()
    {
        var f = new RenderFrame();

        Assert.That(f.Canopy, Is.Not.Null);
        Assert.That(f.Canopy.Length, Is.EqualTo(Constants.MaxCanopyLayers));
        Assert.That(f.Canopy.All(l => l is not null), Is.True, "every canopy sublayer list is allocated (no null → no NPE on Clear/draw)");

        f.Below[0].Add(new TileDrawCmd(0, 0, 1, 0));
        f.Above[0].Add(new TileDrawCmd(0, 0, 1, 0));
        f.Canopy[0].Add(new TileDrawCmd(0, 0, 1, 0));

        f.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(f.Below.Sum(l => l.Count), Is.Zero);
            Assert.That(f.Above.Sum(l => l.Count), Is.Zero);
            Assert.That(f.Canopy.Sum(l => l.Count), Is.Zero, "Clear empties the canopy stack too");
        });
    }

    [Test]
    public void EntityDrawCommands_DefaultToGround_AndCanCarryFringe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SpriteDrawCmd(0, 0, 0, 0, Direction.Down).Layer, Is.EqualTo(WorldLayer.Ground),
                "an untagged sprite defaults to the ground layer (flat-map behavior unchanged)");
            Assert.That(new SpriteDrawCmd(0, 0, 0, 0, Direction.Down, 1, WorldLayer.Fringe).Layer, Is.EqualTo(WorldLayer.Fringe));
            Assert.That(new ItemDrawCmd(0, 0, 5).Layer, Is.EqualTo(WorldLayer.Ground));
            Assert.That(new CorpseDrawCmd(0, 0, WorldLayer.Fringe).Layer, Is.EqualTo(WorldLayer.Fringe));
            Assert.That(new ContestPointCmd(0, 0, ContestControl.Neutral, "P", WorldLayer.Fringe).Layer, Is.EqualTo(WorldLayer.Fringe));
        });
    }

    // While a sprite is mid-slide across a ramp (walk-offset still animating), it must render on the HIGHER layer
    // (Fringe) so the ramp/fringe tile art doesn't occlude it — the "sliding out from under the ramp" fix.  Once
    // the slide finishes (offset 0) it commits to its destination layer.
    [Test]
    public void SlideRenderLayer_StaysOnFringeThroughACrossLayerSlide_ThenCommits()
    {
        var m = typeof(RenderCommandBuilder).GetMethod("SlideRenderLayer", BindingFlags.NonPublic | BindingFlags.Static)!;
        WorldLayer Call(WorldLayer layer, WorldLayer prev, float xo, float yo) =>
            (WorldLayer)m.Invoke(null, new object[] { layer, prev, xo, yo })!;

        Assert.Multiple(() =>
        {
            // Descending (was Fringe, now Ground) mid-slide → drawn on Fringe (over the ramp art).
            Assert.That(Call(WorldLayer.Ground, WorldLayer.Fringe, 0f, -16f), Is.EqualTo(WorldLayer.Fringe), "descend slide");
            // Ascending (was Ground, now Fringe) mid-slide → also Fringe (never slide UNDER the ramp climbing on).
            Assert.That(Call(WorldLayer.Fringe, WorldLayer.Ground, 0f, 16f), Is.EqualTo(WorldLayer.Fringe), "ascend slide");
            // Same-layer slide → the entity's own layer (flat-map behavior unchanged).
            Assert.That(Call(WorldLayer.Ground, WorldLayer.Ground, 16f, 0f), Is.EqualTo(WorldLayer.Ground), "same-layer slide");
            // Slide finished (offset 0) → commit to the destination layer even though prev differs.
            Assert.That(Call(WorldLayer.Ground, WorldLayer.Fringe, 0f, 0f), Is.EqualTo(WorldLayer.Ground), "settled");
        });
    }
}
