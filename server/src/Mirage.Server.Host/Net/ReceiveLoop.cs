using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;

namespace Mirage.Server.Host.Net;

/// <summary>
/// Per-connection read loop.  Reads newline-delimited JSON lines off the socket and POSTS each one to
/// the single game thread (<see cref="GameLoop.Post"/>) for processing — it never touches game state
/// itself, so multiple connections' reads can run concurrently while their effects stay serialized.
///
/// A single optional <paramref name="firstLine"/> is processed before the read loop begins;
/// this is used when <see cref="TcpConnectionAcceptor"/> already consumed the first line to
/// determine whether the connection is a game player or an editor.
///
/// The game-state leave (player) is issued by <see cref="TcpConnectionAcceptor"/> after this loop
/// returns; here we only tear down the network send channel.
/// </summary>
public static class ReceiveLoop
{
    public static Task RunPlayerAsync(
        int index,
        StreamReader reader,
        string? firstLine,
        PacketHandler handler,
        TcpPacketDispatcher dispatcher,
        GameLoop gameLoop,
        ILogger logger,
        CancellationToken ct)
        => RunAsync(index, reader, firstLine, handler.HandlePacket, dispatcher, gameLoop, logger, isEditor: false, ct);

    public static Task RunEditorAsync(
        int editorIndex,
        StreamReader reader,
        string? firstLine,
        EditorPacketHandler handler,
        TcpPacketDispatcher dispatcher,
        GameLoop gameLoop,
        ILogger logger,
        CancellationToken ct)
        => RunAsync(editorIndex, reader, firstLine, handler.HandleEditorPacket, dispatcher, gameLoop, logger, isEditor: true, ct);

    // ── Core loop ─────────────────────────────────────────────────────────────

    // The two connection kinds are dispatched by different handlers, so the caller binds the right
    // one and the loop just forwards to it — the loop itself has no reason to know which is which
    // beyond labeling its log lines.
    private static async Task RunAsync(
        int index,
        StreamReader reader,
        string? firstLine,
        Action<int, string> handle,
        TcpPacketDispatcher dispatcher,
        GameLoop gameLoop,
        ILogger logger,
        bool isEditor,
        CancellationToken ct)
    {
        try
        {
            // Process the pre-read first line (used for routing detection)
            if (firstLine is { Length: > 0 })
            {
                Dispatch(index, firstLine, handle, gameLoop, isEditor, logger);
            }

            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;            // clean EOF / connection closed
                if (line.Length == 0) continue;     // keep-alive blank line

                Dispatch(index, line, handle, gameLoop, isEditor, logger);
            }
        }
        catch (OperationCanceledException) { /* server shutting down */ }
        catch (IOException) { /* normal client disconnect */ }
        catch (ObjectDisposedException) { /* socket closed by graceful disconnect */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReceiveLoop error for {Type} index {Index}",
                isEditor ? "editor" : "player", index);
        }
        finally
        {
            // Tear down the network send channel only.  The player's game-state leave is posted to the
            // game thread by TcpConnectionAcceptor once this returns.
            if (isEditor)
                await dispatcher.UnregisterEditorAsync(index).ConfigureAwait(false);
            else
                await dispatcher.UnregisterPlayerAsync(index).ConfigureAwait(false);
        }
    }

    private static void Dispatch(int index, string line, Action<int, string> handle, GameLoop gameLoop, bool isEditor, ILogger logger)
    {
        logger.LogDebug("[RX {Type} {Index}] {Line}", isEditor ? "editor" : "player", index, RedactPass(line));
        // Hand the packet to the game thread; it deserializes + processes it there, serialized with the
        // AI ticks and every other connection's packets.
        gameLoop.Post(() => handle(index, line));
    }

    // Replace the value of any "pass" or "newpass" key in the JSON with "[REDACTED]" before logging.
    // Only the log output is affected; the original line is processed unchanged.
    private static string RedactPass(string line)
    {
        if (!line.Contains("pass\"", StringComparison.Ordinal)) return line;
        line = System.Text.RegularExpressions.Regex.Replace(
            line, @"""pass""\s*:\s*""[^""]*""", @"""pass"":""[REDACTED]""");
        line = System.Text.RegularExpressions.Regex.Replace(
            line, @"""newpass""\s*:\s*""[^""]*""", @"""newpass"":""[REDACTED]""");
        return line;
    }
}
