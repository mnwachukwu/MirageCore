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

namespace Mirage.Server.Tests.World;

/// <summary>
/// Crossing a map boundary speaks about the map being LEFT before the map being JOINED — every line,
/// whichever lines they happen to be.
///
/// <para>🔴 The rule is a property of the CROSSING, not of any one pair of lines. The departure half and
/// the arrival half bracket the position update, so anything a game adds to either half is in sequence
/// by construction rather than by whoever wrote it last remembering the order. This once failed the
/// other way: two independent blocks each decided whether to speak, and the one that ran first was not
/// the one that belonged first.</para>
///
/// <para>Core speaks one line on each side — the map's own LeaveSay and JoinSay. A game that adds more
/// adds them to the same two halves.</para>
/// </summary>
[TestFixture]
public class MapTransitionMessageOrderTests
{
    const int From = 1, To = 2, Idx = 1;

    /// <summary>A player standing on <see cref="From"/>, ready to walk to <see cref="To"/>. Both maps carry
    /// a greeting so the transition speaks one on each side of the crossing.</summary>
    static (MovementSystem Move, CapturingDispatcher Chat, GameWorld World) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var chat = new CapturingDispatcher();
        var move = new MovementSystem(world, pm, chat);

        Dress(world.Maps[From], "Gatekeeper", "Welcome to the first map.", "Farewell from the first map.");
        Dress(world.Maps[To], "Warden", "Welcome to the second map.", "Farewell from the second map.");

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = From;
        p.X = 5;
        p.Y = 5;
        world.MapObservers[From].Add(Idx);
        return (move, chat, world);
    }

    static void Dress(MapRecord map, string speaker, string join, string leave)
    {
        map.GreetingSpeaker = speaker;
        map.JoinSay = join;
        map.LeaveSay = leave;
    }

    [Test]
    public void ACrossingFinishesWithTheMapBeingLeftBeforeItStartsOnTheOneBeingJoined()
    {
        var (move, chat, _) = Setup();

        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.EqualTo(new[]
        {
            ServerStrings.MapGreeting_LeaveSay,
            ServerStrings.MapGreeting_JoinSay,
        }));
    }

    /// <summary>Two rooms of one building share a greeting, so stepping between them is not entering or
    /// leaving anything and says nothing at all. Without this a corridor would announce itself at every
    /// doorway.</summary>
    [Test]
    public void ACrossingThatChangesNothingSaysNothing()
    {
        var (move, chat, world) = Setup();
        Dress(world.Maps[To], "Gatekeeper", "Welcome to the first map.", "Farewell from the first map.");

        move.PlayerWarp(Idx, To, 5, 5);

        Assert.That(chat.Keys, Is.Empty);
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
