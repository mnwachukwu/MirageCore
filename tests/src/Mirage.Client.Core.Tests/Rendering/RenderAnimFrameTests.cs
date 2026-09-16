using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>The sprite animation-frame selector (RenderCommandBuilder.AnimFrame, private): frame
/// 0 = neutral/idle, 1 = walk stride, 2 = attack. Stride toggles at the tile midpoint (offset crosses +/-PicX/2).
/// Reflected because it's a private static pure helper, matching the house style for pinning internal math.
///
/// <para>🔴 It reads the offsets and NOT a facing. Which way a body looks and which way it is going are
/// the same thing for a player and often are not for a creature.</para></summary>
[TestFixture]
public class RenderAnimFrameTests
{
    static readonly MethodInfo AnimMethod = typeof(RenderCommandBuilder)
        .GetMethod("AnimFrame", BindingFlags.NonPublic | BindingFlags.Static)!;

    static int AnimFrame(bool attacking, bool lockWalk, int xOff, int yOff)
        => (int)AnimMethod.Invoke(null, new object[] { attacking, lockWalk, xOff, yOff })!;

    [Test]
    public void Attacking_ShowsAttackFrame()
        => Assert.That(AnimFrame(true, false, 0, 0), Is.EqualTo(2));

    // After the attack frame expires but within the lock window, show idle (frame 0), not a walk stride.
    [Test]
    public void AttackLockExpired_ShowsIdleNotWalk()
        => Assert.That(AnimFrame(false, true, 0, 8), Is.EqualTo(0));

    [Test]
    public void Still_ShowsIdle()
        => Assert.That(AnimFrame(false, false, 0, 0), Is.EqualTo(0));

    // Walking up: the Y offset starts at +PicY (32) and decreases to 0; stride (frame 1) once past the midpoint.
    [Test]
    public void WalkingUp_StridesPastMidpoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AnimFrame(false, false, 0, 20), Is.EqualTo(0), "early in the step: neutral");
            Assert.That(AnimFrame(false, false, 0, 10), Is.EqualTo(1), "past the midpoint: stride");
        });
    }

    // Walking right: the X offset starts at -PicX (-32) and increases to 0; stride once past -PicX/2.
    [Test]
    public void WalkingRight_StridesPastMidpoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AnimFrame(false, false, -10, 0), Is.EqualTo(0));
            Assert.That(AnimFrame(false, false, -20, 0), Is.EqualTo(1));
        });
    }

    /// <summary>🔴 <b>A body facing one way and travelling another still strides.</b> A chasing creature
    /// turns to face its target and then steps sideways around an obstacle; reading the stride off the
    /// facing reads the axis that is NOT moving — zero — so it slid along frozen in one pose. The offsets
    /// alone say where in the step it is, whichever way its head is pointed.</summary>
    [Test]
    public void TravellingSidewaysWhileFacingElsewhere_StillStrides()
    {
        Assert.Multiple(() =>
        {
            // Stepping right (x runs -32 → 0) while facing a target above or below it.
            Assert.That(AnimFrame(false, false, -20, 0), Is.EqualTo(1), "past the midpoint of a rightward step");
            // Stepping up (y runs +32 → 0) while facing left or right.
            Assert.That(AnimFrame(false, false, 0, 10), Is.EqualTo(1), "past the midpoint of an upward step");
        });
    }
}
