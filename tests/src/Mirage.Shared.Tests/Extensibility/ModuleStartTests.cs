using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// Declaring and acting are two phases.
///
/// <para>What a module declares shapes the engine that is then built, so there is nothing to hand it
/// while it is declaring. <see cref="ICoreModule.Start"/> is the other end, and it is the only reference
/// a module ever gets: an observer, a policy and a piece of tick work all act through the
/// <see cref="IWorld"/> caught there.</para>
/// </summary>
[TestFixture]
public class ModuleStartTests
{
    private sealed class Recorder : ICoreModule
    {
        public string Name => "Recorder";
        public IWorld? Caught { get; private set; }
        public bool ConfiguredBeforeStart { get; private set; }
        private bool _configured;

        public void Configure(ICoreBuilder builder) => _configured = true;

        public void Start(IWorld world)
        {
            ConfiguredBeforeStart = _configured;
            Caught = world;
        }
    }

    /// <summary>A module that only declares data implements no second phase at all.</summary>
    private sealed class Quiet : ICoreModule
    {
        public string Name => "Quiet";
        public void Configure(ICoreBuilder builder) { }
    }

    [Test]
    public void TheRegistryKeepsTheModules_SoTheHostCanStartThem()
    {
        var recorder = new Recorder();
        var registry = CoreRegistry.Build([recorder]);

        Assert.That(registry.Modules, Does.Contain(recorder));
    }

    /// <summary>Core is the first module through the same interface, so it is in the list a host starts
    /// and its names line up with <see cref="CoreRegistry.ModuleNames"/> one for one.</summary>
    [Test]
    public void CoreIsInTheListToo_AndTheNamesLineUp()
    {
        var registry = CoreRegistry.Build([new Quiet()]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Modules, Has.Count.EqualTo(registry.ModuleNames.Count));
            Assert.That(registry.Modules, Has.Count.EqualTo(2), "Core, then the one that was loaded");
            Assert.That(registry.ModuleNames[^1], Is.EqualTo("Quiet"));
        });
    }

    [Test]
    public void AModuleIsConfiguredBeforeItIsStarted()
    {
        var recorder = new Recorder();
        var registry = CoreRegistry.Build([recorder]);

        foreach (var module in registry.Modules) module.Start(null!);

        Assert.That(recorder.ConfiguredBeforeStart, Is.True);
    }

    [Test]
    public void AModuleKeepsWhatItIsHanded()
    {
        var recorder = new Recorder();
        var world = new NoWorld();

        foreach (var module in CoreRegistry.Build([recorder]).Modules) module.Start(world);

        Assert.That(recorder.Caught, Is.SameAs(world));
    }

    [Test]
    public void AModuleWithNoSecondPhase_StartsWithoutComplaint()
        => Assert.DoesNotThrow(() => ((ICoreModule)new Quiet()).Start(new NoWorld()));

    /// <summary>Stands in for the engine. Every member answers for a body that is not there, which is
    /// what the real one does for a handle naming nothing.</summary>
    private sealed class NoWorld : IWorld
    {
        public bool IsInWorld(EntityHandle who) => false;
        public WorldPlace PlaceOf(EntityHandle who) => WorldPlace.Nowhere;
        public void Tell(EntityHandle who, string text, ChatChannel channel, int color) { }
        public AttributeBag? AttributesOf(EntityHandle who) => null;
        public bool SetAttribute(EntityHandle who, string key, AttributeValue value) => false;
        public bool SetAttributes(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values) => false;
        public bool RemoveAttribute(EntityHandle who, string key) => false;
        public void SetEngaged(EntityHandle who, int seconds) { }
        public void SetDowned(EntityHandle who, int seconds) { }
        public void SetMarked(EntityHandle who, int seconds) { }
        public void SetAggressor(EntityHandle who, int seconds) { }
        public void SetActionCooldown(EntityHandle who, int seconds) { }
        public bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "") => false;
        public bool Warp(EntityHandle who, WorldPlace to) => false;
        public void Give(EntityHandle who, int itemNum, int quantity = 1) { }
        public void Take(EntityHandle who, int itemNum, int quantity = 1) { }
        public void ReleaseGhost(EntityHandle who) { }
        public void Stain(WorldPlace at, int size, WorldLayer layer, float amount) { }
        public IReadOnlyList<AttributeBag> RecordsOf(string familyId) => [];
        public AttributeBag? RecordAt(string familyId, int num) => null;

        public string NameOf(EntityHandle who) => who.IsSet ? who.ToString() : string.Empty;
    }
}
