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
/// Who a kill counts for on a QUEST — player objectives, guild objectives, and the valor rolled for advancing
/// one. A narrower question than who earns EXP from it, and decoupled from it entirely.
///
/// <para>🔴 EXP divides by damage share, so a token hit earns a token amount and gains nothing by it. An
/// objective tick does NOT divide — it is the same size however little was done for it — so without a floor,
/// one point of damage on a mob somebody else killed is a full tick and tagging is the fastest way to quest.
/// The floor is <see cref="Constants.QuestCreditDamagePercent"/>.</para>
///
/// <para>A PARTY PARTNER of someone who clears it shares that credit on ONE damaging blow, so a pair splits a
/// qualifying effort — but both of them hit the mob, and the pair still puts a real share in between them.
/// Every class deals damage off the same stat spread, so a blow is a bar any build can clear.</para>
/// </summary>
[TestFixture]
public class QuestCreditTests
{
    private const int Map = 1;
    private const int Slot = 1;

    private static (CombatSystem Combat, GameWorld World, PlayerManager Pm) NewCombat()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return (combat, world, pm);
    }

    private static void Register(GameWorld world, PlayerManager pm, int index, int mapNum = Map)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = $"player{index}";
        sp.Char.Map = mapNum;
        world.MapObservers[mapNum].Add(index);
    }

    private static void Party(PlayerManager pm, int a, int b)
    {
        pm[a].InParty = true;
        pm[a].PartyPlayer = b;
        pm[b].InParty = true;
        pm[b].PartyPlayer = a;
    }

    // The damage ledger for one kill, and who the kernel is told about. A player with 0 damage is absent from
    // the contributor set, which is where the one-blow requirement lives.
    private static HashSet<int> CreditFor(CombatSystem combat, GameWorld world, params (int Index, int Damage)[] hits)
    {
        var mn = world.MapNpcs[Map, Slot];
        mn.Num = 1;
        long total = 0;
        var contributors = new HashSet<int>();
        foreach (var (index, damage) in hits)
        {
            mn.DamageByPlayer[index] = damage;
            total += damage;
            if (damage > 0) contributors.Add(index);
        }

        return combat.QuestCreditFor(mn, Map, total, contributors);
    }

    // ── The floor, unpartied ────────────────────────────────────────────────────

    [Test]
    public void AShareBelowTheFloor_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);

        // 11 of 100 — just under.
        var credit = CreditFor(combat, world, (1, 89), (2, 11));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1), "the player who did the work");
            Assert.That(credit, Does.Not.Contain(2), "and not the one who tagged it");
        });
    }

    [Test]
    public void AShareAtTheFloor_Earns()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);

        var credit = CreditFor(combat, world, (1, 88), (2, Constants.QuestCreditDamagePercent));

        Assert.That(credit, Does.Contain(2), "exactly the floor is enough — the boundary belongs to the player");
    }

    /// <summary>The floor is chosen so a group genuinely sharing a mob all qualify. Eight is well past any
    /// party, so nobody working a mob together is cut out by it.</summary>
    [Test]
    public void EightWaysIsStillComfortable()
    {
        var (combat, world, pm) = NewCombat();
        var hits = new (int, int)[8];
        for (int i = 0; i < 8; i++)
        {
            Register(world, pm, i + 1);
            hits[i] = (i + 1, 125);        // 12.5% each
        }

        var credit = CreditFor(combat, world, hits);

        Assert.That(credit, Has.Count.EqualTo(8), "an even eight-way split leaves everyone above the floor");
    }

    [Test]
    public void ASoloKiller_Earns()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);

        Assert.That(CreditFor(combat, world, (1, 1)), Does.Contain(1), "all of one point of damage is all of it");
    }

    // ── The party split ─────────────────────────────────────────────────────────

    /// <summary>One partner carries the fight, the other lands a blow: the pair splits ONE qualifying effort.</summary>
    [Test]
    public void APartnerWhoLandedABlow_SharesCredit()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 99), (2, 1));   // 1% — nowhere near the floor

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1), "cleared the floor");
            Assert.That(credit, Does.Contain(2), "one blow is the partner's whole bar");
        });
    }

    /// <summary>🔴 The blow is not optional. A partner who watched earns nothing — no objective tick, no valor.</summary>
    [Test]
    public void APartnerWhoDealtNothing_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 100));   // player 2 never hit it

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Not.Contain(2), "standing next to a kill is not participating in it");
        });
    }

    /// <summary>Neither half of the pair clears the floor: the split has nothing to divide, so neither earns.</summary>
    [Test]
    public void APairThatNeitherClearsTheFloor_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        for (int i = 1; i <= 3; i++) Register(world, pm, i);
        Party(pm, 2, 3);   // 2 and 3 each tag it while 1 does the work

        var credit = CreditFor(combat, world, (1, 90), (2, 5), (3, 5));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Not.Contain(2), "a tag earns nothing");
            Assert.That(credit, Does.Not.Contain(3), "and two tags cannot be pooled into one qualifying share");
        });
    }

    [Test]
    public void APairThatBothClearTheFloor_BothEarn()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 50), (2, 50));

        Assert.That(credit, Is.EquivalentTo(new[] { 1, 2 }));
    }

    /// <summary>Partner reach rides on the contributor set, which already requires observing the map the kill
    /// happened on — the same test the partner-kill EXP bonus applies. A partner who is not there never lands
    /// the blow the share is gated on.</summary>
    [Test]
    public void APartnerOutOfReach_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2, mapNum: Map + 4);   // elsewhere, and not observing this map
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 100));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Not.Contain(2));
        });
    }

    /// <summary>Credit cannot be laundered outward: a partner shares from someone who CLEARED the floor, never
    /// from someone who was themselves only sharing.</summary>
    [Test]
    public void ASharedCredit_DoesNotChainOnward()
    {
        var (combat, world, pm) = NewCombat();
        for (int i = 1; i <= 3; i++) Register(world, pm, i);
        Party(pm, 1, 2);   // 1 clears the floor, 2 shares off it
        pm[3].InParty = true;
        pm[3].PartyPlayer = 2;   // 3 points at 2, who only ever shared

        var credit = CreditFor(combat, world, (1, 98), (2, 1), (3, 1));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Contain(2), "shares off the player who cleared it");
            Assert.That(credit, Does.Not.Contain(3), "and that share is not itself shareable");
        });
    }

    private sealed class NoOpDispatcher : IPacketDispatcher
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
