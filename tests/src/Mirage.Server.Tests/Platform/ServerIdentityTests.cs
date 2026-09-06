using Mirage.Server.Host.Net;
using NUnit.Framework;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// The server's TLS identity, which is the thing certificate pinning stands on.
///
/// <para> The property that matters is that the fingerprint SURVIVES A RESTART. A certificate that
/// changes per start makes every reconnection look like an interception, which is how a security
/// warning becomes something people learn to dismiss.</para>
/// </summary>
[TestFixture]
public class ServerIdentityTests
{
    private string _dir = "";
    private string Path0 => Path.Combine(_dir, "server-identity.pfx");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Test]
    public void TheFingerprintSurvivesARestart()
    {
        using var first = SelfSignedCertificate.LoadOrCreate(Path0);
        using var second = SelfSignedCertificate.LoadOrCreate(Path0);

        Assert.That(SelfSignedCertificate.Fingerprint(second),
            Is.EqualTo(SelfSignedCertificate.Fingerprint(first)),
            "a second start must present the identity the first one wrote — pinning depends on it");
    }

    [Test]
    public void TheIdentityIsWrittenOnFirstUse()
    {
        Assert.That(File.Exists(Path0), Is.False);
        using var cert = SelfSignedCertificate.LoadOrCreate(Path0);
        Assert.That(File.Exists(Path0), Is.True, "nothing to load next time means nothing to pin");
    }

    /// <summary>Both listeners ask for the identity independently; they must get the same one, or an
    /// operator comparing a fingerprint has to know which port produced it.</summary>
    [Test]
    public void BothListenersGetOneIdentity()
    {
        using var game = SelfSignedCertificate.LoadOrCreate(Path0);
        using var management = SelfSignedCertificate.LoadOrCreate(Path0);

        Assert.That(SelfSignedCertificate.Fingerprint(management),
            Is.EqualTo(SelfSignedCertificate.Fingerprint(game)));
    }

    /// <summary> An unreadable identity must NOT be silently replaced. Overwriting it would change who
    /// the server claims to be and trip every client that had pinned it, so it is the operator's call.</summary>
    [Test]
    public void ACorruptIdentityIsAnError_NotASilentReissue()
    {
        File.WriteAllText(Path0, "this is not a certificate");

        Assert.Throws<InvalidOperationException>(() => SelfSignedCertificate.LoadOrCreate(Path0));
        Assert.That(File.ReadAllText(Path0), Is.EqualTo("this is not a certificate"),
            "the unreadable file is left exactly as found for the operator to look at");
    }

    [Test]
    public void AFreshIdentityIsUniquePerServer()
    {
        using var a = SelfSignedCertificate.Create();
        using var b = SelfSignedCertificate.Create();

        Assert.That(SelfSignedCertificate.Fingerprint(a), Is.Not.EqualTo(SelfSignedCertificate.Fingerprint(b)),
            "two servers must not share an identity, or pinning one would accept the other");
    }

    [Test]
    public void TheFingerprintIsASha256Hex_AndItsDisplayFormIsTheSameValue()
    {
        using var cert = SelfSignedCertificate.LoadOrCreate(Path0);
        string raw = SelfSignedCertificate.Fingerprint(cert);

        Assert.Multiple(() =>
        {
            Assert.That(raw, Does.Match("^[0-9a-f]{64}$"), "64 lower-case hex characters");
            Assert.That(SelfSignedCertificate.FingerprintForDisplay(cert).Replace(" ", ""),
                Is.EqualTo(raw.ToUpperInvariant()),
                "the spaced form an operator reads must be the same value a client pins");
        });
    }

    /// <summary>The private key has to come back out of the file, or the server cannot serve TLS with it —
    /// a certificate that loads but cannot sign fails at the first connection instead of at startup.</summary>
    [Test]
    public void TheReloadedIdentityStillHasItsPrivateKey()
    {
        using (var _ = SelfSignedCertificate.LoadOrCreate(Path0)) { }
        using var reloaded = SelfSignedCertificate.LoadOrCreate(Path0);

        Assert.That(reloaded.HasPrivateKey, Is.True);
    }
}
