using Mirage.Client.Core.Logic;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// How a bar over a head catches up with its value.
///
/// <para>The rule is the one the meters in a panel follow, so the same health does not move at two
/// speeds depending on which bar somebody happens to be looking at.</para>
/// </summary>
[TestFixture]
public sealed class OverheadBarEaseTests
{
    private static readonly EntityHandle Somebody = EntityHandle.ForPlayer(1);
    private static readonly EntityHandle SomebodyElse = EntityHandle.ForPlayer(2);

    /// <summary>🔴 <b>A body nobody has drawn yet is at its value, not on its way to it.</b> Easing up
    /// from empty as somebody comes into view says they were nearly dead a moment ago — a sentence about
    /// something that never happened.</summary>
    [Test]
    public void TheFirstDrawIsExact()
    {
        var ease = new OverheadBarEase();

        Assert.That(ease.Toward(Somebody, 0, 0.42f), Is.EqualTo(0.42f));
    }

    /// <summary>⚠ A negative fraction is <c>OverheadBar.Absent</c>: the row is not drawn at all. Easing
    /// it would turn "nothing to say about this body" into a bar sitting at zero, which reads as a body
    /// about to die rather than one the client is not tracking.</summary>
    [Test]
    public void NothingToSayPassesStraightThrough()
    {
        var ease = new OverheadBarEase();

        Assert.That(ease.Toward(Somebody, 0, OverheadBar.Absent), Is.EqualTo(OverheadBar.Absent));
    }

    /// <summary>A row that goes away and comes back starts at its value again rather than resuming from
    /// wherever the slide had reached.</summary>
    [Test]
    public void ARowThatWentAwayComesBackAtItsValue()
    {
        var ease = new OverheadBarEase();

        ease.Toward(Somebody, 0, 1f);
        ease.Toward(Somebody, 0, OverheadBar.Absent);

        Assert.That(ease.Toward(Somebody, 0, 0.2f), Is.EqualTo(0.2f));
    }

    /// <summary>Three bars over one body are three values, and two bodies are two more. A key that
    /// collided would show one body's health over another's head.</summary>
    [Test]
    public void EveryBodyAndEveryRowIsItsOwn()
    {
        var ease = new OverheadBarEase();

        Assert.Multiple(() =>
        {
            Assert.That(ease.Toward(Somebody, 0, 1f), Is.EqualTo(1f));
            Assert.That(ease.Toward(Somebody, 1, 0.5f), Is.EqualTo(0.5f));
            Assert.That(ease.Toward(SomebodyElse, 0, 0.25f), Is.EqualTo(0.25f));
        });
    }

    /// <summary>Snapping forgets everything, so the next draw of each bar is exact again — what the
    /// client does when the world changes under it and every remembered value is stale at once.</summary>
    [Test]
    public void SnappingPutsEveryBarBackAtItsValue()
    {
        var ease = new OverheadBarEase();

        ease.Toward(Somebody, 0, 1f);
        ease.Snap();

        Assert.That(ease.Toward(Somebody, 0, 0f), Is.EqualTo(0f));
    }

    /// <summary>⚠ Two of them ease separately. The state belongs to a client rather than to the type, so
    /// a second client in the same process — which is what a test is — cannot move the first one's bars.
    /// </summary>
    [Test]
    public void TwoClientsDoNotShareBars()
    {
        var mine = new OverheadBarEase();
        var theirs = new OverheadBarEase();

        mine.Toward(Somebody, 0, 1f);

        Assert.That(theirs.Toward(Somebody, 0, 0.1f), Is.EqualTo(0.1f),
                    "the second client has never drawn this body, so it starts at the value");
    }
}
