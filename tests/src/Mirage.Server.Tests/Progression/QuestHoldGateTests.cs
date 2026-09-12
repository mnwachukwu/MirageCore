using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Progression;

/// <summary>
/// Whether a character may hold a quest — the one answer the accept path and the editor's account browser
/// both take.
///
/// <para>One gate is left, and it is about the LOG rather than about the body: a chain's earlier link must
/// be finished. Everything else a quest used to ask — a level, four stats — was a question only a game
/// with those concepts can put, and a game puts it from its own script.</para>
///
/// <para>It says nothing about whether the character already holds the quest, which is a separate question
/// the accept path asks and the editor deliberately does not.</para>
/// </summary>
[TestFixture]
public class QuestHoldGateTests
{
    private const int Prereq = 4;

    private static (PlayerRecord P, QuestRecord Q) Setup() =>
        (new PlayerRecord(), new QuestRecord { Name = "The Long Road" });

    [Test]
    public void AQuestThatAsksNothing_MayBeHeldByAnybody()
    {
        var (p, q) = Setup();

        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }

    [Test]
    public void AnUnfinishedPrerequisite_IsRefused()
    {
        var (p, q) = Setup();
        q.PrereqQuest = Prereq;

        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.PrereqNotDone));
    }

    [Test]
    public void APrerequisiteInProgress_IsStillUnfinished()
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

    /// <summary>Already holding a quest is a separate question: the editor sets a state on a quest that may
    /// well already be in the log, so the gate must not refuse for that.</summary>
    [Test]
    public void AlreadyHoldingIt_IsNotAReasonToRefuse()
    {
        var (p, q) = Setup();
        p.Quests.Add(new PlayerQuest { QuestNum = 1, Status = QuestStatus.InProgress });

        Assert.That(QuestSystem.CanHold(p, q), Is.EqualTo(QuestSystem.HoldResult.Ok));
    }
}
