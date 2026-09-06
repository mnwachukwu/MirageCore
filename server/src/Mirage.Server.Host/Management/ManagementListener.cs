using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Localization;
using Mirage.Server.Host.Net;
using Mirage.Server.Host.Services;
using Mirage.Shared.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Mirage.Server.Host.Management;

/// <summary>
/// Serves the server's console over a socket, so the management shell can attach to a server it is not
/// hosting.
///
/// <para>This is a TRANSPORT, not a second command protocol: a client writes the same lines it would type
/// into stdin and reads the same lines the server prints to stdout. <see cref="ConsoleCommands"/> stays
/// the only place that knows what a command does.</para>
///
/// <para>Off unless both a port and a token are configured. Traffic is TLS, because the token crosses the
/// wire.</para>
/// </summary>
public sealed class ManagementListener : IHostedService, IDisposable
{
    private ManagementConfig _config;
    private readonly ConsoleTee _tee;
    private readonly ConsoleCommands _commands;
    private readonly ILogger<ManagementListener> _logger;
    private readonly AuthThrottle _throttle = new();
    private readonly ConcurrentDictionary<ManagementSession, byte> _sessions = new();

    private TcpListener? _listener;
    private X509Certificate2? _cert;
    private CancellationTokenSource? _cts;

    private readonly StatusBroadcaster _status;

    public ManagementListener(
        ServerConfig config,
        ConsoleCommands commands,
        ConsoleTee tee,
        StatusBroadcaster status,
        ILogger<ManagementListener> logger)
    {
        _config = config.Management;
        _commands = commands;
        _tee = tee;
        _status = status;
        _logger = logger;
    }

    /// <summary>The line a client sends after the token to ask for status snapshots.
    /// <see cref="ServerStatus.RequestStatus"/> is the definition; this alias keeps the accept loop
    /// reading the name it always did.</summary>
    public const string RequestStatus = ServerStatus.RequestStatus;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Whether operators can attach right now. Distinct from <see cref="ManagementConfig.IsEnabled"/>,
    /// which says what the CONFIG asks for — a port already in use leaves the two disagreeing, and the
    /// operator needs to be told which.</summary>
    public bool IsListening => _listener is not null;

    /// <summary>The port actually bound, or 0. Reads off the socket rather than the config so a stale
    /// setting cannot be reported as a live one.</summary>
    public int BoundPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    /// <summary>How many operators are attached.</summary>
    public int AttachedOperators => _sessions.Count;

    /// <summary>The config as it stands, for a status readout.</summary>
    public ManagementConfig Current => _config;

    private CancellationToken _hostToken = CancellationToken.None;

    public Task StartAsync(CancellationToken ct)
    {
        // Kept so a later Reconfigure links its own CTS to the same host lifetime rather than to nothing.
        _hostToken = ct;
        Listen();
        return Task.CompletedTask;
    }

    /// <summary>Bind and start accepting, or do nothing when the config says off.
    ///
    /// <para>Returns null on success, or the reason it did not start — which the console command surfaces
    /// to the operator who just asked for it. Refusing a port with no token is deliberate: an open port
    /// with no secret is an unauthenticated console, and half-enabling one is worse than staying off.</para></summary>
    private string? Listen()
    {
        if (_config.Port <= 0) return null;

        if (_config.Token.Length == 0)
        {
            LocalizedLog.Error(_logger, ServerStrings.Management_TokenMissing);
            return ServerStrings.Get(ServerStrings.Management_TokenMissing);
        }

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_hostToken);
            // The same identity the game listener presents: one server, one fingerprint.
            _cert = SelfSignedCertificate.LoadOrCreate();
            _listener = new TcpListener(IPAddress.Any, _config.Port);
            _listener.Start(backlog: 4);
        }
        catch (SocketException ex)
        {
            // Almost always the port being in use. The listener stays off rather than half-built, so a
            // later Reconfigure starts from a clean slate.
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            return ex.Message;
        }

        _tee.LineWritten += Broadcast;
        // Status rides the socket DIRECTLY, not the console tee: it must reach only the operators who
        // asked, and must never land in the local console the tee feeds.
        _status.MachineLineReady += BroadcastStatus;

        LocalizedLog.Info(_logger, ServerStrings.Management_Listening, ("Port", _config.Port));
        _ = AcceptLoopAsync(_cts.Token);
        return null;
    }

    /// <summary>Stop listening, without ending the process. Attached operators are dropped — their socket
    /// is the thing being turned off.</summary>
    private void Unlisten()
    {
        if (_listener is null) return;
        _tee.LineWritten -= Broadcast;
        _status.MachineLineReady -= BroadcastStatus;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { _listener.Stop(); } catch (SocketException) { }
        _listener = null;
        foreach (var session in _sessions.Keys) session.Complete();
        _sessions.Clear();
        SyncOperatorCount();
    }

    /// <summary>Apply a new management config to the RUNNING server: stop, take the new settings, start
    /// again. Returns null when the result is what the config asked for, or the reason it is not.
    ///
    /// <para>This is what lets a headless operator turn remote access on without a restart, which is the
    /// deployment that most needs it and the one that cannot edit a file and bounce the process
    /// casually.</para></summary>
    public string? Reconfigure(ManagementConfig next)
    {
        Unlisten();
        _config = next;
        return Listen();
    }

    public Task StopAsync(CancellationToken ct)
    {
        Unlisten();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cert?.Dispose();
    }

    // Fans one console line out to every attached operator. Runs on whichever thread wrote the line —
    // often the game thread — so it must only ever queue.
    private void Broadcast(string line)
    {
        foreach (var session in _sessions.Keys) session.Enqueue(line);
    }

    // Only to operators who asked. Same non-blocking queue as console output.
    private void BroadcastStatus(string line)
    {
        foreach (var session in _sessions.Keys)
            if (session.WantsStatus) session.Enqueue(line);
    }

    // Kept on the broadcaster so the snapshot has one assembly point rather than reaching back in here.
    private void SyncOperatorCount() => _status.OperatorCount = _sessions.Count;

    // ── Accept loop ───────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _ = Task.Run(() => HandleAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                _logger.LogError(ex, "Error accepting management connection");
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var connection = client;
        string address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";

        if (_throttle.IsLockedOut(address))
        {
            LocalizedLog.Warn(_logger, ServerStrings.Management_AuthLockedOut, ("Ip", address));
            return;
        }

        using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(_cert!, clientCertificateRequired: false,
                checkCertificateRevocation: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
            return;
        }

        using var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = false };

        if (!await AuthenticateAsync(reader, writer, address, ct).ConfigureAwait(false)) return;

        var session = new ManagementSession(address);
        _sessions[session] = 0;
        SyncOperatorCount();
        LocalizedLog.Info(_logger, ServerStrings.Management_OperatorAttached, ("Ip", address));

        // Two directions at once: queued console lines out, command lines in. Whichever finishes first
        // ends the session, because either one stopping means the connection is over. The linked token
        // is what stops the OTHER one — a blocked read does not notice that the write side died.
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = session.DrainAsync(writer, DropNotice, sessionCts.Token);
        var intake = ReadCommandsAsync(reader, session, sessionCts.Token);
        try
        {
            await Task.WhenAny(pump, intake).ConfigureAwait(false);
        }
        finally
        {
            // Unregistered first so nothing new is queued, then completed and cancelled so both loops
            // finish. BOTH are awaited before the writer goes out of scope — leaving the drain running
            // against a disposed stream would fault a task nobody is watching.
            _sessions.TryRemove(session, out _);
            SyncOperatorCount();
            session.Complete();
            await sessionCts.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(pump, intake).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }

            LocalizedLog.Info(_logger, ServerStrings.Management_OperatorDetached, ("Ip", address));
        }
    }

    private static string DropNotice(int count) =>
        ServerStrings.Format(ServerStrings.Management_LinesDropped, ("Count", count));

    // ── Auth ──────────────────────────────────────────────────────────────────

    /// <summary>Reads the first line and checks it against the configured token. A refusal says nothing
    /// about why, and the token is never written anywhere — not to the log, not at Debug.</summary>
    private async Task<bool> AuthenticateAsync(
        StreamReader reader, StreamWriter writer, string address, CancellationToken ct)
    {
        string? presented;
        try { presented = await reader.ReadLineAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { return false; }

        if (presented is not null && Matches(presented))
        {
            _throttle.RecordSuccess(address);
            await writer.WriteLineAsync(HandshakeOk).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }

        if (_throttle.RecordFailure(address))
            LocalizedLog.Warn(_logger, ServerStrings.Management_AuthLockedOut, ("Ip", address));
        else
            LocalizedLog.Warn(_logger, ServerStrings.Management_AuthFailed, ("Ip", address));
        return false;
    }

    /// <summary>The one line a client waits for before it considers itself attached.</summary>
    public const string HandshakeOk = "MIRAGE-MANAGEMENT-OK";

    // Compared over the full length in constant time, so a wrong token cannot be narrowed down by how
    // long the refusal took.
    private bool Matches(string presented)
    {
        byte[] expected = Encoding.UTF8.GetBytes(_config.Token);
        byte[] actual = Encoding.UTF8.GetBytes(presented.Trim());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // ── Command intake ────────────────────────────────────────────────────────

    private async Task ReadCommandsAsync(StreamReader reader, ManagementSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { break; }

            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;

            // Not a command — a capability request. Handled before the audit line so it never appears as
            // something an operator "ran".
            if (line == RequestStatus)
            {
                session.WantsStatus = true;
                _status.Publish();
                continue;
            }

            // Logged, not printed: the audit belongs in the log file with a timestamp. It reaches every
            // attached operator and the local console anyway, because the log pipeline writes to the
            // same stdout this listener is teeing.
            LocalizedLog.Info(_logger, ServerStrings.Management_RemoteCommand,
                ("Ip", session.RemoteAddress), ("Command", line));
            _commands.Execute(line);
        }
    }
}
