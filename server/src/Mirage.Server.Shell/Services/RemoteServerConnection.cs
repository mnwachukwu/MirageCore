using Mirage.Shared.Protocol;
using Mirage.Shared.Security;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Channels;

namespace Mirage.Server.Shell.Services;

/// <summary>
/// A server on another machine, reached over its management port.
///
/// <para>The same two streams a child process gives you, over a socket: command lines out, console lines
/// in. Nothing here knows what a command means — that stays on the server, where the console lives.</para>
///
/// <para>Stopping DETACHES. A remote server is not this shell's to end, and the operator who wants it
/// down has <c>/shutdown</c> for that, which is the honest way to say so.</para>
/// </summary>
public sealed class RemoteServerConnection(string host, int port, string token) : IServerConnection
{
    /// <summary>The line the server sends once the token is accepted.</summary>
    private const string HandshakeOk = "MIRAGE-MANAGEMENT-OK";

    /// <summary>How long to wait for the connection and the handshake before giving up.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly object _lock = new();
    private TcpClient? _client;
    private SslStream? _ssl;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    // Commands wait here instead of on the UI thread. Unbounded: an operator cannot type fast enough to
    // matter, and dropping a command would be worse than queueing one.
    private Channel<string>? _outbox;

    public event Action<string>? OutputReceived;
    public event Action<ServerState>? StateChanged;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>False: a process on another machine is not this shell's to start or stop.</summary>
    public bool CanSupervise => false;

    public string Host { get; } = host;
    public int Port { get; } = port;

    /// <summary>Both fingerprints from the last refused handshake, or null. Set only alongside
    /// <see cref="RemoteError.IdentityChanged"/>, so the shell can name what changed and offer to
    /// drop the pin rather than only reporting that it refused.</summary>
    public ServerIdentityChangedException? LastIdentityChange { get; private set; }

    /// <summary>Connects and presents the token. Returns null on success, or a message to show.</summary>
    public async Task<string?> StartAsync()
    {
        if (State != ServerState.Stopped) return null;

        LastIdentityChange = null;
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeout);
            await client.ConnectAsync(Host, Port, timeout.Token).ConfigureAwait(false);

            var pinned = new PinnedServer(ServerPinStore.Store, Host, Port);
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, pinned.Validate);
            try
            {
                await ssl.AuthenticateAsClientAsync("mirage-server").ConfigureAwait(false);
            }
            catch (AuthenticationException ex) when (pinned.Trust == ServerTrust.Changed)
            {
                ssl.Dispose();
                client.Dispose();
                LastIdentityChange = pinned.Translate(ex) as ServerIdentityChangedException;
                return RemoteError.IdentityChanged;
            }
            pinned.Commit();

            var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(token).ConfigureAwait(false);
            string? greeting = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (greeting != HandshakeOk)
            {
                // A refusal closes without saying why, so this is the shell's reading of the silence
                // rather than anything the server said. That silence is deliberate — telling a caller
                // WHICH refusal it hit is a hint worth giving nobody — but it costs the operator the one
                // fact they need, because five wrong attempts lock the address out for five minutes and
                // the CORRECT token is refused for the rest of it. Retrying is what sustains it. So the
                // message names both possibilities rather than asserting the wrong one.
                ssl.Dispose();
                client.Dispose();
                return RemoteError.Rejected;
            }

            // Ask for status snapshots. Until this line the socket is a plain console, so BOTH the
            // dashboard and the moderation tab stay empty — they ride the same opt-in stream, which
            // is how one missing line blanked two tabs. A local shell asks by starting the server with
            // --status-events; attached remotely there is no command line, so it asks here.
            await writer.WriteLineAsync(ServerStatus.RequestStatus).ConfigureAwait(false);

            lock (_lock)
            {
                _client = client;
                _ssl = ssl;
                _writer = writer;
                _cts = new CancellationTokenSource();
                _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            }

            _ = ReadLoopAsync(reader, _cts.Token);
            _ = WriteLoopAsync(writer, _outbox.Reader, _cts.Token);
            SetState(ServerState.Running);
            return null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException
                                      or OperationCanceledException)
        {
            client.Dispose();
            return RemoteError.Unreachable;
        }
    }

    /// <summary>What went wrong, as a token the view model turns into a translated sentence — this layer
    /// has no business holding display text.</summary>
    public static class RemoteError
    {
        public const string Rejected = nameof(Rejected);
        public const string Unreachable = nameof(Unreachable);
        public const string IdentityChanged = nameof(IdentityChanged);
    }

    /// <summary>Hands a command to the outbox and returns at once.
    ///
    /// <para>🔴 It never touches the socket. This is called from a button, on the UI thread, and a write to
    /// a TLS stream is synchronous: it blocks until the bytes are away. A connection whose peer has gone but
    /// whose TCP has not yet noticed accepts nothing, so that write waits — and the whole window stops
    /// answering while still presenting its last frame, which reads as a freeze rather than a lost server.</para>
    ///
    /// <para>One channel with one reader also keeps commands in the order they were pressed, which a
    /// write-per-thread would not.</para></summary>
    public void SendCommand(string line) => _outbox?.Writer.TryWrite(line);

    /// <summary>Drains the outbox onto the socket. The only place the writer is used, so no lock is needed
    /// to keep two writes off each other.</summary>
    private async Task WriteLoopAsync(StreamWriter writer, ChannelReader<string> outbox, CancellationToken ct)
    {
        try
        {
            await foreach (string line in outbox.ReadAllAsync(ct).ConfigureAwait(false))
                await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Teardown();
        }
    }

    /// <summary>Detaches. The server keeps running; it was never this shell's process.</summary>
    public Task StopAsync()
    {
        Teardown();
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;   // the server closed
                OutputReceived?.Invoke(line);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            reader.Dispose();
            Teardown();
        }
    }

    private void Teardown()
    {
        lock (_lock)
        {
            if (_client is null) return;
            _outbox?.Writer.TryComplete();
            _outbox = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _writer?.Dispose();
            _ssl?.Dispose();
            _client.Dispose();
            _cts = null;
            _writer = null;
            _ssl = null;
            _client = null;
        }
        SetState(ServerState.Stopped);
    }

    private void SetState(ServerState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose() => Teardown();
}
