using Mirage.Client.Core.Logic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>Press-order movement resolution ("input stack"): the most-recently-pressed still-held
/// direction wins, releasing it falls back to whatever is still held, and same-tick ties resolve to
/// the legacy fixed precedence Up > Down > Left > Right.</summary>
[TestFixture]
public class MovementInputStackTests
{
    static Direction? Up(MovementInputStack s) => s.Resolve(up: true, down: false, left: false, right: false);

    [Test]
    public void NoKeysHeld_ResolvesNull()
    {
        var s = new MovementInputStack();
        Assert.That(s.Resolve(false, false, false, false), Is.Null);
    }

    [Test]
    public void SingleKey_ResolvesThatDirection()
    {
        var s = new MovementInputStack();
        Assert.That(Up(s), Is.EqualTo(Direction.Up));
    }

    // The scenario from the feature request: W, then D, then release D → up, right, up again.
    [Test]
    public void PressWThenD_ThenReleaseD_FallsBackToUp()
    {
        var s = new MovementInputStack();
        Assert.Multiple(() =>
        {
            // W down.
            Assert.That(s.Resolve(up: true, down: false, left: false, right: false), Is.EqualTo(Direction.Up));
            // D added while W still held — most-recently-pressed wins.
            Assert.That(s.Resolve(up: true, down: false, left: false, right: true), Is.EqualTo(Direction.Right));
            // D released, W still held — falls back to the still-held key.
            Assert.That(s.Resolve(up: true, down: false, left: false, right: false), Is.EqualTo(Direction.Up));
        });
    }

    // Releasing the older key while the newer is still held keeps moving in the newer direction.
    [Test]
    public void PressWThenD_ThenReleaseW_KeepsRight()
    {
        var s = new MovementInputStack();
        s.Resolve(up: true, down: false, left: false, right: false);              // W
        s.Resolve(up: true, down: false, left: false, right: true);               // + D → Right
        Assert.That(s.Resolve(up: false, down: false, left: false, right: true), Is.EqualTo(Direction.Right));
    }

    // Opposite directions use the same rule: last press wins, releasing it restores the other.
    [Test]
    public void OppositeDirections_LastPressedWins_ThenFallsBack()
    {
        var s = new MovementInputStack();
        Assert.Multiple(() =>
        {
            Assert.That(s.Resolve(up: true, down: false, left: false, right: false), Is.EqualTo(Direction.Up));
            Assert.That(s.Resolve(up: true, down: true, left: false, right: false), Is.EqualTo(Direction.Down));
            Assert.That(s.Resolve(up: true, down: false, left: false, right: false), Is.EqualTo(Direction.Up));
        });
    }

    // A third key layered on top wins, and releasing it falls back to the previous (not the oldest).
    [Test]
    public void ThreeKeys_FallBackUnwindsInReversePressOrder()
    {
        var s = new MovementInputStack();
        s.Resolve(up: true, down: false, left: false, right: false);              // W → Up
        s.Resolve(up: true, down: false, left: true, right: false);              // + A → Left
        Assert.Multiple(() =>
        {
            Assert.That(s.Resolve(up: true, down: false, left: true, right: true), Is.EqualTo(Direction.Right)); // + D → Right
            Assert.That(s.Resolve(up: true, down: false, left: true, right: false), Is.EqualTo(Direction.Left)); // -D → back to Left
            Assert.That(s.Resolve(up: true, down: false, left: false, right: false), Is.EqualTo(Direction.Up));  // -A → back to Up
        });
    }

    // Keys that first appear together on one tick have no real order, so they fall back to the
    // legacy fixed precedence: Up beats Down beats Left beats Right.
    [Test]
    public void SameTickMultiPress_UsesLegacyPrecedence()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new MovementInputStack().Resolve(up: true, down: false, left: false, right: true), Is.EqualTo(Direction.Up), "Up beats Right");
            Assert.That(new MovementInputStack().Resolve(up: false, down: true, left: true, right: false), Is.EqualTo(Direction.Down), "Down beats Left");
            Assert.That(new MovementInputStack().Resolve(up: false, down: false, left: true, right: true), Is.EqualTo(Direction.Left), "Left beats Right");
        });
    }

    // Releasing everything empties the stack; a later press starts fresh with no stale bias.
    [Test]
    public void ReleaseAll_ThenPress_StartsFresh()
    {
        var s = new MovementInputStack();
        s.Resolve(up: true, down: false, left: false, right: true);               // Up (legacy tie-break)
        Assert.That(s.Resolve(false, false, false, false), Is.Null);              // all released
        Assert.That(s.Resolve(up: false, down: false, left: false, right: true), Is.EqualTo(Direction.Right)); // D alone
    }

    // A direction held continuously across a re-press of another key keeps its place in the stack.
    [Test]
    public void HeldKeyRetainsOrder_AcrossNeighborTogglingAboveIt()
    {
        var s = new MovementInputStack();
        s.Resolve(up: true, down: false, left: false, right: false);              // W → Up
        s.Resolve(up: true, down: false, left: false, right: true);               // + D → Right
        s.Resolve(up: true, down: false, left: false, right: false);              // -D → Up
        // Re-press D: it goes back on top of the still-held W.
        Assert.That(s.Resolve(up: true, down: false, left: false, right: true), Is.EqualTo(Direction.Right));
    }
}
