using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A live NPC's current vitals never exceed its max for the weather in force.
///
/// <para>The Snow transition rescales current HP/MP/SP by the ratio between the old and new max, rounding
/// AWAY FROM ZERO. That rounding overfills: a pool of 7 snows down to 6, and 6 x 1.25 rounds back to 8.
/// Everything that reads current-over-max as a fraction then works with a value above 1.0 — the AI's mana
/// taper on the cast/melee weave is the one that shows, since it stops tapering entirely.</para>
/// </summary>
[TestFixture]
public class NpcVitalCeilingTests
{
    const int Map = 1, Slot = 1, NpcNum = 1;

    static readonly MethodInfo Transition = typeof(WeatherSystem).GetMethod(
        "ApplySnowVitalTransition", BindingFlags.NonPublic | BindingFlags.Instance)!;

    static (GameWorld world, WeatherSystem weather) Build()
    {
        var world = new GameWorld();
        return (world, new WeatherSystem(world, new NoOpDispatcher(), new PlayerManager()));
    }

    /// <summary>Move the weather the way the system's own callers do: assign the new value, then rescale.</summary>
    static void Shift(GameWorld world, WeatherSystem weather, WeatherType to)
    {
        var from = world.Weather;
        world.Weather = to;
        Transition.Invoke(weather, [from, to]);
    }

    static MapNpcRecord SeatAtFullVitals(GameWorld world, int str, int def, int intel, int spd)
    {
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Str = str;
        npc.Def = def;
        npc.Int = intel;
        npc.Spd = spd;

        var mn = world.MapNpcs[Map, Slot];
        mn.Num = NpcNum;
        mn.X = 5;
        mn.Y = 5;
        mn.Hp = world.EffectiveNpcMaxHp(npc);
        mn.Mp = world.EffectiveNpcMaxMp(npc);
        mn.Sp = world.EffectiveNpcMaxSp(npc);
        return mn;
    }

    /// <summary>Int values whose base MP pool round-trips through Snow to MORE than it started with. If this
    /// is ever empty the sweep below proves nothing, so it is asserted rather than assumed.</summary>
    static List<int> OverfillingInts()
    {
        var hits = new List<int>();
        for (int intel = 1; intel <= 80; intel++)
        {
            int baseMax = StatFormulas.GetNpcMaxMp(20, 10, intel, 10);
            int snowed = (int)Math.Round(baseMax * Constants.WeatherSnowMaxMpMultiplier, MidpointRounding.AwayFromZero);
            if (snowed <= 0) continue;
            int back = (int)Math.Round(snowed / Constants.WeatherSnowMaxMpMultiplier, MidpointRounding.AwayFromZero);
            if (back > baseMax) hits.Add(intel);
        }
        return hits;
    }

    [Test]
    public void TheRoundTripGenuinelyOverfills_OrThisSuiteProvesNothing()
    {
        Assert.That(OverfillingInts(), Is.Not.Empty,
            "no Int value round-trips above its own pool, so the ceiling below is untested — "
            + "re-derive the case from the current Snow multiplier before trusting the sweep");
    }

    [Test]
    public void CurrentMp_NeverExceedsMax_AcrossASnowRoundTrip()
    {
        foreach (int intel in OverfillingInts())
        {
            var (world, weather) = Build();
            var mn = SeatAtFullVitals(world, str: 20, def: 10, intel: intel, spd: 10);

            Shift(world, weather, WeatherType.Snow);
            Shift(world, weather, WeatherType.Clear);

            int max = world.EffectiveNpcMaxMp(world.Npcs[NpcNum]);
            Assert.That(mn.Mp, Is.LessThanOrEqualTo(max),
                $"Int {intel}: current MP {mn.Mp} sits above the max of {max} after Snow cleared");
        }
    }

    [Test]
    public void EveryCurrentVital_StaysWithinItsMax_AcrossASnowRoundTrip()
    {
        foreach (int intel in new[] { 1, 7, 20, 40, 60 })
        {
            var (world, weather) = Build();
            var mn = SeatAtFullVitals(world, str: 20, def: 10, intel: intel, spd: 10);

            Shift(world, weather, WeatherType.Snow);
            Shift(world, weather, WeatherType.Clear);

            var npc = world.Npcs[NpcNum];
            Assert.Multiple(() =>
            {
                Assert.That(mn.Hp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxHp(npc)), $"Int {intel}: HP over max");
                Assert.That(mn.Mp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxMp(npc)), $"Int {intel}: MP over max");
                Assert.That(mn.Sp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxSp(npc)), $"Int {intel}: SP over max");
                Assert.That(mn.Hp, Is.GreaterThanOrEqualTo(1), $"Int {intel}: a live NPC was scaled to dead");
            });
        }
    }

    /// <summary>The ceiling holds while Snow is in force too, not only after it lifts.</summary>
    [Test]
    public void CurrentVitals_StayWithinTheSnowMax_WhileItIsSnowing()
    {
        var (world, weather) = Build();
        var mn = SeatAtFullVitals(world, str: 20, def: 10, intel: 40, spd: 10);

        Shift(world, weather, WeatherType.Snow);

        var npc = world.Npcs[NpcNum];
        Assert.Multiple(() =>
        {
            Assert.That(mn.Hp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxHp(npc)), "HP over the snow max");
            Assert.That(mn.Mp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxMp(npc)), "MP over the snow max");
            Assert.That(mn.Sp, Is.LessThanOrEqualTo(world.EffectiveNpcMaxSp(npc)), "SP over the snow max");
        });
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
