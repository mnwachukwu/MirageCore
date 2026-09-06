using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// <see cref="PacketSerializer.ReadHeader"/> — the allocation-free top-level scan that replaced a
/// throwaway JsonNode DOM on both dispatchers' hot paths. It reads exactly two things: the
/// <c>cmd</c> discriminator, and whether a top-level <c>index</c> field is present (the only wire
/// difference between the shared-cmd pairs PlayerMove/SendPlayerMove and PlayerDir/SendPlayerDir).
/// <para>The load-bearing property is that the scan stays at depth 1: every container value is
/// skipped the instant its start token appears, so a <c>cmd</c> or <c>index</c> key nested inside a
/// payload object can never be mistaken for the real header field. A DOM parse gets that right
/// implicitly by indexing the root object; a streaming scan has to be held to it explicitly.</para>
/// <para>The contract is also that it NEVER throws — an unreadable line yields default, which every
/// caller treats as "drop this packet".</para>
/// </summary>
[TestFixture]
public class PacketHeaderScanTests
{
    // ── The ordinary path ─────────────────────────────────────────────────────

    [Test]
    public void ReadsCmd_AndReportsNoIndex_ForAPlainPacket()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"login","name":"bob","pass":"x"}""");
        Assert.That(h.Cmd, Is.EqualTo("login"));
        Assert.That(h.HasIndex, Is.False);
    }

    [Test]
    public void ReportsIndex_WhenATopLevelIndexIsPresent()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"playermove","index":4,"x":1,"y":2}""");
        Assert.That(h.Cmd, Is.EqualTo("playermove"));
        Assert.That(h.HasIndex, Is.True);
    }

    // Field order must not matter — cmd after index, and index as the very last field, both work.
    [Test]
    public void FieldOrderIsIrrelevant()
    {
        var indexFirst = PacketSerializer.ReadHeader("""{"index":7,"cmd":"playerdir","dir":2}""");
        Assert.That(indexFirst.Cmd, Is.EqualTo("playerdir"));
        Assert.That(indexFirst.HasIndex, Is.True);

        var indexLast = PacketSerializer.ReadHeader("""{"cmd":"playerdir","dir":2,"index":7}""");
        Assert.That(indexLast.Cmd, Is.EqualTo("playerdir"));
        Assert.That(indexLast.HasIndex, Is.True);
    }

    // ── The shared-cmd disambiguation this exists to serve ────────────────────

    // PlayerMove (C→S, no index) vs SendPlayerMove (S→C, has index) share one cmd string. The
    // client picks the type off HasIndex, so these two lines MUST differ in exactly that bit.
    [Test]
    public void SharedCmdPair_IsSeparatedOnlyByIndexPresence()
    {
        const string clientToServer = """{"cmd":"playermove","dir":1,"movement":2}""";
        const string serverToClient = """{"cmd":"playermove","index":3,"dir":1,"movement":2}""";

        var c2s = PacketSerializer.ReadHeader(clientToServer);
        var s2c = PacketSerializer.ReadHeader(serverToClient);

        Assert.That(c2s.Cmd, Is.EqualTo(s2c.Cmd), "the pair intentionally shares one cmd");
        Assert.That(c2s.HasIndex, Is.False);
        Assert.That(s2c.HasIndex, Is.True);
    }

    // ── Depth safety: the property a naive string search would get wrong ──────

    [Test]
    public void NestedCmdInAnObject_IsNotMistakenForTheHeader()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"editorsavemap","map":{"cmd":"spoofed","name":"m"}}""");
        Assert.That(h.Cmd, Is.EqualTo("editorsavemap"));
    }

    [Test]
    public void NestedIndexInAnObject_DoesNotSetHasIndex()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"playermove","payload":{"index":9}}""");
        Assert.That(h.Cmd, Is.EqualTo("playermove"));
        Assert.That(h.HasIndex, Is.False, "an index one level down is not the top-level index field");
    }

    [Test]
    public void NestedIndexInAnArrayOfObjects_DoesNotSetHasIndex()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"sendinventory","slots":[{"index":1},{"index":2}]}""");
        Assert.That(h.Cmd, Is.EqualTo("sendinventory"));
        Assert.That(h.HasIndex, Is.False);
    }

    // A container appearing BEFORE cmd must be skipped without losing the reader's place.
    [Test]
    public void ContainerBeforeCmd_IsSkippedCleanly()
    {
        var h = PacketSerializer.ReadHeader("""{"objs":[{"a":{"b":[1,2]}},{"c":3}],"cmd":"login"}""");
        Assert.That(h.Cmd, Is.EqualTo("login"));
    }

    // ── Unreadable / absent input: default, never a throw ─────────────────────

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json at all")]
    [TestCase("{\"cmd\":")]
    [TestCase("{\"cmd\":\"login\"")]     // truncated: no closing brace
    [TestCase("[1,2,3]")]                 // array, not an object
    [TestCase("\"login\"")]               // bare scalar
    [TestCase("42")]
    [TestCase("null")]
    [TestCase("{}")]                      // object with no cmd
    [TestCase("{\"other\":1}")]
    public void UnreadableOrCmdlessInput_YieldsDefault(string line)
    {
        var h = PacketSerializer.ReadHeader(line);
        Assert.That(h.Cmd, Is.Null);
        Assert.That(h.HasIndex, Is.False);
    }

    [Test]
    public void ReadHeaderNeverThrows_OnNull()
    {
        Assert.That(() => PacketSerializer.ReadHeader(null!), Throws.Nothing);
        Assert.That(PacketSerializer.ReadHeader(null!).Cmd, Is.Null);
    }

    // A non-string cmd leaves Cmd null rather than throwing, so the caller drops the packet.
    [TestCase("""{"cmd":5}""")]
    [TestCase("""{"cmd":null}""")]
    [TestCase("""{"cmd":true}""")]
    [TestCase("""{"cmd":{"a":1}}""")]
    [TestCase("""{"cmd":["login"]}""")]
    public void NonStringCmd_YieldsNullCmd(string line) =>
        Assert.That(PacketSerializer.ReadHeader(line).Cmd, Is.Null);

    // An explicit JSON null index is "absent" — the S→C forms always carry a real integer.
    [Test]
    public void ExplicitNullIndex_IsTreatedAsAbsent()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"playermove","index":null}""");
        Assert.That(h.Cmd, Is.EqualTo("playermove"));
        Assert.That(h.HasIndex, Is.False);
    }

    // ── Encoding + the array-pool path for large lines ────────────────────────

    // Non-ASCII must survive: the scan encodes to UTF-8 itself, so a multi-byte payload (or a
    // multi-byte cmd) must not shift the reader off the header.
    [Test]
    public void NonAsciiPayload_DoesNotDisturbTheScan()
    {
        var h = PacketSerializer.ReadHeader("""{"cmd":"saymsg","msg":"日本語 — café ✦","index":2}""");
        Assert.That(h.Cmd, Is.EqualTo("saymsg"));
        Assert.That(h.HasIndex, Is.True);
    }

    [Test]
    public void EscapedSequencesInValues_DoNotDisturbTheScan()
    {
        var h = PacketSerializer.ReadHeader("""{"msg":"a \"cmd\": \"spoofed\" b\\","cmd":"saymsg"}""");
        Assert.That(h.Cmd, Is.EqualTo("saymsg"),
            "an escaped quote inside a value must not be read as a property boundary");
    }

    // Crosses StackScanLimit (1024) so the ArrayPool branch runs, including its return path.
    [Test]
    public void LargeLine_TakesThePoolPathAndStillReadsTheHeader()
    {
        string big = new('x', 8000);
        var h = PacketSerializer.ReadHeader($"{{\"cmd\":\"editorsavemap\",\"blob\":\"{big}\",\"index\":6}}");
        Assert.That(h.Cmd, Is.EqualTo("editorsavemap"));
        Assert.That(h.HasIndex, Is.True);
    }

    // cmd sitting past the stack threshold — proves the pool buffer holds the WHOLE line, not a prefix.
    [Test]
    public void LargeLine_WithCmdAfterTheThreshold_StillReadsIt()
    {
        string big = new('y', 4000);
        var h = PacketSerializer.ReadHeader($"{{\"blob\":\"{big}\",\"cmd\":\"editorsavenpc\"}}");
        Assert.That(h.Cmd, Is.EqualTo("editorsavenpc"));
    }

    // ── Agreement with the real registry ─────────────────────────────────────

    // Whatever ReadHeader pulls off a serialized packet must be the cmd that TryDeserialize keys on,
    // for every registered packet type. This is the pairing the dispatchers depend on: the client
    // reads the header, then hands that exact cmd back to TryDeserialize(line, cmd).
    [Test]
    public void EveryRegisteredPacket_RoundTripsThroughReadHeaderAndTryDeserialize()
    {
        var types = typeof(IPacket).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPacket).IsAssignableFrom(t))
            .ToList();

        Assert.That(types, Is.Not.Empty, "sanity: the packet assembly should expose IPacket types");

        foreach (var t in types)
        {
            var instance = (IPacket)Activator.CreateInstance(t)!;
            string line = PacketSerializer.Serialize(instance);

            var header = PacketSerializer.ReadHeader(line);
            Assert.That(header.Cmd, Is.EqualTo(instance.Cmd), $"{t.Name}: header cmd must match IPacket.Cmd");

            // The two-arg overload (client path) and the one-arg overload (server path) must agree.
            Assert.That(PacketSerializer.TryDeserialize(line, header.Cmd!), Is.Not.Null,
                        $"{t.Name}: cmd from ReadHeader must resolve in the switch");
            Assert.That(PacketSerializer.TryDeserialize(line), Is.Not.Null,
                        $"{t.Name}: one-arg overload must resolve the same line");
        }
    }
}
