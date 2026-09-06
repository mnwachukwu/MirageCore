using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Mirage.Server.Host.Net;

/// <summary>
/// Accepts incoming TCP connections on <see cref="Constants.GamePort"/> and routes each one
/// to either a game-player slot or an editor slot based on the first JSON packet received.
///
/// Routing rule (a single listening port serves every client):
///   first packet cmd == "editorLogin"  → EditorSession slot
///   any other packet                   → game-player slot (first packet is re-dispatched)
/// </summary>
public sealed class TcpConnectionAcceptor : IDisposable
{
    private readonly System.Net.Sockets.TcpListener _listener;
    private readonly TcpPacketDispatcher _dispatcher;
    private readonly PacketHandler _handler;
    private readonly EditorPacketHandler _editorHandler;
    private readonly PlayerManager _pm;
    private readonly EditorSessionManager _editors;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly GameLoop _gameLoop;
    private readonly ILogger<TcpConnectionAcceptor> _logger;
    private readonly ILogger _receiveLogger;
    private readonly int _port;
    private readonly X509Certificate2 _cert;
    private readonly ServerConfig _config;
    private readonly LoginQueue _queue;

    public int Port => _port;

    /// <summary>How many are waiting for a slot. Read by the status snapshot; the queue itself is private
    /// because nothing else has any business reaching into it.</summary>
    public int QueueDepth => _queue.Depth;

    private CancellationTokenSource? _cts;

    public TcpConnectionAcceptor(
        TcpPacketDispatcher dispatcher,
        PacketHandler handler,
        EditorPacketHandler editorHandler,
        PlayerManager pm,
        EditorSessionManager editors,
        JoinLeaveSystem joinLeave,
        GameLoop gameLoop,
        Mirage.Server.Core.Persistence.IPersistenceService persistence,
        ILogger<TcpConnectionAcceptor> logger,
        ILoggerFactory loggerFactory,
        ServerConfig config)
    {
        _dispatcher = dispatcher;
        _handler = handler;
        _editorHandler = editorHandler;
        _pm = pm;
        _editors = editors;
        _joinLeave = joinLeave;
        _gameLoop = gameLoop;
        _logger = logger;
        _receiveLogger = loggerFactory.CreateLogger("Mirage.Server.Host.Net.ReceiveLoop");
        // serverconfig.json, not appsettings.json: the port is something an operator sets about their
        // SERVER, so it sits with the language and the game rules rather than with the log pipeline.
        _port = config.Port;
        _config = config;

        // Constructed here rather than injected: the queue's one job is to hand out slots, and claiming
        // one is this class's method. Injecting it would need the container to close that loop.
        _queue = new LoginQueue(config, persistence, ClaimSlotAsync,
            loggerFactory.CreateLogger<LoginQueue>());

        _listener = new System.Net.Sockets.TcpListener(IPAddress.Any, _port);

        try
        {
            _cert = SelfSignedCertificate.LoadOrCreate();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Running with a temporary certificate: clients cannot pin this server, and anyone who has " +
                "connected before will see it as changed.");
            _cert = SelfSignedCertificate.Create();
        }

        _logger.LogInformation("Server certificate fingerprint (SHA-256): {Fingerprint}",
            SelfSignedCertificate.FingerprintForDisplay(_cert));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener.Start(backlog: 10);
        LocalizedLog.Info(_logger, ServerStrings.Net_ListeningOnPort, ("Port", _port));
        _queue.Start(_cts.Token);
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        Stop();
        _queue.Dispose();
        _cts?.Dispose();
        _cert.Dispose();
    }

    // ── Accept loop ───────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                // Handle each connection on a pooled thread; fire-and-forget
                _ = Task.Run(() => HandleConnectionAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Accept loop stopping (listener closed)");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting TCP connection");
            }
        }
    }

    // ── Per-connection handler ────────────────────────────────────────────────

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        LocalizedLog.Info(_logger, ServerStrings.Net_NewConnection, ("Ip", remoteIp));

        NetworkStream stream = client.GetStream();
        using var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(_cert, clientCertificateRequired: false,
                checkCertificateRevocation: false).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            return;
        }

        var reader = new StreamReader(ssl, System.Text.Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(ssl, System.Text.Encoding.UTF8, leaveOpen: true)
        { AutoFlush = false };

        string? firstLine;
        try
        {
            firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            client.Dispose();
            return;
        }

        if (string.IsNullOrEmpty(firstLine))
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            client.Dispose();
            return;
        }

        // ── Route by first packet ─────────────────────────────────────────────

        if (firstLine.Contains($"\"{PacketNames.EditorLogin}\""))
        {
            await RouteAsEditorAsync(client, reader, writer, firstLine, ct).ConfigureAwait(false);
        }
        else
        {
            await RouteAsPlayerAsync(client, reader, writer, firstLine, remoteIp, ct).ConfigureAwait(false);
        }
    }

    private async Task RouteAsEditorAsync(
        TcpClient client, StreamReader reader, StreamWriter writer,
        string firstLine, CancellationToken ct)
    {
        // Claim an editor slot on the game thread (editor packets edit shared maps, so everything the
        // editor touches is serialized there).
        int editorIndex = await ClaimEditorSlotAsync(ct).ConfigureAwait(false);
        if (editorIndex == 0)
        {
            _logger.LogWarning(ServerStrings.Get(ServerStrings.Net_EditorRefusedFull));
            await RefuseAsync(client, reader, writer,
                ServerStrings.ForLocale(LoginQueue.Identity.From(firstLine).LocaleOrDefault,
                                        ServerStrings.Net_EditorFullAlert)).ConfigureAwait(false);
            return;
        }

        _dispatcher.RegisterEditor(editorIndex, client, writer);
        _logger.LogDebug("Editor connection assigned to slot {Slot}", editorIndex);

        // Same greeting the game client gets. An editor is a client too: it needs to know what world it
        // just reached before it acts on anything the world sends back.
        _dispatcher.SendToEditor(editorIndex, BuildHello());

        try
        {
            await ReceiveLoop.RunEditorAsync(editorIndex, reader, firstLine,
                _editorHandler, _dispatcher, _gameLoop, _receiveLogger, ct).ConfigureAwait(false);
        }
        finally
        {
            // Locks go with the socket: a crashed editor must not leave a record shut.
            _gameLoop.Post(() =>
            {
                _editorHandler.OnEditorDisconnected(editorIndex);
                _editors.Disconnect(editorIndex);
            });
            await writer.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            client.Dispose();
            LocalizedLog.Info(_logger, ServerStrings.Net_EditorDisconnected, ("Slot", editorIndex));
        }
    }

    /// <summary>What this server is, for whoever just connected. Built from the config on every send, so
    /// the two routes cannot drift into greeting a client differently.</summary>
    private ServerHelloPacket BuildHello() => new()
    {
        MaxPlayers = _pm.Slots,
        GameName = _config.GameName,
        Records = _config.Records,
    };

    private async Task<int> ClaimEditorSlotAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            var session = _editors.FindOpenSlot();
            if (session is not null) session.IsConnected = true;
            tcs.SetResult(session?.Index ?? 0);
        });
        try { return await tcs.Task.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return 0; }   // shutting down
    }

    private async Task RouteAsPlayerAsync(
        TcpClient client, StreamReader reader, StreamWriter writer,
        string firstLine, string remoteIp, CancellationToken ct)
    {
        // Claim a player slot ON the game thread, serialized with disconnects + the AI tick — otherwise
        // two connects (or a connect racing a disconnect) could collide on FindOpenSlot. The general
        // public may not take the last few slots; those are held for staff, so somebody can always get in
        // and deal with whatever filled the server up.
        int slot = await ClaimSlotAsync(remoteIp, _config.EffectiveReservedSlots, ct).ConfigureAwait(false);
        var who = LoginQueue.Identity.From(firstLine);

        if (slot == 0)
        {
            // Only now is it worth finding out who this is: verifying an account costs a file read, and on
            // the ordinary path — a slot was free — nobody needs to know.
            var access = await _queue.ResolveAccessAsync(who).ConfigureAwait(false);
            if (access >= AdminLevel.Monitor)
                slot = await ClaimSlotAsync(remoteIp, keepFree: 0, ct).ConfigureAwait(false);

            if (slot == 0)
            {
                // Queued and refused are different events and the log says which. They read the same from
                // here only because the second is what the first turns into when the line is also full.
                if (_config.Queue.IsEnabled)
                    LocalizedLog.Info(_logger, ServerStrings.Net_PlayerQueued, ("Ip", remoteIp));

                slot = await _queue.WaitAsync(who, access, remoteIp, client, writer, ct).ConfigureAwait(false);
                if (slot == 0)
                    LocalizedLog.Warn(_logger, ServerStrings.Net_PlayerRefusedFull, ("Ip", remoteIp));
            }
        }

        if (slot == 0)
        {
            // The line was full, or they gave up waiting, or the server is going down. Either way this is
            // the end of the road — and it is said in THEIR language, which the packet they opened with
            // told us. The operator's log line above is in the operator's.
            await RefuseAsync(client, reader, writer,
                ServerStrings.ForLocale(who.LocaleOrDefault, ServerStrings.Net_ServerFullAlert)).ConfigureAwait(false);
            return;
        }

        var sp = _pm[slot];
        _dispatcher.RegisterPlayer(slot, client, writer);
        _logger.LogDebug("Player connection from {Ip} assigned to slot {Slot}", remoteIp, slot);

        // The first thing on the wire, before the login packet is even dispatched: a client compiles
        // against the protocol's ceilings, and this is where it finds out what THIS server's are.
        _dispatcher.SendTo(slot, BuildHello());

        try
        {
            await ReceiveLoop.RunPlayerAsync(slot, reader, firstLine,
                _handler, _dispatcher, _gameLoop, _receiveLogger, ct).ConfigureAwait(false);
        }
        finally
        {
            // Game-state leave + slot reset, serialized on the game thread.  LeftGame may turn the player
            // into a combat ghost (which keeps the slot alive); only reset the slot if it didn't.
            _gameLoop.Post(() =>
            {
                _joinLeave.LeftGame(slot);
                if (!sp.IsGhost) ResetPlayerSlot(sp);
            });

            await writer.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            client.Dispose();
            LocalizedLog.Info(_logger, ServerStrings.Net_PlayerDisconnected, ("Slot", slot), ("Ip", remoteIp));
        }
    }

    /// <summary>Closes a connection with one localized sentence. The alert is the last thing this socket
    /// will carry, so it is written and flushed before everything is torn down.</summary>
    private static async Task RefuseAsync(TcpClient client, StreamReader reader, StreamWriter writer, string message)
    {
        try
        {
            await writer.WriteLineAsync(
                PacketSerializer.Serialize(PacketBuilder.Alert(message)).TrimEnd('\n')).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // They hung up first. Nothing left to say.
        }
        await writer.DisposeAsync().ConfigureAwait(false);
        reader.Dispose();
        client.Dispose();
    }

    // Runs FindOpenSlot + slot reservation on the game thread and hands the result back to this accept
    // task.  Returns 0 when the server is full (or during shutdown, via cancellation).  keepFree is how
    // many slots are held back from this caller — the reserved count for the public, 0 for staff.
    private async Task<int> ClaimSlotAsync(string remoteIp, int keepFree, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            int s = _pm.FindOpenSlot(keepFree);
            if (s != 0)
            {
                var p = _pm[s];
                p.IsConnected = true;
                p.RemoteIp = remoteIp;
            }
            tcs.SetResult(s);
        });
        try { return await tcs.Task.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return 0; }   // shutting down
    }

    private static void ResetPlayerSlot(Mirage.Server.Core.Players.ServerPlayer sp)
    {
        sp.IsConnected = false;
        sp.InGame = false;
        sp.Login = "";
        sp.Password = "";
        sp.CharNum = 0;
        sp.GettingMap = false;
        sp.GhostTransferSlot = 0;
        sp.PartyPlayer = 0;
        sp.InParty = false;
        sp.PartyStarter = false;
        for (int i = 1; i <= Constants.MaxChars; i++)
            sp.Chars[i] = new Mirage.Shared.Records.PlayerRecord();
        sp.Bank = Mirage.Shared.Records.AccountRecord.NewBank();   // drop the previous account's shared vault
    }
}
