using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>Player vital-regen cadence + the combat-suppression rule (RegenerationSystem — newly unit-testable
/// via the GameSystem clock seam and the parameterized Tick(now)). The load-bearing invariant: HP regen PAUSES
/// in combat while MP/SP keep regenerating; a corpse regenerates nothing; and a tick shorter than the interval
/// regenerates nothing. Amounts come from StatFormulas (covered by StatFormulasTests) — this pins the WHEN.
/// The interval tickers are seeded from Environment.TickCount64 in the ctor, so the harness captures that
/// baseline and drives Tick(baseline + delta) with deltas far from the interval boundaries.</summary>
[TestFixture]
public class RegenerationSystemTests
{
    const int Idx = 1;
    const long BigDelta = 10_000;   // exceeds every regen interval (max 5000ms)
    const long TinyDelta = 200;     // shorter than the shortest interval (1000ms)

    sealed class FixedClock : IClock
    {
        public long UtcNowUnix { get; set; } = 1_000_000;
        public DateTime LocalNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
    }

    // Builds the system with a real (null-sub-dep) CombatSystem — the only collaborator Tick touches for an
    // ordinary player is ExpireAggressorIfLapsed, which early-returns when there's no aggressor flag. The
    // baseline is captured immediately before construction so Tick(baseline + delta) controls the deltas.
    static (PlayerManager pm, RegenerationSystem regen, long baseline) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var clock = new FixedClock();
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement: null!, joinLeave: null!,
            blood: null!, objectives: null!, guilds: null!, guildWar: null!, territory: null!, clock: clock);
        long baseline = Environment.TickCount64;
        var regen = new RegenerationSystem(world, pm, dispatcher, joinLeave: null!, combat, clock: clock);
        return (pm, regen, baseline);
    }

    static PlayerRecord WoundedPlayer(PlayerManager pm)
    {
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = 1;
        p.Def = 30;
        p.Int = 30;
        p.Spd = 30;
        p.MaxHp = 100;
        p.MaxMp = 100;
        p.MaxSp = 100;
        p.Hp = 50;
        p.Mp = 0;
        p.Sp = 0;  // room to regenerate every vital
        return p;
    }

    [Test]
    public void OutOfCombat_AllThreeVitalsRegenPastTheInterval()
    {
        var (pm, regen, baseline) = Setup();
        var p = WoundedPlayer(pm);

        regen.Tick(baseline + BigDelta);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.GreaterThan(50), "HP regenerates out of combat");
            Assert.That(p.Mp, Is.GreaterThan(0), "MP regenerates");
            Assert.That(p.Sp, Is.GreaterThan(0), "SP regenerates");
        });
    }

    /// <summary>The headline rule: in combat HP and SP are both frozen and only MP keeps ticking.
    ///
    /// <para>MP is the odd one out on purpose. It is spent in discrete casts a caster chooses, so a
    /// mid-fight trickle just paces them. Stamina is spent CONTINUOUSLY — a sprint is five to seven
    /// points a second — so any rate that felt like recovery would refund the spending as fast as it
    /// happened, and the pool would stop being a budget at all.</para></summary>
    [Test]
    public void InCombat_HpAndSpAreFrozen_ButMpStillRegens()
    {
        var (pm, regen, baseline) = Setup();
        var p = WoundedPlayer(pm);
        pm[Idx].CombatExpiresAt = baseline + BigDelta + 1_000_000;   // still in combat at the tick time

        regen.Tick(baseline + BigDelta);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(50), "HP does not regenerate while in combat");
            Assert.That(p.Sp, Is.EqualTo(0), "and neither does stamina");
            Assert.That(p.Mp, Is.GreaterThan(0), "but MP still regenerates in combat");
        });
    }

    /// <summary>A sprint holds stamina off even out of combat. Without the hold, a rest rate sized to
    /// feel like recovery hands a running player their steps back about as fast as they spend them.</summary>
    [Test]
    public void Sprinting_HoldsStaminaOff_ThenItResumes()
    {
        var (pm, regen, baseline) = Setup();
        var p = WoundedPlayer(pm);
        pm[Idx].LastRunAt = baseline + BigDelta;   // a run step landed on this very tick

        regen.Tick(baseline + BigDelta);
        Assert.That(p.Sp, Is.EqualTo(0), "stamina is held while the sprint is still running");

        // Far enough past the last step that the hold has lapsed.
        regen.Tick(baseline + BigDelta + 60_000);
        Assert.That(p.Sp, Is.GreaterThan(0), "and resumes once the running stops");
    }

    [Test]
    public void Corpse_RegeneratesNothing()
    {
        var (pm, regen, baseline) = Setup();
        var p = WoundedPlayer(pm);
        p.Dead = true;

        regen.Tick(baseline + BigDelta);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(50), "a corpse's HP is frozen");
            Assert.That(p.Mp, Is.EqualTo(0), "and its MP");
        });
    }

    [Test]
    public void BeforeTheInterval_NothingRegenerates()
    {
        var (pm, regen, baseline) = Setup();
        var p = WoundedPlayer(pm);

        regen.Tick(baseline + TinyDelta);   // shorter than any regen interval

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(50));
            Assert.That(p.Mp, Is.EqualTo(0));
            Assert.That(p.Sp, Is.EqualTo(0));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

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
