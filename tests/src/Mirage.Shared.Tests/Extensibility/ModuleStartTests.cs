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
        public EntityHandle At(WorldPlace place) => EntityHandle.None;
        public void TellEveryone(string text, ChatChannel channel, int color) { }
        public void TellEveryoneOn(int mapNum, string text, ChatChannel channel, int color) { }
        public void TellEveryoneNear(WorldPlace at, string text, ChatChannel channel, int color) { }
        public void Float(EntityHandle who, string text, uint rgb, float splatter) { }
        public void Sweep(EntityHandle who, bool connected) { }
        public void Throw(EntityHandle from, EntityHandle to, ProjectileStyle style, uint rgb) { }
        public void Burst(EntityHandle who, uint rgb, float intensity) { }
        public void TellThese(IReadOnlyCollection<EntityHandle> them, string text, ChatChannel channel, int color) { }
        public string GuildOf(EntityHandle who) => string.Empty;
        public IReadOnlyList<EntityHandle> GuildmatesOf(EntityHandle who) => [];
        public IReadOnlyList<EntityHandle> PartyOf(EntityHandle who) => [];
        public bool IsEngaged(EntityHandle who) => false;
        public bool IsDowned(EntityHandle who) => false;
        public bool IsMarked(EntityHandle who) => false;
        public bool IsAggressor(EntityHandle who) => false;
        public bool IsWaiting(EntityHandle who) => false;
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
        public string RecordName(string familyId, int num) => string.Empty;

        public string NameOf(EntityHandle who) => who.IsSet ? who.ToString() : string.Empty;
        public int KindOf(EntityHandle who) => 0;
        public bool Provoke(EntityHandle npc, EntityHandle quarry) => false;
        public bool Forget(EntityHandle npc) => false;
        public IReadOnlyList<EntityHandle> NpcsNear(WorldPlace at, int tiles) => [];
        public string TileAt(WorldPlace place) => string.Empty;
        public bool CanSee(WorldPlace from, WorldPlace to) => false;
        public int Distance(WorldPlace from, WorldPlace to) => -1;
        public string WeatherOn(int mapNum) => string.Empty;
        public int MapGroupOf(int mapNum) => 0;
        public bool Wear(EntityHandle who, int itemNum) => false;
        public bool Remove(EntityHandle who, int itemNum) => false;
        public long Carrying(EntityHandle who, int itemNum) => 0L;
        public bool IsRunning(EntityHandle who) => false;
        public string BehaviorOf(EntityHandle npc) => string.Empty;
        public int GroupOf(EntityHandle npc) => 0;
        public int RangeOf(EntityHandle npc) => 0;
        public bool IsChasing(EntityHandle npc) => false;
        public int GuildNumber(EntityHandle who) => 0;
        public string GuildName(int guild) => string.Empty;
        public int GuildNamed(string name) => 0;
        public string GuildRankOf(EntityHandle who) => string.Empty;
        public AttributeBag? GuildValues(int guild) => null;
        public bool SetGuildValue(int guild, string key, AttributeValue value) => false;
        public IReadOnlyList<EntityHandle> MembersOf(int guild) => [];
        public long GuildGold(int guild) => 0L;
        public long Now() => 0L;
        public IReadOnlyList<int> WornBy(EntityHandle who) => [];
        public int WornIn(EntityHandle who, string slotKey) => 0;
        public (int Left, int Full) DurabilityOf(EntityHandle who, int itemNum) => (0, 0);
        public int Wear(EntityHandle who, int itemNum, int points) => 0;
        public int RepairCost(int itemNum, int points) => 0;
        public AttributeValue? MapValue(int mapNum, string key) => null;
        public bool SetRecordValue(string familyId, int num, string key, AttributeValue value) => false;
        public bool GiveGuildGold(int guild, long amount) => false;
        public bool SpendGuildGold(int guild, long amount, EntityHandle by) => false;
    }
}
