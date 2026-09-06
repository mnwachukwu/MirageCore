using Mirage.Client.Shell.Panels;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// The hotkey bar's count badge, which lives inside a 32-pixel icon.
///
/// <para>A stack runs to billions and the plate is sized to its text, so past three digits it grows out
/// over the art it belongs to. The badge answers "have I got plenty", not "exactly how many" — the bag and
/// the tooltip both carry the real figure — so beyond the cap it says so and stops.</para>
/// </summary>
[TestFixture]
public class HeldCountLabelTests
{
    [TestCase(1, "1")]
    [TestCase(9, "9")]
    [TestCase(250, "250")]
    [TestCase(999, "999")]
    public void ACountThatFits_IsShownExactly(int held, string expected)
    {
        Assert.That(HotkeyBarPanel.HeldCountLabel(held), Is.EqualTo(expected));
    }

    [TestCase(1_000)]
    [TestCase(100_000_000)]
    [TestCase(int.MaxValue)]
    public void ACountThatDoesNot_ReadsAsPlenty(int held)
    {
        Assert.That(HotkeyBarPanel.HeldCountLabel(held), Is.EqualTo("999+"));
    }

    /// <summary>Four characters is the ceiling: the plate is sized to its text, so a label that keeps
    /// growing is the overflow this exists to stop.</summary>
    [TestCase(1)]
    [TestCase(999)]
    [TestCase(1_000)]
    [TestCase(int.MaxValue)]
    public void NoLabel_RunsPastFourCharacters(int held)
    {
        Assert.That(HotkeyBarPanel.HeldCountLabel(held), Has.Length.LessThanOrEqualTo(4));
    }

    /// <summary>The boundary is inclusive on the exact side — 999 is shown, 1000 is not — so the cap and
    /// the label agree about which is the last real number.</summary>
    [Test]
    public void TheBoundaryFallsBetweenTheCapAndOneMore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.HeldCountLabel(HotkeyBarPanel.MaxShownCount),
                Is.EqualTo(HotkeyBarPanel.MaxShownCount.ToString()));
            Assert.That(HotkeyBarPanel.HeldCountLabel(HotkeyBarPanel.MaxShownCount + 1), Does.EndWith("+"));
        });
    }
}
