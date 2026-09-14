using Mirage.Client.Core.Net;
using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Net;

/// <summary>
/// A client composing a message for a game it was never compiled against.
///
/// <para>🔴 <b>This is the seam the scripted route was missing, and it has two halves that are written
/// in different assemblies.</b> The server reads a command nobody built a type for, through a parse
/// delegate a world's rules registered; the client writes one whose command is decided at run time.
/// Either half alone is useless, and neither half's own tests would notice — so they meet here.</para>
///
/// <para>The line is built the way the client builds it and read the way the server reads it, with
/// nothing in between, because the shape they have to agree on is the JSON itself: the model's fields
/// sit at the top level beside <c>cmd</c>, not under a property of their own.</para>
/// </summary>
[TestFixture]
public class GameMessageTravelsTests
{
    private static readonly ScriptModelField[] Sighting =
    [
        new("comment", ScriptFieldShape.Text, "string", []),
        new("count", ScriptFieldShape.Whole, "integer", []),
        new("sure", ScriptFieldShape.Truth, "boolean", []),
        new("where", ScriptFieldShape.Choice, "Habitat", ["Shore", "Woodland"]),
    ];

    /// <summary>What the client writes is what the server reads.</summary>
    [Test]
    public void WhatTheClientComposes_IsWhatTheServerReads()
    {
        string line = PacketSerializer.Serialize(new GameMessage
        {
            Cmd = "Sighting",
            Values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["comment"] = "herons by the shore",
                ["count"] = 4L,
                ["sure"] = true,
                ["where"] = "Shore",
            },
        });

        ScriptedPacket? read = ScriptedPacket.Read("Sighting", line, Sighting);

        Assert.That(read, Is.Not.Null, "the line the client writes parses on the server");

        Assert.Multiple(() =>
        {
            Assert.That(read!.Cmd, Is.EqualTo("Sighting"));
            Assert.That(read.Values.TryGet("comment", out var comment), Is.True);
            Assert.That(comment.AsText(), Is.EqualTo("herons by the shore"));

            Assert.That(read.Values.TryGet("count", out var count), Is.True);
            Assert.That(count.AsLong(), Is.EqualTo(4L));

            Assert.That(read.Values.TryGet("sure", out var sure), Is.True);
            Assert.That(sure.AsBool(), Is.True);

            Assert.That(read.Values.TryGet("where", out var where), Is.True);
            Assert.That(where.AsText(), Is.EqualTo("Shore"), "an enumeration travels as its member");
        });
    }

    /// <summary>⚠ The fields sit beside <c>cmd</c>, not under it. The server's reader pulls them off
    /// the root, so a wrapper object would read as a message that carried nothing.</summary>
    [Test]
    public void TheFieldsSitAtTheTopLevel_BesideTheCommand()
    {
        string line = PacketSerializer.Serialize(new GameMessage
        {
            Cmd = "Sighting",
            Values = new Dictionary<string, object>(StringComparer.Ordinal) { ["count"] = 4L },
        });

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("\"cmd\":\"Sighting\""));
            Assert.That(line, Does.Contain("\"count\":4"));
            Assert.That(line, Does.Not.Contain("\"values\""), "nothing wraps them");
        });
    }

    /// <summary>🔴 The header scan finds the command, which is what picks the parse delegate. A line
    /// whose command the scanner cannot read is dropped before any router sees it.</summary>
    [Test]
    public void TheCommandIsReadableByTheHeaderScan()
    {
        string line = PacketSerializer.Serialize(new GameMessage
        {
            Cmd = "Sighting",
            Values = new Dictionary<string, object>(StringComparer.Ordinal) { ["comment"] = "one" },
        });

        Assert.That(PacketSerializer.ReadHeader(line).Cmd, Is.EqualTo("Sighting"));
    }

    /// <summary>A value the model never named is dropped on arrival, so a client that puts one in
    /// cannot reach past what the rules said it may send.</summary>
    [Test]
    public void AValueTheModelNeverNamed_DoesNotArrive()
    {
        string line = PacketSerializer.Serialize(new GameMessage
        {
            Cmd = "Sighting",
            Values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = 1L,
                ["admin"] = true,
            },
        });

        ScriptedPacket? read = ScriptedPacket.Read("Sighting", line, Sighting);

        Assert.Multiple(() =>
        {
            Assert.That(read!.Values.Has("count"), Is.True);
            Assert.That(read.Values.Has("admin"), Is.False);
        });
    }
}
