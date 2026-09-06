using Mirage.Shared.Security;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;

namespace Mirage.Shared.Tests.Platform;

/// <summary>Trust-on-first-use bookkeeping: what each server's certificate was last time, and whether
/// the one being offered now matches it.</summary>
[TestFixture]
public class ServerPinsTests
{
    private string _dir = "";
    private string _file = "";

    private const string FingerprintA = "aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff6666aaaa7777bbbb8888";
    private const string FingerprintB = "9999888877776666555544443333222211110000aaaabbbbccccddddeeeeffff";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-pins-" + Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_dir, "server-pins.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private ServerPins New() => new(_file);

    [Test]
    public void AServerNeverSeenIsFirstContact()
    {
        Assert.That(New().Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.FirstContact));
    }

    [Test]
    public void TheSameCertificateNextTimeIsKnown()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.Known));
    }

    /// <summary>The case the whole feature exists for.</summary>
    [Test]
    public void ADifferentCertificateIsChanged()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        Assert.That(store.Check("play.example.com", 4000, FingerprintB), Is.EqualTo(ServerTrust.Changed));
    }

    [Test]
    public void WhatWasPinnedSurvivesReopening()
    {
        New().Remember("play.example.com", 4000, FingerprintA);

        Assert.That(New().Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.Known));
    }

    /// <summary>Host and port together identify a server: the game port and the management port of one
    /// machine are reached separately, and two servers can share a host.</summary>
    [Test]
    public void PortIsPartOfTheIdentity()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        Assert.Multiple(() =>
        {
            Assert.That(store.Check("play.example.com", 4001, FingerprintA), Is.EqualTo(ServerTrust.FirstContact));
            Assert.That(store.Check("other.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.FirstContact));
        });
    }

    [Test]
    public void HostMatchingIgnoresCaseAndSurroundingSpace()
    {
        var store = New();
        store.Remember("Play.Example.COM", 4000, FingerprintA);

        Assert.That(store.Check("  play.example.com  ", 4000, FingerprintA), Is.EqualTo(ServerTrust.Known));
    }

    [Test]
    public void FingerprintComparisonIgnoresCase()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA.ToUpperInvariant());

        Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.Known));
    }

    /// <summary>Deciding and recording are separate calls, so a caller that refuses a changed certificate
    /// never writes the fingerprint it refused.</summary>
    [Test]
    public void CheckingRecordsNothing()
    {
        var store = New();

        store.Check("play.example.com", 4000, FingerprintA);

        Assert.That(store.PinnedFingerprint("play.example.com", 4000), Is.Null);
        Assert.That(File.Exists(_file), Is.False);
    }

    [Test]
    public void RememberingAgainReplacesThePin()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);
        store.Remember("play.example.com", 4000, FingerprintB);

        Assert.Multiple(() =>
        {
            Assert.That(store.Check("play.example.com", 4000, FingerprintB), Is.EqualTo(ServerTrust.Known));
            Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.Changed));
        });
    }

    [Test]
    public void ForgettingMakesTheNextConnectionAFirstContact()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        Assert.That(store.Forget("play.example.com", 4000), Is.True);
        Assert.That(store.Check("play.example.com", 4000, FingerprintB), Is.EqualTo(ServerTrust.FirstContact));
        Assert.That(store.Forget("play.example.com", 4000), Is.False);
    }

    /// <summary>Clearing a pin is a user-facing action now, so it has to be safe to invoke against an
    /// address that never had one: it reports that there was nothing to drop and leaves the disk alone.</summary>
    [Test]
    public void ForgettingAServerNeverSeenReportsNothingAndWritesNothing()
    {
        var store = New();

        Assert.That(store.Forget("play.example.com", 4000), Is.False);
        Assert.That(File.Exists(_file), Is.False);
    }

    /// <summary>The whole point of clearing one pin: every other server stays pinned. A clear that emptied
    /// the store would silently re-trust every server the installation had ever seen.</summary>
    [Test]
    public void ForgettingOneServerLeavesTheOthersPinned()
    {
        var store = New();
        store.Remember("a.example.com", 4000, FingerprintA);
        store.Remember("b.example.com", 5000, FingerprintB);
        store.Remember("a.example.com", 4001, FingerprintB);

        Assert.That(store.Forget("a.example.com", 4000), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(store.PinnedFingerprint("a.example.com", 4000), Is.Null);
            Assert.That(store.PinnedFingerprint("b.example.com", 5000), Is.EqualTo(FingerprintB));
            Assert.That(store.PinnedFingerprint("a.example.com", 4001), Is.EqualTo(FingerprintB));
            Assert.That(store.All, Has.Count.EqualTo(2));
        });
    }

    /// <summary>The clear has to reach the file, not just the copy in memory: the app that refused the
    /// connection is often restarted before the next attempt.</summary>
    [Test]
    public void AForgottenServerIsAFirstContactAgainInTheNextProcess()
    {
        New().Remember("play.example.com", 4000, FingerprintA);
        New().Forget("play.example.com", 4000);

        Assert.That(New().Check("play.example.com", 4000, FingerprintB), Is.EqualTo(ServerTrust.FirstContact));
    }

    [Test]
    public void ForgettingIgnoresCaseAndSurroundingSpaceLikeCheckDoes()
    {
        var store = New();
        store.Remember("Play.Example.COM", 4000, FingerprintA);

        Assert.That(store.Forget("  play.example.com  ", 4000), Is.True);
        Assert.That(store.PinnedFingerprint("play.example.com", 4000), Is.Null);
    }

    /// <summary>Both fingerprints are shown side by side when one is refused, so they are grouped: an
    /// unbroken 64-character run is neither comparable by eye nor wrappable.</summary>
    [Test]
    public void ForDisplayGroupsTheFingerprintInFours()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerPins.ForDisplay("aabbccdd11223344"), Is.EqualTo("aabb ccdd 1122 3344"));
            Assert.That(ServerPins.ForDisplay(FingerprintA).Replace(" ", ""), Is.EqualTo(FingerprintA));
            Assert.That(ServerPins.ForDisplay(FingerprintA).Split(' '), Has.Length.EqualTo(16));
        });
    }

    /// <summary>A server refused on first contact has no fingerprint on record, and the prompt still has
    /// to render.</summary>
    [Test]
    public void ForDisplayLeavesAnEmptyFingerprintEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerPins.ForDisplay(""), Is.Empty);
            Assert.That(ServerPins.ForDisplay("   "), Is.Empty);
            Assert.That(ServerPins.ForDisplay("abc"), Is.EqualTo("abc"));
        });
    }

    [Test]
    public void PinnedFingerprintReportsWhatIsOnRecord()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        Assert.That(store.PinnedFingerprint("play.example.com", 4000), Is.EqualTo(FingerprintA));
        Assert.That(store.PinnedFingerprint("play.example.com", 9999), Is.Null);
    }

    /// <summary>An unreadable store yields an empty set rather than throwing. Every server becomes a first
    /// contact, which re-pins instead of trusting a value that cannot be verified.</summary>
    [Test]
    public void ACorruptStoreIsEmpty_NotAnException()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "{ this is not json");

        ServerPins store = null!;
        Assert.DoesNotThrow(() => store = New());
        Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.FirstContact));
    }

    [Test]
    public void TheStoreIsCreatedOnFirstRemember()
    {
        Assert.That(File.Exists(_file), Is.False);
        New().Remember("play.example.com", 4000, FingerprintA);
        Assert.That(File.Exists(_file), Is.True);
    }

    [Test]
    public void AllListsEveryPin()
    {
        var store = New();
        store.Remember("a.example.com", 4000, FingerprintA);
        store.Remember("b.example.com", 5000, FingerprintB);

        Assert.That(store.All, Has.Count.EqualTo(2));
        Assert.That(store.All["a.example.com:4000"], Is.EqualTo(FingerprintA));
    }

    [Test]
    public void ReloadPicksUpAnEditMadeElsewhere()
    {
        var store = New();
        store.Remember("play.example.com", 4000, FingerprintA);

        New().Forget("play.example.com", 4000);
        Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.Known));

        store.Reload();
        Assert.That(store.Check("play.example.com", 4000, FingerprintA), Is.EqualTo(ServerTrust.FirstContact));
    }

    [Test]
    public void FingerprintOfIsASha256Hex()
    {
        string fp = ServerPins.FingerprintOf(Encoding.UTF8.GetBytes("certificate bytes"));

        Assert.That(fp, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(ServerPins.FingerprintOf(Encoding.UTF8.GetBytes("certificate bytes")), Is.EqualTo(fp));
        Assert.That(ServerPins.FingerprintOf(Encoding.UTF8.GetBytes("other bytes")), Is.Not.EqualTo(fp));
    }
}
