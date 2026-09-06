using Mirage.Editor.Services;
using Mirage.Shared;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// The rules a folder of graphics sheets obeys.
///
/// <para>A sheet's number is the only part of its filename that is data — every painted tile stores it and
/// nothing else. So the operations here divide sharply: renaming is free, and anything that moves a number
/// silently repoints art. These pin that division, and pin the three ways a file can fail to be a sheet,
/// each of which the loaders pass over without a word.</para>
///
/// <para>Everything runs against a temp folder. <c>UserPaths.RootOverride</c> already redirects per-user
/// state for the whole assembly, so no test here can reach a real assets folder.</para>
/// </summary>
[TestFixture]
public class SheetLibraryTests
{
    private string _dir = "";
    private string _bin = "";

    [SetUp]
    public void MakeFolder()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-sheets-" + Guid.NewGuid().ToString("N"));
        _bin = Path.Combine(_dir, "recycle");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void DropFolder()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp folder is not worth failing on */ }
    }

    // A real 24-bit BMP header, so the size the library reports comes from the same bytes a loader reads.
    private string Bmp(string fileName, int width = 64, int height = 96)
    {
        string path = Path.Combine(_dir, fileName);
        var header = new byte[54];
        header[0] = 0x42; header[1] = 0x4D;
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(width).CopyTo(header, 18);
        BitConverter.GetBytes(height).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);
        File.WriteAllBytes(path, header);
        return path;
    }

    private SheetScan Scan(int maxIndex = Constants.MaxTilesets) =>
        SheetLibrary.Scan(_dir, maxIndex);

    private static IEnumerable<int> IndexesOf(SheetScan scan) => scan.Sheets.Select(s => s.Index);

    private static SheetProblem? ProblemOf(SheetScan scan, SheetProblemKind kind) =>
        scan.Sheets is not null ? scan.Problems.FirstOrDefault(p => p.Kind == kind) : null;

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>The number in the filename is the index, and the rest is a label. This is the whole
    /// convention; if it drifts, every map in the world points at different art.</summary>
    [Test]
    public void TheLeadingNumberIsTheIndexAndTheRestIsALabel()
    {
        Bmp("7_forest.bmp");

        var sheet = Scan().Sheets.Single();

        Assert.That(sheet.Index, Is.EqualTo(7));
        Assert.That(sheet.DisplayName, Is.EqualTo("forest"));
    }

    /// <summary>Dimensions come from the file header. The manager lists a size for every sheet, and
    /// decoding each one to learn it would hold a bitmap per row for a number the first bytes carry.</summary>
    [Test]
    public void SizeIsReadFromTheHeader()
    {
        Bmp("0_tiles.bmp", width: 128, height: 256);

        var sheet = Scan().Sheets.Single();

        Assert.That((sheet.PixelWidth, sheet.PixelHeight), Is.EqualTo((128, 256)));
        Assert.That(sheet.TileGrid, Is.EqualTo((4, 8)));
        Assert.That(sheet.IsTileAligned, Is.True);
    }

    /// <summary>A file with no leading digits is not a sheet, and today it is skipped in silence. Reporting
    /// it is the whole point: the author sees a folder with their art in it and no art in the editor.</summary>
    [Test]
    public void AFileWithNoIndexIsReportedNotIgnored()
    {
        Bmp("Tiles.bmp");

        var scan = Scan();

        Assert.That(scan.Sheets, Is.Empty, "it is genuinely not a sheet");
        Assert.That(ProblemOf(scan, SheetProblemKind.NoIndexPrefix)?.Paths.Single(),
            Does.EndWith("Tiles.bmp"));
    }

    /// <summary>Both claimants are named. Which one the loader keeps is decided by directory enumeration
    /// order, so reporting one as the winner would be dressing an accident up as a rule.</summary>
    [Test]
    public void TwoFilesClaimingOneIndexAreBothReported()
    {
        Bmp("3_forest.bmp");
        Bmp("3_swamp.bmp");

        var problem = ProblemOf(Scan(), SheetProblemKind.DuplicateIndex);

        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Index, Is.EqualTo(3));
        Assert.That(problem.Paths.Count, Is.EqualTo(2));
        Assert.That(problem.Paths.Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "3_forest.bmp", "3_swamp.bmp" }));
    }

    /// <summary>An index at or past the ceiling is dropped by the loader. The control half matters: the
    /// last legal index must still load, or a bug that rejects everything would pass.</summary>
    [Test]
    public void AnIndexPastTheCeilingIsReportedAndTheLastLegalOneIsNot()
    {
        Bmp("255_high.bmp");
        Bmp("256_toohigh.bmp");

        var scan = Scan();

        Assert.That(IndexesOf(scan), Is.EqualTo(new[] { 255 }));
        Assert.That(ProblemOf(scan, SheetProblemKind.IndexOutOfRange)?.Index, Is.EqualTo(256));
    }

    /// <summary>A sheet that is not a whole number of tiles has a strip along its edge holding cells the
    /// picker can never reach. Nothing else in the editor says so.</summary>
    [Test]
    public void ASheetThatIsNotAWholeNumberOfTilesIsReported()
    {
        Bmp("0_ragged.bmp", width: 100, height: 96);

        var scan = Scan();

        Assert.That(ProblemOf(scan, SheetProblemKind.NotTileAligned)?.Index, Is.EqualTo(0));
        Assert.That(scan.Sheets.Single().IsTileAligned, Is.False);
    }

    /// <summary>A tidy folder reports nothing. Without this the problem list could be noise nobody reads,
    /// and every other test here would still pass.</summary>
    [Test]
    public void AFolderWithNothingWrongReportsNoProblems()
    {
        Bmp("0_tiles.bmp");
        Bmp("1_cave.bmp");

        var scan = Scan();

        Assert.That(IndexesOf(scan), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(scan.Problems, Is.Empty);
    }

    /// <summary>A file the loaders do not read is not a sheet and not a problem either — a stray .txt or a
    /// .psd working file beside the art is normal, and flagging it would train people to ignore the list.</summary>
    [Test]
    public void AnUnsupportedFileIsNeitherASheetNorAProblem()
    {
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "0_source.psd"), "x");

        var scan = Scan();

        Assert.That(scan.Sheets, Is.Empty);
        Assert.That(scan.Problems, Is.Empty);
    }

    // ── Index allocation ──────────────────────────────────────────────────────

    /// <summary>The lowest free index is taken, so a hole left by a delete is filled. The control half is
    /// what makes this test worth having: an implementation returning highest+1 also satisfies "returns a
    /// free index".</summary>
    [Test]
    public void TheLowestFreeIndexIsTaken()
    {
        Bmp("0_a.bmp");
        Bmp("1_b.bmp");
        Bmp("3_d.bmp");

        int next = SheetLibrary.NextFreeIndex(Scan(), Constants.MaxTilesets);

        Assert.That(next, Is.EqualTo(2));
        Assert.That(next, Is.Not.EqualTo(4), "highest+1 would leave the hole open forever");
    }

    /// <summary>A full folder has no index to give, and says so rather than handing back one in use.</summary>
    [Test]
    public void AFullFolderHasNoFreeIndex()
    {
        for (int i = 0; i < 4; i++) Bmp($"{i}_s.bmp");

        Assert.That(SheetLibrary.NextFreeIndex(Scan(maxIndex: 4), 4), Is.EqualTo(-1));
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    /// <summary>Renaming changes the label and keeps the number. Because the number is what maps store,
    /// this is the one edit here that cannot break a world — and it must stay that way.</summary>
    [Test]
    public void RenameKeepsTheIndex()
    {
        Bmp("7_forest.bmp");
        var before = Scan().Sheets.Single();

        string path = SheetLibrary.Rename(before, "woods");

        Assert.That(Path.GetFileName(path), Is.EqualTo("7_woods.bmp"));
        var after = Scan().Sheets.Single();
        Assert.That(after.Index, Is.EqualTo(7));
        Assert.That(after.DisplayName, Is.EqualTo("woods"));
    }

    /// <summary>A name no filesystem would take is refused before it reaches one. Asked of
    /// PortableFileName rather than of this machine: a name Windows accepts and Linux does not would
    /// author a world that cannot be opened there.</summary>
    [Test]
    public void ARenameToAnUnusableNameIsRefused()
    {
        Bmp("0_tiles.bmp");
        var sheet = Scan().Sheets.Single();

        foreach (string bad in new[] { "", "   ", "for/est", "wood:s", "up\\one", "what?", "star*" })
            Assert.That(() => SheetLibrary.Rename(sheet, bad),
                Throws.ArgumentException, $"'{bad}' should be refused");

        Assert.That(File.Exists(sheet.Path), Is.True, "a refused rename leaves the file alone");
    }

    /// <summary>A label that would be a reserved device name on its own is fine, because the index prefix
    /// means it never is one: "NUL" becomes "0_NUL.bmp", and DOS only ever objected to the bare stem.
    /// Refusing it would be the tool inventing a rule the filesystem does not have.</summary>
    [Test]
    public void AReservedWordIsAFineLabelBecauseThePrefixIsPartOfTheName()
    {
        Bmp("0_tiles.bmp");
        var sheet = Scan().Sheets.Single();

        string path = SheetLibrary.Rename(sheet, "NUL");

        Assert.That(Path.GetFileName(path), Is.EqualTo("0_NUL.bmp"));
        Assert.That(File.Exists(path), Is.True);
    }

    /// <summary>Import copies rather than moves, so the file the author picked is still where they left
    /// it, and lands on the index it was told to.</summary>
    [Test]
    public void ImportCopiesTheFileToItsIndex()
    {
        string source = Bmp("source.bmp");
        string target = Path.Combine(_dir, "sheets");

        string landed = SheetLibrary.Import(source, target, 5);

        Assert.That(Path.GetFileName(landed), Is.EqualTo("5_source.bmp"));
        Assert.That(File.Exists(source), Is.True, "the original is copied, not moved");
        Assert.That(SheetLibrary.Scan(target, Constants.MaxTilesets).Sheets.Single().Index, Is.EqualTo(5));
    }

    /// <summary>Replacing keeps the index, which is the entire reason to offer it: iterating on art must
    /// not repoint the maps already painted with it.</summary>
    [Test]
    public void ReplaceKeepsTheIndexAndTheName()
    {
        Bmp("4_town.bmp", width: 64, height: 64);
        var sheet = Scan().Sheets.Single();
        string replacement = Bmp("newart.bmp", width: 128, height: 128);

        SheetLibrary.Replace(sheet, replacement);

        var after = Scan().Sheets.Single();
        Assert.That(after.Index, Is.EqualTo(4));
        Assert.That(after.DisplayName, Is.EqualTo("town"));
        Assert.That(after.PixelWidth, Is.EqualTo(128), "the art really changed");
    }

    /// <summary>Replacing a BMP with a PNG removes the old file. Left behind it would claim the same index
    /// and turn one sheet into a duplicate-index problem the next time the folder is read.</summary>
    [Test]
    public void ReplaceAcrossFormatsLeavesNoDuplicate()
    {
        Bmp("2_cave.bmp");
        var sheet = Scan().Sheets.Single();

        // Outside the scanned folder: a source file sitting in it would itself be an unindexed sheet.
        string sourceDir = Path.Combine(_dir, "incoming");
        Directory.CreateDirectory(sourceDir);
        string png = Path.Combine(sourceDir, "fresh.png");
        File.WriteAllBytes(png, PngBytes(64, 64));
        SheetLibrary.Replace(sheet, png);

        var scan = Scan();
        Assert.That(scan.Problems.Any(p => p.Kind == SheetProblemKind.DuplicateIndex), Is.False,
            "the .bmp must not still be sitting on index 2");
        Assert.That(Path.GetFileName(scan.Sheets.Single().Path), Is.EqualTo("2_cave.png"));
    }

    // ── Recycle bin ───────────────────────────────────────────────────────────

    /// <summary>Delete moves the file out of the folder and frees its number for the next import.</summary>
    [Test]
    public void DeleteMovesToTheBinAndFreesTheIndex()
    {
        Bmp("0_a.bmp");
        Bmp("1_b.bmp");
        var doomed = Scan().Sheets.First(s => s.Index == 1);

        SheetLibrary.Delete(doomed, _bin);

        Assert.That(IndexesOf(Scan()), Is.EqualTo(new[] { 0 }));
        Assert.That(SheetLibrary.ListRecycled(_bin).Select(Path.GetFileName), Is.EqualTo(new[] { "1_b.bmp" }));
        Assert.That(SheetLibrary.NextFreeIndex(Scan(), Constants.MaxTilesets), Is.EqualTo(1));
    }

    /// <summary>Two sheets deleted under one label both survive in the bin. Overwriting would destroy the
    /// first silently, which is the one thing a recycle bin exists not to do.</summary>
    [Test]
    public void TwoDeletesWithTheSameNameBothSurvive()
    {
        Bmp("0_tiles.bmp");
        SheetLibrary.Delete(Scan().Sheets.Single(), _bin);
        Bmp("0_tiles.bmp");
        SheetLibrary.Delete(Scan().Sheets.Single(), _bin);

        Assert.That(SheetLibrary.ListRecycled(_bin).Count, Is.EqualTo(2));
    }

    /// <summary>Restore puts a sheet back at the index it is given, not the one in its old filename —
    /// because that number may have been taken while it sat in the bin.</summary>
    [Test]
    public void RestoreTakesTheIndexItIsGiven()
    {
        Bmp("1_b.bmp");
        SheetLibrary.Delete(Scan().Sheets.Single(), _bin);
        Bmp("1_somethingelse.bmp");

        string recycled = SheetLibrary.ListRecycled(_bin).Single();
        int free = SheetLibrary.NextFreeIndex(Scan(), Constants.MaxTilesets);
        string restored = SheetLibrary.Restore(recycled, _dir, free);

        Assert.That(free, Is.EqualTo(0));
        Assert.That(Path.GetFileName(restored), Is.EqualTo("0_b.bmp"));
        Assert.That(IndexesOf(Scan()), Is.EqualTo(new[] { 0, 1 }));
    }

    // ── Tombstones ────────────────────────────────────────────────────────────

    /// <summary>Deleting a shipped sheet is recorded, because the seeder copies back every bundled file it
    /// finds missing on startup. Without the record the delete is undone before anyone sees it.</summary>
    [Test]
    public void DeletingABundledSheetIsRecordedSoItStaysDeleted()
    {
        Bmp("0_Tiles.bmp");
        var bundled = new SheetEntry(0, Path.Combine(_dir, "0_Tiles.bmp"), "Tiles", 54, 64, 96, IsBundled: true, SheetTransparency.ColorKey);

        SheetLibrary.Delete(bundled, _bin, "tiles/0_Tiles.bmp");

        Assert.That(SheetLibrary.ReadTombstones(_bin), Does.Contain("tiles/0_Tiles.bmp"));
    }

    /// <summary>A sheet the editor does not ship needs no record — the seeder was never going to restore
    /// it, and a tombstone for it would be a lie about where the file came from.</summary>
    [Test]
    public void DeletingAnImportedSheetRecordsNothing()
    {
        Bmp("4_mine.bmp");

        SheetLibrary.Delete(Scan().Sheets.Single(), _bin, "tiles/4_mine.bmp");

        Assert.That(SheetLibrary.ReadTombstones(_bin), Is.Empty);
    }

    /// <summary>Restoring lifts the record, so the sheet is an ordinary file again and the seeder may
    /// legitimately replace it if it later goes missing.</summary>
    [Test]
    public void RestoringLiftsTheRecord()
    {
        Bmp("0_Tiles.bmp");
        var bundled = new SheetEntry(0, Path.Combine(_dir, "0_Tiles.bmp"), "Tiles", 54, 64, 96, IsBundled: true, SheetTransparency.ColorKey);
        SheetLibrary.Delete(bundled, _bin, "tiles/0_Tiles.bmp");

        string recycled = SheetLibrary.ListRecycled(_bin).Single();
        SheetLibrary.Restore(recycled, _dir, 0, "tiles/0_Tiles.bmp");

        Assert.That(SheetLibrary.ReadTombstones(_bin), Is.Empty);
    }

    /// <summary>No record is not an error. An unreadable or absent list means nothing is tombstoned, so the
    /// seeder does its ordinary job — losing a deletion is recoverable, refusing to start is not.</summary>
    [Test]
    public void AnAbsentOrUnreadableRecordMeansNothingIsTombstoned()
    {
        Assert.That(SheetLibrary.ReadTombstones(_bin), Is.Empty);

        Directory.CreateDirectory(_bin);
        File.WriteAllText(Path.Combine(_bin, "deleted.json"), "{ not json");

        Assert.That(SheetLibrary.ReadTombstones(_bin), Is.Empty);
    }

    // A PNG signature plus an IHDR carrying the size — enough for the header reader, not a valid image.
    private static byte[] PngBytes(int width, int height)
    {
        var bytes = new byte[26];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[11] = 13;
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BitConverter.GetBytes(width).Reverse().ToArray().CopyTo(bytes, 16);
        BitConverter.GetBytes(height).Reverse().ToArray().CopyTo(bytes, 20);
        return bytes;
    }
}
