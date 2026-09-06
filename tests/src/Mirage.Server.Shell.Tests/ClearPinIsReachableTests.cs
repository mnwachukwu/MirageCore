using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// The shell can clear a certificate pin without anyone editing JSON by hand.
///
/// <para>A remote server is trusted on first attach and pinned. A different certificate afterwards is
/// refused, and <c>Forget</c> on the address book drops only the saved entry — the pin stays, so
/// re-adding the same address fails identically. The way out is the Clear Pin button and the offer the
/// refusal itself puts up.</para>
///
/// <para>⚠️ Read from SOURCE with comments stripped, for the same reason
/// <see cref="AttachUsesCurrentSettingsTests"/> is: driving this needs a TLS listener and writes the
/// certificate pin store, which is per-user state a test must not touch. This catches the wiring being
/// removed, which is the regression that would strand a user again.</para>
/// </summary>
[TestFixture]
public class ClearPinIsReachableTests
{
    static string RepoRoot()
    {
        string root = typeof(ClearPinIsReachableTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    static string StripComments(string raw) =>
        string.Join("\n", raw.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));

    static string ViewModel() => StripComments(File.ReadAllText(Path.Combine(RepoRoot(),
        "server", "src", "Mirage.Server.Shell", "ViewModels", "MainWindowViewModel.cs")));

    static string Window() => File.ReadAllText(Path.Combine(RepoRoot(),
        "server", "src", "Mirage.Server.Shell", "Views", "MainWindow.axaml"));

    static string Connection() => StripComments(File.ReadAllText(Path.Combine(RepoRoot(),
        "server", "src", "Mirage.Server.Shell", "Services", "RemoteServerConnection.cs")));

    [Test]
    public void TheViewModelCanDropAPin()
    {
        Assert.That(ViewModel(), Does.Contain("ServerPinStore.Store.Forget("),
            "nothing in the shell clears a certificate pin, so a changed server identity can only be "
            + "recovered from by hand-editing server-pins.json");
    }

    [Test]
    public void ClearPinIsBoundToAButton()
    {
        Assert.That(Window(), Does.Contain("ClearPinCommand"),
            "the Clear Pin command exists but no control invokes it");
    }

    /// <summary>The offer has to reach the operator at the refusal, not only as a button they might find:
    /// the identity-changed banner is what turns a dead end into a decision.</summary>
    [Test]
    public void TheRefusalRaisesTheOffer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ViewModel(), Does.Contain("ShowIdentityChange()"),
                "a refused certificate no longer raises the trust offer");
            Assert.That(Window(), Does.Contain("TrustNewCertificateCommand"),
                "the identity-changed banner has no way to accept the new certificate");
            Assert.That(Window(), Does.Contain("IdentityMessage"),
                "the identity-changed banner is never shown");
        });
    }

    /// <summary>The banner names both fingerprints, which means the connection has to hand them up rather
    /// than reporting a bare token.</summary>
    [Test]
    public void TheConnectionSurfacesBothFingerprints()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Connection(), Does.Contain("LastIdentityChange"),
                "the refused and expected fingerprints never leave the connection");
            Assert.That(ViewModel(), Does.Contain("ServerPins.ForDisplay("),
                "the fingerprints are shown as an unbroken 64-character run");
        });
    }

    /// <summary>The two stores stay separate: dropping a saved server must not silently drop its pin.
    /// Clearing trust is its own action, taken deliberately.</summary>
    [Test]
    public void ForgettingAServerStillLeavesItsPinAlone()
    {
        string code = ViewModel();
        int forget = code.IndexOf("private void ForgetServer()", StringComparison.Ordinal);
        Assert.That(forget, Is.GreaterThan(-1), "ForgetServer has moved or been renamed");

        int end = code.IndexOf("\n    }", forget, StringComparison.Ordinal);
        Assert.That(code[forget..end], Does.Not.Contain("ServerPinStore"),
            "removing a server from the address book now also drops its certificate pin, which clears "
            + "trust without the operator deciding to");
    }
}
