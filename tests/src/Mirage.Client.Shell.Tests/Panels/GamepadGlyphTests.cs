using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>Controller-brand detection. Deliberately one-sided: an unrecognized pad reads as Xbox, which
/// is what generic drivers report anyway, so a miss shows the right button in the wrong alphabet rather
/// than the wrong button.</summary>
[TestFixture]
public class GamepadGlyphTests
{
    [TearDown]
    public void ResetOverride() => GamepadGlyphs.Override(null);

    [TestCase("Sony DualShock 4")]
    [TestCase("DualSense Wireless Controller")]
    [TestCase("PS4 Controller")]
    [TestCase("PS5 Controller")]
    [TestCase("PLAYSTATION(R)3 Controller")]
    [TestCase("dualshock 3")]                 // matching is case-insensitive
    [TestCase("Wireless Controller")]         // what a DS4 reports through several drivers
    public void LooksLikeSony_RecognizesSonyPads(string name)
        => Assert.That(GamepadGlyphs.LooksLikeSony(name), Is.True);

    [TestCase("Xbox 360 Controller")]
    [TestCase("Xbox Series X|S Controller")]
    [TestCase("Controller (XBOX 360 For Windows)")]
    [TestCase("Logitech Gamepad F310")]
    [TestCase("Nintendo Switch Pro Controller")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void LooksLikeSony_LeavesEverythingElseOnXbox(string? name)
        => Assert.That(GamepadGlyphs.LooksLikeSony(name), Is.False);

    // An explicit choice must beat the driver-string guess — this is the seam a future Options setting
    // hangs on, and the point of it is that it does not get re-probed away a moment later.
    [Test]
    public void Override_WinsOverDetectionUntilCleared()
    {
        GamepadGlyphs.Override(true);
        Assert.That(GamepadGlyphs.PreferPlayStation, Is.True);
        Assert.That(GamepadGlyphs.PreferPlayStation, Is.True, "a second read must not re-probe over the override");

        GamepadGlyphs.Override(false);
        Assert.That(GamepadGlyphs.PreferPlayStation, Is.False);
    }

    // With no pad attached — which is the case in CI — detection must answer false rather than throw on
    // whatever the platform backend does with GetCapabilities.
    [Test]
    public void PreferPlayStation_IsSafeWithNoControllerAttached()
    {
        GamepadGlyphs.Override(null);
        Assert.DoesNotThrow(() => _ = GamepadGlyphs.PreferPlayStation);
    }
}
