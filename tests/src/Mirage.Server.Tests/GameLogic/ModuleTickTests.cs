using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// The loop driving what the loaded modules registered.
///
/// <para><c>TickSchedule</c> is tested on its own; what these pin is the WIRING — that the loop advances a
/// tick number at all, that module work reaches the schedule through it, and that the loop survives a
/// module that throws.</para>
/// </summary>
[TestFixture]
public class ModuleTickTests
{
    private sealed class Work(string name, int everyTicks = 1) : ITickWork
    {
        public string Name { get; } = name;
        public int EveryTicks => everyTicks;
        public List<long> Ran { get; } = [];
        public void Tick(long tick) => Ran.Add(tick);
    }

    private sealed class Exploding(string name) : ITickWork
    {
        public string Name { get; } = name;
        public int Calls { get; private set; }
        public void Tick(long tick)
        {
            Calls++;
            throw new InvalidOperationException("this module is broken");
        }
    }

    private sealed class Module(params ITickWork[] work) : ICoreModule
    {
        public string Name => "Test";
        public void Configure(ICoreBuilder builder)
        {
            foreach (var w in work) builder.AddTickWork(w);
        }
    }

    // ModuleTick reads only the schedule and the logger, so everything else the loop needs can be absent.
    // Same shape as the other loop tests: a constructor argument is not a dependency of every method.
    private static GameLoop Loop(CoreRegistry registry) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, NullLogger<GameLoop>.Instance, clock: null, registry: registry);

    [Test]
    public void TheTickNumber_StartsAtZeroAndAdvancesByOne()
    {
        var loop = Loop(CoreRegistry.CoreOnly);
        Assert.That(loop.CurrentTick, Is.Zero, "nothing has run yet");

        loop.ModuleTick();
        loop.ModuleTick();

        Assert.That(loop.CurrentTick, Is.EqualTo(2));
    }

    [Test]
    public void RegisteredWork_IsDrivenByTheLoop()
    {
        var work = new Work("pocket-spawns");
        var loop = Loop(CoreRegistry.Build(new Module(work)));

        loop.ModuleTick();
        loop.ModuleTick();
        loop.ModuleTick();

        Assert.That(work.Ran, Is.EqualTo(new long[] { 1, 2, 3 }), "the tick it is handed is the loop's own count");
    }

    [Test]
    public void WorkOnALongerBeat_RunsOnlyOnItsOwnTicks()
    {
        var slow = new Work("slow", everyTicks: 4);
        var loop = Loop(CoreRegistry.Build(new Module(slow)));

        for (int i = 0; i < 9; i++) loop.ModuleTick();

        Assert.That(slow.Ran, Is.EqualTo(new long[] { 4, 8 }));
    }

    [Test]
    public void WithNoModulesLoaded_TheTickIsHarmless()
    {
        var loop = Loop(CoreRegistry.CoreOnly);

        Assert.DoesNotThrow(() => loop.ModuleTick());
        Assert.That(loop.CurrentTick, Is.EqualTo(1));
    }

    // 🔴 A module that throws must not starve the ones registered after it. Caught around the whole
    // schedule instead, this reads as a feature that quietly stopped working, and the log names the tick
    // rather than the module to look at.
    [Test]
    public void AModuleThatThrows_DoesNotStopTheOnesAfterIt()
    {
        var broken = new Exploding("broken");
        var after = new Work("after");
        var loop = Loop(CoreRegistry.Build(new Module(broken, after)));

        Assert.DoesNotThrow(() => loop.ModuleTick());

        Assert.Multiple(() =>
        {
            Assert.That(broken.Calls, Is.EqualTo(1));
            Assert.That(after.Ran, Is.EqualTo(new long[] { 1 }), "the work after the failure still ran");
        });
    }

    [Test]
    public void AModuleThatThrows_IsStillCalledOnTheNextTick()
    {
        var broken = new Exploding("broken");
        var loop = Loop(CoreRegistry.Build(new Module(broken)));

        loop.ModuleTick();
        loop.ModuleTick();

        Assert.That(broken.Calls, Is.EqualTo(2), "a throwing module is not quarantined, only contained");
    }
}
