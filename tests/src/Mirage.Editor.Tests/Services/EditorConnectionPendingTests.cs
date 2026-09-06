using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// What happens to a request whose answer never comes.
///
/// <para>Every request awaits a <c>TaskCompletionSource</c> that only the receive loop can complete, and
/// callers pass no cancellation token. So anything that ends the loop — the server closing the socket, a
/// read throwing — has to release the waiters itself, or the await never returns and the UI operation
/// that started it is stuck with no way out but restarting the editor.</para>
/// </summary>
[TestFixture]
public class EditorConnectionPendingTests
{
    /// <summary>How long a released caller is given before the test calls it a lock-up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A reader the test drives line by line, and can close on demand — the server end of the
    /// wire, standing in for a socket.</summary>
    private sealed class ScriptedReader : TextReader
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _lines = new();

        public void Send(string line) => _lines.Add(line);
        public void CloseStream() => _lines.CompleteAdding();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            await Task.Yield();
            try { return _lines.Take(ct); }
            catch (InvalidOperationException) { return null; }   // completed = the far end hung up
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
    }

    private static (EditorConnection conn, ScriptedReader server) Connect()
    {
        var conn = new EditorConnection();
        var server = new ScriptedReader();
        conn.AttachTransport(server, new StringWriter(new StringBuilder()));
        return (conn, server);
    }

    /// <summary>Asserts a request was released. The failure being pinned here IS an unbounded wait, so
    /// this must not reproduce it by waiting unboundedly itself: a regression has to fail the run, not
    /// hang it.</summary>
    private static async Task AssertReleased(Task request)
    {
        Assert.That(await Task.WhenAny(request, Task.Delay(Patience)), Is.SameAs(request),
            "the caller is still waiting on an answer that can never arrive — this is the lock-up");
        Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>(),
            "and it is told so, rather than completing with a silent null");
    }

    /// <summary>The reported lock-up: a request outstanding when the connection dies.</summary>
    [Test]
    public async Task ARequestInFlight_IsReleasedWhenTheConnectionDrops()
    {
        var (conn, server) = Connect();

        var request = conn.RequestItemAsync(itemNum: 1);
        Assert.That(request.IsCompleted, Is.False, "precondition: it is waiting on a response");

        server.CloseStream();                            // the server hangs up
        await conn.ReceiveLoop!.WaitAsync(Patience);     // the loop notices and ends

        await AssertReleased(request);
    }

    /// <summary>A second bulk request for the same response command displaces the first. The displaced
    /// caller is still awaiting, so it has to be released too.</summary>
    [Test]
    public async Task ADisplacedBulkRequest_IsReleased()
    {
        var (conn, _) = Connect();

        var first = conn.RequestAllItemsAsync();
        var second = conn.RequestAllItemsAsync();

        await AssertReleased(first);
        Assert.That(second.IsCompleted, Is.False, "while the newer one is still live");
    }

    [Test]
    public async Task DisconnectAlsoReleasesAWaitingRequest()
    {
        var (conn, _) = Connect();

        var request = conn.RequestItemAsync(itemNum: 1);
        await conn.DisconnectAsync();

        await AssertReleased(request);
    }

    /// <summary>The happy path, so the releases above cannot be mistaken for "everything is cancelled".</summary>
    [Test]
    public async Task AResponseStillCompletesItsRequest()
    {
        var (conn, server) = Connect();

        var request = conn.RequestItemAsync(itemNum: 7);
        server.Send(PacketSerializer.Serialize(new UpdateItemPacket { ItemNum = 7 }).TrimEnd('\n'));

        var packet = await request.WaitAsync(Patience);

        Assert.That(packet, Is.Not.Null);
        Assert.That(packet!.ItemNum, Is.EqualTo(7));
    }

    /// <summary>A map slice completes the request that asked for it.
    ///
    /// <para>Maps are the one family fetched a slice at a time, so this reply is the one a whole-world read
    /// waits on twenty times over. Unrouted, it is taken for a server-pushed broadcast and the read stops on
    /// its first slice — with the status line still on the message that precedes the maps, since that line
    /// only advances once a slice lands.</para></summary>
    [Test]
    public async Task AMapSliceCompletesItsRequest()
    {
        var (conn, server) = Connect();

        var request = conn.RequestAllMapsAsync(start: 1, count: 50);
        server.Send(PacketSerializer.Serialize(new EditorAllMapsPacket
        {
            Start = 1,
            Total = 100,
            Maps = [new SendMapPacket { MapNum = 1 }],
        }).TrimEnd('\n'));

        // WaitAsync rather than a bare await: the regression this pins IS an unbounded wait, and a test
        // that reproduces it hangs the run instead of failing it.
        var packet = await request.WaitAsync(Patience);

        Assert.That(packet, Is.Not.Null);
        Assert.That(packet!.Start, Is.EqualTo(1));
        Assert.That(packet.Maps, Has.Length.EqualTo(1));
    }

    /// <summary>Every bulk reply the server can send is routable back to the request that awaits it.
    ///
    /// <para>A reply with no case in <c>BulkCommandOf</c> is indistinguishable from a live broadcast, so the
    /// waiter parked under its command name is never completed. No exception is thrown and nothing is
    /// logged — the caller simply awaits a task nothing will finish, and no request passes a cancellation
    /// token. Adding a bulk fetch is therefore two edits, and this fails on the second one being
    /// missed.</para>
    ///
    /// <para>Scoped to the <c>EditorAll*</c> replies, which are the ones that grow: a new collection brings a
    /// new bulk fetch with it.</para></summary>
    [Test]
    public void EveryBulkReplyRoutesHome()
    {
        var replies = typeof(EditorAllMapsPacket).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IPacket).IsAssignableFrom(t)
                        && t.Name.StartsWith("EditorAll") && t.Name.EndsWith("Packet"))
            .OrderBy(t => t.Name)
            .ToArray();

        // A name pattern that matched nothing would make the assertion below pass on an empty set.
        Assert.That(replies, Has.Length.GreaterThanOrEqualTo(9),
            "found no bulk replies to check — the packets were renamed and this test now proves nothing");

        string[] unrouted = [.. replies
            .Select(t => (IPacket)Activator.CreateInstance(t)!)
            .Where(p => EditorConnection.BulkCommandOf(p).Length == 0)
            .Select(p => p.GetType().Name)];

        Assert.That(unrouted, Is.Empty,
            "these replies have no route home, so whoever asked for one waits forever: "
            + string.Join(", ", unrouted));
    }
}
