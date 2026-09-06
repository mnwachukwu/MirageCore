using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Mirage.Server.Host.Net;

/// <summary>
/// TCP implementation of <see cref="IPacketDispatcher"/>.
///
/// Each connected player and editor gets a bounded <see cref="Channel{T}"/> backed by a
/// background drain task that writes serialized JSON lines to their <see cref="StreamWriter"/>.
/// This keeps game-logic threads from blocking on slow network I/O: <c>SendTo</c> is always
/// non-blocking (it just writes to the channel).
///
/// <see cref="RegisterPlayer"/> / <see cref="RegisterEditor"/> are called by
/// <see cref="TcpConnectionAcceptor"/> when a connection is established.
/// <see cref="Disconnect"/> / <see cref="DisconnectEditor"/> close the underlying
/// <see cref="TcpClient"/>, which causes the receive-loop to exit and
/// <see cref="UnregisterPlayerAsync"/> / <see cref="UnregisterEditorAsync"/> to be called in cleanup.
/// </summary>
public sealed class TcpPacketDispatcher : IPacketDispatcher, IDisposable
{
    private readonly PlayerManager _pm;
    private readonly GameWorld _world;
    private readonly ILogger<TcpPacketDispatcher> _logger;

    private readonly ConnectionSlot[] _players;
    private readonly ConnectionSlot[] _editors;

    public TcpPacketDispatcher(PlayerManager pm, GameWorld world, ILogger<TcpPacketDispatcher> logger)
    {
        _pm = pm;
        _world = world;
        _logger = logger;

        _players = new ConnectionSlot[_pm.Slots + 1];
        _editors = new ConnectionSlot[Constants.MaxEditorSessions + 1];
        for (int i = 0; i <= _pm.Slots; i++) _players[i] = new ConnectionSlot();
        for (int i = 0; i <= Constants.MaxEditorSessions; i++) _editors[i] = new ConnectionSlot();
    }

    // ── Registration (called from TcpConnectionAcceptor) ─────────────────────

    public void RegisterPlayer(int index, TcpClient client, StreamWriter writer)
    {
        var slot = _players[index];
        slot.Client = client;
        slot.Writer = writer;
        slot.Channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        slot.Cts = new CancellationTokenSource();
        slot.DrainTask = DrainAsync(slot, slot.Cts.Token);
    }

    /// <summary>
    /// Called from the receive-loop's finally block after the TCP connection is done.
    /// Awaiting the returned Task ensures the drain task has fully exited before the
    /// StreamWriter is disposed.
    /// </summary>
    public Task UnregisterPlayerAsync(int index)
    {
        var slot = _players[index];
        slot.Channel?.Writer.TryComplete();
        slot.Cts?.Cancel();
        var task = slot.DrainTask ?? Task.CompletedTask;
        slot.Writer = null;
        slot.Client = null;
        slot.Channel = null;
        slot.Cts = null;
        slot.DrainTask = null;
        slot.PendingClose = false;
        return task;
    }

    public void RegisterEditor(int editorIndex, TcpClient client, StreamWriter writer)
    {
        var slot = _editors[editorIndex];
        slot.Client = client;
        slot.Writer = writer;
        slot.Channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        slot.Cts = new CancellationTokenSource();
        slot.DrainTask = DrainAsync(slot, slot.Cts.Token);
    }

    public Task UnregisterEditorAsync(int editorIndex)
    {
        var slot = _editors[editorIndex];
        slot.Channel?.Writer.TryComplete();
        slot.Cts?.Cancel();
        var task = slot.DrainTask ?? Task.CompletedTask;
        slot.Writer = null;
        slot.Client = null;
        slot.Channel = null;
        slot.Cts = null;
        slot.DrainTask = null;
        slot.PendingClose = false;
        return task;
    }

    // ── IPacketDispatcher ─────────────────────────────────────────────────────

    public void SendTo(int index, IPacket packet)
    {
        if (!_pm.IsValidSlot(index)) return;
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX player {Index}] {Line}", index, json);
        Enqueue(_players[index], json);
    }

    // Every broadcast below walks PlayerManager.Online rather than 1..Slots, so the cost follows who is
    // actually on rather than the limit the operator configured. The predicates are unchanged; the only
    // slots the set leaves out are combat ghosts, whose socket is already gone — an enqueue to one was
    // always a write into a null channel.

    public void SendToAll(IPacket packet)
    {
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX all] {Line}", json);
        foreach (int i in _pm.Online) Enqueue(_players[i], json);
    }

    public void SendToAllBut(int exclude, IPacket packet)
    {
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX allBut {Exclude}] {Line}", exclude, json);
        foreach (int i in _pm.Online)
        {
            if (i != exclude) Enqueue(_players[i], json);
        }
    }

    public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet)
    {
        if (observers.Count == 0) return;
        string json = PacketSerializer.Serialize(packet);
        foreach (int i in observers)
            if (i >= 1 && i <= _pm.Slots && _pm[i].IsPlaying) Enqueue(_players[i], json);
    }

    public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet)
    {
        if (observers.Count == 0) return;
        string json = PacketSerializer.Serialize(packet);
        foreach (int i in observers)
            if (i != exclude && i >= 1 && i <= _pm.Slots && _pm[i].IsPlaying) Enqueue(_players[i], json);
    }

    public void SendToViewport(int speakerIndex, IPacket packet)
    {
        if (!_pm.IsValidSlot(speakerIndex)) return;
        var sp = _pm[speakerIndex];
        if (!sp.IsPlaying) return;
        var spc = sp.Char;
        SendToViewportAt(spc.Map, spc.X, spc.Y, packet);
    }

    public void SendToViewportAt(int mapNum, int x, int y, IPacket packet)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        // Speaker tile sits at the center cell of its own observable area, so its world coords anchor at
        // ITS OWN map's size — same-map ToWorldRelative reads Width/Height off the record. 🔴 The listener
        // coords below come from that same grid, so a default-size constant here would put speaker and
        // listener on DIFFERENT grids the moment a map is not 16x12, and earshot would answer nonsense.
        // Same arithmetic as the player path above — SendToViewport is SendToViewportAt with the char position.
        var sw = WorldCoordHelper.ToWorldRelative(_world.Maps, mapNum, mapNum, x, y);
        if (sw is null) return;
        int spWX = sw.Value.worldX, spWY = sw.Value.worldY;

        string json = PacketSerializer.Serialize(packet);
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm.IsValidSlot(i) || !_pm[i].IsPlaying) continue;
            var lc = _pm[i].Char;
            var lw = WorldCoordHelper.ToWorldRelative(_world.Maps, mapNum, lc.Map, lc.X, lc.Y);
            if (lw is null) continue;
            if (WorldCoordHelper.IsWithinViewport(spWX, spWY, lw.Value.worldX, lw.Value.worldY))
                Enqueue(_players[i], json);
        }
    }

    public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion)
    {
        if (!_pm.IsValidSlot(speakerIndex)) return;
        var sp = _pm[speakerIndex];
        if (!sp.IsPlaying) return;
        var spc = sp.Char;
        // A dead player's say/yell still posts to the chat log, but shows NO floating bubble — corpses can
        // stack on one tile in a way live players don't, so stacked bubbles would overlap illegibly.
        if (spc.Dead) return;
        string json = PacketSerializer.Serialize(packet);

        if (wholeRegion)
        {
            // Yell-range: every observer of the speaker's map who doesn't ignore them (Monitor+ bypass).
            foreach (int i in _world.MapObservers[spc.Map])
            {
                if (_pm.IsValidSlot(i) && _pm[i].IsPlaying && !IgnoreSuppresses(i, senderLogin, spc.Access))
                    Enqueue(_players[i], json);
            }

            return;
        }

        // Say-range: the speaker's viewport (same earshot test as SendToViewportAt), minus ignorers.
        var sw = WorldCoordHelper.ToWorldRelative(_world.Maps, spc.Map, spc.Map, spc.X, spc.Y);
        if (sw is null) return;
        int spWX = sw.Value.worldX, spWY = sw.Value.worldY;
        foreach (int i in _world.MapObservers[spc.Map])
        {
            if (!_pm.IsValidSlot(i) || !_pm[i].IsPlaying || IgnoreSuppresses(i, senderLogin, spc.Access)) continue;
            var lc = _pm[i].Char;
            var lw = WorldCoordHelper.ToWorldRelative(_world.Maps, spc.Map, lc.Map, lc.X, lc.Y);
            if (lw is null) continue;
            if (WorldCoordHelper.IsWithinViewport(spWX, spWY, lw.Value.worldX, lw.Value.worldY))
                Enqueue(_players[i], json);
        }
    }

    public void SendToAdmins(IPacket packet)
    {
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX admins] {Line}", json);
        foreach (int i in _pm.Online)
        {
            if (_pm[i].IsPlaying && _pm[i].Char.Access > AdminLevel.Player)
                Enqueue(_players[i], json);
        }
    }

    public void SendToGuild(int guildId, IPacket packet)
    {
        if (guildId < 1) return;
        string json = PacketSerializer.Serialize(packet);
        foreach (int i in _pm.Online)
        {
            if (_pm[i].IsPlaying && _pm[i].Guild == guildId)
                Enqueue(_players[i], json);
        }
    }

    public void SendToGuildBut(int guildId, int exclude, IPacket packet)
    {
        if (guildId < 1) return;
        string json = PacketSerializer.Serialize(packet);
        foreach (int i in _pm.Online)
        {
            if (i != exclude && _pm[i].IsPlaying && _pm[i].Guild == guildId)
                Enqueue(_players[i], json);
        }
    }

    // ── Per-recipient localized chat ──────────────────────────────────────────

    // Whether a chat message from `speakerLogin` is suppressed for `recipientIndex` because they ignore that
    // account. A null login = a system/engine message (never suppressible). A Monitor+ speaker BYPASSES the
    // ignore list entirely: ignoring an admin account does nothing while they hold access, but the ignore
    // re-applies automatically if that account ever drops back to Player (SpeakerAccess is the sender's live
    // access at send time). You can still add a Monitor+ to your ignore list — it just has no effect for now.
    private bool IgnoreSuppresses(int recipientIndex, string? speakerLogin, AdminLevel? speakerAccess)
    {
        if (speakerLogin is null) return false;
        if (speakerAccess > AdminLevel.Player) return false;   // Monitor+ bypass
        return _pm[recipientIndex].Ignores(speakerLogin);
    }

    private bool IgnoreSuppresses(int recipientIndex, ChatMetadata meta) =>
        IgnoreSuppresses(recipientIndex, meta.SpeakerLogin, meta.SpeakerAccess);

    public void SendLocalizedChatTo(int index, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (!_pm.IsValidSlot(index)) return;
        if (IgnoreSuppresses(index, meta)) return;
        var text = ServerStrings.ForPlayer(index, key, args);
        var packet = BuildChatPacket(text, meta);
        SendTo(index, packet);
    }

    public void SendLocalizedChatToAll(string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        foreach (int i in _pm.Online)
        {
            if (IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        foreach (int i in _pm.Online)
        {
            if (i == exclude || IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (observers.Count == 0) return;
        foreach (int i in observers)
        {
            if (i < 1 || i > _pm.Slots || !_pm[i].IsPlaying || IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (observers.Count == 0) return;
        foreach (int i in observers)
        {
            if (i == exclude || i < 1 || i > _pm.Slots || !_pm[i].IsPlaying || IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (!_pm.IsValidSlot(speakerIndex)) return;
        var sp = _pm[speakerIndex];
        if (!sp.IsPlaying) return;
        var spc = sp.Char;
        SendLocalizedChatToViewportAt(spc.Map, spc.X, spc.Y, key, meta, args);
    }

    public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return;
        var sw = WorldCoordHelper.ToWorldRelative(_world.Maps, mapNum, mapNum, x, y);
        if (sw is null) return;
        int spWX = sw.Value.worldX, spWY = sw.Value.worldY;

        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm.IsValidSlot(i) || !_pm[i].IsPlaying || IgnoreSuppresses(i, meta)) continue;
            var lc = _pm[i].Char;
            var lw = WorldCoordHelper.ToWorldRelative(_world.Maps, mapNum, lc.Map, lc.X, lc.Y);
            if (lw is null) continue;
            if (!WorldCoordHelper.IsWithinViewport(spWX, spWY, lw.Value.worldX, lw.Value.worldY)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToAdmins(string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        foreach (int i in _pm.Online)
        {
            if (!_pm[i].IsPlaying || _pm[i].Char.Access <= AdminLevel.Player || IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (guildId < 1) return;
        foreach (int i in _pm.Online)
        {
            if (!_pm[i].IsPlaying || _pm[i].Guild != guildId || IgnoreSuppresses(i, meta)) continue;
            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args)
    {
        if (guildId < 1) return;
        foreach (int i in _pm.Online)
        {
            if (!_pm[i].IsPlaying || _pm[i].Guild != guildId || _pm[i].GuildRank < GuildRank.Officer
                || IgnoreSuppresses(i, meta))
            {
                continue;
            }

            var text = ServerStrings.ForPlayer(i, key, args);
            Enqueue(_players[i], PacketSerializer.Serialize(BuildChatPacket(text, meta)));
        }
    }

    private static ChatMsgPacket BuildChatPacket(string text, ChatMetadata meta) =>
        meta.SpeakerName is null
            ? PacketBuilder.ChatMsg(text, meta.Color, meta.Channel)
            : PacketBuilder.ChatMsg(text, meta.Color, meta.Channel,
                meta.SpeakerName, meta.SpeakerAccess ?? AdminLevel.Player, meta.SpeakerShowAsPk ?? false);

    public void SendToEditor(int editorIndex, IPacket packet)
    {
        if (editorIndex < 1 || editorIndex > Constants.MaxEditorSessions) return;
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX editor {Index}] {Line}", editorIndex, json);
        Enqueue(_editors[editorIndex], json);
    }

    public void SendToAllEditors(IPacket packet)
    {
        string json = PacketSerializer.Serialize(packet);
        _logger.LogDebug("[TX all editors] {Line}", json);
        for (int i = 1; i <= Constants.MaxEditorSessions; i++) Enqueue(_editors[i], json);
    }

    /// <summary>
    /// Closes the underlying TCP socket. The receive-loop task running on that connection will
    /// detect the IOException and exit, then call <see cref="UnregisterPlayerAsync"/>.
    /// </summary>
    public void Disconnect(int index)
    {
        if (!_pm.IsValidSlot(index)) return;
        try { _players[index].Client?.Close(); } catch { /* already closed */ }
    }

    public void DisconnectEditor(int editorIndex)
    {
        if (editorIndex < 1 || editorIndex > Constants.MaxEditorSessions) return;
        try { _editors[editorIndex].Client?.Close(); } catch { /* already closed */ }
    }

    /// <summary>
    /// Completes the send channel so the drain task writes all queued packets, then
    /// closes the socket.  Safe to call immediately after <see cref="SendTo"/>.
    /// </summary>
    public void GracefulDisconnect(int index)
    {
        if (!_pm.IsValidSlot(index)) return;
        var slot = _players[index];
        slot.PendingClose = true;
        slot.Channel?.Writer.TryComplete();   // drain task exits loop then closes socket
    }

    public void GracefulDisconnectEditor(int editorIndex)
    {
        if (editorIndex < 1 || editorIndex > Constants.MaxEditorSessions) return;
        var slot = _editors[editorIndex];
        slot.PendingClose = true;
        slot.Channel?.Writer.TryComplete();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static void Enqueue(ConnectionSlot slot, string line)
    {
        slot.Channel?.Writer.TryWrite(line);
    }

    private async Task DrainAsync(ConnectionSlot slot, CancellationToken ct)
    {
        var channelReader = slot.Channel!.Reader;
        bool drained = false;
        try
        {
            // Drain the channel in BURSTS: wait for at least one packet to arrive, then drain every
            // packet currently queued, then flush once.  Without this pattern we'd do one syscall
            // (FlushAsync → TCP send) per packet, and a busy tick that broadcasts dozens of NPC moves
            // to a player would issue dozens of separate sends; with it, the same dozen packets go
            // out in one syscall.  Per-packet latency stays in microseconds — the next flush fires as
            // soon as the channel goes quiet — but throughput at scale improves dramatically.
            while (await channelReader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                var writer = slot.Writer;
                if (writer is null) break;
                while (channelReader.TryRead(out var line))
                    await writer.WriteAsync(line.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
            // WaitToReadAsync returned false — channel was completed (not canceled).
            drained = true;
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* connection dropped mid-send */ }
        catch (ObjectDisposedException) { /* socket already gone */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Drain task exiting with error");
        }

        // For a graceful disconnect, close the socket here after all queued packets
        // have been written.  The receive loop detects the close and calls Unregister.
        if (drained && slot.PendingClose)
        {
            try { slot.Client?.Client.Shutdown(System.Net.Sockets.SocketShutdown.Both); } catch { }
            try { slot.Client?.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        for (int i = 1; i <= _pm.Slots; i++) _players[i].Cts?.Cancel();
        for (int i = 1; i <= Constants.MaxEditorSessions; i++) _editors[i].Cts?.Cancel();
    }

    // ── ConnectionSlot ────────────────────────────────────────────────────────

    private sealed class ConnectionSlot
    {
        public TcpClient? Client { get; set; }
        public StreamWriter? Writer { get; set; }
        public Channel<string>? Channel { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public Task? DrainTask { get; set; }
        public bool PendingClose { get; set; }
    }
}
