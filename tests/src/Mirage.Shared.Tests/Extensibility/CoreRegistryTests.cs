using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// Loading modules: what a game may declare, what it may not, and what the engine looks like with none
/// loaded at all.
///
/// <para>This is the seam a game is built on, so most of these tests are about what the engine REFUSES.
/// Every collision here would otherwise be a last-one-wins that nothing reports: a shadowed packet
/// command, a family reading another family's files, an attribute whose visibility is whichever of two
/// declarations happened to run second.</para>
/// </summary>
[TestFixture]
public class CoreRegistryTests
{
    // ── A module that declares whatever the test needs ────────────────────────

    private sealed class Module(string name, Action<ICoreBuilder> configure) : ICoreModule
    {
        public string Name { get; } = name;
        public void Configure(ICoreBuilder builder) => configure(builder);
    }

    private sealed class Work(string name, int order = 0, int everyTicks = 1) : ITickWork
    {
        public string Name { get; } = name;
        public int Order => order;
        public int EveryTicks => everyTicks;
        public List<long> Ran { get; } = [];
        public void Tick(long tick) => Ran.Add(tick);
    }

    private static RecordFamily Family(string id, string? directory = null)
        => new() { Id = id, Directory = directory ?? id.ToLowerInvariant() };

    // ── An engine with no game loaded ─────────────────────────────────────────

    [Test]
    public void WithNoModules_TheEngineIsStillItself()
    {
        var registry = CoreRegistry.Build();

        Assert.Multiple(() =>
        {
            Assert.That(registry.ModuleNames, Is.EqualTo(new[] { "Core" }), "Core loads as a module like any other");
            Assert.That(registry.Schema.Families, Is.Not.Empty, "Core's own families are declared through the seam");
            Assert.That(registry.Schema.Family(CoreRecordFamilies.Maps), Is.Not.Null);
            Assert.That(registry.Tick.Work, Is.Empty, "nothing is on the tick until a game puts it there");
            Assert.That(registry.Attributes.Declarations, Is.Empty, "Core declares no attribute keys of its own");
        });
    }

    [Test]
    public void CoreOnly_IsTheSameAsBuildingWithNothing()
    {
        Assert.That(CoreRegistry.CoreOnly.Schema.Families.Select(f => f.Id),
                    Is.EqualTo(CoreRegistry.Build().Schema.Families.Select(f => f.Id)));
    }

    // Core's packet table has to arrive through the module seam rather than beside it, or the seam is
    // not actually load-bearing and the first game to need a packet finds out.
    [Test]
    public void CorePackets_ArriveThroughTheSeam()
    {
        Assert.That(CoreRegistry.Build().Packets.Knows(PacketNames.PlayerMove), Is.True);
    }

    // ── What a game adds ──────────────────────────────────────────────────────

    [Test]
    public void AModule_AddsItsFamiliesAfterCores()
    {
        var registry = CoreRegistry.Build(new Module("Pocket", b => b.AddFamily(Family("Species"))));

        Assert.Multiple(() =>
        {
            Assert.That(registry.ModuleNames, Is.EqualTo(new[] { "Core", "Pocket" }));
            Assert.That(registry.Schema.Family("Species"), Is.Not.Null);
            Assert.That(registry.Schema.Families[^1].Id, Is.EqualTo("Species"), "a game's families follow Core's");
        });
    }

    [Test]
    public void AModule_AddsAttributeKeysAndChoiceSets()
    {
        var registry = CoreRegistry.Build(new Module("Pocket", b =>
        {
            b.Attributes.Declare("friendship", AttributeVisibility.Owner);
            b.AddChoiceSet(new ChoiceSet { Id = "types" });
        }));

        Assert.Multiple(() =>
        {
            Assert.That(registry.Attributes.TryGet("friendship", out var friendship), Is.True);
            Assert.That(friendship.Visibility, Is.EqualTo(AttributeVisibility.Owner));
            Assert.That(registry.Schema.Choices("types"), Is.Not.Null);
        });
    }

    [Test]
    public void ModulesAreConfiguredInTheOrderGiven()
    {
        var order = new List<string>();
        var registry = CoreRegistry.Build(
            new Module("First", _ => order.Add("First")),
            new Module("Second", _ => order.Add("Second")));

        Assert.That(order, Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(registry.ModuleNames, Is.EqualTo(new[] { "Core", "First", "Second" }));
    }

    // ── What the engine refuses, and who it blames ────────────────────────────

    [Test]
    public void TwoModulesClaimingOneFamilyId_IsRefusedAndNamesTheSecond()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.AddFamily(Family("Species"))),
            new Module("Second", b => b.AddFamily(Family("Species", "species2")))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"), "the module that collided is the one that has to change");
        Assert.That(ex.Message, Does.Contain("Species"));
    }

    [Test]
    public void AModuleClaimingACoreFamilyId_IsRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Pocket", b => b.AddFamily(Family(CoreRecordFamilies.Items, "mine")))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Pocket"), "Core is not privileged, it is just first");
    }

    // Two families in one directory read each other's files as their own, which presents as record
    // corruption rather than as a registration mistake.
    [Test]
    public void TwoFamiliesInOneDirectory_IsRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Pocket", b => b.AddFamily(Family("Species", "items")))));

        Assert.That(ex!.Message, Does.Contain("items").And.Contain(CoreRecordFamilies.Items));
    }

    [Test]
    public void AFamilyWithNoId_IsRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Pocket", b => b.AddFamily(new RecordFamily()))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Pocket"));
    }

    [Test]
    public void TwoModulesClaimingOneChoiceSetId_IsRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.AddChoiceSet(new ChoiceSet { Id = "types" })),
            new Module("Second", b => b.AddChoiceSet(new ChoiceSet { Id = "types" }))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"));
    }

    // The builders below already refuse a duplicate; what this pins is that the refusal reaches the
    // caller as a named module rather than as a bare ArgumentException from somewhere in a builder.
    [Test]
    public void TwoModulesClaimingOneAttributeKey_IsRefusedAndNamesTheModule()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.Attributes.Declare("friendship")),
            new Module("Second", b => b.Attributes.Declare("friendship"))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"));
        Assert.That(ex.Message, Does.Contain("friendship"));
    }

    [Test]
    public void TwoModulesClaimingOnePacketCommand_IsRefusedAndNamesTheModule()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.Packets.Register("pocket:catch", (_, _) => null)),
            new Module("Second", b => b.Packets.Register("pocket:catch", (_, _) => null))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"));
    }

    [Test]
    public void AModuleClaimingACorePacketCommand_IsRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Pocket", b => b.Packets.Register(PacketNames.PlayerMove, (_, _) => null))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Pocket"));
    }

    [Test]
    public void AModuleThatThrows_IsReportedByName()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("Pocket", _ => throw new InvalidOperationException("no data folder"))));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ModuleName, Is.EqualTo("Pocket"));
            Assert.That(ex.Message, Does.Contain("no data folder"));
            Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
        });
    }

    // A module that keeps the builder is declaring into registries somebody is already reading, which is
    // the exact failure the one-pass design exists to prevent.
    [Test]
    public void AModuleThatKeepsTheBuilder_CannotUseItLater()
    {
        ICoreBuilder? escaped = null;
        CoreRegistry.Build(new Module("Pocket", b => escaped = b));

        var ex = Assert.Throws<CoreModuleException>(() => escaped!.AddFamily(Family("Late")));
        Assert.That(ex!.Message, Does.Contain("after loading finished"));
    }

    [Test]
    public void AModuleWithNoName_IsBlamedByItsType()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("  ", _ => throw new InvalidOperationException("boom"))));

        Assert.That(ex!.ModuleName, Is.EqualTo(nameof(Module)));
    }

    // ── Tick work ─────────────────────────────────────────────────────────────

    [Test]
    public void TickWork_RunsInOrderThenRegistrationOrder()
    {
        var ran = new List<string>();
        var registry = CoreRegistry.Build(new Module("Pocket", b =>
        {
            b.AddTickWork(new Ordered("late", 10, ran));
            b.AddTickWork(new Ordered("earlyA", -5, ran));
            b.AddTickWork(new Ordered("earlyB", -5, ran));
        }));

        registry.Tick.RunDue(0);

        Assert.That(ran, Is.EqualTo(new[] { "earlyA", "earlyB", "late" }));
    }

    [Test]
    public void TickWork_RunsOnlyOnItsOwnBeat()
    {
        var every3 = new Work("slow", everyTicks: 3);
        var registry = CoreRegistry.Build(new Module("Pocket", b => b.AddTickWork(every3)));

        for (long t = 0; t < 7; t++) registry.Tick.RunDue(t);

        Assert.That(every3.Ran, Is.EqualTo(new long[] { 0, 3, 6 }));
    }

    [Test]
    public void TickWorkFromTwoModules_SharesOneSchedule()
    {
        var a = new Work("a");
        var b = new Work("b");
        var registry = CoreRegistry.Build(
            new Module("First", x => x.AddTickWork(a)),
            new Module("Second", x => x.AddTickWork(b)));

        registry.Tick.RunDue(0);

        Assert.That(registry.Tick.Work, Has.Count.EqualTo(2));
        Assert.That((a.Ran.Count, b.Ran.Count), Is.EqualTo((1, 1)));
    }

    [Test]
    public void WithNoModule_FoundingAGuildCostsNothing()
    {
        Assert.That(CoreRegistry.Build().GuildCost, Is.Zero);
    }

    [Test]
    public void TheGameNamesWhatAGuildCosts()
    {
        var registry = CoreRegistry.Build(new Module("Pocket", b => b.SetGuildCost(35_000)));

        Assert.That(registry.GuildCost, Is.EqualTo(35_000));
    }

    [Test]
    public void APriceBelowNothing_IsNothing()
    {
        var registry = CoreRegistry.Build(new Module("Pocket", b => b.SetGuildCost(-5)));

        Assert.That(registry.GuildCost, Is.Zero);
    }

    [Test]
    public void TwoModulesPricingAGuild_AreRefused()
    {
        var ex = Assert.Throws<CoreModuleException>(() => CoreRegistry.Build(
            new Module("First", b => b.SetGuildCost(100)),
            new Module("Second", b => b.SetGuildCost(200))));

        Assert.That(ex!.ModuleName, Is.EqualTo("Second"));
    }

    [Test]
    public void OneModuleRestatingItsOwnPrice_IsFine()
    {
        var registry = CoreRegistry.Build(new Module("Pocket", b =>
        {
            b.SetGuildCost(100);
            b.SetGuildCost(200);
        }));

        Assert.That(registry.GuildCost, Is.EqualTo(200));
    }

    private sealed class Ordered(string name, int order, List<string> log) : ITickWork
    {
        public string Name { get; } = name;
        public int Order => order;
        public void Tick(long tick) => log.Add(Name);
    }
}
