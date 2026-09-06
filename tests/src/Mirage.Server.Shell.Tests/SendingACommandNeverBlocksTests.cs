using Mirage.Server.Shell.Services;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// Sending a command hands it to a queue and returns. It never writes to the socket or the pipe.
///
/// <para>🔴 <c>SendCommand</c> is called straight from a button, on the UI thread. Both destinations block
/// when they cannot take the bytes: a TLS write waits on a peer that has gone but whose TCP has not
/// noticed, and a stdin write waits once the pipe buffer fills, which is what a server that has stopped
/// reading its console does. Either one stops the window answering while it still presents its last frame,
/// so the operator sees a frozen shell rather than a lost server.</para>
///
/// <para>Read from source. Reproducing the block for real needs a peer that accepts and then stalls, and a
/// test that reproduces a hang is worse than the hang: against unbounded code the run stops instead of
/// failing.</para>
/// </summary>
[TestFixture]
public class SendingACommandNeverBlocksTests
{
    private static string SourceOf(string file)
    {
        string root = typeof(SendingACommandNeverBlocksTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string path = Path.Combine(root, "server", "src", "Mirage.Server.Shell", "Services", file);
        Assert.That(File.Exists(path), Is.True, path);

        string raw = File.ReadAllText(path);
        return string.Join("\n", raw.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
    }

    /// <summary>The method body, up to the first line that is back at member indentation.</summary>
    private static string SendCommandBody(string file)
    {
        string code = SourceOf(file);
        int start = code.IndexOf("public void SendCommand(string line)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(-1), $"{file} no longer has a SendCommand");

        int next = code.IndexOf("\n    /// ", start, StringComparison.Ordinal);
        int alt = code.IndexOf("\n    private ", start, StringComparison.Ordinal);
        int end = next < 0 ? alt : (alt < 0 ? next : Math.Min(next, alt));
        return end < 0 ? code[start..] : code[start..end];
    }

    [TestCase("RemoteServerConnection.cs")]
    [TestCase("ServerProcess.cs")]
    public void SendCommand_OnlyEnqueues(string file)
    {
        string body = SendCommandBody(file);

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("_outbox"), "the command does not go through the queue");
            // The two destinations by name, rather than "a write": the queue's own TryWrite is the point.
            Assert.That(body, Does.Not.Contain("_writer"),
                "SendCommand touches the socket, on whatever thread pressed the button");
            Assert.That(body, Does.Not.Contain("StandardInput"),
                "SendCommand touches the pipe, on whatever thread pressed the button");
            Assert.That(body, Does.Not.Contain("Flush"), "SendCommand flushes, which is the blocking part");
            Assert.That(body, Does.Not.Contain("lock ("),
                "a lock here is held across whatever the writer is doing");
        });
    }

    /// <summary>One reader keeps commands in the order they were pressed. Writing per-thread instead would
    /// let two commands land inverted.</summary>
    [TestCase("RemoteServerConnection.cs")]
    [TestCase("ServerProcess.cs")]
    public void TheOutbox_HasASingleReader(string file)
    {
        string code = SourceOf(file);

        Assert.That(code, Does.Match(@"Channel\.CreateUnbounded<string>\(\s*new UnboundedChannelOptions \{ SingleReader = true \}\s*\)"),
            "the outbox is not a single-reader channel, so command order is not guaranteed");
    }

    /// <summary>Nothing connected yet: the command is dropped rather than throwing at the caller, which is a
    /// button press.</summary>
    [Test]
    public void SendingBeforeConnecting_DoesNothing()
    {
        using var remote = new RemoteServerConnection("127.0.0.1", 1, "token");
        using var local = new ServerProcess();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => remote.SendCommand("/help"));
            Assert.DoesNotThrow(() => local.SendCommand("/help"));
        });
    }
}
