using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Guilds;

/// <summary><see cref="GuildSystem.QuestTargetsNpc"/> — the gate for the per-contributor quest-valor roll:
/// true only when the guild has an active KILL quest whose target matches the slain NPC (0 = wildcard).</summary>
[TestFixture]
public class GuildQuestTargetTests
{
    private static GuildRecord GuildWithKillQuest(int target) =>
        new() { Quest = new GuildQuestDef { Objective = new Objective { Kind = ObjectiveKind.Kill, Target = target, Count = 3 } } };

    [Test]
    public void MatchingTarget_True()
        => Assert.That(GuildSystem.QuestTargetsNpc(GuildWithKillQuest(5), 5), Is.True);

    [Test]
    public void NonMatchingTarget_False()
        => Assert.That(GuildSystem.QuestTargetsNpc(GuildWithKillQuest(5), 6), Is.False);

    [Test]
    public void WildcardTarget_MatchesAny()
        => Assert.That(GuildSystem.QuestTargetsNpc(GuildWithKillQuest(0), 99), Is.True);

    [Test]
    public void NoGuild_False()
        => Assert.That(GuildSystem.QuestTargetsNpc(null, 5), Is.False);

    [Test]
    public void NoActiveQuest_False()
        => Assert.That(GuildSystem.QuestTargetsNpc(new GuildRecord(), 5), Is.False);
}
