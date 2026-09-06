using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.Accounts;

/// <summary>The access-gate rule: Monitor+ (any admin access) cannot engage in PvP on either side.
/// <see cref="CombatSystem.GetPvpBlock"/> is the authority; this locks the threshold at Monitor and
/// confirms two ordinary players may still fight.</summary>
[TestFixture]
public class AccessGateTests
{
    // Two clean, level-10 players on a normal (non-safe) map. GetPvpBlock reads only PlayerManager +
    // GameWorld, so the other CombatSystem deps are never touched here and pass as null.
    private static CombatSystem Build(out PlayerManager pm)
    {
        var world = new GameWorld();
        pm = new PlayerManager();
        for (int i = 1; i <= 2; i++)
        {
            pm[i].CharNum = 1;            // Char resolves to Chars[CharNum]; Chars[1] is the initialized slot
            var c = pm[i].Char;
            c.Map = 1;
            c.Level = 10;
            c.Access = AdminLevel.Player;
        }
        return new CombatSystem(world, pm, dispatcher: null!, items: null!, movement: null!, joinLeave: null!, blood: null!, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    [Test]
    public void TwoPlayers_PvpAllowed()
    {
        var combat = Build(out _);
        Assert.That(combat.GetPvpBlock(1, 2), Is.EqualTo(PvpBlock.None));
    }

    [TestCase(AdminLevel.Monitor)]
    [TestCase(AdminLevel.Mapper)]
    [TestCase(AdminLevel.Creator)]
    public void AdminAttacker_Blocked(AdminLevel access)
    {
        var combat = Build(out var pm);
        pm[1].Char.Access = access;
        Assert.That(combat.GetPvpBlock(1, 2), Is.EqualTo(PvpBlock.AttackerAdmin));
    }

    [TestCase(AdminLevel.Monitor)]
    [TestCase(AdminLevel.Mapper)]
    [TestCase(AdminLevel.Creator)]
    public void AdminVictim_Blocked(AdminLevel access)
    {
        var combat = Build(out var pm);
        pm[2].Char.Access = access;
        Assert.That(combat.GetPvpBlock(1, 2), Is.EqualTo(PvpBlock.VictimAdmin));
    }
}
