using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>
/// A swing that reaches a target costs the attacker their beat, whatever it resolves to — landed, crit,
/// mitigated to nothing, blocked, dodged, or torn off course by the wind.
///
/// <para>Block and dodge are the DEFENDER succeeding; the attacker still swung. Charging the cooldown only
/// on the branches that dealt damage would mean a defending target handed the attacker a free extra
/// attempt, so defending would raise the incoming swing rate — backwards. The stamp therefore happens once,
/// before the outcome cascade, which is also why no future outcome can be added that forgets to pay.</para>
///
/// <para>The outcome is a roll, so these swing repeatedly rather than forcing one branch: across enough
/// attempts every branch is exercised, and the invariant has to hold for all of them.</para>
/// </summary>
[TestFixture]
public class SwingCostsTheBeatTests
{
    private const int Attempts = 200;

    private static CombatSystem Build(out PlayerManager pm, out GameWorld world)
    {
        world = new GameWorld();
        pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
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
            c.MaxHp = 10_000;
            c.Hp = 10_000;
            c.MaxSp = 100;
            c.Sp = 100;
        }
        // Arena so the PvP gate lets the swing through without PK bookkeeping.
        world.Maps[1].Moral = MapMoral.Arena;

        pm[1].Char.X = 8;
        pm[1].Char.Y = 6;
        pm[1].Char.Dir = Direction.Down;
        pm[2].Char.X = 8;
        pm[2].Char.Y = 7;
        world.MapObservers[1].Add(1);
        world.MapObservers[1].Add(2);

        // Real Blood and Movement: a landed swing runs the damage path, which reaches both. The victim's
        // HP is topped up between swings so a kill never reaches the null joinLeave.
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!,
            blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
    }

    /// <summary>Every attempt, from a cooldown-ready state, must leave the attacker on cooldown.</summary>
    private static void AssertEverySwingPays(CombatSystem combat, PlayerManager pm, string because)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            pm[1].AttackTimer = 0;             // ready to swing
            pm[2].Char.Hp = pm[2].Char.MaxHp;  // never let the victim die out from under the loop
            pm[2].Char.Sp = pm[2].Char.MaxSp;  // keep block/dodge affordable so those branches are reached

            combat.HandleAttack(1);

            Assert.That(pm[1].AttackTimer, Is.Not.Zero,
                $"{because}: swing {attempt} resolved without costing the attacker their beat");
        }
    }

    [Test]
    public void EverySwingCostsTheBeat_InClearWeather()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;

        AssertEverySwingPays(combat, pm, "clear weather");
    }

    /// <summary>Heavy Wind disables every proc and tears a share of swings off course, so this covers the
    /// miss branch — the outcome that reaches the target least.</summary>
    [Test]
    public void EverySwingCostsTheBeat_InHeavyWind()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.HeavyWind;

        AssertEverySwingPays(combat, pm, "heavy wind");
    }

    /// <summary>A shielded defender blocks rather than dodges, so this leans the cascade onto that branch.</summary>
    [Test]
    public void ABlockedSwingStillCostsTheBeat()
    {
        var combat = Build(out var pm, out var world);
        world.Weather = WeatherType.Clear;
        pm[2].Char.Def = 200;   // block/dodge chance scales off Def — make the defender succeed often

        AssertEverySwingPays(combat, pm, "a defender that blocks and dodges constantly");
    }

    /// <summary>Swinging at empty air is not a resolved swing: nothing was reached, so nothing is charged.</summary>
    [Test]
    public void SwingingAtNothingDoesNotCostTheBeat()
    {
        var combat = Build(out var pm, out _);
        pm[2].Char.Y = 11;      // out of reach — no target in the faced tile
        pm[1].AttackTimer = 0;

        combat.HandleAttack(1);

        Assert.That(pm[1].AttackTimer, Is.Zero, "a whiff at empty air reaches no target and costs no beat");
    }

    // ── Dispatcher (per-file convention: copied from FriendlyFireTests) ────────
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
}
