using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// Attaching dials what is in the boxes NOW.
///
/// <para>🔴 A <c>RemoteServerConnection</c> takes its host, port and token at CONSTRUCTION, and the only
/// other thing that built one was the local/remote toggle. So editing any of the three left the live
/// object holding what it was born with, while the boxes, the saved settings file and the "Attaching
/// to…" line — which reads the view-model, not the connection — all showed the new values.</para>
///
/// <para>Pasting a fresh token and pressing Attach therefore presented the PREVIOUS one and came back
/// "the server refused the token", with every visible piece of state agreeing it should have worked.
/// Restarting the shell appeared to fix it, because construction read the saved file.</para>
///
/// <para>⚠️ Read from SOURCE with comments stripped. Driving it needs a TLS listener and touches the
/// certificate pin store, which is per-user state a test must not write. This catches the rebuild being
/// removed, which is the regression that happened.</para>
/// </summary>
[TestFixture]
public class AttachUsesCurrentSettingsTests
{
    static string RepoRoot()
    {
        string root = typeof(AttachUsesCurrentSettingsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>The view-model with line comments stripped: a mention inside a comment must not satisfy a
    /// claim about what the code does.</summary>
    static string Code()
    {
        string raw = File.ReadAllText(Path.Combine(RepoRoot(),
            "server", "src", "Mirage.Server.Shell", "ViewModels", "MainWindowViewModel.cs"));
        return string.Join("\n", raw.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
    }

    /// <summary>Everything between "private async Task StartAsync()" and the dial.</summary>
    static string BeforeTheDial()
    {
        string code = Code();
        int start = code.IndexOf("private async Task StartAsync()", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(-1), "StartAsync has moved or been renamed");

        int dial = code.IndexOf("_server.StartAsync()", start, StringComparison.Ordinal);
        Assert.That(dial, Is.GreaterThan(start), "StartAsync no longer dials the connection");

        return code[start..dial];
    }

    [Test]
    public void AttachingRebuildsTheConnectionFirst()
    {
        Assert.That(BeforeTheDial(), Does.Contain("AttachConnection(CreateConnection())"),
            "Attach dials a connection built earlier, so a host, port or token edited since is ignored and "
            + "the previous one is presented instead");
    }

    /// <summary>The old one is disposed rather than abandoned: it owns a socket and an SSL stream.</summary>
    [Test]
    public void TheReplacedConnectionIsDisposed()
    {
        string before = BeforeTheDial();
        int dispose = before.IndexOf("_server.Dispose()", StringComparison.Ordinal);
        int rebuild = before.IndexOf("AttachConnection(CreateConnection())", StringComparison.Ordinal);

        Assert.That(dispose, Is.GreaterThan(-1), "the connection being replaced is never disposed");
        Assert.That(dispose, Is.LessThan(rebuild), "the old connection must be disposed BEFORE the new one replaces it");
    }

    /// <summary>The rebuild is remote-only. A local server is a child process this window owns, and
    /// tearing it down on every Start would kill the thing being started.</summary>
    [Test]
    public void OnlyTheRemoteConnectionIsRebuilt()
    {
        string before = BeforeTheDial();
        int guard = before.IndexOf("if (IsRemote)", StringComparison.Ordinal);
        int rebuild = before.IndexOf("AttachConnection(CreateConnection())", StringComparison.Ordinal);

        Assert.That(guard, Is.GreaterThan(-1), "the rebuild is no longer behind an IsRemote check");
        Assert.That(guard, Is.LessThan(rebuild), "the rebuild must sit inside the remote-only branch");
    }
}
