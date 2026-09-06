using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Switching the asset manager between tiles, sprites and items.
///
/// <para>🔴 The folder being managed is DERIVED from the category rather than selected, and the size
/// selector's list is fixed. Both matter: a ComboBox whose ItemsSource stops containing its SelectedItem
/// clears that selection and writes the null back through the two-way binding, and the generated setter
/// stores the null before any hook can refuse it. A selection that never leaves its list cannot be
/// cleared, and a folder nothing binds to cannot be nulled — which is what makes picking Sprites safe
/// rather than merely defended.</para>
///
/// <para>No view is built here. What is reproduced is the ComboBox's rule, which is the part the
/// view-model has to hold up its end of.</para>
/// </summary>
[TestFixture]
public class AssetCategorySwitchTests
{
    private string _root = "";

    [SetUp]
    public void MakeFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirage-assetcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DropFolders()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder is not worth failing on */ }
    }

    private AssetManagerDialogViewModel Build()
    {
        var vm = new AssetManagerDialogViewModel(Path.Combine(_root, "assets"), Path.Combine(_root, "bundled"));
        vm.Refresh();
        return vm;
    }

    private static AssetCategoryOption OptionFor(AssetManagerDialogViewModel vm, AssetCategoryKind kind) =>
        vm.Categories.Single(c => c.Kind == kind);

    /// <summary>The reported crash, as the selector produced it.</summary>
    [Test]
    public void SwitchingToSpritesDoesNotThrow()
    {
        var vm = Build();

        Assert.DoesNotThrow(() => vm.Category = OptionFor(vm, AssetCategoryKind.Sprites));
    }

    /// <summary>Every category, in both directions — sprites is the one with more than one folder, so it
    /// is the switch into and out of it that used to move a list underneath a selection.</summary>
    [Test]
    public void EverySwitchLandsOnAFolderOfTheNewCategory()
    {
        var vm = Build();

        foreach (var kind in new[]
                 {
                     AssetCategoryKind.Sprites, AssetCategoryKind.Items, AssetCategoryKind.Tiles,
                     AssetCategoryKind.Items, AssetCategoryKind.Sprites, AssetCategoryKind.Tiles,
                 })
        {
            Assert.DoesNotThrow(() => vm.Category = OptionFor(vm, kind), $"switching to {kind} threw");
            Assert.That(vm.Intro, Does.Contain(FolderNameOf(kind)), $"{kind} is not showing its own folder");
        }
    }

    private static string FolderNameOf(AssetCategoryKind kind) => kind switch
    {
        AssetCategoryKind.Sprites => "sprites",
        AssetCategoryKind.Items => "items",
        _ => "tiles",
    };

    /// <summary>🔴 The size list is FIXED. A ComboBox only clears its selection when its ItemsSource stops
    /// containing it, so a list that never changes is what makes that impossible — this is the assertion
    /// that fails if anyone rebuilds it per category again.</summary>
    [Test]
    public void TheSizeListIsTheSameInstanceInEveryCategory()
    {
        var vm = Build();
        var atStart = vm.SpriteSizes;

        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);
        vm.Category = OptionFor(vm, AssetCategoryKind.Items);
        vm.Category = OptionFor(vm, AssetCategoryKind.Tiles);

        Assert.That(vm.SpriteSizes, Is.SameAs(atStart));
        Assert.That(vm.SpriteSizes, Does.Contain(vm.SpriteSize));
    }

    /// <summary>Mimics the ComboBox: at every notification, the selected size must still be one of the
    /// list's members. A moment where it is not is a moment the control would clear it.</summary>
    [Test]
    public void TheSelectedSizeIsNeverOutsideItsOwnList()
    {
        var vm = Build();
        bool wouldClearSelection = false;

        vm.PropertyChanged += (_, _) =>
        {
            if (vm.SpriteSize is null || !vm.SpriteSizes.Contains(vm.SpriteSize)) wouldClearSelection = true;
        };

        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);
        vm.SpriteSize = vm.SpriteSizes[2];
        vm.Category = OptionFor(vm, AssetCategoryKind.Items);
        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);

        Assert.That(wouldClearSelection, Is.False);
    }

    /// <summary>And if a selection is ever cleared anyway, the folder falls back rather than being read
    /// as null. Nothing binds to the folder, so this is the last line rather than the defence.</summary>
    [Test]
    public void AClearedSizeSelectionStillResolvesAFolder()
    {
        var vm = Build();
        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);

        Assert.DoesNotThrow(() => vm.SpriteSize = null!);
        Assert.DoesNotThrow(() => vm.Refresh());
        Assert.That(vm.Intro, Does.Contain("sprites"));
    }

    /// <summary>The chosen size decides which sprite folder is managed, and the choice survives a trip
    /// through another category.</summary>
    [Test]
    public void TheChosenSizePicksTheFolderAndSurvivesACategoryTrip()
    {
        var vm = Build();
        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);
        vm.SpriteSize = vm.SpriteSizes.Single(f => f.CellSize == 96);

        Assert.That(vm.Intro, Does.Contain("96x96"));

        vm.Category = OptionFor(vm, AssetCategoryKind.Tiles);
        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);

        Assert.That(vm.SpriteSize.CellSize, Is.EqualTo(96));
        Assert.That(vm.Intro, Does.Contain("96x96"));
    }

    /// <summary>The size selector shows for sprites and nothing else.</summary>
    [Test]
    public void OnlySpritesOfferASizeSelector()
    {
        var vm = Build();

        vm.Category = OptionFor(vm, AssetCategoryKind.Tiles);
        Assert.That(vm.HasSizes, Is.False);

        vm.Category = OptionFor(vm, AssetCategoryKind.Items);
        Assert.That(vm.HasSizes, Is.False);

        vm.Category = OptionFor(vm, AssetCategoryKind.Sprites);
        Assert.That(vm.HasSizes, Is.True);
        Assert.That(vm.SpriteSizes.Select(f => f.CellSize), Is.EqualTo(AssetFolder.SpriteSizes.ToArray()));
    }
}
