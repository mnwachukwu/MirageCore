using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// Whose midnight it is, and where a game keeps what belongs to the world.
///
/// <para>🔴 <b>Both are answers a game cannot work out for itself.</b> <c>Now</c> is UTC, so anything
/// that turns over at midnight — a daily settlement, a weekly tax, a season — lands on the wrong day
/// everywhere but Greenwich unless the engine says how far the server's own civil day is from it. And
/// a season number belongs to no body and no record, so without somewhere to put it a game either
/// invents one from the epoch or forgets it every restart.</para>
/// </summary>
[TestFixture]
public class WorldClockAndValuesTests
{
    private sealed class FixedClock : IClock
    {
        public long UtcNowUnix { get; set; }
        public DateTime LocalNow { get; set; }
    }

    /// <summary>A world with a clock a test moves by hand. Everything a body would need is null: nothing
    /// here asks about one.</summary>
    private static (IWorld World, GameWorld Game, FixedClock Clock) Build()
    {
        var game = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var clock = new FixedClock();

        var world = new ServerWorld(game, pm, attributes: null!, deaths: null!, movement: null!,
                                    items: null!, joinLeave: null!, decals: null!, ai: null!,
                                    guilds: null!, dispatcher, markers: null, clock: clock);

        return (world, game, clock);
    }

    /// <summary>Noon UTC on an arbitrary day, and the same instant read off a clock that many hours
    /// away.</summary>
    private static (IWorld World, GameWorld Game) At(int offsetHours)
    {
        var (world, game, clock) = Build();
        var utc = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        clock.UtcNowUnix = new DateTimeOffset(utc).ToUnixTimeSeconds();
        clock.LocalNow = utc.AddHours(offsetHours);

        return (world, game);
    }

    // ── Whose midnight ────────────────────────────────────────────────────────

    [Test]
    public void AtGreenwich_TheOffsetIsNothing() =>
        Assert.That(At(0).World.LocalOffset(), Is.Zero);

    [TestCase(2)]
    [TestCase(-5)]
    [TestCase(13)]
    public void EastOrWest_TheOffsetIsHowFarTheCivilDayIsFromUtc(int hours) =>
        Assert.That(At(hours).World.LocalOffset(), Is.EqualTo(hours * 3_600));

    /// <summary>⚠ Read off the clock's own two answers rather than off a zone, so a place that keeps
    /// summer time answers differently in July than in January without anything here knowing what summer
    /// time is — and a fixed clock in a test moves both halves together.</summary>
    [Test]
    public void TheOffsetFollowsTheClock_SoSummerTimeNeedsNoSpecialCase()
    {
        var (world, _, clock) = Build();
        var january = new DateTime(2026, 1, 14, 12, 0, 0, DateTimeKind.Utc);
        var july = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        clock.UtcNowUnix = new DateTimeOffset(january).ToUnixTimeSeconds();
        clock.LocalNow = january.AddHours(1);
        int winter = world.LocalOffset();

        clock.UtcNowUnix = new DateTimeOffset(july).ToUnixTimeSeconds();
        clock.LocalNow = july.AddHours(2);
        int summer = world.LocalOffset();

        Assert.Multiple(() =>
        {
            Assert.That(winter, Is.EqualTo(3_600));
            Assert.That(summer, Is.EqualTo(7_200));
        });
    }

    /// <summary>The point of the whole thing: added to <c>Now</c>, the day number is the server's own
    /// civil day rather than UTC's. Half past eleven at night in Greenwich is already tomorrow two hours
    /// east of it.</summary>
    [Test]
    public void AddedToNow_TheDayIsTheServersOwnDay()
    {
        var (world, _, clock) = Build();
        var lateEvening = new DateTime(2026, 6, 14, 23, 30, 0, DateTimeKind.Utc);

        clock.UtcNowUnix = new DateTimeOffset(lateEvening).ToUnixTimeSeconds();
        clock.LocalNow = lateEvening.AddHours(2);

        long utcDay = world.Now() / 86_400;
        long localDay = (world.Now() + world.LocalOffset()) / 86_400;

        Assert.That(localDay, Is.EqualTo(utcDay + 1));
    }

    // ── The world's own notebook ──────────────────────────────────────────────

    [Test]
    public void AWorldNobodyHasWrittenTo_IsEmpty() =>
        Assert.That(At(0).World.WorldValues().Keys, Is.Empty);

    [Test]
    public void WhatIsWritten_ReadsBack()
    {
        var (world, _) = At(0);

        world.SetWorldValue("season", AttributeValue.From(4L));
        world.SetWorldValue("festival", AttributeValue.From("harvest"));

        Assert.Multiple(() =>
        {
            Assert.That(world.WorldValues().TryGet("season", out var season), Is.True);
            Assert.That(season.AsLong(), Is.EqualTo(4L));
            Assert.That(world.WorldValues().TryGet("festival", out var festival), Is.True);
            Assert.That(festival.AsText(), Is.EqualTo("harvest"));
        });
    }

    /// <summary>⚠ A blank key is refused rather than written. Nothing could read it back, and an
    /// unnamed entry leaves the whole bag undescribable.</summary>
    [Test]
    public void ABlankKey_IsNotWritten()
    {
        var (world, _) = At(0);

        world.SetWorldValue("   ", AttributeValue.From(1L));

        Assert.That(world.WorldValues().Keys, Is.Empty);
    }

    /// <summary>🔴 <b>It has to come back after a restart, or it is not state.</b> It rides on
    /// environment.json beside the time of day and the weather, which are the engine's own answers to
    /// the same question.</summary>
    [Test]
    public void ItSurvivesBeingWrittenAndReadAgain()
    {
        var kept = new AttributeBag();
        kept.Set("season", AttributeValue.From(7L));

        var before = new EnvironmentState(1234, WeatherType.Rain, 5678, kept);

        string json = JsonSerializer.Serialize(before, RecordJson.Options);
        var after = JsonSerializer.Deserialize<EnvironmentState>(json, RecordJson.Options);

        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.Values, Is.Not.Null);
            Assert.That(after.Values!.TryGet("season", out var season), Is.True);
            Assert.That(season.AsLong(), Is.EqualTo(7L));
            Assert.That(after.TodPositionMs, Is.EqualTo(1234), "and the engine's own half is untouched");
            Assert.That(after.Weather, Is.EqualTo(WeatherType.Rain));
        });
    }

    /// <summary>⚠ A file written before a game kept anything carries none, and must still load. That is
    /// every environment.json that exists today.</summary>
    [Test]
    public void AFileFromBeforeThisExisted_StillLoads()
    {
        var after = JsonSerializer.Deserialize<EnvironmentState>(
            """{"TodPositionMs":10,"Weather":0,"WeatherRemainingMs":20}""", RecordJson.Options);

        Assert.That(after, Is.Not.Null);
        Assert.That(after!.Values, Is.Null);
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
