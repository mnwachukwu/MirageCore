using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mirage.Server.Tests.Progression;

/// <summary>Player quests on <see cref="QuestSystem"/> — the first customer of the objective kernel. Covers
/// accept + eligibility gates (level/stat/prereq), kill-progress through the kernel, turn-in rewards, abandon,
/// prerequisite chains, repeatable re-light, non-repeatable done-forever, and login re-tracking of progress.
/// Kills are driven straight through <see cref="ObjectiveSystem.RecordNpcKill"/> (the same hook CombatSystem
/// calls). Messages resolve through ServerStrings (loaded once by StringsSetUpFixture).</summary>
[TestFixture]
public class QuestSystemTests
{
    const int Rat = 20, Sword = 10;

    static (GameWorld world, PlayerManager pm, ObjectiveSystem objectives, QuestSystem quests) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);
        var objectives = new ObjectiveSystem();
        var quests = new QuestSystem(world, pm, dispatcher, items, mail, objectives);
        return (world, pm, objectives, quests);
    }

    static ServerPlayer AddPlayer(PlayerManager pm, int idx, int level = 5)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "acc" + idx;
        sp.Char.Name = "P" + idx;
        sp.Char.Map = 1;
        return sp;
    }

    // Define a simple "kill N rats" quest in a slot, returning the quest record for further tweaking.
    static QuestRecord KillQuest(GameWorld world, int questNum, int count = 3)
    {
        var q = world.Quests[questNum];
        q.Name = "Quest " + questNum;
        q.Objectives.Clear();
        q.Objectives.Add(new Objective { Kind = ObjectiveKind.Kill, Target = Rat, Count = count });
        return q;
    }

    static void Kill(ObjectiveSystem objectives, int npcNum, int contributor)
        => objectives.RecordNpcKill(npcNum, new[] { contributor });

    // ── Accept + eligibility ──────────────────────────────────────────────────────

    [Test]
    public void Accept_Eligible_AddsInProgressQuest()
    {
        var (world, pm, _, quests) = Setup();
        KillQuest(world, 1);
        var sp = AddPlayer(pm, 1);

        quests.Accept(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests, Has.Count.EqualTo(1));
            Assert.That(sp.Char.Quests[0].QuestNum, Is.EqualTo(1));
            Assert.That(sp.Char.Quests[0].Status, Is.EqualTo(QuestStatus.InProgress));
            Assert.That(sp.Char.Quests[0].Progress, Has.Count.EqualTo(1).And.All.Zero, "one objective, no progress yet");
        });
    }

    // ── Kill progress through the kernel ──────────────────────────────────────────

    [Test]
    public void Kill_AdvancesProgress_ThenCompletes()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 3);
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);

        Kill(objectives, Rat, 1);
        Kill(objectives, Rat, 1);
        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests[0].Progress[0], Is.EqualTo(2), "two kills tracked");
            Assert.That(quests.AllObjectivesComplete(1, 1), Is.False, "not done at 2/3");
        });

        Kill(objectives, Rat, 1);
        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests[0].Progress[0], Is.EqualTo(3), "clamped at the count");
            Assert.That(quests.AllObjectivesComplete(1, 1), Is.True, "3/3 -> ready to turn in");
            Assert.That(sp.Char.Quests[0].Status, Is.EqualTo(QuestStatus.InProgress), "still in progress until turned in");
        });
    }

    [Test]
    public void Kill_OnlyOwnerContributorCounts()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 2);
        var owner = AddPlayer(pm, 1);
        _ = AddPlayer(pm, 2);
        quests.Accept(1, 1);

        Kill(objectives, Rat, 2);   // a DIFFERENT player's kill

        Assert.That(owner.Char.Quests[0].Progress[0], Is.EqualTo(0), "someone else's kill doesn't advance my quest");
    }

    // ── Turn in + rewards ─────────────────────────────────────────────────────────

    [Test]
    public void TurnIn_Complete_GrantsRewards_AndMarksDone()
    {
        var (world, pm, objectives, quests) = Setup();
        world.Items[Constants.GoldItemIndex].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Equipment;
        var q = KillQuest(world, 1, count: 1);
        q.RewardItems.Add(new QuestReward { ItemNum = Constants.GoldItemIndex, Quantity = 250 });   // gold is item #1
        q.RewardItems.Add(new QuestReward { ItemNum = Sword, Quantity = 1 });
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);   // complete the objective
        quests.TurnIn(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests[0].Status, Is.EqualTo(QuestStatus.Done));
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Constants.GoldItemIndex), Is.EqualTo(250), "gold rewarded");
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => sp.Char.Inv[i].Num == Sword), Is.True, "item rewarded into the bag");
        });
    }

    [Test]
    public void TurnIn_Incomplete_Refused()
    {
        var (world, pm, _, quests) = Setup();
        KillQuest(world, 1, count: 3);
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);   // 0/3

        quests.TurnIn(1, 1);

        Assert.That(sp.Char.Quests[0].Status, Is.EqualTo(QuestStatus.InProgress), "can't turn in an unfinished quest");
    }

    // ── Abandon ───────────────────────────────────────────────────────────────────

    [Test]
    public void Abandon_RemovesQuest_ReAcceptable()
    {
        var (world, pm, _, quests) = Setup();
        KillQuest(world, 1);
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);

        quests.Abandon(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests, Is.Empty, "abandoning drops it back to not-started");
            Assert.That(quests.IsEligible(1, 1), Is.True, "and it can be taken again");
        });
    }

    // ── Prerequisite chains ───────────────────────────────────────────────────────

    [Test]
    public void Prereq_GatesUntilPredecessorDone()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 1);              // quest A
        KillQuest(world, 2, count: 1).PrereqQuest = 1;   // quest B requires A done
        var sp = AddPlayer(pm, 1);

        Assert.That(quests.IsEligible(1, 2), Is.False, "B is locked until A is done");

        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);   // A done

        Assert.That(quests.IsEligible(1, 2), Is.True, "B unlocks once A is done");
    }

    // ── Repeatable re-light + non-repeatable done-forever ─────────────────────────

    [Test]
    public void Repeatable_DoneThisPeriod_RelightsNextPeriod()
    {
        var (world, pm, objectives, quests) = Setup();
        var q = KillQuest(world, 1, count: 1);
        q.Repeatable = true;
        q.Cadence = QuestCadence.Daily;
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);

        Assert.That(quests.IsEligible(1, 1), Is.False, "already done this period");

        // Simulate the period rolling over by staling the stored key (can't fast-forward the clock).
        sp.Char.Quests[0].PeriodKey = "1999-01-01";
        Assert.That(quests.IsEligible(1, 1), Is.True, "re-opens once the period key differs");
    }

    // The period gate is the one ineligibility reason with no requirement of its own, so it's reported separately
    // for the client to name — otherwise a grayed Accept lists only met requirements and explains nothing.
    [Test]
    public void Repeatable_DoneThisPeriod_ReportedAsOnCooldown()
    {
        var (world, pm, objectives, quests) = Setup();
        var q = KillQuest(world, 1, count: 1);
        q.Repeatable = true;
        q.Cadence = QuestCadence.Daily;
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);

        Assert.That(quests.IsOnRepeatCooldown(1, 1), Is.True, "ineligible because this period is already used");

        sp.Char.Quests[0].PeriodKey = "1999-01-01";   // the period rolls over
        Assert.That(quests.IsOnRepeatCooldown(1, 1), Is.False, "re-lit, so nothing left to explain");
    }

    [Test]
    public void NonRepeatable_DoneForever()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 1);   // not repeatable
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);

        Assert.That(quests.IsEligible(1, 1), Is.False, "a one-time quest never re-opens");
    }

    // ── Login re-tracking ─────────────────────────────────────────────────────────

    [Test]
    public void LoginReTrack_ResumesProgress()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 3);
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);   // progress 1/3

        // Simulate logout (stop tracking) then login (re-track from the persisted progress).
        quests.OnPlayerGone(1);
        Assert.That(objectives.ActiveCount, Is.EqualTo(0), "tracking stopped on logout");
        quests.OnPlayerJoin(1);

        Kill(objectives, Rat, 1);   // should resume: 2/3
        Assert.That(sp.Char.Quests[0].Progress[0], Is.EqualTo(2), "progress resumes after a re-login");
    }

    // ── Active-quest cap ──────────────────────────────────────────────────────────

    [Test]
    public void ActiveQuestCap_RefusesBeyondLimit()
    {
        var (world, pm, _, quests) = Setup();
        for (int i = 1; i <= Constants.MaxActiveQuests + 1; i++) KillQuest(world, i);
        var sp = AddPlayer(pm, 1);
        for (int i = 1; i <= Constants.MaxActiveQuests; i++) quests.Accept(1, i);
        Assert.That(sp.Char.Quests, Has.Count.EqualTo(Constants.MaxActiveQuests), "accepted up to the cap");

        quests.Accept(1, Constants.MaxActiveQuests + 1);   // one too many

        Assert.That(sp.Char.Quests, Has.Count.EqualTo(Constants.MaxActiveQuests), "an 11th in-progress quest is refused");
    }

    // ── Repeat rewards + abandon can't farm the first-completion set ───────────────

    [Test]
    public void RepeatRewards_FirstCompletionMain_ThenRepeat()
    {
        var (world, pm, objectives, quests) = Setup();
        world.Items[Constants.GoldItemIndex].Type = ItemType.Currency;
        var q = KillQuest(world, 1, count: 1);
        q.Repeatable = true;
        q.Cadence = QuestCadence.Daily;
        q.RewardItems.Add(new QuestReward { ItemNum = Constants.GoldItemIndex, Quantity = 1000 });        // main
        q.RepeatRewardItems.Add(new QuestReward { ItemNum = Constants.GoldItemIndex, Quantity = 100 });   // repeat
        var sp = AddPlayer(pm, 1);

        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);
        Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Constants.GoldItemIndex), Is.EqualTo(1000), "first completion pays main");

        sp.Char.Quests[0].PeriodKey = "1999-01-01";   // simulate the period rolling over
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);
        Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Constants.GoldItemIndex), Is.EqualTo(1100), "subsequent completion pays repeat (+100)");
    }

    [Test]
    public void Abandon_AfterCompletion_KeepsHistory_NoMainReFarm()
    {
        var (world, pm, objectives, quests) = Setup();
        world.Items[Constants.GoldItemIndex].Type = ItemType.Currency;
        var q = KillQuest(world, 1, count: 1);
        q.Repeatable = true;
        q.Cadence = QuestCadence.Daily;
        q.RewardItems.Add(new QuestReward { ItemNum = Constants.GoldItemIndex, Quantity = 1000 });        // main
        q.RepeatRewardItems.Add(new QuestReward { ItemNum = Constants.GoldItemIndex, Quantity = 100 });   // repeat
        var sp = AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);  // completed once (main 1000)

        sp.Char.Quests[0].PeriodKey = "1999-01-01";   // period rolls
        quests.Accept(1, 1);
        quests.Abandon(1, 1);   // abandon the re-run before completing

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Quests, Has.Count.EqualTo(1), "a completed repeatable isn't wiped by abandon");
            Assert.That(sp.Char.Quests[0].Status, Is.EqualTo(QuestStatus.Done), "it reverts to Done, not removed");
        });

        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);
        Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Constants.GoldItemIndex), Is.EqualTo(1100),
            "abandon + re-run still pays repeat (+100), not a fresh main (no farming)");
    }

    // ── NPC interaction: actionable-quest resolver. This is the ONLY quest gate on the interaction spine — the
    //    client's overhead glyph and its NPC context menu derive from the same rule, so what an NPC advertises,
    //    what its menu lists, and what the server will act on are one set. A quest whose requirements aren't met
    //    yet appears in none of the three; the quest LOG is where it is listed, with the requirements. ──────────

    [Test]
    public void HasActionableQuestAt_EligibleGiver_True()
    {
        var (world, pm, _, quests) = Setup();
        KillQuest(world, 1).GiverNpc = 7;
        AddPlayer(pm, 1);

        Assert.Multiple(() =>
        {
            Assert.That(quests.HasActionableQuestAt(1, 7), Is.True, "an eligible quest at its giver is actionable");
            Assert.That(quests.HasActionableQuestAt(1, 8), Is.False, "a different NPC offers nothing");
            Assert.That(quests.HasActionableQuestAt(1, 0), Is.False, "npc 0 is never a role");
        });
    }

    [Test]
    public void HasActionableQuestAt_TurnIn_ReadyOnly()
    {
        var (world, pm, objectives, quests) = Setup();
        var q = KillQuest(world, 1, count: 1);
        q.GiverNpc = 7;
        q.TurnInNpc = 9;
        AddPlayer(pm, 1);
        quests.Accept(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(quests.HasActionableQuestAt(1, 9), Is.False, "turn-in NPC has nothing while objectives are unfinished");
            Assert.That(quests.HasActionableQuestAt(1, 7), Is.False, "the giver has nothing once the quest is active");
        });

        Kill(objectives, Rat, 1);   // complete the objective
        Assert.That(quests.HasActionableQuestAt(1, 9), Is.True, "a ready quest at its turn-in NPC is actionable");
    }

    [Test]
    public void HasActionableQuestAt_TurnInFallsBackToGiver()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 1).GiverNpc = 7;   // TurnInNpc 0 => effective turn-in = giver
        AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Kill(objectives, Rat, 1);

        Assert.That(quests.HasActionableQuestAt(1, 7), Is.True, "with no explicit turn-in NPC, you turn in at the giver");
    }

    [Test]
    public void HasActionableQuestAt_MidQuestAndDoneForever_False()
    {
        var (world, pm, objectives, quests) = Setup();
        KillQuest(world, 1, count: 1).GiverNpc = 7;   // no explicit turn-in, so 7 is both giver and turn-in
        AddPlayer(pm, 1);
        quests.Accept(1, 1);
        Assert.That(quests.HasActionableQuestAt(1, 7), Is.False,
            "accepted but unfinished → nothing to do here (the overhead gray \"!\" is what says come back)");

        Kill(objectives, Rat, 1);
        quests.TurnIn(1, 1);   // done, non-repeatable
        Assert.That(quests.HasActionableQuestAt(1, 7), Is.False, "a one-time quest done forever stays quiet");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
