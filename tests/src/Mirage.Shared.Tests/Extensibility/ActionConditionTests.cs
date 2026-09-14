using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// When a declared verb is offered, asked of an attribute bag.
///
/// <para>🔴 <b>This is the one implementation, and that is the point.</b> The client grays the entry out
/// and the server refuses the invoke; both call this. Two readings of one predicate would be a verb the
/// menu offers and the server rejects, or worse, the other way round.</para>
/// </summary>
[TestFixture]
public class ActionConditionTests
{
    private static AttributeBag Carrying(string key, long value)
        => new AttributeBag().Set(key, value);

    [Test]
    public void TheDefault_AsksNothingAndAlwaysHolds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ActionCondition.Always.AsksNothing, Is.True);
            Assert.That(ActionCondition.Always.Holds(null), Is.True,
                "a verb that says nothing about when it applies is offered always, not never");
            Assert.That(ActionCondition.Always.Holds(new AttributeBag()), Is.True);
        });
    }

    [Test]
    public void Present_AsksOnlyWhetherTheKeyIsThere()
    {
        var when = ActionCondition.Carrying("harvest.satchel");

        Assert.Multiple(() =>
        {
            Assert.That(when.Holds(Carrying("harvest.satchel", 0)), Is.True,
                "carrying zero of a thing is still carrying it — the key is what was asked about");
            Assert.That(when.Holds(new AttributeBag()), Is.False);
            Assert.That(when.Holds(null), Is.False);
        });
    }

    [Test]
    public void Absent_IsWhatAVerbThatGrantsSomethingWants()
    {
        var when = ActionCondition.NotCarrying("harvest.license");

        Assert.Multiple(() =>
        {
            Assert.That(when.Holds(new AttributeBag()), Is.True, "offered until they have one");
            Assert.That(when.Holds(null), Is.True, "and to a body with no attributes at all");
            Assert.That(when.Holds(Carrying("harvest.license", 1)), Is.False, "and gone afterwards");
        });
    }

    [Test]
    public void TheComparisons_ReadTheNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ActionCondition.AtLeast("n", 3).Holds(Carrying("n", 3)), Is.True, "at least is inclusive");
            Assert.That(ActionCondition.AtLeast("n", 3).Holds(Carrying("n", 2)), Is.False);

            Assert.That(new ActionCondition("n", ConditionTest.AtMost, 3).Holds(Carrying("n", 3)), Is.True);
            Assert.That(new ActionCondition("n", ConditionTest.AtMost, 3).Holds(Carrying("n", 4)), Is.False);

            Assert.That(new ActionCondition("n", ConditionTest.Exactly, 3).Holds(Carrying("n", 3)), Is.True);
            Assert.That(new ActionCondition("n", ConditionTest.Exactly, 3).Holds(Carrying("n", 2)), Is.False);
        });
    }

    /// <summary>A comparison against a key that is not there is false, never an exception.
    ///
    /// <para>A body that has never been given a key is the ordinary case — a character who has not
    /// started the game's loop yet — so this is the answer most bodies give most of the time.</para>
    /// </summary>
    [Test]
    public void AComparisonAgainstNothing_IsFalse()
    {
        Assert.That(ActionCondition.AtLeast("harvest.baskets", 1).Holds(new AttributeBag()), Is.False);
    }

    /// <summary>🔴 A test neither side recognises refuses rather than allows.
    ///
    /// <para>An unknown value means a client and a server disagreeing about what a number means. A verb
    /// that will not appear is a bug somebody reports; a verb that appears when it should not is a game's
    /// rule silently gone.</para>
    /// </summary>
    [Test]
    public void ATestFromTheFuture_RefusesRatherThanAllows()
    {
        var alien = new ActionCondition("n", (ConditionTest)200, 0);

        Assert.That(alien.Holds(Carrying("n", 5)), Is.False);
    }
}
