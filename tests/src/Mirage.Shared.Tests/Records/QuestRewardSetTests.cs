using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// Which of a quest's two reward sets a given run pays.
///
/// <para>🔴 One rule, three readers: the turn-in that grants the reward, the journal entry, and the giver
/// NPC's offer. They quote the same amount only because they ask the same question — an offer that names the
/// first-completion figure while the turn-in pays the repeat one is the failure this exists to prevent.</para>
///
/// <para><see cref="QuestStatus.Done"/> answers the same as an active repeat run: accepting from Done starts
/// one, so the offer to re-run a finished quest quotes what accepting it will pay.</para>
/// </summary>
[TestFixture]
public class QuestRewardSetTests
{
    private static QuestRecord WithRepeatSet() => new()
    {
        RewardExp = 1705,
        RepeatRewardExp = 909,
    };

    private static QuestRecord WithoutRepeatSet() => new() { RewardExp = 1705 };

    [Test]
    public void AFirstRun_PaysTheMainSet()
    {
        var q = WithRepeatSet();

        Assert.Multiple(() =>
        {
            Assert.That(q.PaysRepeatRewards(QuestStatus.NotStarted), Is.False, "never accepted");
            Assert.That(q.PaysRepeatRewards(QuestStatus.InProgress), Is.False, "a first run under way");
        });
    }

    [Test]
    public void ARepeatRun_PaysTheRepeatSet()
    {
        Assert.That(WithRepeatSet().PaysRepeatRewards(QuestStatus.InProgressRepeat), Is.True);
    }

    /// <summary>The offer case: a finished repeatable quest is re-accepted into a repeat run, so the giver has
    /// to quote the repeat figure while the player still holds the quest at Done.</summary>
    [Test]
    public void AFinishedQuestOffered_QuotesTheRepeatSet()
    {
        Assert.That(WithRepeatSet().PaysRepeatRewards(QuestStatus.Done), Is.True);
    }

    /// <summary>With no repeat set authored, every completion keeps paying the main one — so a repeat run must
    /// not read an empty set and pay nothing.</summary>
    [Test]
    public void WithNoRepeatSetAuthored_EveryRunPaysTheMainSet()
    {
        var q = WithoutRepeatSet();

        Assert.Multiple(() =>
        {
            Assert.That(q.HasRepeatRewards, Is.False);
            Assert.That(q.PaysRepeatRewards(QuestStatus.InProgressRepeat), Is.False);
            Assert.That(q.PaysRepeatRewards(QuestStatus.Done), Is.False);
        });
    }

    /// <summary>Repeat ITEMS alone define the set — a quest can repeat for loot and no exp.</summary>
    [Test]
    public void RepeatItemsAlone_DefineTheSet()
    {
        var q = new QuestRecord { RewardExp = 1705 };
        q.RepeatRewardItems.Add(new QuestReward { ItemNum = 4, Quantity = 1 });

        Assert.That(q.PaysRepeatRewards(QuestStatus.InProgressRepeat), Is.True);
    }
}
