using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Configuration;

/// <summary>
/// The editor learns what world it reached from the same greeting the game client gets. That greeting
/// arrives BEFORE the login reply, so the editor's handshake has to read past it rather than mistake it
/// for the reply it was waiting for.
/// </summary>
[TestFixture]
public class GameNameReachesTheEditorTests
{
    [Test]
    public void TheGreetingCarriesTheNameAcrossTheWire()
    {
        string line = PacketSerializer.Serialize(new ServerHelloPacket { GameName = "Test Realm" });

        var back = PacketSerializer.TryDeserialize(line) as ServerHelloPacket;
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.GameName, Is.EqualTo("Test Realm"));
    }

    [Test]
    public void AServerThatNamesNothingLeavesItEmptyRatherThanNull()
    {
        var back = PacketSerializer.TryDeserialize(
            $$"""{"cmd":"{{PacketNames.ServerHello}}"}""") as ServerHelloPacket;

        Assert.That(back, Is.Not.Null);
        Assert.That(back!.GameName, Is.Empty);
    }

    /// <summary>The editor's handshake distinguishes these three by type, not by arrival order, so each
    /// has to deserialize back to something it can tell apart.</summary>
    [Test]
    public void TheThreeHandshakeLinesStayTellableApart()
    {
        var lines = new[]
        {
            PacketSerializer.Serialize(new ServerHelloPacket { GameName = "Test Realm" }),
            PacketSerializer.Serialize(new EditorLoginResponsePacket { Success = true, Message = "hi" }),
            PacketSerializer.Serialize(new EditorDataPacket()),
        };

        var read = lines.Select(PacketSerializer.TryDeserialize).ToArray();

        Assert.That(read[0], Is.TypeOf<ServerHelloPacket>());
        Assert.That(read[1], Is.TypeOf<EditorLoginResponsePacket>());
        Assert.That(read[2], Is.TypeOf<EditorDataPacket>());
    }
}
