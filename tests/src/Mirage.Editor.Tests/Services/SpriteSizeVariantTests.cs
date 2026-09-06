using Mirage.Editor.Services;
using Mirage.Shared;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// One sprite sheet number is one character at every footprint size.
///
/// <para>A sprite's number is its row, and which of 32x32, 64x64 and 96x96 is read is decided by the NPC's
/// size rather than by anything in the sheet. So sheet 1 has to be the same roster in the same order in all
/// three folders, and nothing at load time checks that: a size that is missing draws nothing at all, and a
/// size holding a different number of rows quietly makes one number two different creatures. Neither
/// produces an error anywhere, which is why they are checked here.</para>
/// </summary>
[TestFixture]
public class SpriteSizeVariantTests
{
    private string _root = "";

    [SetUp]
    public void MakeFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirage-sprites-" + Guid.NewGuid().ToString("N"));
        foreach (int cell in AssetFolder.SpriteSizes)
            Directory.CreateDirectory(Folder(cell));
    }

    [TearDown]
    public void DropFolders()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder is not worth failing on */ }
    }

    private string Folder(int cell) => Path.Combine(_root, $"{cell}x{cell}");

    // A real 24-bit BMP header, so the grid the library reports comes from the same bytes a loader reads.
    private void Sheet(int cell, string fileName, int cols, int rows)
    {
        var header = new byte[54];
        header[0] = 0x42; header[1] = 0x4D;
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(cols * cell).CopyTo(header, 18);
        BitConverter.GetBytes(rows * cell).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);
        File.WriteAllBytes(Path.Combine(Folder(cell), fileName), header);
    }

    private IReadOnlyList<SheetProblem> Check() =>
        SheetLibrary.ScanSizeVariants(AssetFolder.SpriteSizes.ToDictionary(
            cell => cell,
            cell => SheetLibrary.Scan(Folder(cell), Constants.MaxTilesets, null, cell)));

    /// <summary>The shipped shape: every size present, each an exact multiple of the 32x32 grid. This is
    /// the control half — without it a check that reports everything would pass the tests below.</summary>
    [Test]
    public void MatchingSizesAreNotReported()
    {
        foreach (int cell in AssetFolder.SpriteSizes) Sheet(cell, "0_Sprites.bmp", cols: 12, rows: 47);

        Assert.That(Check(), Is.Empty);
    }

    /// <summary>An NPC of a size whose sheet is absent draws no sprite at all, silently.</summary>
    [Test]
    public void ASizeWithNoSheetAtThatIndexIsReported()
    {
        Sheet(32, "1_Beasts.bmp", cols: 12, rows: 20);
        Sheet(64, "1_Beasts.bmp", cols: 12, rows: 20);
        // 96x96 deliberately has nothing at index 1.

        var problems = Check();

        Assert.That(problems.Select(p => p.Kind), Is.EqualTo(new[] { SheetProblemKind.MissingSizeVariant }));
        Assert.That(problems[0].Index, Is.EqualTo(1));
    }

    /// <summary>Different row counts mean sprite 15 is one creature at 32x32 and another at 64x64 — the
    /// exact failure the one-number-one-character rule exists to prevent.</summary>
    [Test]
    public void ASizeWithADifferentRowCountIsReported()
    {
        Sheet(32, "0_Sprites.bmp", cols: 12, rows: 47);
        Sheet(64, "0_Sprites.bmp", cols: 12, rows: 40);
        Sheet(96, "0_Sprites.bmp", cols: 12, rows: 47);

        var problems = Check();

        Assert.That(problems.Select(p => p.Kind),
            Is.EqualTo(new[] { SheetProblemKind.SizeVariantRowMismatch }));
        Assert.That(Path.GetFileName(problems[0].Paths[0]), Is.EqualTo("0_Sprites.bmp"));
        Assert.That(problems[0].Paths[0], Does.Contain("64x64"),
            "the mismatch has to name the size that disagrees, not the baseline it disagrees with");
    }

    /// <summary>Columns are animation frames, read by position, so a sheet with room for more frames is
    /// padding rather than a different roster. Only rows decide who a number is.</summary>
    [Test]
    public void AWiderSheetWithTheSameRowsIsNotReported()
    {
        Sheet(32, "0_Sprites.bmp", cols: 12, rows: 47);
        Sheet(64, "0_Sprites.bmp", cols: 16, rows: 47);
        Sheet(96, "0_Sprites.bmp", cols: 12, rows: 47);

        Assert.That(Check(), Is.Empty);
    }

    /// <summary>An index only the larger sizes have is not a missing variant: the baseline is the smallest
    /// size, and a sheet no NPC of size 1 can reach is the author's business rather than a fault.</summary>
    [Test]
    public void AnIndexAbsentFromTheBaselineIsNotReported()
    {
        Sheet(64, "5_Giants.bmp", cols: 12, rows: 4);
        Sheet(96, "5_Giants.bmp", cols: 12, rows: 4);

        Assert.That(Check(), Is.Empty);
    }
}
