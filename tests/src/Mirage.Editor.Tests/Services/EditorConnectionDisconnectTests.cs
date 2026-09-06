using Mirage.Editor.Services;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// Whether the end of a session counts as news.
///
/// <para><c>OnDisconnected</c> is what puts the lost-connection dialog on screen. It must fire when the
/// server goes away and must NOT fire when the disconnect was asked for — a spurious one opens a modal over
/// the main window during an ordinary Disconnect, and the editor reads as frozen.</para>
///
/// <para>The distinction cannot be read off the exception. Closing a socket under a pending read raises
/// IOException or ObjectDisposedException far more often than OperationCanceledException, so the loop is told
/// the intent rather than left to infer it. These drive that with a reader that throws an ordinary
/// IOException, which is the case inference got wrong.</para>
/// </summary>
[TestFixture]
public class EditorConnectionDisconnectTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A reader that blocks until the test decides how the read ends: a line, a clean end of
    /// stream, or the IOException a closed socket really produces.</summary>
    private sealed class BlockingReader : TextReader
    {
        private readonly SemaphoreSlim _gate = new(0);
        private Exception? _fail;
        private bool _ended;

        public void FailWith(Exception ex) { _fail = ex; _gate.Release(); }
        public void EndCleanly() { _ended = true; _gate.Release(); }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            if (_fail is not null) throw _fail;
            if (_ended) return null;
            return "";
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
    }

    private static (EditorConnection conn, BlockingReader reader, bool[] fired) Attached()
    {
        var conn = new EditorConnection();
        var reader = new BlockingReader();
        var fired = new bool[1];
        conn.OnDisconnected += () => fired[0] = true;
        conn.AttachTransport(reader, new StringWriter(new StringBuilder()));
        return (conn, reader, fired);
    }

    /// <summary>The reported freeze: an ordinary Disconnect reported itself as a lost connection, and the
    /// handler put an undismissable modal over the main window on the way out.</summary>
    [Test]
    public async Task ADisconnectWeAskedFor_IsNotReportedAsALostConnection()
    {
        var (conn, reader, fired) = Attached();

        await conn.DisconnectAsync();
        reader.FailWith(new IOException("socket closed under the read"));
        await conn.ReceiveLoop!.WaitAsync(Patience);

        Assert.That(fired[0], Is.False, "we closed it, so its ending is not news");
    }

    [Test]
    public async Task AConnectionThatDiesOnItsOwn_StillReportsIt()
    {
        var (conn, reader, fired) = Attached();

        reader.FailWith(new IOException("the server went away"));
        await conn.ReceiveLoop!.WaitAsync(Patience);

        Assert.That(fired[0], Is.True, "nobody asked for this, so the author has to be told");
    }

    [Test]
    public async Task AServerThatClosesTheStreamCleanly_StillReportsIt()
    {
        var (conn, reader, fired) = Attached();

        reader.EndCleanly();
        await conn.ReceiveLoop!.WaitAsync(Patience);

        Assert.That(fired[0], Is.True, "an orderly close from the far end is still a loss on this end");
    }

    /// <summary>The flag must not outlive the session it describes. Without a reset, every genuine loss
    /// after the first deliberate disconnect would be swallowed for the rest of the process — a far worse
    /// bug than the one being fixed, and a silent one.</summary>
    [Test]
    public async Task AFreshSession_ReportsLossesAgain()
    {
        var (conn, reader, _) = Attached();
        await conn.DisconnectAsync();
        reader.FailWith(new IOException("closed"));
        await conn.ReceiveLoop!.WaitAsync(Patience);

        // Re-attach, as a reconnect does.
        bool firedAgain = false;
        conn.OnDisconnected += () => firedAgain = true;
        var reader2 = new BlockingReader();
        conn.AttachTransport(reader2, new StringWriter(new StringBuilder()));

        reader2.FailWith(new IOException("the server went away"));
        await conn.ReceiveLoop!.WaitAsync(Patience);

        Assert.That(firedAgain, Is.True, "the new session is not the old one, and its loss is news");
    }
}
