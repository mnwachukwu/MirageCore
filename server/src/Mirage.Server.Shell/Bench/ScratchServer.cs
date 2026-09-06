using Mirage.Server.Core.Configuration;
using Mirage.Shared.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Mirage.Server.Shell.Bench;

/// <summary>
/// A throwaway server for the benchmark to load: the operator's world copied to a temporary folder, run
/// on a free port by a second process, and deleted afterwards.
///
/// <para><b>Why a copy rather than the operator's own server.</b> The bench creates hundreds of accounts
/// and walks them around. Doing that to a live world would leave the accounts behind and give whoever was
/// playing a very bad evening. Copying is also what makes the measurement honest: the maps, NPC spawns and
/// item tables are the operator's real ones, and those are where the game thread spends its time — a blank
/// padded world would report a number no live server could reproduce.</para>
///
/// <para>Everything except <c>accounts/</c> comes across, so the scratch world starts with nobody in it.
/// The scratch config keeps the operator's rules and language and overrides only what has to differ: the
/// port, the world folder, the player limit, and remote management, which is switched off — a temporary
/// server has no business opening an administration socket.</para>
/// </summary>
public sealed class ScratchServer : IAsyncDisposable
{
    /// <summary>How long the world is given to load before the bench gives up. A thousand-map world takes
    /// a while, and on a slow disk it takes longer than anyone would guess.</summary>
    private static readonly TimeSpan BootTimeout = TimeSpan.FromMinutes(3);

    /// <summary>One reading a second. Set on the child, not in anyone's config: the shell's own dashboard
    /// keeps the thirty-second backstop.</summary>
    private const double StatusSeconds = 1;

    private readonly string _root;
    private Process? _process;

    private ScratchServer(string root, int port)
    {
        _root = root;
        Port = port;
    }

    public int Port { get; }

    /// <summary>The most recent snapshot off the child's stdout, or null before the first one lands.</summary>
    public ServerStatus? LatestStatus { get; private set; }

    /// <summary>Raised for each console line that is not a status snapshot, so a failure to boot is
    /// visible instead of being a silent timeout.</summary>
    public event Action<string>? OutputReceived;

    private static readonly System.Text.Json.JsonSerializerOptions StatusJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Copies the world, writes the scratch config, and starts the server. Throws
    /// <see cref="FileNotFoundException"/> when the console executable is missing and
    /// <see cref="TimeoutException"/> when the port never opens.</summary>
    public static async Task<ScratchServer> StartAsync(ServerConfig template, string sourceDataDir,
                                                       int maxPlayers, string exe, CancellationToken ct)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException(exe, exe);

        string root = Path.Combine(Path.GetTempPath(), "mirage-bench-" + Guid.NewGuid().ToString("n")[..8]);
        var scratch = new ScratchServer(root, FreePort());
        try
        {
            Directory.CreateDirectory(root);
            string dataDir = Path.Combine(root, "data");
            CopyWorld(sourceDataDir, dataDir, ct);

            string configPath = Path.Combine(root, "serverconfig.json");
            var config = template with
            {
                Port = scratch.Port,
                DataDir = dataDir,
                MaxPlayers = maxPlayers,
                Management = new ManagementConfig(),
            };
            if (ServerConfigStore.Save(configPath, config) is { } error)
                throw new IOException(error);

            await scratch.LaunchAsync(exe, configPath, ct).ConfigureAwait(false);
            return scratch;
        }
        catch
        {
            await scratch.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>A port nobody is on. Binding to 0 and releasing leaves a window in which something else
    /// could take it; the alternative is guessing a number, which has the same window and no evidence
    /// behind it.</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Copies the world, skipping <c>accounts/</c>. Everything else comes across: the bench is
    /// measuring THIS world's maps and spawns, not a generic one.</summary>
    private static void CopyWorld(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source)) return;

        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (string dir in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("accounts", StringComparison.OrdinalIgnoreCase)) continue;
            ct.ThrowIfCancellationRequested();
            CopyWorld(dir, Path.Combine(destination, name), ct);
        }
    }

    private async Task LaunchAsync(string exe, string configPath, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        // ArgumentList rather than a command line, because these carry paths and a temp folder is free to
        // have a space in it.
        info.ArgumentList.Add("--config=" + configPath);
        info.ArgumentList.Add("--LogsDir=" + Path.Combine(_root, "logs"));
        info.ArgumentList.Add("--status-events=" + StatusSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Receive(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        await WaitForPortAsync(ct).ConfigureAwait(false);
    }

    private void Receive(string line)
    {
        if (!line.StartsWith(ServerStatus.LinePrefix, StringComparison.Ordinal))
        {
            OutputReceived?.Invoke(line);
            return;
        }
        try
        {
            var next = System.Text.Json.JsonSerializer.Deserialize<ServerStatus>(
                line[ServerStatus.LinePrefix.Length..], StatusJson);
            if (next is not null) LatestStatus = next;
        }
        catch (System.Text.Json.JsonException) { /* the next one is a second away */ }
    }

    /// <summary>Waits until the acceptor answers. The port opening is the signal the simulated players
    /// actually need — a status snapshot only proves the game loop is turning, which happens first.</summary>
    private async Task WaitForPortAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + BootTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
                throw new InvalidOperationException($"the benchmark server exited with code {_process.ExitCode}");
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, Port, ct).ConfigureAwait(false);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
        throw new TimeoutException($"the benchmark server did not open port {Port} within {BootTimeout.TotalMinutes:0} minutes");
    }

    /// <summary>Stops the server and removes the scratch world. Shutdown is asked for rather than forced
    /// for the same reason <see cref="Services.ServerProcess"/> asks: the server drains its write queues on
    /// the way out, and the files it is draining into are about to be deleted anyway — but a process still
    /// holding them is a delete that fails.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false } process)
        {
            try
            {
                process.StandardInput.WriteLine("/shutdown");
                process.StandardInput.Flush();
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
        _process?.Dispose();
        _process = null;
        await DeleteRootAsync().ConfigureAwait(false);
    }

    /// <summary>Removes the scratch folder, retrying while the file handles the exiting process held are
    /// released. A leftover folder in temp is not worth throwing over, so a stubborn one is left there.</summary>
    private async Task DeleteRootAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(400).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Where the operator's world actually is, resolved the way the server resolves it —
    /// through the same function, so the two cannot drift apart.</summary>
    public static string ResolveDataDir(ServerConfig config) => ServerPaths.ResolveDataDir(config);
}
