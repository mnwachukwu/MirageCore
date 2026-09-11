using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// Every registry here shares one rule: a second row for a key that already has one is an error at
/// startup rather than a silent shadowing, because two subsystems claiming one name is the bug and the
/// one that loses would be unobservable.
/// </summary>
[TestFixture]
public class AttributeSchemaTests
{
    [Test]
    public void AnUndeclaredKeyIsVisibleToNobodyAndPersistedAnyway()
    {
        var schema = AttributeSchema.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(schema.IsVisibleTo("hp", AttributeVisibility.Viewport), Is.False);
            Assert.That(schema.IsVisibleTo("hp", AttributeVisibility.Owner), Is.False);
            Assert.That(schema.IsPersisted("hp"), Is.True, "authored content is never dropped");
        });
    }

    /// <summary>An owner reads everything an onlooker does and more. The direction matters: getting it
    /// backwards hands an entity's private values to anyone standing near it, and nothing downstream
    /// would report that as wrong.</summary>
    [Test]
    public void AnOwnerSeesTheirOwnValuesAndThePublicOnesWhileAnOnlookerSeesOnlyThePublic()
    {
        var schema = new AttributeSchema.Builder()
            .Declare("hidden", AttributeVisibility.None)
            .Declare("mine", AttributeVisibility.Owner)
            .Declare("everyones", AttributeVisibility.Viewport)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(schema.IsVisibleTo("hidden", AttributeVisibility.Owner), Is.False,
                        "a server-only value reaches nobody, not even its owner");
            Assert.That(schema.IsVisibleTo("hidden", AttributeVisibility.Viewport), Is.False);

            Assert.That(schema.IsVisibleTo("mine", AttributeVisibility.Owner), Is.True);
            Assert.That(schema.IsVisibleTo("mine", AttributeVisibility.Viewport), Is.False,
                        "an owner-private value does not leak to onlookers");

            Assert.That(schema.IsVisibleTo("everyones", AttributeVisibility.Owner), Is.True,
                        "the owner is also someone who can see the entity");
            Assert.That(schema.IsVisibleTo("everyones", AttributeVisibility.Viewport), Is.True);
        });
    }

    [Test]
    public void OrdinalsAreHandedOutInDeclarationOrder()
    {
        var schema = new AttributeSchema.Builder()
            .Declare("first")
            .Declare("second")
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(schema.TryGet("first", out var first), Is.True);
            Assert.That(first.Ordinal, Is.Zero);
            Assert.That(schema.TryGet("second", out var second), Is.True);
            Assert.That(second.Ordinal, Is.EqualTo(1));
            Assert.That(schema.TryGet(1, out var byOrdinal), Is.True);
            Assert.That(byOrdinal.Key, Is.EqualTo("second"));
        });
    }

    [Test]
    public void AKeyDeclaredTwiceIsRefused()
    {
        var builder = new AttributeSchema.Builder().Declare("hp");

        Assert.That(() => builder.Declare("hp"), Throws.ArgumentException);
    }

    [Test]
    public void ABlankKeyIsRefused()
    {
        Assert.That(() => new AttributeSchema.Builder().Declare("  "), Throws.ArgumentException);
    }

    [Test]
    public void AKeyMayOptOutOfPersistence()
    {
        var schema = new AttributeSchema.Builder().Declare("transient", persist: false).Build();

        Assert.That(schema.IsPersisted("transient"), Is.False);
    }
}

[TestFixture]
public class PacketRegistryTests
{
    private sealed record Probe(string Cmd) : IPacket;

    [Test]
    public void AnUnknownCommandYieldsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PacketRegistry.Empty.Deserialize("whatever", "{}", false), Is.Null);
            Assert.That(PacketRegistry.Empty.Knows("whatever"), Is.False);
        });
    }

    [Test]
    public void ARegisteredCommandIsRead()
    {
        var registry = new PacketRegistry.Builder()
            .Register("probe", (_, _) => new Probe("probe"))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Knows("probe"), Is.True);
            Assert.That(registry.Deserialize("probe", "{}", false), Is.InstanceOf<Probe>());
            Assert.That(registry.Commands, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ACommandRegisteredTwiceIsRefused()
    {
        var builder = new PacketRegistry.Builder().Register("probe", (_, _) => null);

        Assert.That(() => builder.Register("probe", (_, _) => null), Throws.ArgumentException);
    }

    [Test]
    public void ABlankCommandIsRefused()
    {
        Assert.That(() => new PacketRegistry.Builder().Register(" ", (_, _) => null), Throws.ArgumentException);
    }

    /// <summary>Some command strings are used by both directions, and the only thing separating them on
    /// the wire is whether the line carries a top-level index.</summary>
    [Test]
    public void ACommandUsedBothWaysIsSplitByWhetherAnIndexIsPresent()
    {
        var registry = new PacketRegistry.Builder()
            .Register("move",
                withIndex: (_, _) => new Probe("inbound"),
                withoutIndex: (_, _) => new Probe("outbound"))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(((Probe)registry.Deserialize("move", "{}", true)!).Cmd, Is.EqualTo("inbound"));
            Assert.That(((Probe)registry.Deserialize("move", "{}", false)!).Cmd, Is.EqualTo("outbound"));
        });
    }

    [Test]
    public void BuildingTakesACopySoTheBuilderCanKeepBeingUsed()
    {
        var builder = new PacketRegistry.Builder().Register("one", (_, _) => null);
        var first = builder.Build();

        builder.Register("two", (_, _) => null);

        Assert.Multiple(() =>
        {
            Assert.That(first.Knows("two"), Is.False);
            Assert.That(builder.Build().Knows("two"), Is.True);
        });
    }
}

[TestFixture]
public class TickScheduleTests
{
    private sealed class Work(string name, int order = 0, int every = 1) : ITickWork
    {
        public string Name { get; } = name;
        public int Order { get; } = order;
        public int EveryTicks { get; } = every;
        public List<long> Ran { get; } = new();
        public void Tick(long tick) => Ran.Add(tick);
    }

    [Test]
    public void NothingRegisteredRunsNothing()
    {
        Assert.That(TickSchedule.Empty.DueOn(0), Is.Empty);
    }

    [Test]
    public void WorkRunsInOrderAndRegistrationBreaksTies()
    {
        var late = new Work("late", order: 10);
        var earlyA = new Work("earlyA");
        var earlyB = new Work("earlyB");

        var schedule = new TickSchedule.Builder().Add(late).Add(earlyA).Add(earlyB).Build();

        Assert.That(schedule.DueOn(0).Select(w => w.Name),
                    Is.EqualTo(new[] { "earlyA", "earlyB", "late" }));
    }

    [Test]
    public void WorkWithACadenceRunsOnlyOnItsTicks()
    {
        var everyThird = new Work("third", every: 3);
        var schedule = new TickSchedule.Builder().Add(everyThird).Build();

        for (long tick = 0; tick < 7; tick++) schedule.RunDue(tick);

        Assert.That(everyThird.Ran, Is.EqualTo(new long[] { 0, 3, 6 }));
    }

    [Test]
    public void ACadenceBelowOneIsReadAsEveryTick()
    {
        var broken = new Work("broken", every: 0);
        var schedule = new TickSchedule.Builder().Add(broken).Build();

        for (long tick = 0; tick < 3; tick++) schedule.RunDue(tick);

        Assert.That(broken.Ran, Is.EqualTo(new long[] { 0, 1, 2 }));
    }

    [Test]
    public void NullWorkIsRefusedWhereItIsAddedRatherThanWhereItRuns()
    {
        Assert.That(() => new TickSchedule.Builder().Add(null!), Throws.ArgumentNullException);
    }
}
