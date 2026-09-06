using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace Mirage.Server.Shell.Bench;

/// <summary>
/// One fake player, over the REAL transport.
///
/// <para>Deliberately not an in-process shortcut. A load figure is only worth having if it includes what
/// a connection actually costs: the TLS handshake, the per-packet encryption, the JSON parse and the
/// dispatch hop onto the game thread. Calling the handlers directly would measure a server nobody runs.
/// The framing mirrors <c>TcpClientTransport</c> exactly — newline-delimited JSON over
/// <see cref="SslStream"/>, permissive certificate check, because the server's certificate is
/// self-signed.</para>
///
/// <para><b>Two connections, not one.</b> Creating an account ends with the server saying so and hanging
/// up, which is the real client's flow as well: the account screen and the login screen are separate
/// visits. So is each handshake step — the server answers <c>AddChar</c> with a fresh character list, and
/// selecting a character before that list arrives is asking for a slot the server has not written
/// yet.</para>
///
/// <para>Each instance owns a throwaway account and one character, created against a SCRATCH world —
/// see <see cref="ScratchServer"/>.</para>
/// </summary>
public sealed class SimulatedPlayer : IDisposable
{
    private const string Password = "benchmark-password";

    /// <summary>How long any one handshake step is given. Generous on purpose: under a heavy ramp the
    /// server is slow, not broken, and calling that a failure would report the bench's impatience as the
    /// machine's limit.</summary>
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Beats a player keeps walking one way before picking another. Long enough to actually get
    /// somewhere: a heading re-rolled every beat is a random walk that barely leaves the tile it started
    /// on, and a crowd that never disperses is a crowd whose moves are all refused by the scenery.</summary>
    private const int BeatsPerHeading = 40;

    private readonly string _login;
    private readonly Random _wander;
    /// <summary>Map revisions this player has already been sent, as a real client's disk cache. Without
    /// one the bench would re-download every map on every crossing and measure a load nobody generates.</summary>
    private readonly HashSet<long> _mapCache = [];
    private TcpClient? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Direction _heading;
    private int _beatsLeft;

    private volatile bool _inGame;
    private volatile bool _dropped;

    // Handshake state, all written by the receive loop.
    private volatile bool _accountCreated;
    private volatile int _charLists;
    private volatile string? _refusal;

    public SimulatedPlayer(int ordinal)
    {
        // Long enough to clear the minimum-length rule, and ordinal-keyed so a failure names the client.
        _login = $"bench{ordinal:D5}";
        // Seeded from the ordinal: every player wanders differently, and two runs of the same ramp walk
        // the same route, so a result can be compared against the one before it.
        _wander = new Random(ordinal);
        _heading = (Direction)_wander.Next(4);
    }

    /// <summary>The connection died on its own. Under a ramp this is the symptom that matters most: the
    /// server stopped keeping up rather than refusing politely.</summary>
    public bool Dropped => _dropped;

    /// <summary>What ended a connection that had already got in. Kept apart from
    /// <see cref="FailureReason"/>, which is why a player never arrived — a ramp that dies from drops and
    /// one that dies from refusals are different failures, and the report says which.</summary>
    public string? DropReason { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Creates an account, makes a character, and enters the world. Returns false with
    /// <see cref="FailureReason"/> set rather than throwing, because a ramp expects to hit a wall and
    /// wants to record where.</summary>
    public async Task<bool> JoinAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            await ConnectAsync(host, port, ct).ConfigureAwait(false);
            Send(new NewAccountPacket { Username = _login, Password = Password });
            if (!await WaitFor(() => _accountCreated || _refusal is not null, ct).ConfigureAwait(false)
                || !_accountCreated)
            {
                return Fail(_refusal ?? "the account was never confirmed");
            }

            // The server hung up on us after that. Everything from here happens on a second connection,
            // which is also where the load being measured lives.
            Teardown();
            await ConnectAsync(host, port, ct).ConfigureAwait(false);

            Send(new LoginPacket
            {
                Username = _login,
                Password = Password,
                Major = Constants.ClientMajor,
                Minor = Constants.ClientMinor,
                Revision = Constants.ClientRevision,
            });
            if (!await Step(() => _charLists >= 1, ct).ConfigureAwait(false))
                return Fail(_refusal ?? "login produced no character list");

            Send(new AddCharPacket { Name = _login, Sex = Sex.Male, Class = 1 });
            if (!await Step(() => _charLists >= 2, ct).ConfigureAwait(false))
                return Fail(_refusal ?? "the character was never created");

            Send(new UseCharPacket { Slot = 1 });
            if (!await Step(() => _inGame, ct).ConfigureAwait(false))
                return Fail(_refusal ?? "never reached in-game");

            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException
                                      or ObjectDisposedException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>One beat of ordinary play: keep walking the way this player is headed, and every so often
    /// pick another.
    ///
    /// <para>Movement rather than idling, because a parked connection costs almost nothing and would
    /// flatter the result — walking is what drives the collision checks, observer resolution and broadcast
    /// fan-out the game thread spends its time on. Held headings rather than a fresh direction each beat,
    /// because cycling through four directions walks a player back to where it started: the crowd never
    /// leaves the spawn tile, every move is refused by whatever is next to it, and the measurement quietly
    /// becomes one of rejected moves.</para></summary>
    public void Act()
    {
        if (!_inGame) return;
        if (--_beatsLeft <= 0)
        {
            _heading = (Direction)_wander.Next(4);
            _beatsLeft = BeatsPerHeading;
        }
        Send(new PlayerMovePacket { Dir = _heading, Movement = MovementType.Walking });
    }

    private async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(host, port, _cts.Token).ConfigureAwait(false);

        var ssl = new SslStream(_client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("mirage-server").ConfigureAwait(false);
        _writer = new StreamWriter(ssl, System.Text.Encoding.UTF8) { AutoFlush = true };
        var reader = new StreamReader(ssl, System.Text.Encoding.UTF8);

        _dropped = false;
        _refusal = null;
        _ = ReceiveLoopAsync(reader, _cts.Token);
    }

    private void Teardown()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _writer?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();
        _writer = null;
        _client = null;
        _cts = null;
    }

    private async Task ReceiveLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) { Drop("the server closed the connection"); return; }
                if (line.Length == 0) continue;
                Observe(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Drop(ex.Message); }
        finally { _inGame = false; }
    }

    /// <summary>Reads only what the handshake turns on: the packet's <c>cmd</c>, and an alert's code and
    /// message. Deserializing every packet into its record would put the BENCH under measurable load, and
    /// a fake player has no use for the contents.</summary>
    private void Observe(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("cmd", out var cmd)) return;
            switch (cmd.GetString())
            {
                case PacketNames.AlertMsg:
                    bool created = doc.RootElement.TryGetProperty("code", out var code)
                                   && code.GetInt32() is (int)AlertCode.AccountCreated;
                    if (created) _accountCreated = true;
                    // Anything else is the server turning us away, and it says why. Recorded rather than
                    // waited out: the reason is the most useful thing a failed ramp step can report.
                    else _refusal = doc.RootElement.TryGetProperty("message", out var m)
                        ? m.GetString() : "refused";
                    break;
                case PacketNames.SendChars:
                    _charLists++;
                    break;
                case PacketNames.PlayerInGame:
                    _inGame = true;
                    break;
                case PacketNames.CheckForMap:
                    AnswerMapCheck(doc.RootElement);
                    break;
                case PacketNames.SeamlessCross:
                    // Walking off an edge into an already-loaded neighbor. The client shifts its grid and
                    // asks to be re-synced; that re-sync is real server work, so the bench asks for it too.
                    Send(new RequestRegionSyncPacket());
                    break;
            }
        }
        catch (JsonException) { /* a partial or unexpected line is not the bench's problem */ }
    }

    /// <summary>Completes the map handshake.
    ///
    /// <para><b>Nothing this player sends counts until this is answered.</b> A warp — including the one
    /// that places a character in the world on join — parks the session in <c>GettingMap</c> and the
    /// server drops its movement packets on the floor until the client confirms it has the map. A fake
    /// player that skipped this measured several hundred connections whose traffic was being discarded,
    /// and reported a machine that could take any number of them.</para>
    ///
    /// <para><b>The centre cell and the eight neighbours are different questions.</b> A join asks about
    /// all nine, but only the centre is answered with <c>needmap</c>: that reply re-syncs the whole
    /// region, which asks about all nine again. Answering a neighbour with it is an infinite handshake
    /// that ends with the server aborting the connection. Neighbours get <c>needneighbormap</c>, which
    /// names its cell, and a neighbour already in cache gets no reply at all.</para></summary>
    private void AnswerMapCheck(JsonElement root)
    {
        int mapNum = root.TryGetProperty("mapNum", out var m) ? m.GetInt32() : 0;
        int revision = root.TryGetProperty("revision", out var r) ? r.GetInt32() : 0;
        int col = root.TryGetProperty("col", out var c) ? c.GetInt32() : 1;
        int row = root.TryGetProperty("row", out var w) ? w.GetInt32() : 1;
        if (mapNum <= 0) return;

        bool fresh = _mapCache.Add(((long)mapNum << 32) | (uint)revision);
        if (col == 1 && row == 1)
        {
            Send(fresh
                ? new NeedMapPacket { MapNum = mapNum, Revision = revision }
                : new MapDataClientPacket { MapNum = mapNum });
        }
        else if (fresh)
        {
            Send(new NeedNeighborMapPacket { MapNum = mapNum, Col = col, Row = row });
        }
    }

    /// <summary>Waits for one handshake step, giving up early if the server refused instead.</summary>
    private async Task<bool> Step(Func<bool> done, CancellationToken ct)
    {
        await WaitFor(() => done() || _refusal is not null, ct).ConfigureAwait(false);
        return done();
    }

    private static async Task<bool> WaitFor(Func<bool> done, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StageTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (done()) return true;
            await Task.Delay(25, ct).ConfigureAwait(false);
        }
        return done();
    }

    private bool Fail(string reason)
    {
        FailureReason = reason;
        return false;
    }

    /// <summary>Writes one packet. Locked because two threads send: the beat driver walks every player
    /// calling <see cref="Act"/> while each player's own receive loop answers the map handshake. A
    /// <see cref="StreamWriter"/> shared between them interleaves mid-line, and the server disconnects a
    /// client that sends it half a packet.</summary>
    private void Send(IPacket packet)
    {
        string line = PacketSerializer.Serialize(packet).TrimEnd('\n');
        lock (_sendLock)
        {
            try { _writer?.WriteLine(line); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Drop(ex.Message); }
        }
    }

    private readonly Lock _sendLock = new();

    /// <summary>Records the FIRST thing that went wrong. Everything after it is a consequence.</summary>
    private void Drop(string reason)
    {
        DropReason ??= reason;
        _dropped = true;
    }

    public void Dispose()
    {
        _inGame = false;
        Teardown();
    }
}
