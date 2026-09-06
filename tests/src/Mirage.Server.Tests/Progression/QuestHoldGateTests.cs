using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Progression;

/// <summary>
/// Whether a character meets what a quest asks of them — the one answer the accept path and the editor's
/// account browser both take.
///
/// <para>The same rule as the spell gate, for the same reason: an operator may put a quest in somebody's log,
/// but not somewhere the game would not have put it. It says nothing about whether they already hold the
/// quest, which is a separate question the accept path asks and the editor deliberately does not.</para>
/// </summary>
[TestFixture]
public class QuestHoldGateTests
{
    const int Prereq = 4;

    static (PlayerRecord P, QuestRecord Q) Setup()
    {
        var p = new PlayerRecord { Class = 2, Level = 20, Str = 30, Def = 30, Spd = 30, Int = 30 };
        var q = new QuestRecord { Name = "The Long Road", ReqLevel = 10 };
        return (p, q);
    }

    [Test]
    public void ACharacterWhoMeetsEverything_MayHoldIt()
    {
        var (p, q) = Setup();
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    [Test]
    public void TooLowALevel_IsRefused()
    {
        var (p, q) = Setup();
        p.Level = 9;
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.LevelTooLow));
    }

    [Test]
    public void ExactlyTheLevelRequired_IsEnough()
    {
        var (p, q) = Setup();
        p.Level = q.ReqLevel;
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    [TestCase("str")]
    [TestCase("def")]
    [TestCase("spd")]
    [TestCase("int")]
    public void AnyStatShortOfWhatItAsks_IsRefused(string which)
    {
        var (p, q) = Setup();
        q.ReqStr = q.ReqDef = q.ReqSpd = q.ReqInt = 25;
        switch (which)
        {
            case "str": p.Str = 24; break;
            case "def": p.Def = 24; break;
            case "spd": p.Spd = 24; break;
            default: p.Int = 24; break;
        }
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.StatTooLow));
    }

    [Test]
    public void TheWrongClass_IsRefused()
    {
        var (p, q) = Setup();
        q.AllowedClasses = [7, 8];
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.WrongClass));
    }

    [Test]
    public void AnUnrestrictedQuest_IsOpenToEveryClass()
    {
        var (p, q) = Setup();
        q.AllowedClasses = null;
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    [Test]
    public void AnUnfinishedPrerequisite_IsRefused()
    {
        var (p, q) = Setup();
        q.PrereqQuest = Prereq;
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.PrereqNotDone));
    }

    /// <summary>Having ACCEPTED the prerequisite is not having finished it.</summary>
    [Test]
    public void APrerequisiteMerelyInProgress_IsStillUnfinished()
    {
        var (p, q) = Setup();
        q.PrereqQuest = Prereq;
        p.Quests.Add(new PlayerQuest { QuestNum = Prereq, Status = QuestStatus.InProgress });
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.PrereqNotDone));
    }

    [Test]
    public void AFinishedPrerequisite_OpensIt()
    {
        var (p, q) = Setup();
        q.PrereqQuest = Prereq;
        p.Quests.Add(new PlayerQuest { QuestNum = Prereq, Status = QuestStatus.Done });
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    /// <summary>Level before stats before class before prerequisite — the refusal reported is the first one
    /// that fails, so the message is stable rather than depending on which gates happen to fail together.</summary>
    [Test]
    public void TheEarliestFailure_IsTheOneReported()
    {
        var (p, q) = Setup();
        p.Level = 1;
        q.ReqStr = 999;
        q.AllowedClasses = [7];
        q.PrereqQuest = Prereq;
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.LevelTooLow));
    }

    /// <summary>Already holding a quest is a separate question: the editor sets a state on a quest that may
    /// well already be in the log, so the gate must not refuse for that.</summary>
    [Test]
    public void AlreadyHoldingIt_IsNotAReasonToRefuse()
    {
        var (p, q) = Setup();
        p.Quests.Add(new PlayerQuest { QuestNum = 9, Status = QuestStatus.InProgress });
        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    // ── Finding a row ─────────────────────────────────────────────────────────

    [Test]
    public void FindQuest_ReturnsTheRowOrNothing()
    {
        var (p, _) = Setup();
        var row = new PlayerQuest { QuestNum = 12, Status = QuestStatus.Done };
        p.Quests.Add(row);

        Assert.Multiple(() =>
        {
            Assert.That(QuestSystem.FindQuest(p, 12), Is.SameAs(row));
            Assert.That(QuestSystem.FindQuest(p, 13), Is.Null);
        });
    }
}
