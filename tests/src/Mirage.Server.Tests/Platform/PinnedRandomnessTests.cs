using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Rolled outcomes, asserted exactly. These are the rules players argue about — who won the loot, which
/// stat a death cost them, where a mob appeared — and before <see cref="IRandomSource"/> existed none of
/// them could be pinned, only sampled.
///
/// <para>Each test feeds a scripted sequence and asserts the resulting decision, then (where it matters)
/// feeds a DIFFERENT sequence and asserts a different decision — so a test cannot pass against a
/// hard-coded return value. The scripted source throws when over-consumed, so a path that draws more
/// randomness than the test accounted for fails loudly rather than silently reverting to real chance.</para>
/// </summary>
[TestFixture]
public class PinnedRandomnessTests
{
    // ── Scripted source ───────────────────────────────────────────────────────

    /// <summary>Returns the given values in order for the bounded Next overloads. Values are used
    /// verbatim when in range, so a test reads as "this roll happened", not "this seed happened".</summary>
    sealed class Rolls : IRandomSource
    {
        private readonly int[] _v;
        private int _i;
        public Rolls(params int[] v) => _v = v;
        public int Consumed => _i;

        public int Next(int maxExclusive)
        {
            int r = Take();
            Assert.That(r, Is.InRange(0, maxExclusive - 1), $"scripted roll {r} is out of range for Next({maxExclusive})");
            return r;
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            int r = Take();
            Assert.That(r, Is.InRange(minInclusive, maxExclusive - 1),
                        $"scripted roll {r} is out of range for Next({minInclusive},{maxExclusive})");
            return r;
        }

        public long NextInt64(long minInclusive, long maxExclusive) => Take();
        public double NextDouble() => Take() / 100.0;

        private int Take()
        {
            if (_i >= _v.Length)
            {
                throw new InvalidOperationException(
                $"the path drew more randomness than the test scripted ({_v.Length} value(s))");
            }

            return _v[_i++];
        }
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    // CombatSystem with only the collaborators these paths touch; the rest stay null! on purpose so a
    // future edit that reaches for one fails the test instead of passing quietly.
    static CombatSystem Combat(IRandomSource rng, GameWorld? world = null, PlayerManager? pm = null) =>
        new(world ?? new GameWorld(), pm ?? new PlayerManager(), new NoOpDispatcher(),
            items: null!, movement: null!, joinLeave: null!, blood: null!,
            objectives: null!, guilds: null!, guildWar: null!, territory: null!, rng: rng);

    static T Invoke<T>(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(target.GetType().Name, method);
        return (T)m.Invoke(target, args)!;
    }

    // ── Loot rolls ────────────────────────────────────────────────────────────

    // Every contributor rolls 1..LootRollSides; the highest wins. Pinning the rolls pins the winner —
    // the single most contested outcome in the game.
    [Test]
    public void LootRoll_HighestScriptedRollWins()
    {
        var combat = Combat(new Rolls(12, 47, 3));           // player 7 rolls highest
        var contributors = new List<int> { 5, 7, 9 };

        var args = new object?[] { contributors, null };
        int winner = Invoke<int>(combat, "ResolveLootRoll", args);

        Assert.That(winner, Is.EqualTo(7), "the highest roll must take the loot");

        var finalRolls = (List<(int player, int roll)>)args[1]!;
        Assert.That(finalRolls, Is.EquivalentTo(new[] { (5, 12), (7, 47), (9, 3) }),
                    "each contributor's recorded roll must be the one that was rolled");
    }

    // Same three contributors, different script — a different player must win, so the test above is not
    // passing against a fixed winner or list ordering.
    [Test]
    public void LootRoll_WinnerFollowsTheScript_NotTheOrdering()
    {
        var combat = Combat(new Rolls(50, 2, 9));            // now the FIRST player rolls highest
        var args = new object?[] { new List<int> { 5, 7, 9 }, null };
        Assert.That(Invoke<int>(combat, "ResolveLootRoll", args), Is.EqualTo(5));
    }

    // A tie re-rolls only the tied players, and only until one is ahead. Three players tie at 40, then
    // the two survivors re-roll — the second round decides. Locks the round structure, not just the winner.
    [Test]
    public void LootRoll_TieRerollsOnlyTheTiedPlayers()
    {
        // Round 1: 5→40, 7→40, 9→10  (5 and 7 tie for best)
        // Round 2: 5→8,  7→30         (only the two tied players roll again)
        var rng = new Rolls(40, 40, 10, 8, 30);
        var combat = Combat(rng);
        var args = new object?[] { new List<int> { 5, 7, 9 }, null };

        Assert.That(Invoke<int>(combat, "ResolveLootRoll", args), Is.EqualTo(7),
                    "the tie-break round decides between the tied players only");
        Assert.That(rng.Consumed, Is.EqualTo(5),
                    "three rolls in round one plus two in the tie-break — the eliminated player must not re-roll");
    }

    [Test]
    public void LootRoll_SingleContributorTakesItWithoutRolling()
    {
        var rng = new Rolls();                               // no rolls scripted: none may be drawn
        var combat = Combat(rng);
        var args = new object?[] { new List<int> { 4 }, null };

        Assert.That(Invoke<int>(combat, "ResolveLootRoll", args), Is.EqualTo(4));
        Assert.That(rng.Consumed, Is.Zero, "an uncontested drop must not roll at all");
    }

    // ── Death stat drain ──────────────────────────────────────────────────────

    // A de-level drains `count` points across the stats the player still has. The roll picks an index
    // into the CURRENTLY drainable set, so the mapping shifts as stats hit zero — which is exactly the
    // behavior that was impossible to assert before.
    [Test]
    public void StatDrain_DrainsTheScriptedStat()
    {
        var pm = new PlayerManager();
        var combat = Combat(new Rolls(1), pm: pm);           // index 1 = Def, of {Str,Def,Int,Spd}
        pm[1].CharNum = 1;                                   // Char resolves Chars[CharNum]; 0 is not a slot
        var p = pm[1].Char;
        (p.Str, p.Def, p.Int, p.Spd) = (10, 10, 10, 10);

        Invoke<string>(combat, "DrainRandomStats", 1, p, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Def, Is.EqualTo(9), "the scripted index selected Def");
            Assert.That(p.Str, Is.EqualTo(10));
            Assert.That(p.Int, Is.EqualTo(10));
            Assert.That(p.Spd, Is.EqualTo(10));
        });
    }

    // With Str and Int already at zero the drainable set is {Def, Spd}, so index 1 now means SPD, not
    // Int. The roll indexes the live set, not a fixed stat order.
    [Test]
    public void StatDrain_RollIndexesTheDrainableSet_NotAFixedStatOrder()
    {
        var pm = new PlayerManager();
        var combat = Combat(new Rolls(1), pm: pm);
        pm[1].CharNum = 1;                                   // Char resolves Chars[CharNum]; 0 is not a slot
        var p = pm[1].Char;
        (p.Str, p.Def, p.Int, p.Spd) = (0, 5, 0, 5);

        Invoke<string>(combat, "DrainRandomStats", 1, p, 1);

        Assert.That(p.Spd, Is.EqualTo(4), "with Str and Int at zero, index 1 of {Def,Spd} is Spd");
        Assert.That(p.Def, Is.EqualTo(5));
    }

    // A stat at zero can never be selected, and a player with nothing left to lose loses nothing —
    // the loop breaks rather than driving a stat negative.
    [Test]
    public void StatDrain_StopsWhenNothingIsDrainable()
    {
        var pm = new PlayerManager();
        var rng = new Rolls();                               // must not draw: nothing is drainable
        var combat = Combat(rng, pm: pm);
        pm[1].CharNum = 1;                                   // Char resolves Chars[CharNum]; 0 is not a slot
        var p = pm[1].Char;
        (p.Str, p.Def, p.Int, p.Spd) = (0, 0, 0, 0);

        Invoke<string>(combat, "DrainRandomStats", 1, p, 3);

        Assert.Multiple(() =>
        {
            Assert.That(p.Str, Is.Zero);
            Assert.That(p.Def, Is.Zero);
            Assert.That(p.Int, Is.Zero);
            Assert.That(p.Spd, Is.Zero);
            Assert.That(rng.Consumed, Is.Zero, "with nothing drainable the loop must break before rolling");
        });
    }

    // Draining several points consumes one roll each and can hit the same stat twice.
    [Test]
    public void StatDrain_MultiplePointsConsumeOneRollEach()
    {
        var pm = new PlayerManager();
        var rng = new Rolls(0, 0, 3);                        // Str, Str, Spd
        var combat = Combat(rng, pm: pm);
        pm[1].CharNum = 1;                                   // Char resolves Chars[CharNum]; 0 is not a slot
        var p = pm[1].Char;
        (p.Str, p.Def, p.Int, p.Spd) = (10, 10, 10, 10);

        Invoke<string>(combat, "DrainRandomStats", 1, p, 3);

        Assert.Multiple(() =>
        {
            Assert.That(p.Str, Is.EqualTo(8), "two of the three points landed on Str");
            Assert.That(p.Spd, Is.EqualTo(9));
            Assert.That(p.Def, Is.EqualTo(10));
            Assert.That(p.Int, Is.EqualTo(10));
            Assert.That(rng.Consumed, Is.EqualTo(3), "one roll per drained point");
        });
    }

    // ── NPC spawn placement ───────────────────────────────────────────────────

    // Spawn placement rolls an anchor tile, clamped so an oversize footprint cannot straddle the map
    // edge. Pinning the roll pins where the NPC appears.
    [Test]
    public void SpawnPlacement_UsesTheScriptedAnchorTile()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        const int Map = 1, Slot = 1;

        // An all-walkable map so the first scripted anchor is accepted.
        var map = world.Maps[Map];
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
                map.EditTile(x, y, t => t with { Type = TileType.Walkable });
        }

        // An UNPINNED entry — a pinned one would spawn at its tile and never roll (see
        // NpcSpawnPlacementTests). Entry [0] drives spawn post 1.
        var npc = world.Npcs[1];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
        map.Npcs.Add(new MapNpcEntry(1, PinX: null, PinY: null));

        // SpawnNpc draws the facing FIRST (Next(NumDirections)), then the x and y anchors — so the
        // script has to account for all three. That ordering is itself worth pinning: a future edit
        // that adds or reorders a draw here silently shifts every spawn position, and this test is
        // what would catch it.
        var rng = new Rolls((int)Direction.Left, 6, 4);
        var spawn = new SpawnSystem(world, pm, new NoOpDispatcher(), rng: rng);
        spawn.SpawnNpc(Slot, Map);

        var mn = world.MapNpcs[Map, Slot];
        Assert.Multiple(() =>
        {
            Assert.That(mn.Dir, Is.EqualTo(Direction.Left), "the first draw is the spawn facing");
            Assert.That(mn.X, Is.EqualTo(6), "the second draw is the x anchor");
            Assert.That(mn.Y, Is.EqualTo(4), "the third is the y anchor");
            Assert.That(rng.Consumed, Is.EqualTo(3), "facing plus one anchor pair — the first tile was clear");
        });
    }

    // ── Contest-point placement ───────────────────────────────────────────────

    // GuildTerritorySystem draws from the injected source like every other system rather than owning a
    // private `Random`, so a scripted roll picks a determined capture tile.
    [Test]
    public void ContestPointPlacement_UsesTheInjectedSource()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        const int Map = 3;

        var map = world.Maps[Map];
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
                map.EditTile(x, y, t => t with { Type = TileType.Walkable });
        }

        var rng = new Rolls(2);
        var territory = new GuildTerritorySystem(world, pm, new NoOpDispatcher(),
            guilds: null!, spawn: null!, persistence: null!, bg: null!,
            NullLogger<GuildTerritorySystem>.Instance, rng: rng);

        var args = new object?[] { Map, 0, 0 };
        bool picked = Invoke<bool>(territory, "TryPickWalkable", args);

        Assert.That(picked, Is.True, "an all-walkable map must yield a tile");
        Assert.That(rng.Consumed, Is.GreaterThan(0),
                    "placement must draw from the injected source, not a private Random field");
    }

    // ── No-op dispatcher ──────────────────────────────────────────────────────

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
