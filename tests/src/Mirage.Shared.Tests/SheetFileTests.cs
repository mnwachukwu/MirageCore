using NUnit.Framework;

namespace Mirage.Shared.Tests;

/// <summary>
/// What a graphics sheet's filename means.
///
/// <para>The editor and the game both read the same folder and must agree about it completely: which files
/// are sheets, which number each one claims, and where its transparency comes from. Answered separately
/// they drift, and the drift is invisible — a sheet that loads in one and not the other looks like a
/// rendering bug rather than a naming disagreement.</para>
/// </summary>
[TestFixture]
public class SheetFileTests
{
    /// <summary>Both authored formats are sheets, whatever case the extension is written in — a file
    /// picked on Windows arrives as ".PNG" often enough to matter.</summary>
    [Test]
    public void BothFormatsAreSupportedInAnyCase()
    {
        foreach (string name in new[] { "0_a.bmp", "0_a.BMP", "0_a.png", "0_a.PnG" })
            Assert.That(SheetFile.IsSupported(name), Is.True, name);
    }

    /// <summary>Anything else is not a sheet. A working file beside the art is normal, and treating one as
    /// a sheet would give it an index and put it in the world.</summary>
    [Test]
    public void OtherFilesAreNotSheets()
    {
        foreach (string name in new[] { "0_a.jpg", "0_a.psd", "0_a.gif", "notes.txt", "0_a" })
            Assert.That(SheetFile.IsSupported(name), Is.False, name);
    }

    /// <summary>The transparency rule is the extension and nothing else: a BMP is keyed, a PNG is not.
    /// Deciding it from the pixels instead would mean an art edit that removed the last transparent pixel
    /// silently changed how the whole sheet loads.</summary>
    [Test]
    public void OnlyBmpIsColorKeyed()
    {
        Assert.That(SheetFile.UsesColorKey("0_tiles.bmp"), Is.True);
        Assert.That(SheetFile.UsesColorKey("0_tiles.BMP"), Is.True);
        Assert.That(SheetFile.UsesColorKey("0_tiles.png"), Is.False);
        Assert.That(SheetFile.UsesColorKey("0_tiles.PNG"), Is.False);
    }

    /// <summary>The leading digits are the index. This is what every painted tile stores, so it is the one
    /// piece of a filename that is data.</summary>
    [Test]
    public void TheLeadingDigitsAreTheIndex()
    {
        Assert.That(SheetFile.ParseIndex("0_Tiles"), Is.EqualTo(0));
        Assert.That(SheetFile.ParseIndex("12_dungeon"), Is.EqualTo(12));
        Assert.That(SheetFile.ParseIndex("07_forest"), Is.EqualTo(7), "leading zeros are the same number");
        Assert.That(SheetFile.ParseIndex("12dungeon"), Is.EqualTo(12), "the separator is convention, not rule");
    }

    /// <summary>A name that does not start with digits claims no index, and is not a sheet at all.</summary>
    [Test]
    public void ANameWithoutLeadingDigitsClaimsNoIndex()
    {
        foreach (string stem in new[] { "Tiles", "_7_forest", "-1_x", "", " 3_x" })
            Assert.That(SheetFile.ParseIndex(stem), Is.EqualTo(-1), stem);
    }

    /// <summary>A number too large to be one reads as no index rather than wrapping or throwing.</summary>
    [Test]
    public void AnUnparseableNumberClaimsNoIndex()
    {
        Assert.That(SheetFile.ParseIndex("99999999999999999999_x"), Is.EqualTo(-1));
    }

    /// <summary>The label is what is left after the index and one separator. It is shown in the tileset
    /// picker, so the manager and the picker have to agree about it exactly.</summary>
    [Test]
    public void TheLabelIsWhatFollowsTheIndex()
    {
        Assert.That(SheetFile.DisplayName("0_Tiles"), Is.EqualTo("Tiles"));
        Assert.That(SheetFile.DisplayName("12-dungeon"), Is.EqualTo("dungeon"));
        Assert.That(SheetFile.DisplayName("3 cave"), Is.EqualTo("cave"));
        Assert.That(SheetFile.DisplayName("12__x"), Is.EqualTo("_x"), "only one separator is eaten");
    }

    /// <summary>A name that is only digits keeps them. An empty label would leave the sheet with nothing
    /// to call it in the picker.</summary>
    [Test]
    public void ANameThatIsOnlyDigitsKeepsThem()
    {
        Assert.That(SheetFile.DisplayName("7"), Is.EqualTo("7"));
    }

    /// <summary>Building a filename and reading it back gives the same index and label, which is what makes
    /// rename and import safe to round-trip.</summary>
    [Test]
    public void AFileNameRoundTripsThroughItsParts()
    {
        string name = SheetFile.FileName(42, "deep cave", ".png");

        Assert.That(name, Is.EqualTo("42_deep cave.png"));
        Assert.That(SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(name)), Is.EqualTo(42));
        Assert.That(SheetFile.DisplayName(Path.GetFileNameWithoutExtension(name)), Is.EqualTo("deep cave"));
        Assert.That(SheetFile.UsesColorKey(name), Is.False);
    }
}
