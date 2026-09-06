using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Mirage.Server.Tests.World;

/// <summary>
/// Crossing a map boundary speaks about the map being LEFT before the map being JOINED — every line,
/// whichever lines they happen to be.
///
/// <para>🔴 An Arena→Safe step said "You are entering a safe zone." and then "You exit the arena.",
/// because the safe branch and the arena branch were two blocks and the safe one ran first. Both halves
/// are now split out of the crossing (<c>AnnounceLeavingZone</c> / <c>AnnounceEnteringZone</c>) and
/// bracket the position update, so the split — not the order two branches happen to sit in — is what
/// keeps the lines in sequence.</para>
///
/// <para>The map's own JoinSay/LeaveSay greeting obeys the same rule, so the whole departure (greeting +
/// zone rules) lands before any of the arrival.</para>
/// </summary>
[TestFixture]
public class MapTransitionMessageOrderTests
{
    const int From = 1, To = 2, Idx = 1;

    // Every line that belongs to the map being left, and every line that belongs to the map being joined.
    static readonly string[] Departure =
    [
        ServerStrings.MapGreeting_LeaveSay,
        ServerStrings.MovementSystem_LeaveSafeBase,
        ServerStrings.MovementSystem_LeaveSafeNonPk,
        ServerStrings.MovementSystem_LeaveArena,
    ];

    static readonly string[] Arrival =
    [
        ServerStrings.MapGreeting_JoinSay,
        ServerStrings.MovementSystem_EnterSafeBase,
        ServerStrings.MovementSystem_EnterSafePk,
        ServerStrings.MovementSystem_EnterSafeNonPk,
        ServerStrings.MovementSystem_EnterArenaBase,
        ServerStrings.MovementSystem_EnterArenaPvp,
    ];

    /// <summary>A player standing on <see cref="From"/>, ready to walk to <see cref="To"/>. Both maps carry
    /// a greeting so the transition speaks one on each side of the crossing.</summary>
    static (MovementSystem move, CapturingDispatcher chat) Setup(MapMoral from, MapMoral to, bool isPk, int level = 20)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var chat = new CapturingDispatcher();
        var move = new MovementSystem(world, pm, chat, new BloodSystem(world, chat));

        Dress(world.Maps[From], from, "Gatekeeper", "Welcome to the first map.", "Farewell from the first map.");
        Dress(world.Maps[To], to, "Warden", "Welcome to the second map.", "Farewell from the second map.");

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = From;
        p.X = 5;
        p.Y = 5;
        p.Level = level;
        p.MaxHp = 100;
        p.Hp = 100;                                        // full HP => no blood trail during the move
        p.PkExpiryUtc = isPk ? long.MaxValue : 0;
        world.MapObservers[From].Add(Idx);
        return (move, chat);
    }

    static void Dress(MapRecord map, MapMoral moral, string speaker, string join, string leave)
    {
        map.Moral = moral;
        map.GreetingSpeaker = speaker;
        map.JoinSay = join;
        map.LeaveSay = leave;
    }

    // ── The reported case ────────────────────────────────────────────────────

    [Test]
    public void ArenaToSafe_ExitsTheArenaBeforeEnteringTheSafeZone()
    {
        var (move, chat) = Setup(MapMoral.Arena, MapMoral.Safe, isPk: false);
        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.EqualTo(new[]
        {
            ServerStrings.MapGreeting_LeaveSay,
            ServerStrings.MovementSystem_LeaveArena,
            ServerStrings.MapGreeting_JoinSay,
            ServerStrings.MovementSystem_EnterSafeBase,
            ServerStrings.MovementSystem_EnterSafeNonPk,
        }));
    }

    [Test]
    public void SafeToArena_LeavesTheSafeZoneBeforeEnteringTheArena()
    {
        var (move, chat) = Setup(MapMoral.Safe, MapMoral.Arena, isPk: false);
        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.EqualTo(new[]
        {
            ServerStrings.MapGreeting_LeaveSay,
            ServerStrings.MovementSystem_LeaveSafeBase,
            ServerStrings.MovementSystem_LeaveSafeNonPk,
            ServerStrings.MapGreeting_JoinSay,
            ServerStrings.MovementSystem_EnterArenaBase,
            ServerStrings.MovementSystem_EnterArenaPvp,
        }));
    }

    // ── The rule itself, over every crossing that says anything ──────────────

    [Test]
    public void NoArrivalLineEverPrecedesADepartureLine(
        [Values(MapMoral.None, MapMoral.Safe, MapMoral.Arena)] MapMoral from,
        [Values(MapMoral.None, MapMoral.Safe, MapMoral.Arena)] MapMoral to,
        [Values(true, false)] bool isPk)
    {
        var (move, chat) = Setup(from, to, isPk);
        move.PlayerWarp(Idx, To, 5, 5);

        int lastDeparture = chat.Keys.FindLastIndex(Departure.Contains);
        int firstArrival = chat.Keys.FindIndex(Arrival.Contains);
        if (lastDeparture < 0 || firstArrival < 0) return;   // a crossing with only one half to say

        Assert.That(lastDeparture, Is.LessThan(firstArrival),
            $"{from}->{to} spoke about the map being joined before it finished with the map being left: "
            + string.Join(", ", chat.Keys));
    }

    /// <summary>A crossing that changes nothing about the zone still speaks the greeting in order, and
    /// says nothing about zone rules.</summary>
    [Test]
    public void SameMoral_SpeaksOnlyTheGreetings()
    {
        var (move, chat) = Setup(MapMoral.Safe, MapMoral.Safe, isPk: false);
        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.EqualTo(new[]
        {
            ServerStrings.MapGreeting_LeaveSay,
            ServerStrings.MapGreeting_JoinSay,
        }));
    }

    /// <summary>Under level 10 a player is outside PvP entirely, so only the base lines are spoken — and
    /// the order still holds with the follow-up lines absent.</summary>
    [Test]
    public void BelowLevelTen_SkipsThePvpNotesAndKeepsTheOrder()
    {
        var (move, chat) = Setup(MapMoral.Arena, MapMoral.Safe, isPk: false, level: 9);
        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.EqualTo(new[]
        {
            ServerStrings.MapGreeting_LeaveSay,
            ServerStrings.MovementSystem_LeaveArena,
            ServerStrings.MapGreeting_JoinSay,
            ServerStrings.MovementSystem_EnterSafeBase,
        }));
    }

    // Records the localized chat lines sent to the player, in the order they were sent.
    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<string> Keys = new();

        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args)
        {
            if (index == Idx) Keys.Add(key);
        }

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
