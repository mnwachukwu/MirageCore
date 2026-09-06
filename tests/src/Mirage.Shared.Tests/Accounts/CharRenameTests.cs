using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>
/// Whether a character may take a name — the whole decision the editor's rename runs, minus the one lookup
/// that needs the name registry.
///
/// <para>A rename has a small blast radius, and that is a property of the data rather than luck: guild
/// membership, friends, ignore lists, mail and market listings all key off the account LOGIN. The character
/// name is a key in exactly one place, the registry that stops two players sharing one.</para>
/// </summary>
[TestFixture]
public class CharRenameTests
{
    // ── The name on its own terms ─────────────────────────────────────────────

    [Test]
    public void AnOrdinaryName_IsAccepted()
    {
        Assert.That(CharRename.CheckName("Tavin"), Is.EqualTo(CharRenameResult.Ok));
    }

    [Test]
    public void ANameWithCharactersANameCannotHave_IsRefused()
    {
        Assert.That(CharRename.CheckName("Tav!n"), Is.EqualTo(CharRenameResult.BadChars));
    }

    [TestCase("ab")]
    [TestCase("a")]
    [TestCase("")]
    public void ANameShorterThanTheFloor_IsRefused(string name)
    {
        Assert.That(CharRename.CheckName(name), Is.EqualTo(CharRenameResult.TooShort));
    }

    /// <summary>Underscores are decoration, not letters: they do not count toward the minimum. A name that is
    /// all underscores would otherwise pass the length check and read as blank in game.</summary>
    [Test]
    public void UnderscoresDoNotCountTowardTheMinimum()
    {
        Assert.That(CharRename.CheckName("a_b"), Is.EqualTo(CharRenameResult.TooShort));
    }

    [Test]
    public void ANameOverTheCeiling_IsRefused()
    {
        Assert.That(CharRename.CheckName(new string('a', Constants.NameLength + 1)),
            Is.EqualTo(CharRenameResult.TooLong));
    }

    [Test]
    public void ANameExactlyAtTheCeiling_IsAccepted()
    {
        Assert.That(CharRename.CheckName(new string('a', Constants.NameLength)),
            Is.EqualTo(CharRenameResult.Ok));
    }

    // ── The character it would land on ────────────────────────────────────────

    [Test]
    public void AnEmptySlot_HasNothingToRename()
    {
        Assert.That(CharRename.CheckTarget("", "Tavin", isOnline: false),
            Is.EqualTo(CharRenameResult.NoCharacter));
    }

    [Test]
    public void RenamingToTheNameItAlreadyHas_IsRefused()
    {
        Assert.That(CharRename.CheckTarget("Tavin", "Tavin", isOnline: false),
            Is.EqualTo(CharRenameResult.Unchanged));
    }

    /// <summary>The name is live identity — on the map, in party lists, in somebody else's open trade window.
    /// Moving it out from under all that is refused; the operator can kick first.</summary>
    [Test]
    public void ACharacterThatIsLoggedIn_CannotBeRenamed()
    {
        Assert.That(CharRename.CheckTarget("Tavin", "Bree", isOnline: true),
            Is.EqualTo(CharRenameResult.Online));
    }

    [Test]
    public void AnOfflineCharacterWithARealNewName_IsAccepted()
    {
        Assert.That(CharRename.CheckTarget("Tavin", "Bree", isOnline: false),
            Is.EqualTo(CharRenameResult.Ok));
    }

    /// <summary>Refused for being unchanged before it is refused for being online: a no-op rename should say
    /// what it is, not send the operator off to kick somebody for nothing.</summary>
    [Test]
    public void UnchangedIsReportedAheadOfOnline()
    {
        Assert.That(CharRename.CheckTarget("Tavin", "Tavin", isOnline: true),
            Is.EqualTo(CharRenameResult.Unchanged));
    }

    [Test]
    public void SurroundingSpaceIsNotAChange()
    {
        Assert.That(CharRename.CheckTarget("Tavin ", " Tavin", isOnline: false),
            Is.EqualTo(CharRenameResult.Unchanged));
    }

    // ── Identity ──────────────────────────────────────────────────────────────
    // The registry keys names case- and underscore-insensitively, so "B_o_b" cannot be created beside "Bob".
    // A character respelling its OWN name collides with itself, and the taken check has to let that through.

    [TestCase("Bob", "bob")]
    [TestCase("Bob", "B_o_b")]
    [TestCase("B_o_b", "bOB")]
    [TestCase("Bob", " Bob ")]
    public void SpellingsOfOneName_AreOneIdentity(string a, string b)
    {
        Assert.That(CharRename.SameIdentity(a, b), Is.True);
    }

    [TestCase("Bob", "Bobb")]
    [TestCase("Bob", "Rob")]
    public void DifferentNames_AreDifferentIdentities(string a, string b)
    {
        Assert.That(CharRename.SameIdentity(a, b), Is.False);
    }

    /// <summary>A case-only change is a real rename — it is not Unchanged, and it must not be refused as
    /// taken by its own registry entry.</summary>
    [Test]
    public void ARespellingOfItsOwnName_IsARenameAndCollidesWithNobodyElse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CharRename.CheckTarget("bob", "Bob", isOnline: false), Is.EqualTo(CharRenameResult.Ok));
            Assert.That(CharRename.SameIdentity("bob", "Bob"), Is.True);
        });
    }
}
