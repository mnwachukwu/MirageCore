using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The quest-editor row: dynamic objective/reward child tables — blank by default,
/// grown via AddObjective/AddReward up to the MaxQuestObjectives ceiling. Row edits AND structural add/remove
/// dirty the quest; empties are dropped on ToRecord/BuildSavePacket (dense, no gaps); ApplyPacket loads from the
/// wire without dirtying; and the scalar fields, the class gate, and the pickers round-trip.</summary>
[TestFixture]
public class QuestRowViewModelTests
{
    static QuestRowViewModel Quest(QuestRecord? r = null) =>
        new(1, r ?? new QuestRecord(), () => [], () => [], () => [], () => [], _ => false);

    [Test]
    public void NewQuest_StartsWithBlankTables()
    {
        var q = Quest();
        Assert.Multiple(() =>
        {
            Assert.That(q.Objectives, Is.Empty);
            Assert.That(q.RewardItems, Is.Empty);
            Assert.That(q.RepeatRewardItems, Is.Empty);
            Assert.That(q.HasNoObjectives, Is.True);
            Assert.That(q.IsDirty, Is.False);
        });
    }

    [Test]
    public void EditingAnObjectiveRow_MarksTheQuestDirty()
    {
        var q = Quest(new QuestRecord { Objectives = { new Objective { Kind = ObjectiveKind.Kill, Target = 20, Count = 1 } } });
        Assume.That(q.IsDirty, Is.False, "a freshly loaded quest is clean");
        q.Objectives[0].Count = 5;
        Assert.That(q.IsDirty, Is.True, "a dirty objective row makes the whole quest dirty");
    }

    [Test]
    public void EditingARewardRow_MarksTheQuestDirty()
    {
        var q = Quest(new QuestRecord { RewardItems = { new QuestReward { ItemNum = 1, Quantity = 10 } } });
        Assume.That(q.IsDirty, Is.False);
        q.RewardItems[0].ItemNum = 3;
        Assert.That(q.IsDirty, Is.True);
    }

    [Test]
    public void AddObjective_AppendsAndDirties_Remove_RemovesIt()
    {
        var q = Quest();
        q.AddObjectiveCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(q.Objectives, Has.Count.EqualTo(1));
            Assert.That(q.HasNoObjectives, Is.False);
            Assert.That(q.IsDirty, Is.True, "a structural change dirties the quest");
        });

        q.RemoveObjectiveCommand.Execute(q.Objectives[0]);
        Assert.That(q.Objectives, Is.Empty);
    }

    [Test]
    public void AddObjective_IsDisabledAtTheCeiling()
    {
        var q = Quest();
        for (int i = 0; i < Constants.MaxQuestObjectives; i++) q.AddObjectiveCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(q.Objectives, Has.Count.EqualTo(Constants.MaxQuestObjectives));
            Assert.That(q.AddObjectiveCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void ClearDirty_ClearsQuestLevelAndNestedRows()
    {
        var q = Quest(new QuestRecord
        {
            Objectives = { new Objective { Kind = ObjectiveKind.Kill, Count = 1 } },
            RewardItems = { new QuestReward { ItemNum = 1, Quantity = 10 } },
        });
        q.Name = "Rat Problem";
        q.Objectives[0].Count = 5;
        q.RewardItems[0].ItemNum = 2;
        q.AddObjectiveCommand.Execute(null);   // structural dirt too
        Assume.That(q.IsDirty, Is.True);

        q.ClearDirty();

        Assert.That(q.IsDirty, Is.False);
    }

    [Test]
    public void ToRecord_DropsEmptyRows_KeepsFilledOnes()
    {
        var q = Quest();
        q.AddObjectiveCommand.Execute(null);   // will fill
        q.AddObjectiveCommand.Execute(null);   // leave empty
        q.Objectives[0].Kind = ObjectiveKind.Kill;
        q.Objectives[0].Target = 20;
        q.Objectives[0].Count = 5;
        q.AddRewardCommand.Execute(null);      // will fill
        q.AddRewardCommand.Execute(null);      // leave empty
        q.RewardItems[0].ItemNum = 1;
        q.RewardItems[0].Value = 100;

        var rec = q.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(rec.Objectives, Has.Count.EqualTo(1), "only the filled objective survives");
            Assert.That(rec.Objectives[0].Target, Is.EqualTo(20));
            Assert.That(rec.Objectives[0].Count, Is.EqualTo(5));
            Assert.That(rec.RewardItems, Has.Count.EqualTo(1), "empty reward rows are dropped");
            Assert.That(rec.RewardItems[0].ItemNum, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyPacket_LoadsFields_AndIsNotDirty()
    {
        var q = Quest();
        q.ApplyPacket(new UpdateQuestPacket
        {
            Name = "Deliver the Letter",
            GiverNpc = 7,
            TurnInNpc = 9,
            ReqLevel = 3,
            Repeatable = true,
            Cadence = QuestCadence.Daily,
            Objectives = new List<Objective> { new() { Kind = ObjectiveKind.Kill, Target = 20, Count = 2 } },
            RewardExp = 100,
            RewardItems = new List<QuestReward> { new() { ItemNum = 1, Quantity = 250 } },
        });

        Assert.Multiple(() =>
        {
            Assert.That(q.Name, Is.EqualTo("Deliver the Letter"));
            Assert.That(q.GiverNpc, Is.EqualTo(7));
            Assert.That(q.TurnInNpc, Is.EqualTo(9));
            Assert.That(q.ReqLevel, Is.EqualTo(3));
            Assert.That(q.Repeatable, Is.True);
            Assert.That(q.Cadence, Is.EqualTo(QuestCadence.Daily));
            Assert.That(q.Objectives, Has.Count.EqualTo(1), "loads exactly the wire objectives — no padding");
            Assert.That(q.Objectives[0].Count, Is.EqualTo(2), "the row carries the packet's objective");
            Assert.That(q.RewardItems[0].ItemNum, Is.EqualTo(1));
            Assert.That(q.IsLoaded, Is.True);
            Assert.That(q.IsDirty, Is.False, "loading from the wire is not an edit");
        });
    }

    [Test]
    public void ToRecord_RoundTripsScalarsAndFlags()
    {
        var rec = new QuestRecord
        {
            Name = "Q", ReqLevel = 5, AllowedClasses = [2, 1], GiverNpc = 4, TurnInNpc = 4,
            RewardExp = 500, Repeatable = true, Cadence = QuestCadence.Weekly,
        };

        var back = Quest(rec).ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Q"));
            Assert.That(back.ReqLevel, Is.EqualTo(5));
            // Sorted by the save-path normalize, not left as authored.
            Assert.That(back.AllowedClasses, Is.EqualTo(new short[] { 1, 2 }));
            Assert.That(back.GiverNpc, Is.EqualTo(4));
            Assert.That(back.RewardExp, Is.EqualTo(500));
            Assert.That(back.Repeatable, Is.True);
            Assert.That(back.Cadence, Is.EqualTo(QuestCadence.Weekly));
        });
    }

    [Test]
    public void BuildSavePacket_CarriesIndexAndDropsEmpties()
    {
        var q = Quest();
        q.AddObjectiveCommand.Execute(null);
        q.Objectives[0].Kind = ObjectiveKind.Kill;
        q.Objectives[0].Count = 3;

        var pkt = q.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.QuestNum, Is.EqualTo(1));
            Assert.That(pkt.Objectives, Has.Count.EqualTo(1));
            Assert.That(pkt.RewardItems, Is.Empty);
        });
    }
}
