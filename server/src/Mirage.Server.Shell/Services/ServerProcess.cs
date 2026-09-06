using Mirage.Shared;
using System.Diagnostics;
using System.Threading.Channels;

namespace Mirage.Server.Shell.Services;

/// <summary>
/// A server running as a child process: started here, relayed through stdin and stdout, and shut down
/// on request.
///
/// <para>Stopping is a request, not a kill. The server drains four write queues on the way out, and
/// killing it mid-drain loses whatever had not landed — so Stop writes <c>/shutdown</c> and waits, and
/// kills only as a backstop.</para>
/// </summary>
public sealed class ServerProcess : IServerConnection
{
    /// <summary>How long a graceful shutdown is given before the process is killed. Generous on
    /// purpose — the alternative to waiting is losing writes.</summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private Process? _process;
    // Commands wait here rather than on the UI thread. Unbounded: an operator cannot type fast enough
    // to matter, and dropping a command would be worse than queueing one.
    private Channel<string>? _outbox;

    public event Action<string>? OutputReceived;
    public event Action<ServerState>? StateChanged;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>True: this shell owns the process, so it can start and stop it.</summary>
    public bool CanSupervise => true;

    /// <summary>The headless server beside this one. They ship in the same package, so the shell never
    /// has to be told where it is. Built from <see cref="Constants.GameName"/> rather than hardcoded,
    /// because that is the same single value the build derives assembly names from.
    ///
    /// <para><b>"-Server-Console", not "-Server".</b> The plain name belongs to THIS window — it is what
    /// the installer's shortcut points at. Spawning "-Server" would have the shell launch itself.</para></summary>
    public static string DefaultExecutablePath =>
        Path.Combine(AppContext.BaseDirectory,
            Constants.GameName.Replace(' ', '-') + "-Server-Console" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public string ExecutablePath { get; set; } = DefaultExecutablePath;

    /// <summary>Launches the server. Returns null on success, or the path it looked at when nothing was
    /// there — by far the likeliest failure, and worth naming.</summary>
    public Task<string?> StartAsync()
    {
        lock (_lock)
        {
            if (State != ServerState.Stopped) return Task.FromResult<string?>(null);
            if (!File.Exists(ExecutablePath)) return Task.FromResult<string?>(ExecutablePath);

            var info = new ProcessStartInfo(ExecutablePath)
            {
                // Asks the child for status snapshots on its stdout. Safe to put there because in this
                // mode nothing but this shell reads that pipe; a server started from a terminal gets no
                // flag and prints no sentinels.
                Arguments = "--status-events",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                // The server pins its own working directory to its exe folder on startup, but setting it
                // here too means the data and log paths resolve identically whether it was launched from
                // the shell, a terminal, or a service manager.
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
            };

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.Exited += (_, _) => SetState(ServerState.Stopped);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            _ = WriteLoopAsync(process, _outbox.Reader);
        }
        SetState(ServerState.Running);
        return Task.FromResult<string?>(null);
    }

    /// <summary>Types a line at the server's console. Everything <c>ConsoleCommands</c> understands
    /// arrives this way, so the shell inherits the whole command set without reimplementing any of
    /// it.</summary>
    /// <summary>Hands a line to the outbox and returns at once.
    ///
    /// <para>🔴 It never touches the pipe. This is called from a button, on the UI thread, and a write to
    /// stdin blocks once the pipe buffer is full — which is exactly what a server that has stopped reading
    /// its console does. The window then stops answering while still presenting its last frame, so a wedged
    /// server reads as a broken shell.</para></summary>
    public void SendCommand(string line) => _outbox?.Writer.TryWrite(line);

    /// <summary>Drains the outbox into the process’s stdin, one line at a time and in order.</summary>
    private static async Task WriteLoopAsync(Process process, ChannelReader<string> outbox)
    {
        try
        {
            await foreach (string line in outbox.ReadAllAsync().ConfigureAwait(false))
            {
                if (process.HasExited) return;
                await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
        // The pipe closed under us; Exited follows and the state goes with it.
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Asks the server to shut down and waits for it to finish draining. Kills it only if it
    /// outstays <see cref="ShutdownGrace"/>.</summary>
    public async Task StopAsync()
    {
        Process? process;
        lock (_lock)
        {
            if (_process is not { HasExited: false }) return;
            process = _process;
        }
        SetState(ServerState.Stopping);

        SendCommand("/shutdown");
        using var timeout = new CancellationTokenSource(ShutdownGrace);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Out of patience. Anything still queued is lost, which is why the grace above is long.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        SetState(ServerState.Stopped);
    }

    private void SetState(ServerState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose()
    {
        // A shell that closes must not orphan a running server with no console attached to it — there
        // would be no way left to shut it down gracefully. Ask, wait, and only then let go.
        try { StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        lock (_lock)
        {
            _outbox?.Writer.TryComplete();
            _outbox = null;
            _process?.Dispose();
            _process = null;
        }
    }
}
