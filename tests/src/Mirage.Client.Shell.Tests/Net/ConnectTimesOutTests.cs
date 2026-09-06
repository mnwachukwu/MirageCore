using Mirage.Client.Shell.Net;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage.Client.Shell.Tests.Net;

/// <summary>
/// A connection attempt always ends.
///
/// <para>🔴 Two things in the attempt can hang and neither bounds itself. An address with nothing listening
/// may never answer at all rather than refusing — a firewall that drops packets, a host that is not
/// there — and a port that DOES answer but belongs to something else accepts the socket and then never
/// completes a handshake it does not understand. Both leave the screen saying "connecting" with nothing
/// coming, and no way out but killing the client.</para>
///
/// <para>The silent-listener case below is the one a plain connect timeout would miss: the TCP connect
/// succeeds in a millisecond and the wait is entirely in the TLS handshake.</para>
///
/// <para>🔴 The failure has to arrive as a FAULT. A task that merely ends up cancelled completes without
/// faulting, and the login screens read "finished and not faulted" as a connection to send credentials
/// down — so a timeout that cancels instead of throwing turns a hang into a wrong success.</para>
/// </summary>
[TestFixture]
public class ConnectTimesOutTests
{
    /// <summary>Short enough to keep the suite fast; the shipped default is seconds.</summary>
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(250);

    /// <summary>🔴 Nothing here ever awaits a connect on its own. These test a HANG, and awaiting one
    /// reproduces it: against unbounded code the run stops responding instead of failing, which is a worse
    /// outcome than the bug. Every attempt is raced against this and then inspected.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Waits for the attempt to end, or gives up. Never throws — what happened is asserted from
    /// the task's own state.</summary>
    private static async Task Settle(Task connect)
    {
        await Task.WhenAny(connect, Task.Delay(Patience));
        if (connect.IsCompleted) _ = connect.Exception;   // observe the fault so it is not unhandled
    }

    /// <summary>A listener that accepts and then says nothing at all — a port that is open but is not this
    /// server. Holds its accepted sockets so they are not collected mid-test.</summary>
    private sealed class SilentListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _accepted = [];

        public SilentListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                        _accepted.Add(await _listener.AcceptTcpClientAsync(_cts.Token));
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            });
        }

        public int Port { get; }

        public void Dispose()
        {
            _cts.Cancel();
            foreach (var c in _accepted) c.Dispose();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    [Test]
    public async Task APortThatAnswersButNeverHandshakes_TimesOut()
    {
        using var listener = new SilentListener();
        using var transport = new TcpClientTransport { ConnectTimeout = Brief };

        var connect = transport.ConnectAsync("127.0.0.1", listener.Port, CancellationToken.None);
        await Settle(connect);

        Assert.That(connect.IsCompleted, Is.True, "the attempt never ended");
        Assert.Multiple(() =>
        {
            Assert.That(connect.Exception?.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(transport.IsConnected, Is.False, "a timed-out attempt left the transport looking connected");
        });
    }

    /// <summary>The fault is what the screens read. A cancelled task completes without faulting, and they
    /// would take that for a live connection.</summary>
    [Test]
    public async Task ATimeout_FaultsRatherThanCancels()
    {
        using var listener = new SilentListener();
        using var transport = new TcpClientTransport { ConnectTimeout = Brief };

        var connect = transport.ConnectAsync("127.0.0.1", listener.Port, CancellationToken.None);
        await Settle(connect);

        Assert.That(connect.IsCompleted, Is.True, "the attempt never ended");
        Assert.Multiple(() =>
        {
            Assert.That(connect.IsFaulted, Is.True, "a timeout that only cancels reads as success");
            Assert.That(connect.IsCanceled, Is.False);
            Assert.That(connect.IsCompletedSuccessfully, Is.False);
        });
    }

    /// <summary>Nothing listening at all. The OS refuses this one outright, so it is not the timeout doing
    /// the work — it is here because it must also end, and end as a fault.</summary>
    [Test]
    public async Task ARefusedPort_FailsWithoutWaiting()
    {
        int deadPort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();                       // the port is now nobody's
        }

        using var transport = new TcpClientTransport { ConnectTimeout = TimeSpan.FromSeconds(30) };
        var connect = transport.ConnectAsync("127.0.0.1", deadPort, CancellationToken.None);
        await Settle(connect);

        Assert.That(connect.IsCompleted, Is.True, "a refused port should fail at once, not wait out the timeout");
        Assert.That(connect.IsCompletedSuccessfully, Is.False);
    }

    /// <summary>The caller calling it off is not a timeout, and must not be reported as one.</summary>
    [Test]
    public async Task TheCallerCancelling_IsNotReportedAsATimeout()
    {
        using var listener = new SilentListener();
        using var transport = new TcpClientTransport { ConnectTimeout = TimeSpan.FromSeconds(30) };
        using var caller = new CancellationTokenSource();

        var connect = transport.ConnectAsync("127.0.0.1", listener.Port, caller.Token);
        caller.Cancel();
        await Settle(connect);

        Assert.That(connect.IsCompleted, Is.True, "cancelling did not end the attempt");
        Assert.That(connect.Exception?.InnerException, Is.Not.TypeOf<TimeoutException>(),
            "the caller's own cancellation was blamed on the server");
    }

    [Test]
    public void TheDefaultTimeout_IsBounded()
    {
        var shipped = new TcpClientTransport().ConnectTimeout;

        Assert.Multiple(() =>
        {
            Assert.That(shipped, Is.GreaterThan(TimeSpan.Zero), "a zero timeout would refuse every connection");
            Assert.That(shipped, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(30)),
                "long enough to feel like the hang it replaces");
        });
    }
}
