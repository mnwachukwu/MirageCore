using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Security;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Mirage.Editor.Services;

public sealed class EditorConnection : IDisposable
{
    private TcpClient? _client;
    // The transport as TEXT, not as sockets: everything below this line uses ReadLineAsync, WriteAsync
    // and FlushAsync and nothing else, so the receive loop and the request bookkeeping can be driven
    // over any reader/writer pair. See AttachTransport.
    private TextReader? _reader;
    private TextWriter? _writer;
    private CancellationTokenSource? _cts;

    // Pending on-demand record requests keyed by (responseCmd, recordNum)
    private readonly ConcurrentDictionary<(string, int), TaskCompletionSource<IPacket>> _pending = new();
    // Pending bulk (all-records) requests keyed by responseCmd
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPacket>> _pendingBulk = new();

    public bool IsConnected => _client?.Connected == true;

    /// <summary>Runs the connection over an arbitrary reader/writer pair rather than a socket, and starts
    /// the receive loop against it. The loop, the pending-request bookkeeping and the disconnect path are
    /// the same code the real transport runs; only the stream underneath differs.</summary>
    internal void AttachTransport(TextReader reader, TextWriter writer)
    {
        _closingDeliberately = false;
        _reader = reader;
        _writer = writer;
        _cts = new CancellationTokenSource();
        ReceiveLoop = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>The running receive loop, so a caller can await its completion.</summary>
    internal Task? ReceiveLoop { get; private set; }

    public event Action<string>? OnServerMessage;
    public event Action? OnDisconnected;

    /// <summary>A recognized packet the server pushed to us that isn't a pending request's response — i.e. a
    /// SendToAll live broadcast (e.g. UpdateNpc after another editor's save). Fires on the receive-loop thread,
    /// so subscribers MUST marshal any UI work to the UI thread.</summary>
    public event Action<IPacket>? OnLivePacket;

    /// <summary>The outcome of a connect + editor-login handshake. <see cref="Data"/> is non-null only when
    /// <see cref="Success"/> is true; <see cref="Message"/> carries the server's reason on failure and its
    /// greeting on success. Named rather than a four-element tuple so a caller cannot bind the failure
    /// message to the success flag's neighbor and act on the wrong one.</summary>
    public readonly record struct AuthResult(bool Success, string Message, EditorDataPacket? Data, AdminLevel AccessLevel)
    {
        public static AuthResult Failed(string message) => new(false, message, null, AdminLevel.Player);
    }

    /// <summary>Where this session is actually attached — what the connect dialog was given, not what the
    /// saved defaults happen to say. The two differ whenever an operator types a host or port without
    /// saving it, which is exactly when knowing which server you are editing matters most.</summary>
    public string Endpoint { get; private set; } = "";

    /// <summary>The account this editor session authenticated as. Read by the account browser, which must
    /// not offer to change your OWN access — the server refuses it, and a control that looks live but
    /// silently does nothing is worse than one that is plainly disabled.</summary>
    public string Login { get; private set; } = "";

    /// <summary>What the server calls this connection, minted fresh at every login. Record locks are held per
    /// session rather than per account, so this — not <see cref="Login"/> — is what tells this editor's locks
    /// from those of another window signed in as the same person. Blank while offline.</summary>
    public string SessionId { get; private set; } = "";

    public async Task<AuthResult> ConnectAndAuthAsync(string host, int port, string username, string password,
                                                     CancellationToken ct = default)
    {
        // A fresh session: whatever ended the LAST one is no longer true of this one. Without this reset a
        // genuine loss after any deliberate disconnect would be swallowed for the rest of the process.
        _closingDeliberately = false;
        Endpoint = $"{host}:{port}";
        Login = username;
        SessionId = "";
        EditorLog.Info("Connecting to {Endpoint} as {Login}.", Endpoint, username);
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, ct);

        var stream = _client.GetStream();
        var pinned = new PinnedServer(ServerPinStore.Store, host, port);
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false, pinned.Validate);
        try
        {
            await ssl.AuthenticateAsClientAsync("mirage-server");
        }
        catch (AuthenticationException ex)
        {
            EditorLog.Error(ex, "TLS handshake with {Endpoint} failed.", Endpoint);
            await DisconnectAsync();
            throw pinned.Translate(ex);
        }
        pinned.Commit();
        _reader = new StreamReader(ssl, leaveOpen: true);
        _writer = new StreamWriter(ssl, leaveOpen: true) { AutoFlush = true };

        await _writer.WriteAsync(PacketSerializer.Serialize(new EditorLoginPacket
        {
            Username = username,
            Password = password,
            Locale = AppSettings.Current.Language,
        }));

        var (responsePacket, closed) = await ReadHandshakeAsync(ct);
        if (closed)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ClosedUnexpectedly));
        if (responsePacket is not EditorLoginResponsePacket response)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_UnexpectedResponse));

        if (!response.Success)
        {
            await DisconnectAsync();
            return AuthResult.Failed(response.Message);
        }
        SessionId = response.SessionId;

        var (dataPacketRead, closedBeforeData) = await ReadHandshakeAsync(ct);
        if (closedBeforeData)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ClosedBeforeData));
        if (dataPacketRead is not EditorDataPacket dataPacket)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ExpectedDataPacket));

        _cts = new CancellationTokenSource();
        ReceiveLoop = ReceiveLoopAsync(_cts.Token);

        ServerBookStore.Book.Remember(Hello?.GameName ?? "", host, port);
        return new AuthResult(true, response.Message, dataPacket, response.AccessLevel);
    }

    /// <summary>What the server said it is, from the greeting it opens with. Null against a server old
    /// enough not to greet editors.</summary>
    public ServerHelloPacket? Hello { get; private set; }

    private Task<(IPacket? Packet, bool Closed)> ReadHandshakeAsync(CancellationToken ct)
        => ReadPastGreetingAsync(_reader!, h => Hello = h, ct);

    /// <summary>Reads the next handshake packet, handing off any greeting on the way. The greeting is not
    /// one of the packets the handshake waits for, so leaving it in the stream would trip the next read.
    /// <c>Closed</c> is true when the stream ended; <c>Packet</c> is null for a line this build does not
    /// recognize.</summary>
    public static async Task<(IPacket? Packet, bool Closed)> ReadPastGreetingAsync(
        TextReader reader, Action<ServerHelloPacket> onGreeting, CancellationToken ct = default)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) return (null, true);

            var packet = PacketSerializer.TryDeserialize(line);
            if (packet is ServerHelloPacket hello)
            {
                onGreeting(hello);
                continue;
            }
            return (packet, false);
        }
    }

    public async Task SendSaveAsync(IPacket packet)
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        EditorLog.Info("Sending {Packet} to {Endpoint}.", packet.Cmd, Endpoint);
        await _writer.WriteAsync(PacketSerializer.Serialize(packet));
    }

    // ── Bulk fetch (all records of a type in one round-trip) ─────────────────

    /// <summary>Claims a record, sent the moment it is dirtied. Fire and forget: the server answers with
    /// the whole table, which arrives as a live packet rather than as a reply to this.</summary>
    public async Task SendLockAsync(string section, int num)
    {
        if (_writer is null) return;
        await _writer.WriteAsync(PacketSerializer.Serialize(new EditorLockPacket { Section = section, Num = num }));
    }

    /// <summary>Gives a record back, on save or discard.</summary>
    public async Task SendUnlockAsync(string section, int num)
    {
        if (_writer is null) return;
        await _writer.WriteAsync(PacketSerializer.Serialize(new EditorUnlockPacket { Section = section, Num = num }));
    }

    public Task<EditorAllItemsPacket?> RequestAllItemsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllItemsPacket>(PacketNames.EditorAllItems, new EditorRequestAllItemsPacket(), ct);

    public Task<EditorAllNpcsPacket?> RequestAllNpcsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllNpcsPacket>(PacketNames.EditorAllNpcs, new EditorRequestAllNpcsPacket(), ct);

    public Task<EditorAllShopsPacket?> RequestAllShopsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllShopsPacket>(PacketNames.EditorAllShops, new EditorRequestAllShopsPacket(), ct);

    public Task<EditorAllQuestsPacket?> RequestAllQuestsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllQuestsPacket>(PacketNames.EditorAllQuests, new EditorRequestAllQuestsPacket(), ct);

    public Task<EditorAllConversationsPacket?> RequestAllConversationsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllConversationsPacket>(PacketNames.EditorAllConversations, new EditorRequestAllConversationsPacket(), ct);

    public Task<EditorAllSpellsPacket?> RequestAllSpellsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllSpellsPacket>(PacketNames.EditorAllSpells, new EditorRequestAllSpellsPacket(), ct);

    public Task<EditorAllClassesPacket?> RequestAllClassesAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllClassesPacket>(PacketNames.EditorAllClasses, new EditorRequestAllClassesPacket(), ct);

    public Task<EditorAllMapGroupsPacket?> RequestAllMapGroupsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllMapGroupsPacket>(PacketNames.EditorAllMapGroups, new EditorRequestAllMapGroupsPacket(), ct);

    /// <summary>Asks for one slice of the world's maps. The server clamps the count to its own chunk
    /// ceiling, so a short answer is normal and the caller reads the next slice from where this one ends.</summary>
    public Task<EditorAllMapsPacket?> RequestAllMapsAsync(int start, int count, CancellationToken ct = default)
        => RequestBulkAsync<EditorAllMapsPacket>(PacketNames.EditorAllMaps,
            new EditorRequestAllMapsPacket { Start = start, Count = count }, ct);

    // ── Accounts (Creator only) ───────────────────────────────────────────────
    // These reuse the bulk channel because each is a single request answered by a single reply, keyed by
    // the reply's command name. A page and a record never overlap: the browser asks for one at a time.

    public Task<EditorAccountListPacket?> RequestAccountsAsync(string search, AdminLevel? access, int page,
                                                               int pageSize, CancellationToken ct = default)
        => RequestBulkAsync<EditorAccountListPacket>(PacketNames.EditorAccountList,
            new EditorRequestAccountsPacket { Search = search, Access = access, Page = page, PageSize = pageSize }, ct);

    public Task<EditorAccountPacket?> RequestAccountAsync(string login, CancellationToken ct = default)
        => RequestBulkAsync<EditorAccountPacket>(PacketNames.EditorAccount,
            new EditorRequestAccountPacket { Login = login }, ct);

    /// <summary>Saves an account and waits for the server's re-read, so the form shows what actually
    /// landed rather than what was typed — the server clamps levels and refuses an unknown map.</summary>
    public Task<EditorAccountPacket?> SaveAccountAsync(EditorSaveAccountPacket save, CancellationToken ct = default)
        => RequestBulkAsync<EditorAccountPacket>(PacketNames.EditorAccount, save, ct);

    /// <summary>Renames one character, answering with what came of it. A rename can be refused — the name is
    /// taken, the character is logged in — so the reply is the notice rather than the record, and the caller
    /// re-reads the account once it knows the rename landed.</summary>
    public Task<EditorNoticePacket?> RenameCharAsync(string login, int slot, string name, CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorRenameCharPacket { Login = login, Slot = slot, Name = name }, ct);

    /// <summary>Puts an item in a character's bag. <paramref name="quantity"/> is the stack size for a
    /// currency item and is ignored for anything else.</summary>
    public Task<EditorNoticePacket?> GiveItemAsync(string login, int slot, int itemNum, int quantity,
        CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorGiveItemPacket { Login = login, Slot = slot, ItemNum = itemNum, Quantity = quantity }, ct);

    /// <summary>Takes a stack out of one of a character's bag slots. <paramref name="quantity"/> 0 means all
    /// of it, which is the only thing a non-stacking item can mean.</summary>
    public Task<EditorNoticePacket?> TakeItemAsync(string login, int slot, int invSlot, int quantity,
        CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorTakeItemPacket { Login = login, Slot = slot, InvSlot = invSlot, Quantity = quantity }, ct);

    /// <summary>Teaches a character a spell. The class, level and INT gates a scroll enforces do not apply.</summary>
    public Task<EditorNoticePacket?> LearnSpellAsync(string login, int slot, int spellNum, CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorLearnSpellPacket { Login = login, Slot = slot, SpellNum = spellNum }, ct);

    public Task<EditorNoticePacket?> ForgetSpellAsync(string login, int slot, int spellSlot, CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorForgetSpellPacket { Login = login, Slot = slot, SpellSlot = spellSlot }, ct);

    /// <summary>Puts an item in the account vault. No character slot — the bank is account-shared.</summary>
    public Task<EditorNoticePacket?> BankGiveAsync(string login, int itemNum, int quantity, CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorBankGivePacket { Login = login, ItemNum = itemNum, Quantity = quantity }, ct);

    public Task<EditorNoticePacket?> BankTakeAsync(string login, int bankSlot, int quantity, CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorBankTakePacket { Login = login, BankSlot = bankSlot, Quantity = quantity }, ct);

    /// <summary>Puts one quest of a character's log into a state. <see cref="QuestStatus.NotStarted"/> takes
    /// it out of the log, which is what that state means.</summary>
    public Task<EditorNoticePacket?> SetQuestStatusAsync(string login, int slot, int questNum, QuestStatus status,
        CancellationToken ct = default)
        => RequestBulkAsync<EditorNoticePacket>(PacketNames.EditorNotice,
            new EditorSetQuestStatusPacket { Login = login, Slot = slot, QuestNum = questNum, Status = status }, ct);

    private async Task<T?> RequestBulkAsync<T>(string responseCmd, IPacket request,
                                                CancellationToken ct) where T : class, IPacket
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        // One waiter per response command. A second request for the same one displaces the first, so the
        // displaced caller is released rather than left awaiting a slot nothing points at any more.
        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_pendingBulk.TryGetValue(responseCmd, out var displacedBulk))
        {
            EditorLog.Debug("Bulk request {Response} displaced an earlier waiter.", responseCmd);
            displacedBulk.TrySetCanceled();
        }
        _pendingBulk[responseCmd] = tcs;
        try
        {
            EditorLog.Debug("Bulk request {Request}, awaiting {Response}.", request.Cmd, responseCmd);
            await _writer.WriteAsync(PacketSerializer.Serialize(request));
            using var reg = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                _pendingBulk.TryRemove(responseCmd, out _);
            });
            return await tcs.Task.ConfigureAwait(false) as T;
        }
        catch
        {
            _pendingBulk.TryRemove(responseCmd, out _);
            throw;
        }
    }

    // ── Per-record lazy fetch ─────────────────────────────────────────────────

    public Task<UpdateItemPacket?> RequestItemAsync(int itemNum, CancellationToken ct = default)
        => RequestAsync<UpdateItemPacket>(
            PacketNames.UpdateItem, itemNum,
            new EditorRequestItemPacket { ItemNum = itemNum }, ct);

    public Task<UpdateNpcPacket?> RequestNpcAsync(int npcNum, CancellationToken ct = default)
        => RequestAsync<UpdateNpcPacket>(
            PacketNames.UpdateNpc, npcNum,
            new EditorRequestNpcPacket { NpcNum = npcNum }, ct);

    public Task<UpdateShopPacket?> RequestShopAsync(int shopNum, CancellationToken ct = default)
        => RequestAsync<UpdateShopPacket>(
            PacketNames.UpdateShop, shopNum,
            new EditorRequestShopPacket { ShopNum = shopNum }, ct);

    public Task<UpdateQuestPacket?> RequestQuestAsync(int questNum, CancellationToken ct = default)
        => RequestAsync<UpdateQuestPacket>(
            PacketNames.UpdateQuest, questNum,
            new EditorRequestQuestPacket { QuestNum = questNum }, ct);

    public Task<UpdateConversationPacket?> RequestConversationAsync(int convNum, CancellationToken ct = default)
        => RequestAsync<UpdateConversationPacket>(
            PacketNames.UpdateConversation, convNum,
            new EditorRequestConversationPacket { ConvNum = convNum }, ct);

    public Task<UpdateSpellPacket?> RequestSpellAsync(int spellNum, CancellationToken ct = default)
        => RequestAsync<UpdateSpellPacket>(
            PacketNames.UpdateSpell, spellNum,
            new EditorRequestSpellPacket { SpellNum = spellNum }, ct);

    public Task<SendMapPacket?> RequestMapAsync(int mapNum, CancellationToken ct = default)
        => RequestAsync<SendMapPacket>(
            PacketNames.SendMap, mapNum,
            new EditorRequestMapPacket { MapNum = mapNum }, ct);

    public Task<UpdateClassPacket?> RequestClassAsync(int classNum, CancellationToken ct = default)
        => RequestAsync<UpdateClassPacket>(
            PacketNames.UpdateClass, classNum,
            new EditorRequestClassPacket { ClassNum = classNum }, ct);

    public Task<UpdateMapGroupPacket?> RequestMapGroupAsync(int groupNum, CancellationToken ct = default)
        => RequestAsync<UpdateMapGroupPacket>(
            PacketNames.UpdateMapGroup, groupNum,
            new EditorRequestMapGroupPacket { GroupNum = groupNum }, ct);

    private async Task<T?> RequestAsync<T>(string responseCmd, int num, IPacket request,
                                           CancellationToken ct) where T : class, IPacket
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");

        // One waiter per (command, record). As in RequestBulkAsync, a displaced caller is released.
        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_pending.TryGetValue((responseCmd, num), out var displaced))
        {
            EditorLog.Debug("Request {Response} #{Num} displaced an earlier waiter.", responseCmd, num);
            displaced.TrySetCanceled();
        }
        _pending[(responseCmd, num)] = tcs;
        try
        {
            EditorLog.Debug("Request {Request} #{Num}, awaiting {Response}.", request.Cmd, num, responseCmd);
            await _writer.WriteAsync(PacketSerializer.Serialize(request));
            using var reg = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                _pending.TryRemove((responseCmd, num), out _);
            });
            return await tcs.Task.ConfigureAwait(false) as T;
        }
        catch
        {
            _pending.TryRemove((responseCmd, num), out _);
            throw;
        }
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    /// <summary>Whether this session's teardown was ASKED FOR. Set before the socket is touched, and read
    /// by the receive loop to decide whether its exit is news.
    /// <para>The loop cannot tell from the exception. Closing a socket out from under a pending read raises
    /// IOException or ObjectDisposedException far more often than OperationCanceledException, so inferring
    /// intent from the exception type reports a deliberate disconnect as a lost connection — which puts an
    /// unasked-for modal over the main window on the way out of an ordinary Disconnect.</para></summary>
    private volatile bool _closingDeliberately;

    public async Task DisconnectAsync()
    {
        _closingDeliberately = true;
        SessionId = "";
        _cts?.Cancel();
        _cts = null;
        FailAllPending();
        if (_writer is not null)
        {
            try
            {
                await _writer.FlushAsync();
            }
            catch { }
        }
        _client?.Close();
        _client = null;
        _reader = null;
        _writer = null;
    }

    // ── Background receive loop ───────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        bool unexpected = false;
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line is null)
                {
                    unexpected = true;
                    break;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;

                var packet = PacketSerializer.TryDeserialize(line);
                if (packet is not null)
                {
                    if (TryCompletePending(packet)) continue;
                    // Recognized packet, no pending request waiting on it = a server-pushed live broadcast.
                    OnLivePacket?.Invoke(packet);
                    continue;
                }

                OnServerMessage?.Invoke(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { unexpected = true; }

        // A teardown we asked for is never news, whatever the read happened to throw on the way out.
        if (_closingDeliberately) unexpected = false;

        // Nothing can answer a request once this loop is over, so every caller still waiting on one is
        // released here. A request whose response can never arrive would otherwise await forever, and
        // the UI operation that started it — connecting, loading a collection — would sit half-done.
        FailAllPending();

        if (unexpected) OnDisconnected?.Invoke();
    }

    /// <summary>Cancels every in-flight request. Called wherever the connection stops being able to
    /// answer one, whether that was asked for or not.</summary>
    private void FailAllPending()
    {
        foreach (var tcs in _pending.Values) tcs.TrySetCanceled();
        _pending.Clear();
        foreach (var tcs in _pendingBulk.Values) tcs.TrySetCanceled();
        _pendingBulk.Clear();
    }

    /// <summary>The command a bulk reply completes the wait for, or "" if the packet is not one.
    ///
    /// <para>The other half of <c>RequestBulkAsync</c>, which parks a waiter under the same name. A reply
    /// missing from here has no route home: it is taken for a server-pushed broadcast, the waiter is never
    /// completed, and the caller awaits a task nothing will finish. `BulkRepliesAllRouteHome` is the
    /// guard.</para></summary>
    internal static string BulkCommandOf(IPacket packet) => packet switch
    {
        // Login reads its copy straight off the stream before the read loop runs, so routing it here only
        // ever catches the one a refresh asked for.
        EditorDataPacket => PacketNames.EditorData,
        EditorAllItemsPacket => PacketNames.EditorAllItems,
        EditorAllNpcsPacket => PacketNames.EditorAllNpcs,
        EditorAllShopsPacket => PacketNames.EditorAllShops,
        EditorAllQuestsPacket => PacketNames.EditorAllQuests,
        EditorAllConversationsPacket => PacketNames.EditorAllConversations,
        EditorAllSpellsPacket => PacketNames.EditorAllSpells,
        EditorAllClassesPacket => PacketNames.EditorAllClasses,
        EditorAllMapGroupsPacket => PacketNames.EditorAllMapGroups,
        EditorAllMapsPacket => PacketNames.EditorAllMaps,
        EditorAccountListPacket => PacketNames.EditorAccountList,
        EditorAccountPacket => PacketNames.EditorAccount,
        EditorNoticePacket => PacketNames.EditorNotice,
        _ => "",
    };

    private bool TryCompletePending(IPacket packet)
    {
        string bulkCmd = BulkCommandOf(packet);
        if (bulkCmd != "")
            return _pendingBulk.TryRemove(bulkCmd, out var btcs) && btcs.TrySetResult(packet);

        // Per-record responses
        (string cmd, int num) key = packet switch
        {
            UpdateItemPacket p => (PacketNames.UpdateItem, p.ItemNum),
            UpdateNpcPacket p => (PacketNames.UpdateNpc, p.NpcNum),
            UpdateShopPacket p => (PacketNames.UpdateShop, p.ShopNum),
            UpdateQuestPacket p => (PacketNames.UpdateQuest, p.QuestNum),
            UpdateConversationPacket p => (PacketNames.UpdateConversation, p.ConvNum),
            UpdateSpellPacket p => (PacketNames.UpdateSpell, p.SpellNum),
            SendMapPacket p => (PacketNames.SendMap, p.MapNum),
            UpdateClassPacket p => (PacketNames.UpdateClass, p.ClassNum),
            UpdateMapGroupPacket p => (PacketNames.UpdateMapGroup, p.GroupNum),
            _ => ("", 0),
        };
        if (key.cmd == "") return false;
        return _pending.TryRemove(key, out var tcs) && tcs.TrySetResult(packet);
    }

    public void Dispose() => _client?.Dispose();
}
