using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// How long a body is held off acting after it acts.
///
/// <para>🔴 <b>The seam took a number of seconds and threw it away.</b> Every cooldown, for every body
/// in every world, ran for the engine's own beat instead: a rule asking for two seconds got one, and a
/// rule asking for ten got one. Nothing reported it — the gate worked, it just always measured the
/// same length — so a game's pacing silently became the engine's.</para>
///
/// <para>And the weather half. The client already draws the bar twice as long in a gale; the server did
/// not measure it that way, so the bar emptied while the body was still waiting.</para>
/// </summary>
[TestFixture]
public class ActionCooldownTests
{
    private const int Map = 1, Slot = 1, Kind = 1, Index = 1;

    private static (ServerWorld Seam, GameWorld World) Build(WeatherType weather = WeatherType.Clear)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        world.Maps[Map] = new MapRecord(16, 12);
        world.Npcs[Kind].Name = "Adept";

        var body = world.MapNpcs[Map, Slot];
        body.Num = Kind;
        body.X = 5;
        body.Y = 5;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 6;
        sp.Char.Y = 5;

        world.Weather = weather;

        var seam = new ServerWorld(world, pm, new AttributeSystem(world, pm, dispatcher),
                                   deaths: null!, movement: null!, items: null!, joinLeave: null!,
                                   decals: null!, ai: null!, guilds: null!, dispatcher: dispatcher);
        return (seam, world);
    }

    private static EntityHandle TheCreature => EntityHandle.ForNpc(Map, Slot);
    private static EntityHandle ThePlayer => EntityHandle.ForPlayer(Index);

    /// <summary>A rule that asks for longer than the engine's beat gets it. One second is the default
    /// and would pass whatever the seam did, so the case that matters is the one that differs.</summary>
    [Test]
    public void ACooldownRunsForAsLongAsTheGameAsked()
    {
        var (seam, _) = Build();

        seam.SetActionCooldown(TheCreature, 5);

        Assert.That(seam.IsWaiting(TheCreature), Is.True,
            "five seconds must not expire inside the engine's one-second beat");
    }

    [Test]
    public void APlayersCooldownRunsForAsLongToo()
    {
        var (seam, _) = Build();

        seam.SetActionCooldown(ThePlayer, 5);

        Assert.That(seam.IsWaiting(ThePlayer), Is.True);
    }

    /// <summary>Clearing it clears the length with it, so the next stamp does not inherit the last
    /// rule's answer.</summary>
    [Test]
    public void ClearingItClearsTheLength()
    {
        var (seam, _) = Build();

        seam.SetActionCooldown(TheCreature, 5);
        seam.SetActionCooldown(TheCreature, 0);

        Assert.That(seam.IsWaiting(TheCreature), Is.False);
    }

    /// <summary>A body nothing has held is not waiting.</summary>
    [Test]
    public void AnUntouchedBodyIsNotWaiting()
    {
        var (seam, _) = Build();

        Assert.That(seam.IsWaiting(TheCreature), Is.False);
    }

    /// <summary>A gale stretches every beat, which is what the client has always drawn.</summary>
    [Test]
    public void AGaleStretchesTheBeat()
    {
        var (calm, _) = Build();
        var (gale, _) = Build(WeatherType.HeavyWind);

        // Stamp both, then wind the clock past the plain beat but not past a doubled one.
        calm.SetActionCooldown(TheCreature, 1);
        gale.SetActionCooldown(TheCreature, 1);
        Thread.Sleep((int)Constants.NpcAttackCooldownMs + 120);

        Assert.Multiple(() =>
        {
            Assert.That(calm.IsWaiting(TheCreature), Is.False, "the plain beat has passed");
            Assert.That(gale.IsWaiting(TheCreature), Is.True, "and the gale's has not");
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
