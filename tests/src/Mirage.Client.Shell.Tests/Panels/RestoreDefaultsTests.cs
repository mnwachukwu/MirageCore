using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// Guards the Options panel's two reset buttons.
///
/// <para>The failure these exist for: a reset that writes the defaults into the checkboxes and the config
/// file WITHOUT pushing them into live game state, so an option reads as restored, persists as restored,
/// and still behaves the old way until the next relog. Both restore paths apply a fresh
/// <see cref="AccountConfig.CharacterConfig"/> through the same method world entry uses, which leaves the
/// checkbox defaults as the one copy of "what the default is" still able to drift. That is what these
/// pin.</para>
///
/// <para>The wiring itself — GameplayScreen and MirageGame — needs a GraphicsDevice and stays a manual
/// playtest.</para>
/// </summary>
[TestFixture]
public class RestoreDefaultsTests
{
    // Each per-character option as (name, what the panel says, what the config says). Named so a failure
    // reports which option drifted rather than just "expected True but was False".
    private static IEnumerable<TestCaseData> PerCharacterOptions()
    {
        var panel = new OptionsPanel();
        var config = new AccountConfig.CharacterConfig();
        yield return Case("AlwaysShowBars", panel.AlwaysShowBars, config.AlwaysShowBars);
        yield return Case("ShowCombatNumbers", panel.ShowCombatNumbers, config.ShowCombatNumbers);
        yield return Case("SkipPlayersWithTabTarget", panel.SkipPlayersWithTabTarget, config.SkipPlayersWithTabTarget);
        yield return Case("ShowNpcNames", panel.ShowNpcNames, config.ShowNpcNames);
        yield return Case("ShowBlood", panel.ShowBlood, config.ShowBlood);
        yield return Case("ShowOtherPlayerNames", panel.ShowOtherPlayerNames, config.ShowOtherPlayerNames);
        yield return Case("ShowPlayerName", panel.ShowPlayerName, config.ShowPlayerName);
        yield return Case("ShowCooldownBar", panel.ShowCooldownBar, config.ShowCooldownBar);
        yield return Case("ShowOtherCooldownBars", panel.ShowOtherCooldownBars, config.ShowOtherCooldownBars);
        yield return Case("ShowChatTimestamps", panel.ShowChatTimestamps, config.ShowChatTimestamps);
        yield return Case("Use24HourClock", panel.Use24HourClock, config.Use24HourClock);
        yield return Case("ShowChannelLabels", panel.ShowChannelLabels, config.ShowChannelLabels);

        static TestCaseData Case(string name, bool panelValue, bool configValue) =>
            new TestCaseData(panelValue, configValue).SetName($"FreshOptionsPanel_MatchesCharacterConfig({name})");
    }

    /// <summary>A brand-new character gets <see cref="AccountConfig.CharacterConfig"/>'s initializers, so
    /// those ARE the shipped defaults. A checkbox that starts on a different value shows the player a
    /// setting the game is not actually using.</summary>
    [TestCaseSource(nameof(PerCharacterOptions))]
    public void FreshOptionsPanel_MatchesCharacterConfigDefaults(bool panelValue, bool configValue)
    {
        Assert.That(panelValue, Is.EqualTo(configValue),
            "the OptionsPanel checkbox default disagrees with AccountConfig.CharacterConfig — "
            + "CharacterConfig is what a new character actually gets, so the checkbox must follow it");
    }

    /// <summary>Restore Defaults applies <c>ApplyCharPrefs(new CharacterConfig())</c>, so every option
    /// must come back even from the fully inverted state. A property left out of ApplyCharPrefs shows up
    /// here as one stuck value.</summary>
    [Test]
    public void ApplyCharPrefs_WithFreshConfig_RestoresEveryOptionFromTheInvertedState()
    {
        var panel = new OptionsPanel();
        var defaults = new AccountConfig.CharacterConfig();

        // Flip all twelve away from their defaults, the way a player fiddling with the panel would.
        panel.AlwaysShowBars = !defaults.AlwaysShowBars;
        panel.ShowCombatNumbers = !defaults.ShowCombatNumbers;
        panel.SkipPlayersWithTabTarget = !defaults.SkipPlayersWithTabTarget;
        panel.ShowNpcNames = !defaults.ShowNpcNames;
        panel.ShowBlood = !defaults.ShowBlood;
        panel.ShowOtherPlayerNames = !defaults.ShowOtherPlayerNames;
        panel.ShowPlayerName = !defaults.ShowPlayerName;
        panel.ShowCooldownBar = !defaults.ShowCooldownBar;
        panel.ShowOtherCooldownBars = !defaults.ShowOtherCooldownBars;
        panel.ShowChatTimestamps = !defaults.ShowChatTimestamps;
        panel.Use24HourClock = !defaults.Use24HourClock;
        panel.ShowChannelLabels = !defaults.ShowChannelLabels;

        panel.ApplyCharPrefs(defaults);

        Assert.Multiple(() =>
        {
            Assert.That(panel.AlwaysShowBars, Is.EqualTo(defaults.AlwaysShowBars), nameof(panel.AlwaysShowBars));
            Assert.That(panel.ShowCombatNumbers, Is.EqualTo(defaults.ShowCombatNumbers), nameof(panel.ShowCombatNumbers));
            Assert.That(panel.SkipPlayersWithTabTarget, Is.EqualTo(defaults.SkipPlayersWithTabTarget), nameof(panel.SkipPlayersWithTabTarget));
            Assert.That(panel.ShowNpcNames, Is.EqualTo(defaults.ShowNpcNames), nameof(panel.ShowNpcNames));
            Assert.That(panel.ShowBlood, Is.EqualTo(defaults.ShowBlood), nameof(panel.ShowBlood));
            Assert.That(panel.ShowOtherPlayerNames, Is.EqualTo(defaults.ShowOtherPlayerNames), nameof(panel.ShowOtherPlayerNames));
            Assert.That(panel.ShowPlayerName, Is.EqualTo(defaults.ShowPlayerName), nameof(panel.ShowPlayerName));
            Assert.That(panel.ShowCooldownBar, Is.EqualTo(defaults.ShowCooldownBar), nameof(panel.ShowCooldownBar));
            Assert.That(panel.ShowOtherCooldownBars, Is.EqualTo(defaults.ShowOtherCooldownBars), nameof(panel.ShowOtherCooldownBars));
            Assert.That(panel.ShowChatTimestamps, Is.EqualTo(defaults.ShowChatTimestamps), nameof(panel.ShowChatTimestamps));
            Assert.That(panel.Use24HourClock, Is.EqualTo(defaults.Use24HourClock), nameof(panel.Use24HourClock));
            Assert.That(panel.ShowChannelLabels, Is.EqualTo(defaults.ShowChannelLabels), nameof(panel.ShowChannelLabels));
        });
    }

    /// <summary>ApplyCharPrefs is also the world-entry path, so it has to carry a saved value through
    /// unchanged — not just the default one. A method that hardcoded defaults would pass the test above
    /// and fail this.</summary>
    [Test]
    public void ApplyCharPrefs_WithSavedPrefs_CopiesThoseValuesNotTheDefaults()
    {
        var panel = new OptionsPanel();
        var saved = new AccountConfig.CharacterConfig
        {
            AlwaysShowBars = false,
            ShowCombatNumbers = false,
            ShowBlood = false,
            ShowChatTimestamps = true,
            Use24HourClock = true,
        };

        panel.ApplyCharPrefs(saved);

        Assert.Multiple(() =>
        {
            Assert.That(panel.AlwaysShowBars, Is.False);
            Assert.That(panel.ShowCombatNumbers, Is.False);
            Assert.That(panel.ShowBlood, Is.False);
            Assert.That(panel.ShowChatTimestamps, Is.True);
            Assert.That(panel.Use24HourClock, Is.True);
            Assert.That(panel.ShowNpcNames, Is.True, "untouched options keep the config's own default");
        });
    }

    // ── Reset Panels ──────────────────────────────────────────────────────────

    [Test]
    public void ResetBounds_ReturnsThePanelToItsDeclaredRectangle()
    {
        var declared = new Rectangle(20, 20, 300, 200);
        var panel = new DraggablePanel(declared, minH: 100, minW: 150);

        panel.SetBounds(new Rectangle(400, 350, 260, 180));   // player dragged and resized it
        Assume.That(panel.Bounds, Is.Not.EqualTo(declared));

        panel.ResetBounds();

        Assert.That(panel.Bounds, Is.EqualTo(declared));
    }

    /// <summary>Reset goes through SetBounds rather than assigning the field, so a default rectangle that
    /// predates a later minimum still clamps up instead of restoring a panel too small for its content.</summary>
    [Test]
    public void ResetBounds_ClampsUpToTheMinimums()
    {
        var panel = new DraggablePanel(new Rectangle(10, 10, 100, 50), minH: 120, minW: 200);
        panel.SetBounds(new Rectangle(300, 300, 400, 400));

        panel.ResetBounds();

        Assert.Multiple(() =>
        {
            Assert.That(panel.Bounds.X, Is.EqualTo(10));
            Assert.That(panel.Bounds.Y, Is.EqualTo(10));
            Assert.That(panel.Bounds.Width, Is.EqualTo(200));
            Assert.That(panel.Bounds.Height, Is.EqualTo(120));
        });
    }

    [Test]
    public void ResetColumnLayout_RestoresDeclaredWidthsAndOrder()
    {
        var t = new Table<string>()
            .Column("A", s => s, width: 60)
            .Column("B", s => s, width: 90)
            .Column("C", s => s, width: 120);
        t.AllowReorder = true;

        t.ApplyColumnLayout(order: new[] { 2, 0, 1 }, widths: new[] { 200, 210, 220 }, sortColumn: 1, sortAscending: false);
        Assume.That(t.ColumnWidths, Is.EqualTo(new[] { 200, 210, 220 }));

        t.ResetColumnLayout();

        Assert.Multiple(() =>
        {
            Assert.That(t.ColumnWidths, Is.EqualTo(new[] { 60, 90, 120 }));
            Assert.That(t.ColumnOrder, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    /// <summary>Panels declare a default sort in their constructor (newest mail first, cheapest listing
    /// first). Resetting has to land on that, not on unsorted — otherwise Reset Panels would leave every
    /// table in a state a fresh character never sees.</summary>
    [Test]
    public void ResetColumnLayout_RestoresTheHostsDeclaredSort_NotUnsorted()
    {
        var t = new Table<string>().Column("A", s => s).Column("B", s => s);
        t.SortBy(1, ascending: false);   // the panel's declared default

        t.ToggleSort(0);                 // player clicks a different header
        Assume.That(t.SortColumn, Is.EqualTo(0));

        t.ResetColumnLayout();

        Assert.Multiple(() =>
        {
            Assert.That(t.SortColumn, Is.EqualTo(1));
            Assert.That(t.SortAscending, Is.False);
        });
    }

    /// <summary>A table whose host never declared a sort has nothing to go back to, so a reset leaves it
    /// unsorted rather than inventing one from whatever the player last clicked.</summary>
    [Test]
    public void ResetColumnLayout_WithNoDeclaredSort_ClearsTheSort()
    {
        var t = new Table<string>().Column("A", s => s).Column("B", s => s);
        t.ToggleSort(1);

        t.ResetColumnLayout();

        Assert.That(t.SortColumn, Is.EqualTo(-1));
    }
}
