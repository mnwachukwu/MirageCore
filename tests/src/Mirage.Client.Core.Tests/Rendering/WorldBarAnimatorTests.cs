using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>World-space HP/MP/SP bar animation: snap on first appearance (DispHp < 0) or on an explicit
/// SnapVitals (respawn), otherwise lerp the display fraction toward the true fraction.</summary>
[TestFixture]
public class WorldBarAnimatorTests
{
    [Test]
    public void Tick_FirstAppearance_SnapsPlayerBar()
    {
        var s = new ClientState();
        var p = s.Players[1];
        p.Name = "P";
        p.MaxHp = 100;
        p.Hp = 50;  // DispHp starts at -1 (uninitialized)
        WorldBarAnimator.Tick(s, 0.016f);
        Assert.That(p.DispHp, Is.EqualTo(0.5f), "an uninitialized bar snaps to the true fraction");
    }

    // A respawn sets SnapVitals so the bar jumps to full immediately (no lerp), and the flag is consumed.
    [Test]
    public void Tick_SnapVitals_SnapsAndClearsFlag()
    {
        var s = new ClientState();
        var p = s.Players[1];
        p.Name = "P";
        p.MaxHp = 100;
        p.Hp = 100;
        p.DispHp = 0.2f;
        p.SnapVitals = true;
        WorldBarAnimator.Tick(s, 0.016f);
        Assert.Multiple(() =>
        {
            Assert.That(p.DispHp, Is.EqualTo(1f), "SnapVitals forces an immediate snap");
            Assert.That(p.SnapVitals, Is.False, "the snap flag is consumed");
        });
    }

    // An established bar lerps a fraction of the way each tick (t = min(1, 5*dt)).
    [Test]
    public void Tick_EstablishedBar_LerpsTowardTarget()
    {
        var s = new ClientState();
        var p = s.Players[1];
        p.Name = "P";
        p.MaxHp = 100;
        p.Hp = 100;
        p.DispHp = 0f;  // animating up from empty
        WorldBarAnimator.Tick(s, 0.1f);   // t = min(1, 5*0.1) = 0.5 → 0 + (1-0)*0.5
        Assert.That(p.DispHp, Is.EqualTo(0.5f).Within(1e-4f));
    }

    [Test]
    public void Tick_NpcFirstAppearance_Snaps()
    {
        var s = new ClientState();
        var n = s.MapNpcs[1];
        n.Num = 1;
        n.MaxHp = 100;
        n.Hp = 25;  // DispHp starts at -1
        WorldBarAnimator.Tick(s, 0.016f);
        Assert.That(n.DispHp, Is.EqualTo(0.25f));
    }
}
