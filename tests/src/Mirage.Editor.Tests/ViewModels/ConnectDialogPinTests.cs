using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Security;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Clearing a certificate pin from the connect dialog.
///
/// <para>A self-signed server is trusted on first contact and pinned; a different certificate afterwards
/// refuses the connection. Until this existed the only way back was hand-editing
/// <c>server-pins.json</c> — Forget dropped the address book entry and left the pin behind, so re-adding
/// the server failed exactly the same way.</para>
///
/// <para>The two stores stay separate on purpose, so these tests pin both halves: clearing a pin must not
/// touch the address book, and the pin that gets dropped must be the one for that address alone.</para>
///
/// <para>Per-user paths are redirected for the whole assembly by <see cref="UserStateIsolation"/>, so the
/// pin store these tests write is a temp file, never the developer's own.</para>
/// </summary>
[TestFixture]
public class ConnectDialogPinTests
{
    private const string Host = "pin.example.com";
    private const int Port = 4000;
    private const string Pinned = "aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff6666aaaa7777bbbb8888";
    private const string Offered = "9999888877776666555544443333222211110000aaaabbbbccccddddeeeeffff";

    private EditorConnection _conn = null!;

    [SetUp]
    public void SetUp()
    {
        _conn = new EditorConnection();
        foreach (string key in ServerPinStore.Store.All.Keys.ToArray())
        {
            string[] parts = key.Split(':');
            ServerPinStore.Store.Forget(parts[0], int.Parse(parts[1]));
        }
    }

    [TearDown]
    public void TearDown() => _conn.Dispose();

    private ConnectDialogViewModel Dialog() =>
        new(_conn) { Host = Host, Port = Port };

    /// <summary>The button is the only place the dialog says whether this address has a certificate on
    /// record, so its enabled state has to track that and nothing else.</summary>
    [Test]
    public void ClearPinIsOfferedOnlyWhenSomethingIsPinned()
    {
        var vm = Dialog();
        Assert.That(vm.ClearPinCommand.CanExecute(null), Is.False);

        ServerPinStore.Store.Remember(Host, Port, Pinned);

        Assert.That(vm.ClearPinCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void ClearingAPinDropsOnlyThatServer()
    {
        ServerPinStore.Store.Remember(Host, Port, Pinned);
        ServerPinStore.Store.Remember("other.example.com", 4000, Pinned);
        ServerPinStore.Store.Remember(Host, 4001, Pinned);

        Dialog().ClearPinCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(ServerPinStore.Store.PinnedFingerprint(Host, Port), Is.Null);
            Assert.That(ServerPinStore.Store.PinnedFingerprint("other.example.com", 4000), Is.EqualTo(Pinned));
            Assert.That(ServerPinStore.Store.PinnedFingerprint(Host, 4001), Is.EqualTo(Pinned));
        });
    }

    /// <summary>The two stores are separate on purpose. Clearing the certificate must not quietly delete
    /// the saved server with it.</summary>
    [Test]
    public void ClearingAPinLeavesTheAddressBookEntryAlone()
    {
        ServerBookStore.Book.Rename("Test Server", Host, Port);
        ServerPinStore.Store.Remember(Host, Port, Pinned);

        Dialog().ClearPinCommand.Execute(null);

        Assert.That(ServerBookStore.Book.Find(Host, Port)?.Name, Is.EqualTo("Test Server"));
    }

    /// <summary>The mirror of the above: dropping the saved server leaves the pin in place. This is the
    /// behaviour that stranded the user, and it stays — the pin now has its own way out.</summary>
    [Test]
    public void ForgettingTheServerStillLeavesThePinBehind()
    {
        ServerBookStore.Book.Rename("Test Server", Host, Port);
        ServerPinStore.Store.Remember(Host, Port, Pinned);

        var vm = Dialog();
        vm.ForgetServerCommand.Execute(null);

        Assert.That(ServerPinStore.Store.PinnedFingerprint(Host, Port), Is.EqualTo(Pinned));
        Assert.That(vm.ClearPinCommand.CanExecute(null), Is.True);
    }

    /// <summary>The refusal states both fingerprints, because "the identity changed" alone gives the
    /// operator nothing to check against what the server publishes.</summary>
    [Test]
    public void ARefusedCertificateIsShownBesideTheOneOnRecord()
    {
        var vm = Dialog();

        vm.ShowIdentityChange(new ServerIdentityChangedException(Host, Port, Pinned, Offered));

        Assert.Multiple(() =>
        {
            Assert.That(vm.IdentityMessage, Does.Contain(ServerPins.ForDisplay(Pinned)));
            Assert.That(vm.IdentityMessage, Does.Contain(ServerPins.ForDisplay(Offered)));
            Assert.That(vm.IdentityMessage, Does.Contain(Host));
        });
    }

    [Test]
    public void TrustingTheNewCertificateDropsThePinAndClosesTheOffer()
    {
        ServerPinStore.Store.Remember(Host, Port, Pinned);
        var vm = Dialog();
        vm.ShowIdentityChange(new ServerIdentityChangedException(Host, Port, Pinned, Offered));

        vm.TrustNewCertificateCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(ServerPinStore.Store.PinnedFingerprint(Host, Port), Is.Null);
            Assert.That(vm.IdentityMessage, Is.Empty);
            Assert.That(vm.StatusMessage, Is.Not.Empty);
        });
    }

    /// <summary>Nothing is trusted just because the prompt was raised: with no confirmation the pin
    /// stands, and the next connection is refused again.</summary>
    [Test]
    public void ShowingTheOfferDoesNotItselfDropThePin()
    {
        ServerPinStore.Store.Remember(Host, Port, Pinned);
        var vm = Dialog();

        vm.ShowIdentityChange(new ServerIdentityChangedException(Host, Port, Pinned, Offered));

        Assert.That(ServerPinStore.Store.PinnedFingerprint(Host, Port), Is.EqualTo(Pinned));
    }
}
