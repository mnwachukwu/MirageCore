using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>A big NPC's light reach scales with its footprint (authored Radius + (size-1) tiles), so
/// the LightSpec.Radius stays "how far the glow spills past the body" at any size and the bright inner core
/// grows to cover the 64/96px body.  Size 1 is exact parity with the unscaled radius players and map
/// lights use.</summary>
[TestFixture]
public class NpcLightScaleTests
{
    static float RadiusPx(float radiusTiles, int size) =>
        (float)typeof(RenderCommandBuilder)
            .GetMethod("NpcLightRadiusPx", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { radiusTiles, size })!;

    [Test]
    public void Size1_UnchangedFromAuthoredRadius()
    {
        Assert.That(RadiusPx(3f, 1), Is.EqualTo(3f * Constants.PicX), "size 1 must match the authored radius exactly");
    }

    [Test]
    public void BigNpc_ReachExtendsBySizeMinusOneTiles()
    {
        Assert.That(RadiusPx(3f, 2), Is.EqualTo((3f + 1f) * Constants.PicX));
        Assert.That(RadiusPx(3f, 3), Is.EqualTo((3f + 2f) * Constants.PicX));
    }

    [Test]
    public void InnerCore_CoversBody_ForABigNpc()
    {
        // Even a small 1-tile authored radius: a size-3 reach = (1+2)*32 = 96px, inner core = 96 * 2/3 = 64px
        // radius = 128px diameter, comfortably over the 96px (size-3) body.
        float radiusPx = RadiusPx(1f, 3);
        float innerDiameter = radiusPx * LightModel.InnerRadiusFactor * 2f;
        float bodyPx = 3 * Constants.PicX;
        Assert.That(innerDiameter, Is.GreaterThanOrEqualTo(bodyPx), "the bright inner core must cover the whole body");
    }
}
