using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>The sprite animation-frame selector (RenderCommandBuilder.AnimFrame, private): frame
/// 0 = neutral/idle, 1 = walk stride, 2 = attack. Stride toggles at the tile midpoint (offset crosses +/-PicX/2).
/// Reflected because it's a private static pure helper, matching the house style for pinning internal math.</summary>
[TestFixture]
public class RenderAnimFrameTests
{
    static readonly MethodInfo AnimMethod = typeof(RenderCommandBuilder)
        .GetMethod("AnimFrame", BindingFlags.NonPublic | BindingFlags.Static)!;

    static int AnimFrame(bool attacking, bool lockWalk, int xOff, int yOff, Direction dir)
        => (int)AnimMethod.Invoke(null, new object[] { attacking, lockWalk, xOff, yOff, dir })!;

    [Test]
    public void Attacking_ShowsAttackFrame()
        => Assert.That(AnimFrame(true, false, 0, 0, Direction.Down), Is.EqualTo(2));

    // After the attack frame expires but within the lock window, show idle (frame 0), not a walk stride.
    [Test]
    public void AttackLockExpired_ShowsIdleNotWalk()
        => Assert.That(AnimFrame(false, true, 0, 8, Direction.Up), Is.EqualTo(0));

    [Test]
    public void Still_ShowsIdle()
        => Assert.That(AnimFrame(false, false, 0, 0, Direction.Down), Is.EqualTo(0));

    // Walking up: the Y offset starts at +PicY (32) and decreases to 0; stride (frame 1) once past the midpoint.
    [Test]
    public void WalkingUp_StridesPastMidpoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AnimFrame(false, false, 0, 20, Direction.Up), Is.EqualTo(0), "early in the step: neutral");
            Assert.That(AnimFrame(false, false, 0, 10, Direction.Up), Is.EqualTo(1), "past the midpoint: stride");
        });
    }

    // Walking right: the X offset starts at -PicX (-32) and increases to 0; stride once past -PicX/2.
    [Test]
    public void WalkingRight_StridesPastMidpoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AnimFrame(false, false, -10, 0, Direction.Right), Is.EqualTo(0));
            Assert.That(AnimFrame(false, false, -20, 0, Direction.Right), Is.EqualTo(1));
        });
    }
}
