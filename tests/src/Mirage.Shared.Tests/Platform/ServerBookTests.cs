using Mirage.Shared.Security;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Mirage.Shared.Tests.Platform;

/// <summary>The address book of servers an installation knows about: what it starts with, how entries
/// are keyed, and what survives a reload.</summary>
[TestFixture]
public class ServerBookTests
{
    private string _dir = "";
    private string _file = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-book-" + Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_dir, "known-servers.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private ServerBook New() => new(_file);

    [Test]
    public void AFreshInstallStartsOnTheDefaultAddress()
    {
        var book = New();
        Assert.That(book.All.Count, Is.EqualTo(1));
        Assert.That(book.All[0].Host, Is.EqualTo(ServerBook.DefaultHost));
        Assert.That(book.All[0].Port, Is.EqualTo(ServerBook.DefaultPort));
    }

    [Test]
    public void TheDefaultEntryCarriesItsOwnName()
    {
        Assert.That(New().All[0].Name, Is.EqualTo(ServerBook.DefaultName));
    }

    [Test]
    public void AFreshInstallWritesItsBookRatherThanKeepingItInMemory()
    {
        _ = New();

        Assert.That(File.Exists(_file), Is.True, "an operator has to be able to see and ship this file");
        Assert.That(New().All[0].Name, Is.EqualTo(ServerBook.DefaultName));
    }

    [Test]
    public void AnEntryAddedWithNoNameKeepsNotHavingOne()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file,
            $$"""[{"name":"","host":"localhost","port":{{ServerBook.DefaultPort}}}]""");

        Assert.That(New().All[0].Name, Is.Empty, "leaving the name blank is a choice, not a gap to fill");
    }

    [Test]
    public void AnUnreadableBookIsLeftOnDiskRatherThanOverwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "{ this is not a server list");

        _ = New();

        Assert.That(File.ReadAllText(_file), Does.StartWith("{ this is not"));
    }

    [Test]
    public void AnAppOnAnotherPortSeedsThatPort_UnderTheSameName()
    {
        var book = new ServerBook(_file, defaultPort: 4001);

        Assert.That(book.All[0].Port, Is.EqualTo(4001));
        Assert.That(book.All[0].Name, Is.EqualTo(ServerBook.DefaultName));
    }

    [Test]
    public void ConnectingToTheDefaultLeavesItsNameAlone()
    {
        var book = New();
        book.Remember("Test Realm", ServerBook.DefaultHost, ServerBook.DefaultPort);

        Assert.That(book.All.Count, Is.EqualTo(1));
        Assert.That(book.All[0].Name, Is.EqualTo(ServerBook.DefaultName));
    }

    [Test]
    public void RememberingAddsAnEntryThatSurvivesAReload()
    {
        New().Remember("Test Realm", "play.example.com", 7000);

        var entry = New().Find("play.example.com", 7000);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Name, Is.EqualTo("Test Realm"));
    }

    [Test]
    public void TheSameAddressIsOneEntry_NotTwo()
    {
        var book = New();
        book.Remember("First", "play.example.com", 7000);
        book.Remember("Second", "play.example.com", 7000);

        Assert.That(book.All.Count(e => e.Host == "play.example.com"), Is.EqualTo(1));
    }

    [Test]
    public void AServerCannotRelabelAnEntryThatAlreadyHasAName()
    {
        var book = New();
        book.Remember("The Name I Chose", "play.example.com", 7000);
        book.Remember("What The Server Calls Itself", "play.example.com", 7000);

        Assert.That(book.Find("play.example.com", 7000)!.Name, Is.EqualTo("The Name I Chose"));
    }

    [Test]
    public void AnUnnamedEntryTakesTheNameTheServerReports()
    {
        var book = New();
        book.Rename("", "play.example.com", 7000);

        book.Remember("Test Realm", "play.example.com", 7000);

        Assert.That(book.Find("play.example.com", 7000)!.Name, Is.EqualTo("Test Realm"));
    }

    [Test]
    public void AnUnnamedConnectLeavesAnExistingNameAlone()
    {
        var book = New();
        book.Remember("Test Realm", "play.example.com", 7000);
        book.Remember("", "play.example.com", 7000);

        Assert.That(book.Find("play.example.com", 7000)!.Name, Is.EqualTo("Test Realm"));
    }

    [Test]
    public void AShippedNameSurvivesConnectingToThatServer()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file,
            """[{"name":"Official Server","host":"play.example.com","port":7000}]""");

        var book = New();
        book.Remember("Whatever The Host Named Its World", "play.example.com", 7000);

        Assert.That(New().All[0].Name, Is.EqualTo("Official Server"));
    }

    // ── Renaming (what the user asked for, not what a server reported) ────────

    [Test]
    public void RenamingReplacesANameAServerCouldNotHave()
    {
        var book = New();
        book.Remember("First", "play.example.com", 7000);

        book.Rename("Second", "play.example.com", 7000);

        Assert.That(New().Find("play.example.com", 7000)!.Name, Is.EqualTo("Second"));
    }

    [Test]
    public void RenamingAnAddressNotInTheBookAddsIt()
    {
        var book = New();
        book.Rename("Somewhere New", "new.example.com", 7000);

        Assert.That(New().Find("new.example.com", 7000)!.Name, Is.EqualTo("Somewhere New"));
    }

    [Test]
    public void AddingWithNoNameStillAddsTheAddress()
    {
        var book = New();
        book.Rename("", "new.example.com", 7000);

        var entry = New().Find("new.example.com", 7000);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Name, Is.Empty);
    }

    [Test]
    public void RenamingWithABlankNameDoesNotEraseTheNameThatIsThere()
    {
        var book = New();
        book.Rename("Test Realm", "play.example.com", 7000);

        book.Rename("   ", "play.example.com", 7000);

        Assert.That(New().Find("play.example.com", 7000)!.Name, Is.EqualTo("Test Realm"));
    }

    [Test]
    public void TwoServersMayShareAName()
    {
        var book = New();
        book.Remember("Test Realm", "one.example.com", 4000);
        book.Remember("Test Realm", "two.example.com", 4000);

        Assert.That(book.All.Count(e => e.Name == "Test Realm"), Is.EqualTo(2));
    }

    [Test]
    public void ThePortIsPartOfTheKey()
    {
        var book = New();
        book.Remember("A", "play.example.com", 4000);
        book.Remember("B", "play.example.com", 4001);

        Assert.That(book.All.Count, Is.EqualTo(3));   // the default, plus both
    }

    [Test]
    public void TheHostIsMatchedWithoutRegardToCaseOrPadding()
    {
        var book = New();
        book.Remember("Test Realm", "play.example.com", 7000);

        Assert.That(book.Find("  PLAY.Example.COM  ", 7000), Is.Not.Null);
    }

    [Test]
    public void ForgettingRemovesTheEntry()
    {
        var book = New();
        book.Remember("Test Realm", "play.example.com", 7000);

        Assert.That(book.Forget("play.example.com", 7000), Is.True);
        Assert.That(book.Find("play.example.com", 7000), Is.Null);
    }

    [Test]
    public void ForgettingAnAddressThatIsNotThereReportsSo()
    {
        Assert.That(New().Forget("nobody.example.com", 7000), Is.False);
    }

    [Test]
    public void AnEmptiedBookStaysEmpty_TheDefaultIsNotReseeded()
    {
        var book = New();
        foreach (var e in book.All) book.Forget(e.Host, e.Port);

        Assert.That(New().All, Is.Empty);
    }

    [Test]
    public void EntriesKeepTheOrderTheyWereAddedIn()
    {
        var book = New();
        book.Remember("A", "a.example.com", 4000);
        book.Remember("B", "b.example.com", 4000);

        Assert.That(New().All.Select(e => e.Host),
            Is.EqualTo(new[] { ServerBook.DefaultHost, "a.example.com", "b.example.com" }));
    }

    [Test]
    public void ABookAGameCreatorShippedIsReadAsWritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file,
            """[{"name":"Test Realm","host":"play.example.com","port":7000}]""");

        var book = New();
        Assert.That(book.All.Count, Is.EqualTo(1));
        Assert.That(book.All[0].Name, Is.EqualTo("Test Realm"));
        Assert.That(book.All[0].Port, Is.EqualTo(7000));
    }

    [Test]
    public void ADuplicateAddressInAShippedBookIsCollapsed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file,
            """[{"name":"One","host":"play.example.com","port":7000},{"name":"Two","host":"PLAY.example.com","port":7000}]""");

        Assert.That(New().All.Count, Is.EqualTo(1));
    }

    [Test]
    public void AnUnreadableBookReadsAsEmptyRatherThanThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "{ this is not a server list");

        Assert.That(New().All, Is.Empty);
    }

    [Test]
    public void AnEntryWithNoHostIsDropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file,
            """[{"name":"Nowhere","host":"","port":7000},{"name":"Real","host":"play.example.com","port":7000}]""");

        Assert.That(New().All.Count, Is.EqualTo(1));
        Assert.That(New().All[0].Host, Is.EqualTo("play.example.com"));
    }

    [Test]
    public void RememberingWithNoHostIsIgnored()
    {
        var book = New();
        book.Remember("Nowhere", "   ", 7000);

        Assert.That(book.All.Count, Is.EqualTo(1));
    }
}
