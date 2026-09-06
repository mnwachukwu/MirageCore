using System.Threading.Channels;

namespace Mirage.Server.Host.Management;

/// <summary>
/// One attached operator. Console lines are queued here and drained to the socket by a background task.
///
/// <para>The queue is what keeps a slow or stalled connection off the game thread: <see cref="Enqueue"/>
/// never blocks and never waits on the network. A session that cannot keep up loses lines and is told
/// how many, which is the honest outcome — a console that quietly skips lines looks complete.</para>
/// </summary>
public sealed class ManagementSession
{
    /// <summary>Lines held for a session that is not draining. Roughly a screenful of scrollback; past
    /// this the connection is not keeping up and catching it up is not worth stalling the server.</summary>
    private const int QueueCapacity = 2_000;

    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity)
        {
            // Drop the incoming line rather than the oldest, because dropping is only detectable on this
            // side: TryWrite says no, and that is what lets the operator be told it happened.
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private int _dropped;

    public ManagementSession(string remoteAddress) => RemoteAddress = remoteAddress;

    /// <summary>The address this session connected from, for the audit line each command writes.</summary>
    public string RemoteAddress { get; }

    /// <summary>Whether this operator asked for status snapshots. Off until they say so, so a plain
    /// console client is never handed machine lines it would have to filter.</summary>
    public bool WantsStatus { get; set; }

    /// <summary>Queues a line. Non-blocking; returns immediately whether or not the line was kept.</summary>
    public void Enqueue(string line)
    {
        if (!_queue.Writer.TryWrite(line)) Interlocked.Increment(ref _dropped);
    }

    /// <summary>Stops the drain loop and lets the connection close.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    /// <summary>Writes queued lines to <paramref name="writer"/> until the session completes or the
    /// connection breaks. <paramref name="dropNotice"/> renders the count of lines that were lost.</summary>
    public async Task DrainAsync(TextWriter writer, Func<int, string> dropNotice, CancellationToken ct)
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out string? line))
            {
                int dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0) await writer.WriteLineAsync(dropNotice(dropped)).ConfigureAwait(false);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}
