using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The nav rail collapses to icons, and when it does the icon is the ONLY thing naming a section.
/// These lock the two halves of that rule: a tooltip exists exactly when the label does not, and it says
/// the same thing the label would have.
///
/// <para>The failure this guards is silent in both directions — a tooltip that never appears leaves nine
/// unlabelled glyphs, and one that always appears puts a duplicate of every visible label under the
/// cursor.</para></summary>
[TestFixture]
public class SectionRailTests
{
    private static SectionViewModel Section() =>
        new("Maps", EditorStrings.MainWindow_Section_Maps);

    [Test]
    public void ARailStartsExpanded()
    {
        Assert.That(Section().IsLabelVisible, Is.True);
    }

    [Test]
    public void AVisibleLabelGetsNoTooltip()
    {
        Assert.That(Section().TooltipText, Is.Null, "a tooltip repeating a visible label is noise");
    }

    [Test]
    public void AHiddenLabelMovesToTheTooltip()
    {
        var s = Section();
        s.IsLabelVisible = false;

        Assert.That(s.TooltipText, Is.EqualTo(s.DisplayName));
    }

    [Test]
    public void TheTooltipComesAndGoesWithTheLabel()
    {
        // Collapsing and expanding again has to leave no tooltip behind.
        var s = Section();
        s.IsLabelVisible = false;
        s.IsLabelVisible = true;

        Assert.That(s.TooltipText, Is.Null);
    }

    [Test]
    public void CollapsingRaisesTheTooltipChange()
    {
        // The tooltip is derived, so nothing reaches the UI unless the change is announced.
        var s = Section();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.IsLabelVisible = false;

        Assert.That(raised, Does.Contain(nameof(SectionViewModel.TooltipText)));
    }

    [Test]
    public void ALanguageSwitchReachesTheTooltip()
    {
        // The label key is resolved on read, so a collapsed rail must re-announce its tooltip too —
        // otherwise the labels change language and the tooltips stay in the old one.
        var s = Section();
        s.IsLabelVisible = false;
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.NotifyDisplayNameChanged();

        Assert.That(raised, Does.Contain(nameof(SectionViewModel.TooltipText)));
    }
}
