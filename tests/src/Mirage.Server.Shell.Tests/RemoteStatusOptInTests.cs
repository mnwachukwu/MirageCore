using Mirage.Shared.Protocol;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// An attached operator has to ASK for status, and the remote connection is where the asking happens.
///
/// <para>🔴 It did not ask. The management socket is a plain console until a client sends
/// <see cref="ServerStatus.RequestStatus"/>, and the server only fans status lines out to sessions that
/// did — so a remotely attached shell showed a live console with a blank dashboard and a blank
/// moderation tab. Both ride that one stream, which is how a single missing line emptied two tabs.</para>
///
/// <para>A LOCAL shell asks a different way, by starting the server with <c>--status-events</c>, which is
/// why the gap only ever showed when attaching over the network.</para>
///
/// <para>⚠️ Read from SOURCE. Driving the real thing needs a TLS listener and touches the certificate
/// pin store, which is per-user state a test must not write to. This catches the line being removed or
/// moved before the handshake, which is the regression that actually happened; it does not prove the
/// bytes reach a socket. The end-to-end proof is a manual attach.</para>
/// </summary>
[TestFixture]
public class RemoteStatusOptInTests
{
    static string RepoRoot()
    {
        string root = typeof(RemoteStatusOptInTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>The file with its line comments stripped.
    ///
    /// <para>🔴 Stripping them is the whole reliability of this check. A plain text search is satisfied by
    /// the call appearing in a comment, so commenting the line out — the exact shape of the regression —
    /// left the test green. What is asserted has to be what COMPILES.</para></summary>
    static string Source()
    {
        string raw = File.ReadAllText(Path.Combine(RepoRoot(),
            "server", "src", "Mirage.Server.Shell", "Services", "RemoteServerConnection.cs"));

        // Line comments only. Enough here, and it cannot swallow a string literal containing "//" the
        // way a naive block-comment strip would.
        return string.Join("\n", raw.Split('\n')
            .Select(line => { int i = line.IndexOf("//", StringComparison.Ordinal); return i < 0 ? line : line[..i]; }));
    }

    [Test]
    public void TheRemoteConnection_AsksForStatus()
    {
        Assert.That(Source(), Does.Match(@"WriteLineAsync\(\s*ServerStatus\.RequestStatus\s*\)"),
            "the remote connection never sends ServerStatus.RequestStatus, so the server will treat it as a "
            + "console-only operator and the dashboard and moderation tab stay empty");
    }

    /// <summary>Order matters: the server reads the token off the FIRST line. A status request sent before
    /// it would be taken as the token, fail the comparison, and close the connection.</summary>
    [Test]
    public void ItAsksOnlyAfterTheHandshakeHasPassed()
    {
        string s = Source();
        int token = s.IndexOf("WriteLineAsync(token)", StringComparison.Ordinal);
        int greeting = s.IndexOf("HandshakeOk", StringComparison.Ordinal);
        int request = Regex.Match(s, @"WriteLineAsync\(\s*ServerStatus\.RequestStatus\s*\)").Index;

        Assert.That(token, Is.GreaterThan(-1), "the token is no longer sent");
        Assert.That(request, Is.GreaterThan(token), "the status request must come AFTER the token");
        Assert.That(request, Is.GreaterThan(greeting), "the status request must come AFTER the greeting check");
    }

    /// <summary>One definition, shared. The server's own copy living in its listener is how the client
    /// came to never send it: nothing pointed the two at the same string.</summary>
    [Test]
    public void TheRequestLineIsSharedWithTheServer()
    {
        Assert.That(ServerStatus.RequestStatus, Is.EqualTo("MIRAGE-WANT-STATUS"));

        string listener = File.ReadAllText(Path.Combine(RepoRoot(),
            "server", "src", "Mirage.Server.Host", "Management", "ManagementListener.cs"));

        Assert.That(listener, Does.Contain("ServerStatus.RequestStatus"),
            "the listener declares its own copy of the request line rather than the shared one, so the two "
            + "halves of the protocol can drift apart again");
    }
}
