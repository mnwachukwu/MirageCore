using Mirage.Shared.Protocol;

namespace Mirage.Client.Core.Net;

public interface IClientTransport
{
    Task ConnectAsync(string host, int port, CancellationToken ct = default);
    void Send(IPacket packet);
    void Disconnect();
    bool IsConnected { get; }

    /// <summary>
    /// True when the connection was lost without a local <see cref="Disconnect"/> call
    /// (server-side close, network error). Cleared by the next <see cref="Disconnect"/> call.
    /// Safe to poll from the game loop thread.
    /// </summary>
    bool DroppedUnexpectedly { get; }

    /// <summary>
    /// Non-blocking dequeue of one received JSON line. Returns false when the queue is empty.
    /// Call once per line per frame from the game/update loop.
    /// </summary>
    bool TryDequeue(out string line);
}
