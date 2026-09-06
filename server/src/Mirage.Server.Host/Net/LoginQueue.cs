using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Security;
using System.Net.Sockets;

namespace Mirage.Server.Host.Net;

/// <summary>
/// The line at a full server.
///
/// <para><b>A waiting connection holds no player slot.</b> It is a socket, a TLS session and a place in
/// this list — nothing about it reaches the game thread until the moment it is let in, which is what makes
/// a queue affordable at all. The single promotion step goes through the same claim path an ordinary
/// connect uses, so it stays serialized against disconnects and the AI tick.</para>
///
/// <para>Runs on accept-loop threads, never the game thread, so the list is guarded by a plain lock.</para>
/// </summary>
public sealed class LoginQueue : IDisposable
{
    /// <summary>How often the head of the line is reconsidered. A slot frees on someone else's
    /// disconnect, which this class is deliberately not wired to — polling twice a second keeps the queue
    /// out of the game thread's business, and half a second is imperceptible to somebody who has been
    /// waiting.</summary>
    private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(500);

    private readonly List<Waiting> _line = [];
    private readonly Lock _gate = new();

    private readonly ServerConfig _config;
    private readonly IPersistenceService _persistence;
    private readonly IClock _clock;
    private readonly ILogger<LoginQueue> _logger;

    /// <summary>Claims a player slot on the game thread. Supplied by the acceptor, which owns that path.
    /// The int is how many slots to hold back — 0 for staff, the reserved count for everyone else.</summary>
    private readonly Func<string, int, CancellationToken, Task<int>> _claimSlot;

    private CancellationTokenSource? _cts;

    public LoginQueue(ServerConfig config, IPersistenceService persistence,
                      Func<string, int, CancellationToken, Task<int>> claimSlot,
                      ILogger<LoginQueue> logger, IClock? clock = null)
    {
        _config = config;
        _persistence = persistence;
        _claimSlot = claimSlot;
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>How many are waiting right now.</summary>
    public int Depth { get { lock (_gate) return _line.Count; } }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = PumpAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        // Release anyone still waiting so their accept task unwinds and closes the socket rather than
        // sitting on a task that will never complete.
        lock (_gate)
        {
            foreach (var w in _line) w.Refuse();
            _line.Clear();
        }
    }

    // ── Joining ───────────────────────────────────────────────────────────────

    /// <summary>Who a connection claims to be, read from the packet it opened with.
    ///
    /// <para>The client only connects once the player has pressed Login, so that first packet already
    /// carries an account name and the language the menus are in. A connection whose first packet is
    /// something else — a tool, a client mid-registration — simply has no identity, waits as an ordinary
    /// player, and gets no reconnect grace.</para></summary>
    /// <param name="Secret">The password as presented. Used ONCE, to confirm the claimed account is
    /// really theirs before it can take a reserved slot, and never stored on the queue entry.</param>
    public readonly record struct Identity(string Account, string Locale, string Secret)
    {
        public static Identity From(string firstLine)
        {
            try
            {
                return PacketSerializer.TryDeserialize(firstLine) switch
                {
                    LoginPacket p => new Identity(p.Username.Trim(), p.Locale, p.Password),
                    // Registering, so there is no account to weigh yet — but the locale is real, and it is
                    // what the refusal has to be written in.
                    NewAccountPacket p => new Identity("", p.Locale, ""),
                    EditorLoginPacket p => new Identity("", p.Locale, ""),
                    _ => new Identity("", "en", ""),
                };
            }
            catch (System.Text.Json.JsonException)
            {
                return new Identity("", "en", "");
            }
        }

        /// <summary>The locale, if the server has that translation loaded. A client is free to ask for one
        /// nobody shipped.</summary>
        public string LocaleOrDefault => ServerStrings.IsLoaded(Locale) ? Locale : "en";

        public bool HasAccount => Account.Length > 0;
    }

    /// <summary>The access level this connection is entitled to, verified against the account file.
    ///
    /// <para>Verified, not taken on trust: an unchecked name would let anyone claim a reserved slot by
    /// typing a moderator's login. Only ever called on the path where a connection is about to be turned
    /// away, so the ordinary connect still costs no account read.</para></summary>
    public async Task<AdminLevel> ResolveAccessAsync(Identity who)
    {
        if (!who.HasAccount || who.Secret.Length == 0) return AdminLevel.Player;
        try
        {
            var account = await _persistence.LoadAccountAsync(who.Account).ConfigureAwait(false);
            if (account is null || !PasswordHasher.Verify(who.Secret, account.Password)) return AdminLevel.Player;
            return account.Access;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // A queue placement is not worth failing a login over; the real login will report the problem.
            _logger.LogDebug(ex, "Could not read {Account} while placing it in the queue", who.Account);
            return AdminLevel.Player;
        }
    }

    /// <summary>Waits for a slot. Returns the slot number, or 0 when the line is full, the connection went
    /// away for good, or the server is shutting down.
    ///
    /// <para>Staff go to the HEAD rather than the back. Somebody has to be able to get in and deal with
    /// whatever filled the server up.</para></summary>
    public async Task<int> WaitAsync(Identity who, AdminLevel access, string remoteIp,
                                     TcpClient client, StreamWriter writer, CancellationToken ct)
    {
        if (!_config.Queue.IsEnabled) return 0;

        Waiting entry;
        lock (_gate)
        {
            // A blip does not cost a place: an entry left behind by a dropped connection is resumed by
            // the next connection from the same account, at the position it was holding.
            entry = Resume(who) ?? new Waiting();
            entry.Adopt(who, access, remoteIp, client, writer);

            if (!_line.Contains(entry))
            {
                if (_line.Count >= _config.Queue.MaxDepth) return 0;
                int at = access >= AdminLevel.Monitor ? StaffInsertionPoint() : _line.Count;
                _line.Insert(at, entry);
            }
        }

        Announce();
        try
        {
            return await entry.Slot.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Remove(entry);
            return 0;
        }
    }

    /// <summary>Behind any staff already waiting, in front of everyone else. Staff arriving second still
    /// queue behind staff who arrived first.</summary>
    private int StaffInsertionPoint()
    {
        int at = 0;
        while (at < _line.Count && _line[at].Access >= AdminLevel.Monitor) at++;
        return at;
    }

    private Waiting? Resume(Identity who)
    {
        if (!who.HasAccount) return null;
        foreach (var w in _line)
        {
            if (w.IsAway && string.Equals(w.Account, who.Account, StringComparison.OrdinalIgnoreCase))
                return w;
        }
        return null;
    }

    private void Remove(Waiting entry)
    {
        bool removed;
        lock (_gate) removed = _line.Remove(entry);
        if (removed) Announce();
    }

    // ── The pump ──────────────────────────────────────────────────────────────

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PumpInterval, ct).ConfigureAwait(false);
                await StepAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // A queue that stops pumping strands everyone in it, so nothing here is allowed to be fatal.
                _logger.LogError(ex, "Login queue pump failed");
            }
        }
    }

    /// <summary>One pass: drop the dead, then try to let the head in.</summary>
    private async Task StepAsync(CancellationToken ct)
    {
        if (!Sweep()) return;

        Waiting head;
        lock (_gate)
        {
            if (_line.Count == 0) return;
            head = _line[0];
            // Their turn, but they are not here. The slot waits for them — for the grace window, which
            // Sweep is timing — rather than going straight to the next in line. That idle is the price of
            // not punishing a dropped connection at the worst possible moment.
            if (head.IsAway) return;
        }

        int keepFree = head.Access >= AdminLevel.Monitor ? 0 : _config.EffectiveReservedSlots;
        int slot = await _claimSlot(head.RemoteIp, keepFree, ct).ConfigureAwait(false);
        if (slot == 0) return;

        lock (_gate)
        {
            // Somebody could have gone away or been swept while the claim was in flight on the game thread.
            if (_line.Count == 0 || !ReferenceEquals(_line[0], head) || head.IsAway)
            {
                head.Slot.TrySetResult(0);
                return;
            }
            _line.RemoveAt(0);
        }

        // A granted slot is never orphaned: whoever receives it goes on to the ordinary receive loop, which
        // fails at once on a dead socket and releases the slot in its finally. The only way nobody
        // receives it is a cancelled token, which here means the server is shutting down.
        if (!head.Slot.TrySetResult(slot))
            _logger.LogDebug("Queued connection had already gone when slot {Slot} came free", slot);

        Announce();
    }

    /// <summary>Marks dropped connections away, removes the ones whose grace ran out, and reports whether
    /// anything is still waiting. Peeking at the socket rather than reading it: the queued client sends
    /// nothing while it waits, and a read here would swallow the packet it sends the moment it is let
    /// in.</summary>
    private bool Sweep()
    {
        long now = _clock.UtcNowUnix;
        List<Waiting>? expired = null;
        int remaining;
        lock (_gate)
        {
            foreach (var w in _line)
            {
                if (!w.IsAway && w.HasGoneQuiet()) w.MarkAway(now);
                if (w.IsAway && now - w.AwaySinceUnix > _config.Queue.GraceSeconds) (expired ??= []).Add(w);
            }
            if (expired is not null)
                foreach (var w in expired) _line.Remove(w);
            remaining = _line.Count;
        }

        if (expired is not null)
        {
            foreach (var w in expired) w.Refuse();
            Announce();
        }
        return remaining > 0;
    }

    /// <summary>Tells everyone where they stand. Pushed on change rather than polled, and skipped for
    /// anyone currently away — they will be told when they come back.</summary>
    private void Announce()
    {
        Waiting[] snapshot;
        lock (_gate) snapshot = [.. _line];

        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i].Tell(i + 1, snapshot.Length);
    }

    // ── One place in the line ─────────────────────────────────────────────────

    /// <summary>
    /// A connection waiting for a slot.
    ///
    /// <para>Survives its own socket: when a connection drops, the entry stays in the line marked away,
    /// and the next connection from the same account adopts it at the position it was holding. That is
    /// what makes a blip cost nothing.</para>
    /// </summary>
    private sealed class Waiting
    {
        private TcpClient? _client;
        private StreamWriter? _writer;
        private int _lastToldPosition;
        private int _lastToldTotal;

        public TaskCompletionSource<int> Slot { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Account { get; private set; } = "";
        public AdminLevel Access { get; private set; }
        public string RemoteIp { get; private set; } = "";
        public bool IsAway { get; private set; }

        /// <summary>When the connection went quiet, in the same Unix seconds every other deadline on this
        /// server is kept in.</summary>
        public long AwaySinceUnix { get; private set; }

        public void Adopt(Identity who, AdminLevel access, string remoteIp,
                          TcpClient client, StreamWriter writer)
        {
            Account = who.Account;
            // The higher of the two, so a staff member who first arrived unauthenticated is not demoted by
            // a later reconnect that failed its check.
            Access = access > Access ? access : Access;
            RemoteIp = remoteIp;
            _client = client;
            _writer = writer;
            IsAway = false;
            // A resumed entry gets a fresh completion source: the old one belongs to the accept task that
            // already unwound, and completing it would tell nobody.
            Slot = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _lastToldPosition = 0;
            _lastToldTotal = 0;
        }

        public void MarkAway(long now)
        {
            IsAway = true;
            AwaySinceUnix = now;
            _client = null;
            _writer = null;
            Slot.TrySetResult(0);
        }

        public void Refuse() => Slot.TrySetResult(0);

        /// <summary>True when the peer has closed. <c>Poll</c> reports readable for both "data is waiting"
        /// and "the other end went away"; <c>Available == 0</c> separates them without consuming
        /// anything.</summary>
        public bool HasGoneQuiet()
        {
            var client = _client;
            if (client is null) return true;
            try { return !client.Connected || (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { return true; }
        }

        /// <summary>Pushes a position, and only when something changed — a queue that repeats itself twice
        /// a second is a queue writing to sockets for no reason.
        ///
        /// <para>The TOTAL counts as a change: somebody joining the back moves nobody, but leaving the
        /// person at the front reading "1 of 1" while four people wait behind them is showing them a
        /// number that is wrong.</para></summary>
        public void Tell(int position, int total)
        {
            if (IsAway || (position == _lastToldPosition && total == _lastToldTotal)) return;
            var writer = _writer;
            if (writer is null) return;
            try
            {
                writer.WriteLine(PacketSerializer.Serialize(
                    new QueueUpdatePacket { Position = position, Total = total }).TrimEnd('\n'));
                writer.Flush();
                _lastToldPosition = position;
                _lastToldTotal = total;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The sweep will notice on its next pass.
            }
        }
    }
}
