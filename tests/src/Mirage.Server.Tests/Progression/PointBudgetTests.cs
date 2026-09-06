using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Progression;

/// <summary>
/// The stat budget: a character of level L holds at most <see cref="Constants.PlayerBaseStatTotal"/> +
/// <see cref="Constants.PointsPerLevel"/>·(L−1) of stat value, counting spent stats and unspent points
/// alike.
///
/// <para>Nothing in the game states that rule out loud — it falls out of a class opening at the base total,
/// levelling granting points, and points being spent one for one. The account editor leans on it directly:
/// it is what lets the editor say a hand-typed character sheet is one the game could not have produced, and
/// what the server checks before applying an editor's character edit. So the pieces it rests on are pinned
/// here, from both ends — the arithmetic, and the two progression paths that move a character across it.</para>
/// </summary>
[TestFixture]
public class PointBudgetTests
{
    const int Idx = 1;
    const int Map = 1;
    const int ClassNum = 1;

    // ── The arithmetic ────────────────────────────────────────────────────────

    [Test]
    public void Budget_StartsAtTheClassAllotment_AndGrowsByThreePerLevel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.PointBudgetForLevel(1), Is.EqualTo(Constants.PlayerBaseStatTotal));
            Assert.That(StatFormulas.PointBudgetForLevel(2), Is.EqualTo(23));
            Assert.That(StatFormulas.PointBudgetForLevel(10), Is.EqualTo(47));
            Assert.That(StatFormulas.PointBudgetForLevel(Constants.MaxLevel),
                Is.EqualTo(Constants.PlayerBaseStatTotal + Constants.PointsPerLevel * (Constants.MaxLevel - 1)));
            Assert.That(StatFormulas.PointBudgetForLevel(0), Is.EqualTo(Constants.PlayerBaseStatTotal),
                "a level below one reads as level one rather than as a negative budget");
        });
    }

    [Test]
    public void PointsHeld_CountsUnspentPointsAlongsideTheFourStats()
    {
        Assert.That(StatFormulas.PointsHeld(10, 8, 4, 3, 6), Is.EqualTo(31));
    }

    [Test]
    public void TheBudgetIsAnInclusiveCeiling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StatFormulas.IsWithinPointBudget(10, 20, 15, 6, 6, 0), Is.True, "exactly at budget");
            Assert.That(StatFormulas.IsWithinPointBudget(10, 20, 15, 6, 6, 1), Is.False, "one point over");
            Assert.That(StatFormulas.IsWithinPointBudget(10, 5, 5, 5, 5, 0), Is.True,
                "a drained character sits under budget and is perfectly legitimate");
        });
    }

    [Test]
    public void VirtualLevel_InvertsTheBudget()
    {
        for (int level = 1; level <= 60; level++)
        {
            int budget = StatFormulas.PointBudgetForLevel(level);
            Assert.That(StatFormulas.NpcLevel(budget, 0, 0, 0), Is.EqualTo(level),
                $"a spread worth exactly level {level}'s budget reads back as level {level}");
        }
    }

    // ── The two paths that move a character across it ─────────────────────────

    [Test]
    public void LevellingUp_LandsExactlyOnTheBudget()
    {
        var (combat, p) = Setup();
        p.Exp = ExpFormulas.ExpFloorForLevel(25);
        combat.CheckPlayerLevelUp(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(p.Level, Is.EqualTo(25));
            Assert.That(StatFormulas.PointsHeld(p.Str, p.Def, p.Spd, p.Int, p.Points),
                Is.EqualTo(StatFormulas.PointBudgetForLevel(25)),
                "every point the climb granted is still on the sheet, unspent");
            Assert.That(StatFormulas.IsWithinPointBudget(p.Level, p.Str, p.Def, p.Spd, p.Int, p.Points), Is.True);
        });
    }

    [Test]
    public void SpendingPointsDoesNotChangeWhatIsHeld()
    {
        var (combat, p) = Setup();
        p.Exp = ExpFormulas.ExpFloorForLevel(12);
        combat.CheckPlayerLevelUp(Idx);
        int before = StatFormulas.PointsHeld(p.Str, p.Def, p.Spd, p.Int, p.Points);

        // What the stat-point handler does: one point out of the pool, one point onto a stat.
        p.Points -= 4;
        p.Str += 4;

        Assert.That(StatFormulas.PointsHeld(p.Str, p.Def, p.Spd, p.Int, p.Points), Is.EqualTo(before));
    }

    /// <summary>The death penalty pays a delevel out of unspent points first and drains random stats for
    /// the rest, so a character who has spent everything still comes out under the lower level's budget.
    /// This is the case the account editor deliberately will NOT reproduce — it refuses the row instead of
    /// choosing which stat to cut.</summary>
    [Test]
    public void Delevelling_LeavesTheCharacterUnderTheLowerBudget()
    {
        var (combat, p) = Setup();
        p.Level = 20;
        p.Exp = ExpFormulas.ExpFloorForLevel(20);
        // Everything spent: 20 base + 19 levels of three, all on stats.
        p.Str = 40; p.Def = 25; p.Spd = 6; p.Int = 6; p.Points = 0;
        Assume.That(StatFormulas.PointsHeld(p.Str, p.Def, p.Spd, p.Int, p.Points),
            Is.EqualTo(StatFormulas.PointBudgetForLevel(20)));

        combat.ApplyExpLoss(Idx, ExpFormulas.ExpFloorForLevel(20) - ExpFormulas.ExpFloorForLevel(15));

        Assert.Multiple(() =>
        {
            Assert.That(p.Level, Is.EqualTo(15));
            Assert.That(StatFormulas.IsWithinPointBudget(p.Level, p.Str, p.Def, p.Spd, p.Int, p.Points), Is.True);
        });
    }

    [Test]
    public void ADelevelWithAFullPoolTakesTheGrantBackOutOfPointsAlone()
    {
        var (combat, p) = Setup();
        p.Level = 20;
        p.Exp = ExpFormulas.ExpFloorForLevel(20);
        p.Str = 5; p.Def = 5; p.Spd = 5; p.Int = 5; p.Points = 57;

        combat.ApplyExpLoss(Idx, ExpFormulas.ExpFloorForLevel(20) - ExpFormulas.ExpFloorForLevel(18));

        Assert.Multiple(() =>
        {
            Assert.That(p.Level, Is.EqualTo(18));
            Assert.That(p.Points, Is.EqualTo(51), "two levels reclaimed from the pool");
            Assert.That(p.Str, Is.EqualTo(5), "no stat is drained while the pool can pay");
            Assert.That(p.Def, Is.EqualTo(5));
            Assert.That(p.Spd, Is.EqualTo(5));
            Assert.That(p.Int, Is.EqualTo(5));
        });
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    static (CombatSystem Combat, PlayerRecord Player) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var combat = new CombatSystem(world, pm, dispatcher, items, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!,
            config: new ServerConfig());

        var cls = world.Classes[ClassNum];
        cls.Name = "Warrior";
        cls.Str = 8; cls.Def = 6; cls.Spd = 3; cls.Int = 3;   // the base total, as every authored class is

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.Class = ClassNum;
        p.Level = 1;
        p.Str = cls.Str; p.Def = cls.Def; p.Spd = cls.Spd; p.Int = cls.Int;
        p.Points = 0;
        return (combat, p);
    }

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
