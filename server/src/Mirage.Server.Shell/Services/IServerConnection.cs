namespace Mirage.Server.Shell.Services;

/// <summary>"Running" means the server is there and talking, NOT that the world has finished loading.</summary>
public enum ServerState { Stopped, Running, Stopping }

/// <summary>
/// A server the shell is driving: lines out, lines in, and a state to show.
///
/// <para>Two implementations, and the console tab cannot tell them apart. A local server is a child
/// process reached through stdin and stdout; a remote one is the same two streams over a socket. That is
/// all the management protocol is — a transport for the console that already existed.</para>
/// </summary>
public interface IServerConnection : IDisposable
{
    /// <summary>One line of server output. Raised off the UI thread; a subscriber has to marshal.</summary>
    event Action<string>? OutputReceived;

    /// <summary>Raised on every state transition, from whichever thread caused it.</summary>
    event Action<ServerState>? StateChanged;

    ServerState State { get; }

    /// <summary>Whether this connection can start and stop the server, as opposed to only talking to it.
    /// False when attached remotely: you cannot launch a process on a machine you are not on.</summary>
    bool CanSupervise { get; }

    /// <summary>Brings the server up, or connects to it. Null on success, or a message explaining why
    /// not.</summary>
    Task<string?> StartAsync();

    /// <summary>Types a line at the server's console.</summary>
    void SendCommand(string line);

    /// <summary>Ends the session. Locally that shuts the server down; remotely it only detaches, because
    /// the server outlives the shell that was watching it.</summary>
    Task StopAsync();
}
