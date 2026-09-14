using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The direction back: what a module asks to be told, and what it says about dying.
///
/// <para>Everything else a module declares is data it hands the engine. These two are the seam the other
/// way — without them a module can describe a game and never play one.</para>
/// </summary>
[TestFixture]
public class WorldObserverTests
{
    private sealed class Watcher : IWorldObserver
    {
        public Watcher(string name = "Watcher") => Name = name;
        public string Name { get; }
    }

    private sealed class Policy : IDeathPolicy;

    private sealed class Module(string name, Action<ICoreBuilder> configure) : ICoreModule
    {
        public string Name => name;
        public void Configure(ICoreBuilder builder) => configure(builder);
    }

    /// <summary>An engine with no game loaded tells nobody anything, which is the correct behavior
    /// rather than a gap — the same answer it gives for equipment slots and tick work.</summary>
    [Test]
    public void CoreAlone_ObservesNothingAndSaysNothingAboutDying()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CoreRegistry.CoreOnly.Observers, Is.Empty);
            Assert.That(CoreRegistry.CoreOnly.DeathPolicies, Is.Empty);
        });
    }

    [Test]
    public void AModule_DeclaresWhatItWantsToBeTold()
    {
        var watcher = new Watcher();
        var registry = CoreRegistry.Build([new Module("Pocket", b => b.AddObserver(watcher))]);

        Assert.That(registry.Observers.Single(), Is.SameAs(watcher));
    }

    [Test]
    public void AModule_DeclaresWhatDyingCosts()
    {
        var policy = new Policy();
        var registry = CoreRegistry.Build([new Module("Pocket", b => b.AddDeathPolicy(policy))]);

        Assert.That(registry.DeathPolicies.Single(), Is.SameAs(policy));
    }

    /// <summary>Two modules both wanting to hear about a step is the ordinary case, not a collision —
    /// unlike a family id or an equipment slot key, which name one thing each. All of them are kept, in
    /// the order their modules were configured.</summary>
    [Test]
    public void SeveralModules_AreAllToldInConfigurationOrder()
    {
        var registry = CoreRegistry.Build(
        [
            new Module("First", b => b.AddObserver(new Watcher("first"))),
            new Module("Second", b => b.AddObserver(new Watcher("second"))),
        ]);

        Assert.That(registry.Observers.Select(o => o.Name), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void AModuleThatKeepsTheBuilder_CannotAddAnObserverLater()
    {
        ICoreBuilder? escaped = null;
        CoreRegistry.Build([new Module("Sneaky", b => escaped = b)]);

        Assert.Throws<CoreModuleException>(() => escaped!.AddObserver(new Watcher()));
    }

    // ── What an observer is told ──────────────────────────────────────────────

    [Test]
    public void APlace_KnowsWhetherItNamesAnywhere()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorldPlace.Nowhere.IsSet, Is.False);
            Assert.That(new WorldPlace(1, 5, 6).IsSet, Is.True);
            Assert.That(new WorldPlace(1, 5, 6).SameMapAs(new WorldPlace(1, 0, 0)), Is.True);
            Assert.That(new WorldPlace(1, 5, 6).SameMapAs(new WorldPlace(2, 5, 6)), Is.False);
        });
    }

    /// <summary>Every method has a default, so a game that cares about one event implements one method.
    /// A watcher that overrides nothing is a legitimate observer and must not fail to compile or to
    /// run.</summary>
    [Test]
    public void AnObserverThatOverridesNothing_StillTakesEveryEvent()
    {
        IWorldObserver observer = new Watcher();
        var who = EntityHandle.ForPlayer(1);

        Assert.DoesNotThrow(() =>
        {
            observer.OnPlayerJoined(who);
            observer.OnPlayerLeft(who);
            observer.OnPlayerMoved(who, new WorldPlace(1, 1, 1), new WorldPlace(1, 1, 2));
            observer.OnPlayerWarped(who, WorldPlace.Nowhere, new WorldPlace(2, 3, 4));
            observer.OnContact(EntityHandle.ForNpc(1, 2), who);
            observer.OnItemUsed(who, itemNum: 5, invSlot: 1);
        });
    }
}
