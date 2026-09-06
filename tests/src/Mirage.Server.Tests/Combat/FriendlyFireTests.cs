using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>Friendly-fire gating (party + guild). Partymates and guildmates cannot harm each other
/// through the two player→player damage gates, with ONE asymmetry: party protection is unconditional,
/// while guild protection is lifted whenever either player is on an Arena-moral map (a stakes-free duel
/// zone). <see cref="CombatSystem.GetFriendlyRelation"/> is the single shared classifier both the melee
/// gate (<see cref="CombatSystem.CanAttackPlayer"/>) and the spell sub-spell gate route through; each
/// gate picks its own party/guild rejection message.</summary>
[TestFixture]
public class FriendlyFireTests
{
    // Two clean, level-10, connected players on normal map 1. GetFriendlyRelation reads only
    // PlayerManager + GameWorld.MoralOf, and CanAttackPlayer additionally needs the dispatcher for its
    // rejection message — so every other CombatSystem dep passes as null (mirrors AccessGateTests).
    private static CombatSystem Build(out PlayerManager pm, out GameWorld world, out CapturingDispatcher dispatcher)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        dispatcher = new CapturingDispatcher();
        for (int i = 1; i <= 2; i++)
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            var c = sp.Char;
            c.Map = 1;
            c.Level = 10;
            c.Access = AdminLevel.Player;
            c.MaxHp = 100;
            c.Hp = 100;
        }
        return new CombatSystem(world, pm, dispatcher, items: null!, movement: null!, joinLeave: null!,
            blood: null!, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    private static void MakeArena(GameWorld world, int mapNum) => world.Maps[mapNum].Moral = MapMoral.Arena;

    private static void FormParty(PlayerManager pm)
    {
        pm[1].InParty = true;
        pm[1].PartyPlayer = 2;
        pm[2].InParty = true;
        pm[2].PartyPlayer = 1;
    }

    // Attacker (1) at (8,6) facing Down onto the adjacent victim (2) at (8,7) on the same map, both
    // observed — the geometry CanAttackPlayer's adjacency gate accepts (from CorpseImmunityTests).
    private static void PlaceAdjacent(GameWorld world, PlayerManager pm)
    {
        pm[1].Char.X = 8;
        pm[1].Char.Y = 6;
        pm[1].Char.Dir = Direction.Down;
        pm[2].Char.X = 8;
        pm[2].Char.Y = 7;
        world.MapObservers[1].Add(1);
        world.MapObservers[1].Add(2);
    }

    // ── Classification: GetFriendlyRelation ────────────────────────────────────

    [Test]
    public void Guildmates_AreGuild()
    {
        var combat = Build(out var pm, out _, out _);
        pm[1].Guild = pm[2].Guild = 7;
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.Guild));
    }

    [Test]
    public void Partymates_AreParty()
    {
        var combat = Build(out var pm, out _, out _);
        FormParty(pm);
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.Party));
    }

    [Test]
    public void DifferentGuilds_AreNone()
    {
        var combat = Build(out var pm, out _, out _);
        pm[1].Guild = 7;
        pm[2].Guild = 8;
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.None));
    }

    [Test]
    public void BothGuildless_AreNone()   // the != 0 guard: 0 == 0 must NOT protect
    {
        var combat = Build(out var pm, out _, out _);
        pm[1].Guild = 0;
        pm[2].Guild = 0;
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.None));
    }

    [Test]
    public void Guildmates_OnArena_AreNone()   // guild carve-out
    {
        var combat = Build(out var pm, out var world, out _);
        pm[1].Guild = pm[2].Guild = 7;
        MakeArena(world, 1);
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.None));
    }

    [Test]
    public void Partymates_OnArena_StillParty()   // party has NO arena exception
    {
        var combat = Build(out var pm, out var world, out _);
        FormParty(pm);
        MakeArena(world, 1);
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.Party));
    }

    [Test]
    public void Guildmates_OneSideOnArena_AreNone()   // either side arena lifts guild (locks the && semantics)
    {
        var combat = Build(out var pm, out var world, out _);
        pm[1].Guild = pm[2].Guild = 7;
        pm[2].Char.Map = 2;          // victim off the arena map...
        MakeArena(world, 1);         // ...attacker still on it
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.None));
    }

    [Test]
    public void BothPartyAndGuild_AreParty()   // party wins (drives the party message)
    {
        var combat = Build(out var pm, out _, out _);
        FormParty(pm);
        pm[1].Guild = pm[2].Guild = 7;
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.Party));
    }

    [Test]
    public void BothPartyAndGuild_OnArena_StillParty()   // party precedence unaffected by the guild carve-out
    {
        var combat = Build(out var pm, out var world, out _);
        FormParty(pm);
        pm[1].Guild = pm[2].Guild = 7;
        MakeArena(world, 1);
        Assert.That(combat.GetFriendlyRelation(1, 2), Is.EqualTo(FriendlyRelation.Party));
    }

    // ── Message correctness through the real melee gate (CanAttackPlayer) ───────

    [Test]
    public void Melee_Guildmate_Blocked_WithGuildMessage()
    {
        var combat = Build(out var pm, out var world, out var dispatcher);
        pm[1].Guild = pm[2].Guild = 7;
        PlaceAdjacent(world, pm);

        Assert.Multiple(() =>
        {
            Assert.That(combat.CanAttackPlayer(1, 2), Is.False);
            Assert.That(dispatcher.Chats.Exists(c => c.Index == 1 && c.Key == ServerStrings.CombatSystem_CannotAttackGuild), Is.True,
                "a guildmate must be blocked with the GUILD message, not the party one");
        });
    }

    [Test]
    public void Melee_Partymate_Blocked_WithPartyMessage()
    {
        var combat = Build(out var pm, out var world, out var dispatcher);
        FormParty(pm);
        PlaceAdjacent(world, pm);

        Assert.Multiple(() =>
        {
            Assert.That(combat.CanAttackPlayer(1, 2), Is.False);
            Assert.That(dispatcher.Chats.Exists(c => c.Index == 1 && c.Key == ServerStrings.CombatSystem_CannotAttackParty), Is.True);
        });
    }

    [Test]
    public void Melee_GuildmatesOnArena_Allowed()
    {
        var combat = Build(out var pm, out var world, out var dispatcher);
        pm[1].Guild = pm[2].Guild = 7;
        MakeArena(world, 1);
        PlaceAdjacent(world, pm);

        Assert.Multiple(() =>
        {
            Assert.That(combat.CanAttackPlayer(1, 2), Is.True, "guild protection is lifted in the arena");
            Assert.That(dispatcher.Chats.Exists(c => c.Key == ServerStrings.CombatSystem_CannotAttackGuild), Is.False,
                "no friendly-fire rejection when the arena lifts guild protection");
        });
    }

    // ── Dispatcher (per-file convention: copied from CorpseImmunityTests) ───────
    // SendLocalizedChatTo is virtual so CapturingDispatcher can record the rejection message.
    class NoOpDispatcher : IPacketDispatcher
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
        public virtual void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
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

    // Records the localized chat lines sent to each player so a test can assert the rejection message.
    sealed class CapturingDispatcher : NoOpDispatcher
    {
        public readonly List<(int Index, string Key)> Chats = new();
        public override void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args)
            => Chats.Add((index, key));
    }
}
