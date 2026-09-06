using Mirage.Client.Core.State;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>ClientMapNpc.ApplySnapshot: a same-NPC-same-tile snapshot is a mid-step re-sync (returns true, and
/// must NOT reset the in-flight walk/attack interpolation, so a seam-crossing re-sync doesn't snap the slide);
/// any position/identity change is a real update that resets the interpolation.</summary>
[TestFixture]
public class ClientMapNpcTests
{
    [Test]
    public void ApplySnapshot_SameTile_ReturnsTrue_PreservesInterpolation()
    {
        var n = new ClientMapNpc
        {
            Num = 5, X = 3, Y = 4, XOffset = -16f, Moving = MovementType.Walking, Attacking = true
        };

        bool sameInPlace = n.ApplySnapshot(num: 5, hp: 80, maxHp: 100, mp: 0, maxMp: 0, sp: 0, maxSp: 0,
            x: 3, y: 4, dir: Direction.Down, layer: WorldLayer.Ground, msSinceCombat: int.MaxValue, hasTarget: false, nowMs: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(sameInPlace, Is.True, "same NPC on the same tile = a mid-step re-sync");
            Assert.That(n.XOffset, Is.EqualTo(-16f), "the in-flight slide is NOT reset on a same-tile re-sync");
            Assert.That(n.Moving, Is.EqualTo(MovementType.Walking));
            Assert.That(n.Hp, Is.EqualTo(80), "vitals still update");
        });
    }

    [Test]
    public void ApplySnapshot_MovedTile_ReturnsFalse_ResetsInterpolation()
    {
        var n = new ClientMapNpc
        {
            Num = 5, X = 3, Y = 4, XOffset = -16f, Moving = MovementType.Walking, Attacking = true
        };

        bool sameInPlace = n.ApplySnapshot(5, 80, 100, 0, 0, 0, 0, x: 4, y: 4, dir: Direction.Right,
            layer: WorldLayer.Ground, msSinceCombat: int.MaxValue, hasTarget: false, nowMs: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(sameInPlace, Is.False, "a position change is a real move");
            Assert.That(n.XOffset, Is.EqualTo(0f), "interpolation is reset on a real move");
            Assert.That(n.Moving, Is.EqualTo(MovementType.None));
            Assert.That(n.Attacking, Is.False);
        });
    }

    // A finite msSinceCombat converts to a local LastCombatMs stamp (nowMs - msSinceCombat).
    [Test]
    public void ApplySnapshot_ConvertsMsSinceCombatToLocalStamp()
    {
        var n = new ClientMapNpc();
        n.ApplySnapshot(5, 80, 100, 0, 0, 0, 0, 3, 4, Direction.Down,
            layer: WorldLayer.Ground, msSinceCombat: 250, hasTarget: true, nowMs: 10_000);
        Assert.Multiple(() =>
        {
            Assert.That(n.LastCombatMs, Is.EqualTo(9_750), "LastCombatMs = nowMs - msSinceCombat");
            Assert.That(n.HasTarget, Is.True);
        });
    }
}
