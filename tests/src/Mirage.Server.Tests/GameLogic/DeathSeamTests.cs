using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// Death as Core performs it: a body leaves where it stood and arrives at its home, and everything
/// about WHY comes from a policy.
///
/// <para>🔴 The seam is what makes Core genre-agnostic here. Core has no rule that ends a life, so with
/// no policy loaded a kill must still be a well-defined move rather than a half-applied one — and with a
/// policy that refuses, nothing may have happened at all. Both halves fail silently otherwise: a body
/// left standing where it died, or one moved after a refusal, looks like an ordinary position either
/// way.</para>
/// </summary>
[TestFixture]
public class DeathSeamTests
{
    private const int Map = 1, Index = 1;

    // Past the world's ceiling, so nothing is ever there to land on.
    private const int NoSuchMap = 999_999;

    private static (DeathSystem Deaths, PlayerManager Pm) Build(params IDeathPolicy[] policies)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var spawns = new SpawnSystem(world, pm, dispatcher, items: null!);

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Name = "Matt";
        p.Map = Map;
        p.X = 3;
        p.Y = 4;
        p.SpawnMap = Map;
        p.SpawnX = 9;
        p.SpawnY = 9;
        world.MapObservers[Map].Add(Index);

        return (new DeathSystem(world, pm, dispatcher, movement, spawns, policies), pm);
    }

    [Test]
    public void WithNoPolicy_AKillStillSendsTheBodyHome()
    {
        var (deaths, pm) = Build();

        bool died = deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.Multiple(() =>
        {
            Assert.That(died, Is.True);
            Assert.That((pm[Index].Char.X, pm[Index].Char.Y), Is.EqualTo((9, 9)),
                "with nothing to say otherwise, a body comes back to the home this world already knows");
        });
    }

    [Test]
    public void ARefusingPolicy_LeavesTheBodyExactlyWhereItWas()
    {
        var (deaths, pm) = Build(new Policy { Allow = false });

        bool died = deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.Multiple(() =>
        {
            Assert.That(died, Is.False);
            Assert.That((pm[Index].Char.X, pm[Index].Char.Y), Is.EqualTo((3, 4)),
                "a refused death must not move the body — a last stand that teleports you is not one");
        });
    }

    /// <summary>Cost before move, so a policy shedding inventory drops it on the tile the body fell on
    /// rather than at the respawn point, where nobody would ever have to go back for it.</summary>
    [Test]
    public void TheCostIsPaidWhereTheBodyFell()
    {
        var policy = new Policy();
        var (deaths, pm) = Build(policy);
        policy.Where = () => (pm[Index].Char.X, pm[Index].Char.Y);

        deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.That(policy.DiedAt, Is.EqualTo((3, 4)));
    }

    [Test]
    public void APolicyNamingAPlace_OverridesTheWorldsOwnHome()
    {
        var (deaths, pm) = Build(new Policy { Respawn = new Respawn(Map, 2, 2) });

        deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.That((pm[Index].Char.X, pm[Index].Char.Y), Is.EqualTo((2, 2)));
    }

    /// <summary>A policy naming a map that is not there falls back to the home. A respawn is the one move
    /// with no way back: put somebody on a map that does not exist and they are simply stuck.</summary>
    [Test]
    public void AMapThatIsNotThere_FallsBackToTheHome()
    {
        var (deaths, pm) = Build(new Policy { Respawn = new Respawn(NoSuchMap, 2, 2) });

        deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.That((pm[Index].Char.Map, pm[Index].Char.X, pm[Index].Char.Y), Is.EqualTo((Map, 9, 9)));
    }

    /// <summary>A tile off the edge of a real map is pulled onto it rather than refused — the map the
    /// policy named is the part it meant, and there is somewhere obvious to land.</summary>
    [Test]
    public void ATileOffTheEdge_IsPulledOntoTheMap()
    {
        var (deaths, pm) = Build(new Policy { Respawn = new Respawn(Map, Constants.MaxMapX + 40, 0) });

        deaths.Kill(EntityHandle.ForPlayer(Index));

        Assert.That(pm[Index].Char.X, Is.EqualTo(Constants.MaxMapX));
    }

    [Test]
    public void ThePolicyIsToldWhoDidIt()
    {
        var policy = new Policy();
        var (deaths, _) = Build(policy);

        deaths.Kill(EntityHandle.ForPlayer(Index), EntityHandle.ForNpc(Map, 3), "Combat_Slain");

        Assert.Multiple(() =>
        {
            Assert.That(policy.Seen.WasKilled, Is.True);
            Assert.That(policy.Seen.Killer, Is.EqualTo(EntityHandle.ForNpc(Map, 3)));
            Assert.That(policy.Seen.CauseKey, Is.EqualTo("Combat_Slain"));
        });
    }

    [Test]
    public void KillingNobody_DoesNothing()
    {
        var policy = new Policy();
        var (deaths, _) = Build(policy);

        Assert.Multiple(() =>
        {
            Assert.That(deaths.Kill(EntityHandle.None), Is.False);
            Assert.That(deaths.Kill(EntityHandle.ForPlayer(7)), Is.False, "a slot nobody is playing");
            Assert.That(policy.Deaths, Is.Zero, "and no policy is consulted about a death that cannot happen");
        });
    }

    private sealed class Policy : IDeathPolicy
    {
        public bool Allow { get; init; } = true;
        public Respawn Respawn { get; init; } = Respawn.Default;

        public int Deaths { get; private set; }
        public Death Seen { get; private set; }
        public (int X, int Y) DiedAt { get; private set; }

        public Refusal MayDie(in Death death) => Allow ? Refusal.Allow : Refusal.Deny("nope");

        public void OnDied(in Death death)
        {
            Deaths++;
            Seen = death;
            DiedAt = Where?.Invoke() ?? (0, 0);
        }

        public Respawn RespawnFor(in Death death) => Respawn;

        /// <summary>Where the body stands at the moment the cost is charged. A closure rather than a
        /// player reference so the policy reads the live position, which is the thing under test.</summary>
        public Func<(int X, int Y)>? Where { get; set; }
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
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
