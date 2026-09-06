using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// The server greets an editor before answering its login, exactly as it greets a game client. The
/// handshake reads past that greeting; failing to would make every login report the greeting as an
/// unexpected response and refuse to connect.
/// </summary>
[TestFixture]
public class EditorHandshakeTests
{
    private static StringReader Wire(params IPacket[] packets) =>
        new(string.Concat(packets.Select(PacketSerializer.Serialize)));

    [Test]
    public async Task TheGreetingIsTakenAndTheLoginReplyStillArrives()
    {
        ServerHelloPacket? seen = null;
        var reader = Wire(
            new ServerHelloPacket { GameName = "Test Realm" },
            new EditorLoginResponsePacket { Success = true, Message = "hi" });

        var (packet, closed) = await EditorConnection.ReadPastGreetingAsync(reader, h => seen = h);

        Assert.That(closed, Is.False);
        Assert.That(packet, Is.TypeOf<EditorLoginResponsePacket>());
        Assert.That(seen?.GameName, Is.EqualTo("Test Realm"));
    }

    [Test]
    public async Task AServerThatDoesNotGreetStillWorks()
    {
        ServerHelloPacket? seen = null;
        var reader = Wire(new EditorLoginResponsePacket { Success = true, Message = "hi" });

        var (packet, closed) = await EditorConnection.ReadPastGreetingAsync(reader, h => seen = h);

        Assert.That(closed, Is.False);
        Assert.That(packet, Is.TypeOf<EditorLoginResponsePacket>());
        Assert.That(seen, Is.Null);
    }

    [Test]
    public async Task TheWholeHandshakeReadsInOrder()
    {
        ServerHelloPacket? seen = null;
        var reader = Wire(
            new ServerHelloPacket { GameName = "Test Realm" },
            new EditorLoginResponsePacket { Success = true, Message = "hi" },
            new EditorDataPacket());

        var (first, _) = await EditorConnection.ReadPastGreetingAsync(reader, h => seen = h);
        var (second, _) = await EditorConnection.ReadPastGreetingAsync(reader, h => seen = h);

        Assert.That(first, Is.TypeOf<EditorLoginResponsePacket>());
        Assert.That(second, Is.TypeOf<EditorDataPacket>());
        Assert.That(seen?.GameName, Is.EqualTo("Test Realm"));
    }

    [Test]
    public async Task AClosedStreamIsReportedAsClosed_NotAsAnUnexpectedPacket()
    {
        var (packet, closed) = await EditorConnection.ReadPastGreetingAsync(new StringReader(""), _ => { });

        Assert.That(closed, Is.True);
        Assert.That(packet, Is.Null);
    }

    [Test]
    public async Task AGreetingWithNothingBehindItReadsAsClosed()
    {
        ServerHelloPacket? seen = null;
        var reader = Wire(new ServerHelloPacket { GameName = "Test Realm" });

        var (packet, closed) = await EditorConnection.ReadPastGreetingAsync(reader, h => seen = h);

        Assert.That(closed, Is.True);
        Assert.That(packet, Is.Null);
        Assert.That(seen, Is.Not.Null, "the greeting is still taken before the stream runs out");
    }

    [Test]
    public async Task AnUnrecognizedLineIsHandedBackRatherThanSkipped()
    {
        var (packet, closed) = await EditorConnection.ReadPastGreetingAsync(
            new StringReader("{\"cmd\":\"somethingThisBuildHasNeverHeardOf\"}\n"), _ => { });

        Assert.That(closed, Is.False, "the stream is still open");
        Assert.That(packet, Is.Null, "so the caller reports an unexpected response rather than hanging");
    }
}
