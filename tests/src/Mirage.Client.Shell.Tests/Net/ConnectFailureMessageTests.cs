using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Shared.Security;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mirage.Client.Shell.Tests.Net;

/// <summary>
/// What a failed connection tells the reader, and the gate that decides a connection happened at all.
///
/// <para>"Cannot connect" and "no answer" look the same on screen and mean different things: a server that
/// is down, versus an address or port nothing is listening on. The second is the one a player can fix.</para>
/// </summary>
[TestFixture]
public class ConnectFailureMessageTests
{
    [OneTimeSetUp]
    public void LoadStrings() =>
        ClientStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"), "en");

    private static string Describe(Exception ex) =>
        ConnectFailure.Describe(Task.FromException(ex));

    [Test]
    public void ATimeout_SaysNothingAnswered()
    {
        Assert.That(Describe(new TimeoutException("no answer")),
            Is.EqualTo(ClientStrings.Get(ClientStrings.Common_ConnectionTimedOut)));
    }

    [Test]
    public void ARefusal_SaysItCannotConnect()
    {
        Assert.That(Describe(new SocketException((int)SocketError.ConnectionRefused)),
            Is.EqualTo(ClientStrings.Get(ClientStrings.Common_CannotConnect)));
    }

    /// <summary>A changed server identity outranks both: it is a security notice, not a reachability one.</summary>
    [Test]
    public void AChangedIdentity_KeepsItsOwnWarning()
    {
        Assert.That(Describe(new ServerIdentityChangedException("host", 4000, "aa:bb", "cc:dd")),
            Is.EqualTo(ClientStrings.Get(ClientStrings.Common_ServerIdentityChanged)));
    }

    /// <summary>The screens need the exception itself, not a sentence about it: the prompt they raise has
    /// to name the server and both fingerprints, and drop the pin for that exact address.</summary>
    [Test]
    public void AChangedIdentity_IsHandedBackWithBothFingerprints()
    {
        var changed = new ServerIdentityChangedException("play.example.com", 4000, "aaaa", "bbbb");

        var found = ConnectFailure.IdentityChange(Task.FromException(changed));

        Assert.That(found, Is.SameAs(changed));
        Assert.Multiple(() =>
        {
            Assert.That(found!.Host, Is.EqualTo("play.example.com"));
            Assert.That(found.Port, Is.EqualTo(4000));
            Assert.That(found.Expected, Is.EqualTo("aaaa"));
            Assert.That(found.Actual, Is.EqualTo("bbbb"));
        });
    }

    /// <summary>Every other failure leaves the offer alone: a timeout must not put a "trust this
    /// certificate" button in front of anyone.</summary>
    [Test]
    public void AnyOtherFailure_OffersNothingToTrust()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConnectFailure.IdentityChange(Task.FromException(new TimeoutException())), Is.Null);
            Assert.That(ConnectFailure.IdentityChange(
                Task.FromException(new SocketException((int)SocketError.ConnectionRefused))), Is.Null);
            Assert.That(ConnectFailure.IdentityChange(Task.CompletedTask), Is.Null);
        });
    }

    [Test]
    public void TheThreeMessages_AreActuallyDifferent()
    {
        var messages = new[]
        {
            ClientStrings.Get(ClientStrings.Common_ConnectionTimedOut),
            ClientStrings.Get(ClientStrings.Common_CannotConnect),
            ClientStrings.Get(ClientStrings.Common_ServerIdentityChanged),
        };

        Assert.That(messages, Is.Unique, "two failures that read identically explain nothing");
    }

    /// <summary>🔴 Every screen that connects must test for SUCCESS, not for the absence of a fault. A
    /// cancelled attempt completes without faulting, so "not faulted" sends credentials down a socket that
    /// is already gone. Read from source: the screens need a graphics device to build.</summary>
    [Test]
    public void EveryConnectingScreen_ProceedsOnlyOnSuccess()
    {
        string root = typeof(ConnectFailureMessageTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string dir = Path.Combine(root, "client", "src", "Mirage.Client.Shell", "Screens");

        var screens = Directory.GetFiles(dir, "*.cs")
            .Select(f => (Name: Path.GetFileName(f), Code: Strip(File.ReadAllText(f))))
            .Where(s => s.Code.Contains("_connectTask", StringComparison.Ordinal))
            .ToList();

        Assert.That(screens, Has.Count.EqualTo(4),
            "the screens that connect moved: " + string.Join(", ", screens.Select(s => s.Name)));

        foreach (var (name, code) in screens)
        {
            Assert.That(code, Does.Contain("_connectTask.IsCompletedSuccessfully"),
                $"{name} acts on a connect task that merely finished");
            Assert.That(code, Does.Not.Match(@"if\s*\(\s*_connectTask\.IsFaulted\s*\)"),
                $"{name} still branches on IsFaulted, which a cancelled attempt does not set");
        }
    }

    /// <summary>Source with comments removed — a commented-out branch still contains the text.</summary>
    private static string Strip(string source) =>
        string.Join("\n", source.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
}
