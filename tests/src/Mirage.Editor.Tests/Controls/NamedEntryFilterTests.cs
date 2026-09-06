using Mirage.Editor.Models;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Controls;

/// <summary>
/// The typeahead filter behind every "pick a record" box in the editor.
///
/// <para>These boxes filter against their own text, and once something is selected that text is the
/// selected entry's caption. So the filter is asked, constantly, to judge every entry against the caption
/// of the one already chosen — and if it answers honestly, the list collapses to that single entry and
/// reads as "there is nothing else to pick".</para>
///
/// <para><see cref="DropdownAutoCompleteBox"/> is what stops that happening, by dropping the filter
/// entirely while the list is being browsed. These pin what the filter does once somebody actually types,
/// including the caption case it cannot solve on its own.</para>
/// </summary>
[TestFixture]
public class NamedEntryFilterTests
{
    private static readonly NamedEntry[] Sheets =
    [
        new(0, "Tiles"),
        new(1, "d5xdb0y-7cca5199"),
        new(2, "Cave"),
        new(12, "Deep Cave"),
    ];

    private static int[] Matching(string search) =>
        [.. Sheets.Where(e => NamedEntryFilter.ByNameOrIndex(search, e)).Select(e => e.Id)];

    /// <summary>Nothing typed offers everything, which is what an empty box has to do.</summary>
    [Test]
    public void AnEmptySearchOffersEverything()
    {
        Assert.That(Matching(""), Is.EqualTo(new[] { 0, 1, 2, 12 }));
    }

    /// <summary>Typing part of a name narrows to it, case-insensitively.</summary>
    [Test]
    public void TypingPartOfANameNarrowsToIt()
    {
        Assert.That(Matching("cave"), Is.EqualTo(new[] { 2, 12 }));
        Assert.That(Matching("deep"), Is.EqualTo(new[] { 12 }));
    }

    /// <summary>Typing digits matches on the index, so a sheet can be reached by number alone when its
    /// name is not to hand.</summary>
    [Test]
    public void TypingDigitsMatchesTheIndex()
    {
        Assert.That(Matching("1"), Is.EqualTo(new[] { 1, 12 }), "a prefix match, so 1 also reaches 12");
        Assert.That(Matching("12"), Is.EqualTo(new[] { 12 }));
    }

    /// <summary>🔴 The caption case, and the reason the control drops this filter to open its list. A
    /// selected entry puts its own caption in the box; the filter reads that as a search for its name and
    /// judges every other entry not to match. Left to answer it, the picker offers exactly one entry — the
    /// one already selected — and every other record in the world looks as though it does not exist.</summary>
    [Test]
    public void ACaptionInTheBoxMatchesOnlyItsOwnEntry()
    {
        Assert.That(Matching("0: Tiles"), Is.EqualTo(new[] { 0 }));
        Assert.That(Matching("1: d5xdb0y-7cca5199"), Is.EqualTo(new[] { 1 }));
    }
}
