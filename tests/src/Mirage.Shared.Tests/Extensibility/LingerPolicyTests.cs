using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// How long a dropped connection's body stays in the world.
///
/// <para>Core knows how to leave one behind and how to take it away again. WHETHER to is a rule, and
/// with no game loaded there is none — so a disconnect removes the player at once and the whole ghost
/// path is machinery nothing drives.</para>
/// </summary>
[TestFixture]
public class LingerPolicyTests
{
    private sealed class Linger(long seconds) : ILingerPolicy
    {
        public Deadline LingerFor(EntityHandle who) => Deadline.AtUtc(seconds);
    }

    /// <summary>A policy with no opinion. The default implementation is what makes a game able to answer
    /// one question about a disconnect without answering all of them.</summary>
    private sealed class Silent : ILingerPolicy;

    private sealed class Module(params ILingerPolicy[] policies) : ICoreModule
    {
        public string Name => "Test";
        public void Configure(ICoreBuilder builder)
        {
            foreach (var policy in policies) builder.AddLingerPolicy(policy);
        }
    }

    [Test]
    public void CoreAlone_KeepsNobody()
        => Assert.That(CoreRegistry.CoreOnly.LingerPolicies, Is.Empty);

    [Test]
    public void AModule_DeclaresHowLongABodyStays()
    {
        var policy = new Linger(500);
        var registry = CoreRegistry.Build([new Module(policy)]);

        Assert.That(registry.LingerPolicies.Single(), Is.SameAs(policy));
    }

    /// <summary>Two games disagreeing about how long a body lingers is a question with one answer and no
    /// way to average it, so the first policy naming a deadline is the answer — the same rule
    /// <see cref="IDeathPolicy.RespawnFor"/> uses.</summary>
    [Test]
    public void TheFirstPolicyNamingADeadline_IsTheAnswer()
    {
        var registry = CoreRegistry.Build(
            [new Module(new Silent(), new Linger(500), new Linger(900))]);

        var who = EntityHandle.ForPlayer(1);
        var answer = registry.LingerPolicies
            .Select(p => p.LingerFor(who))
            .First(d => d.IsSet);

        Assert.That(answer.At, Is.EqualTo(500));
    }

    [Test]
    public void APolicyWithNoOpinion_KeepsNobody()
        => Assert.That(((ILingerPolicy)new Silent()).LingerFor(EntityHandle.ForPlayer(1)).IsSet, Is.False);

    [Test]
    public void AModuleThatKeepsTheBuilder_CannotAddAPolicyLater()
    {
        ICoreBuilder? escaped = null;
        CoreRegistry.Build([new Module()]);
        CoreRegistry.Build([new CapturingModule(b => escaped = b)]);

        Assert.Throws<CoreModuleException>(() => escaped!.AddLingerPolicy(new Silent()));
    }

    private sealed class CapturingModule(Action<ICoreBuilder> capture) : ICoreModule
    {
        public string Name => "Sneaky";
        public void Configure(ICoreBuilder builder) => capture(builder);
    }
}
