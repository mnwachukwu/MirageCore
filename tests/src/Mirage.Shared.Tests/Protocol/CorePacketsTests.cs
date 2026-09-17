using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The registry is the single point at which a command string becomes a packet, for the server's
/// router, the client's dispatcher, and the editor's connection alike. A command with no row does not
/// deserialize, and a line that does not deserialize is dropped before any router sees it — so a
/// missing row costs a message entirely rather than degrading a feature.
/// </summary>
[TestFixture]
public class CorePacketsTests
{
    /// <summary>Every packet type in the assembly has a row, and that row resolves back to the type it
    /// came from. The DEBUG constructor asserts this too; having it as a test means Release builds and
    /// CI are covered by the same claim.</summary>
    [Test]
    public void EveryPacketTypeRoundTripsBackToItself()
    {
        var failures = typeof(IPacket).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPacket).IsAssignableFrom(t))
            .Select(t => (IPacket?)Activator.CreateInstance(t))
            .Where(p => p is not null)
            .Select(p => p!)
            .Select(p =>
            {
                var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(p));
                if (back is null) return $"{p.GetType().Name} (cmd \"{p.Cmd}\") has no row";
                return back.GetType() != p.GetType()
                    ? $"{p.GetType().Name} (cmd \"{p.Cmd}\") resolves to {back.GetType().Name}"
                    : null;
            })
            .Where(f => f is not null)
            .ToList();

        Assert.That(failures, Is.Empty, string.Join("; ", failures));
    }

    [Test]
    public void TheRegistryIsNotEmpty()
    {
        Assert.That(PacketSerializer.Registry.Commands, Is.Not.Empty);
    }

    // ── The two commands used in both directions ──────────────────────────────
    //
    // "playermove" and "playerdir" each name two shapes. Nothing on the wire separates them but a
    // top-level "index": the server-to-client form has one, the client-to-server form does not.

    [Test]
    public void AMoveWithAnIndexReadsAsTheInboundShape()
    {
        string line = PacketSerializer.Serialize(new SendPlayerMovePacket { Index = 4 });

        Assert.That(PacketSerializer.TryDeserialize(line), Is.InstanceOf<SendPlayerMovePacket>());
    }

    [Test]
    public void AMoveWithoutAnIndexReadsAsTheOutboundShape()
    {
        string line = PacketSerializer.Serialize(new PlayerMovePacket());

        Assert.That(PacketSerializer.TryDeserialize(line), Is.InstanceOf<PlayerMovePacket>());
    }

    [Test]
    public void ADirWithAnIndexReadsAsTheInboundShape()
    {
        string line = PacketSerializer.Serialize(new SendPlayerDirPacket { Index = 4 });

        Assert.That(PacketSerializer.TryDeserialize(line), Is.InstanceOf<SendPlayerDirPacket>());
    }

    [Test]
    public void ADirWithoutAnIndexReadsAsTheOutboundShape()
    {
        string line = PacketSerializer.Serialize(new PlayerDirPacket());

        Assert.That(PacketSerializer.TryDeserialize(line), Is.InstanceOf<PlayerDirPacket>());
    }

    /// <summary>Both directions share one command string. That shared string is the premise the four
    /// tests above rest on, so it is asserted rather than assumed: two rows telling shapes apart by an
    /// index field only make sense while one command names both.</summary>
    [Test]
    public void TheTwoDirectionsOfEachPairReallyDoShareACommand()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SendPlayerMovePacket().Cmd, Is.EqualTo(new PlayerMovePacket().Cmd));
            Assert.That(new SendPlayerDirPacket().Cmd, Is.EqualTo(new PlayerDirPacket().Cmd));
        });
    }

    // ── Refusals ──────────────────────────────────────────────────────────────

    [Test]
    public void AnUnknownCommandIsDropped()
    {
        Assert.That(PacketSerializer.TryDeserialize("""{"cmd":"nothing-registered"}"""), Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json at all")]
    [TestCase("[1,2,3]")]
    [TestCase("{}")]
    [TestCase("""{"cmd":42}""")]
    public void ALineThatNamesNoCommandIsDropped(string line)
    {
        Assert.That(PacketSerializer.TryDeserialize(line), Is.Null);
    }

    /// <summary>A known command whose payload puts the wrong type in a mapped field drops that one
    /// line. The connection reading it keeps running: a peer sending nonsense must not be able to stop
    /// the process reading from it.</summary>
    [Test]
    public void AKnownCommandWithAMalformedPayloadIsDroppedRatherThanThrown()
    {
        string line = $$"""{"cmd":"{{PacketNames.Login}}","maj":"not a number"}""";

        Assert.That(() => PacketSerializer.TryDeserialize(line), Throws.Nothing);
        Assert.That(PacketSerializer.TryDeserialize(line), Is.Null);
    }

    /// <summary>A field the packet does not declare is ignored rather than fatal, so a peer built
    /// against a version that sends more than this one reads is still understood.</summary>
    [Test]
    public void AFieldThePacketDoesNotDeclareIsIgnored()
    {
        string line = $$$"""{"cmd":"{{{PacketNames.Login}}}","somethingElse":{"a":1}}""";

        Assert.That(PacketSerializer.TryDeserialize(line), Is.InstanceOf<LoginPacket>());
    }

    // ── The table itself ──────────────────────────────────────────────────────

    [Test]
    public void CoreRowsCanBeBuiltIntoATableAlongsideOthers()
    {
        var builder = new PacketRegistry.Builder();
        CorePackets.Register(builder);
        builder.Register("game.something", (_, _) => null);

        var registry = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Knows(PacketNames.Login), Is.True);
            Assert.That(registry.Knows("game.something"), Is.True);
        });
    }

    /// <summary>A game claiming a command Core already speaks is a collision worth stopping for: the
    /// row that lost would be unreachable, and the two call sites disagreeing is the bug.</summary>
    [Test]
    public void AGameCannotQuietlyTakeOverACoreCommand()
    {
        var builder = CorePackets.Register(new PacketRegistry.Builder());

        Assert.That(() => builder.Register(PacketNames.Login, (_, _) => null), Throws.ArgumentException);
    }
}
