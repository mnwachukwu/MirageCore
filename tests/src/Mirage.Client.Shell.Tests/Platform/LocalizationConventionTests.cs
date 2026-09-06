using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Shell.Tests.Platform;

/// <summary>
/// Stops live language switching from rotting as screens are added. Screens are rebuilt on a menu
/// transition but NOT while the player sits on one, so a screen that resolved its captions once in
/// its constructor shows the stale language for as long as it stays up. Only the screen being looked
/// at can show the fault, which is what makes it read as intermittent rather than broken.
/// </summary>
[TestFixture]
public class LocalizationConventionTests
{
    /// <summary>Holding a <see cref="Button"/> means holding its caption, and captions are resolved
    /// at construction — so a Button field is the precise signal that a screen has state to
    /// refresh. Screens that draw every string inline (LoadingScreen) hold no Button and are
    /// correctly exempt rather than being forced to carry a dead field.</summary>
    [Test]
    public void EveryScreenHoldingButtons_TracksClientStringsGeneration()
    {
        var offenders = typeof(MainMenuScreen).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IGameScreen).IsAssignableFrom(t))
            .Where(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Any(f => f.FieldType == typeof(Button) || f.FieldType == typeof(Button[])))
            .Where(t => t.GetField("_labelsGeneration", BindingFlags.NonPublic | BindingFlags.Instance) is null)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "These screens cache button captions but never re-read them, so a language switch made "
            + "while one is showing does nothing. Add `private int _labelsGeneration = -1;` plus a "
            + "RefreshLabels() called from Update() when it trails ClientStrings.Generation: "
            + string.Join(", ", offenders));
    }

    /// <summary>Guards the signal itself. Every re-label above is driven by this counter moving, so
    /// if <c>Load</c> stopped bumping it the whole convention would silently no-op.</summary>
    [Test]
    public void Load_IncrementsGeneration()
    {
        int before = ClientStrings.Generation;
        ClientStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));
        Assert.That(ClientStrings.Generation, Is.GreaterThan(before),
            "ClientStrings.Load must bump Generation — it is the only signal screens and panels have.");
    }
}
