namespace Mirage.Client.Shell.Panels;

/// <summary>
/// What the author just changed in the <see cref="OptionsPanel"/> — one EDGE flag per control, true
/// only on the frame the control was operated, not the control's value. The shell reads the value
/// from the panel property and decides what to do; see <see cref="OptionsPanel.Update"/>.
///
/// <para>This was an eighteen-element tuple, which is why the pre-login handler in
/// <c>MirageGame.UpdateOptionsPanel</c> could drop ten of the per-character options behind a row of
/// <c>_</c> discards without anyone noticing: positional destructuring makes an omission look like
/// punctuation. Named members make the same omission read as a missing <c>if</c>.</para>
///
/// <para><c>default</c> means "nothing changed", which is what the panel returns while closed.</para>
/// </summary>
public readonly record struct OptionsChanges
{
    // ── Global settings (appsettings.json, shared by every character) ─────────
    public bool AspectChanged { get; init; }
    public bool PlayMusicChanged { get; init; }
    public bool VolumeChanged { get; init; }
    public bool GamepadChanged { get; init; }
    /// <summary>The new locale, or null when the language dropdown was not touched.</summary>
    public string? LanguageChanged { get; init; }

    // ── Per-character display options (config/{account}.json) ─────────────────
    public bool BarsChanged { get; init; }
    public bool CombatNumbersChanged { get; init; }
    public bool SkipTabChanged { get; init; }
    public bool ShowNpcNamesChanged { get; init; }
    public bool ShowBloodChanged { get; init; }
    public bool ShowOtherNamesChanged { get; init; }
    public bool ShowPlayerNameChanged { get; init; }
    public bool ShowCooldownBarChanged { get; init; }
    public bool ShowOtherCooldownBarsChanged { get; init; }
    public bool ShowChatTimestampsChanged { get; init; }
    public bool Use24HourClockChanged { get; init; }
    public bool ShowChannelLabelsChanged { get; init; }

    // ── Buttons ──────────────────────────────────────────────────────────────
    /// <summary>Restore Defaults was clicked: every option above goes back to its shipped value.
    /// Deliberately does NOT touch panel layout, the language, or the server address.</summary>
    public bool RestoreDefaults { get; init; }
    /// <summary>Reset Panels was clicked: every floating panel goes back to its declared position and
    /// size, and every table back to its declared column widths, order and sort.</summary>
    public bool ResetPanels { get; init; }
}
