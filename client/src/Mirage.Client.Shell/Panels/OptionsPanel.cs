using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The draggable Options window — display, audio, input, name/bar visibility, chat formatting, and
/// the language picker. Shown both before connecting and in-game, drawn into the same reference frame.
/// <para>Owns only widget state. It never applies a setting itself: <see cref="Update"/> reports which
/// options the author just changed and the shell decides what to do with each.</para>
/// </summary>
public sealed class OptionsPanel : IGamePanel
{
    // Centered in the 800x600 UI reference space so it lands mid-screen in BOTH states — out-of-game and in-game
    // draw this panel into the same reference target, so a centered default reads centered either way. Default
    // height fits all rows; minH keeps the bottom-anchored Restore button clear of the last row at smallest resize.
    private readonly DraggablePanel _panel =
        new(new Rectangle((UiHelper.RefW - 520) / 2, (UiHelper.RefH - 270) / 2, 520, 270), minH: 260);

    /// <summary>Whether the window is showing. While closed, Update and Draw both no-op.</summary>
    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    /// <summary>True when the author has moved or resized the window, so the shell knows to persist it.</summary>
    public bool LayoutChanged => _panel.LayoutChanged;
    /// <summary>Restore a saved position/size.</summary>
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    /// <summary>Return the window to its centered default position and size (Reset Panels).</summary>
    public void ResetBounds() => _panel.ResetBounds();
    /// <summary>Show or hide the window (the Options link and the O hotkey).</summary>
    public void Toggle() { IsOpen = !IsOpen; }
    /// <summary>Whether the pointer is over the open window, so clicks aren't also handled by the
    /// world or a panel underneath.</summary>
    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    // The per-character checkbox defaults below MUST match AccountConfig.CharacterConfig's property
    // initializers, which are what a brand-new character actually gets. Two copies of the same answer,
    // free to disagree in silence — OptionsPanelDefaultsTests is what pins them together.
    private readonly Checkbox _aspectChk = new() { Checked = false };
    private readonly Checkbox _alwaysShowBarsChk = new() { Checked = true };
    private readonly Checkbox _showCombatNumbersChk = new() { Checked = true };
    private readonly Checkbox _playMusicChk = new() { Checked = true };
    private readonly Slider _volumeSlider = new() { Min = 0, Max = 100, Value = 100 };
    private readonly Checkbox _useGamepadChk = new() { Checked = false };
    private readonly Checkbox _skipPlayersTabChk = new() { Checked = true };
    private readonly Checkbox _showNpcNamesChk = new() { Checked = true };
    private readonly Checkbox _showOtherNamesChk = new() { Checked = true };
    private readonly Checkbox _showPlayerNameChk = new() { Checked = true };
    private readonly Checkbox _showCooldownBarChk = new() { Checked = true };
    private readonly Checkbox _showOtherCooldownBarsChk = new() { Checked = false };
    private readonly Checkbox _showChatTimestampsChk = new() { Checked = false };
    private readonly Checkbox _use24HourClockChk = new() { Checked = false };
    private readonly Checkbox _showChannelLabelsChk = new() { Checked = false };
    private readonly Checkbox _showBloodChk = new() { Checked = true };
    private readonly Button _restoreBtn = new();
    private readonly Button _resetPanelsBtn = new();
    private readonly DropDown _languageDropDown = new();
    private string[] _locales = [];
    // Localization generation the captions were last built for. Labels are rebuilt in Draw only when
    // this falls behind ClientStrings.Generation, so a language switch re-labels once rather than
    // rebuilding every string every frame.
    private int _labelsGeneration = -1;
    /// <summary>False when no audio device could be opened; grays the music controls and stops them
    /// reporting changes.</summary>
    public bool AudioAvailable { get; set; } = true;

    // The locale that was selected at the last Update() call (null if dropdown not yet populated).
    // Callers should compare with their stored locale to detect a change.
    public string? SelectedLocale =>
        _languageDropDown.SelectedIndex >= 0 && _languageDropDown.SelectedIndex < _locales.Length
            ? _locales[_languageDropDown.SelectedIndex] : null;

    /// <summary>Populate the language dropdown and select <paramref name="currentLocale"/>. The parallel
    /// locale array is what maps the chosen row back to a locale code (the dropdown holds display names).</summary>
    public void SetLanguages(IReadOnlyList<(string Locale, string DisplayName)> languages, string currentLocale)
    {
        _locales = new string[languages.Count];
        _languageDropDown.Items.Clear();
        for (int i = 0; i < languages.Count; i++)
        {
            _locales[i] = languages[i].Locale;
            _languageDropDown.Items.Add(languages[i].DisplayName);
            if (languages[i].Locale == currentLocale)
                _languageDropDown.SelectedIndex = i;
        }
    }

    public bool MaintainAspectRatio
    {
        get => _aspectChk.Checked;
        set => _aspectChk.Checked = value;
    }
    public bool AlwaysShowBars
    {
        get => _alwaysShowBarsChk.Checked;
        set => _alwaysShowBarsChk.Checked = value;
    }
    public bool ShowCombatNumbers
    {
        get => _showCombatNumbersChk.Checked;
        set => _showCombatNumbersChk.Checked = value;
    }
    public bool PlayMusic
    {
        get => _playMusicChk.Checked;
        set => _playMusicChk.Checked = value;
    }
    public int MusicVolume
    {
        get => _volumeSlider.Value;
        set => _volumeSlider.Value = value;
    }
    public bool UseGamepad
    {
        get => _useGamepadChk.Checked;
        set => _useGamepadChk.Checked = value;
    }
    public bool SkipPlayersWithTabTarget
    {
        get => _skipPlayersTabChk.Checked;
        set => _skipPlayersTabChk.Checked = value;
    }
    public bool ShowNpcNames
    {
        get => _showNpcNamesChk.Checked;
        set => _showNpcNamesChk.Checked = value;
    }
    public bool ShowOtherPlayerNames
    {
        get => _showOtherNamesChk.Checked;
        set => _showOtherNamesChk.Checked = value;
    }
    public bool ShowPlayerName
    {
        get => _showPlayerNameChk.Checked;
        set => _showPlayerNameChk.Checked = value;
    }
    public bool ShowCooldownBar
    {
        get => _showCooldownBarChk.Checked;
        set => _showCooldownBarChk.Checked = value;
    }
    public bool ShowOtherCooldownBars
    {
        get => _showOtherCooldownBarsChk.Checked;
        set => _showOtherCooldownBarsChk.Checked = value;
    }
    public bool ShowChatTimestamps
    {
        get => _showChatTimestampsChk.Checked;
        set => _showChatTimestampsChk.Checked = value;
    }
    public bool Use24HourClock
    {
        get => _use24HourClockChk.Checked;
        set => _use24HourClockChk.Checked = value;
    }
    public bool ShowChannelLabels
    {
        get => _showChannelLabelsChk.Checked;
        set => _showChannelLabelsChk.Checked = value;
    }
    public bool ShowBlood
    {
        get => _showBloodChk.Checked;
        set => _showBloodChk.Checked = value;
    }

    /// <summary>Push a set of per-character display preferences into the widgets. The one place the twelve
    /// per-character options are copied in, shared by world entry (a character's saved prefs) and Restore
    /// Defaults (a fresh <see cref="AccountConfig.CharacterConfig"/>) so the two can never disagree about
    /// what a default is.</summary>
    public void ApplyCharPrefs(AccountConfig.CharacterConfig prefs)
    {
        AlwaysShowBars = prefs.AlwaysShowBars;
        ShowCombatNumbers = prefs.ShowCombatNumbers;
        SkipPlayersWithTabTarget = prefs.SkipPlayersWithTabTarget;
        ShowNpcNames = prefs.ShowNpcNames;
        ShowBlood = prefs.ShowBlood;
        ShowOtherPlayerNames = prefs.ShowOtherPlayerNames;
        ShowPlayerName = prefs.ShowPlayerName;
        ShowCooldownBar = prefs.ShowCooldownBar;
        ShowOtherCooldownBars = prefs.ShowOtherCooldownBars;
        ShowChatTimestamps = prefs.ShowChatTimestamps;
        Use24HourClock = prefs.Use24HourClock;
        ShowChannelLabels = prefs.ShowChannelLabels;
    }

    /// <summary>Tick the widgets and report which options changed THIS frame — each flag is an edge, not
    /// the option's value, so the shell reacts once per change. <c>LanguageChanged</c> carries the new
    /// locale or null. Returns <c>default</c> (all false/null) while closed or on the frame the window
    /// is dismissed, so a close can't be mistaken for a settings change.</summary>
    public OptionsChanges Update(InputState input)
    {
        if (!IsOpen) return default;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return default;
        }

        LayoutControls(out var langLabelRect, out var langDropRect);
        bool volume = AudioAvailable && _volumeSlider.Update(input);
        bool aspect = _aspectChk.Update(input);
        bool bars = _alwaysShowBarsChk.Update(input);
        bool combat = _showCombatNumbersChk.Update(input);
        bool playMusic = AudioAvailable && _playMusicChk.Update(input);
        bool gamepad = _useGamepadChk.Update(input);
        bool skipTab = _skipPlayersTabChk.Update(input);
        bool showNpcNames = _showNpcNamesChk.Update(input);
        bool showOtherNames = _showOtherNamesChk.Update(input);
        bool showPlayerName = _showPlayerNameChk.Update(input);
        bool showCooldownBar = _showCooldownBarChk.Update(input);
        bool showOtherCooldownBars = _showOtherCooldownBarsChk.Update(input);
        bool showChatTimestamps = _showChatTimestampsChk.Update(input);
        // The clock-format sub-option is inert while timestamps are hidden: short-circuit skips its
        // Update so a click neither toggles nor consumes when the parent is off (it draws grayed).
        bool use24HourClock = _showChatTimestampsChk.Checked && _use24HourClockChk.Update(input);
        // Channel labels are an independent toggle (not gated on timestamps).
        bool showChannelLabels = _showChannelLabelsChk.Update(input);
        bool showBlood = _showBloodChk.Update(input);
        string? prevLocale = SelectedLocale;
        _languageDropDown.Update(input, langDropRect);
        string? nowLocale = SelectedLocale;
        string? languageChanged = (nowLocale is not null && nowLocale != prevLocale) ? nowLocale : null;

        bool restore = _restoreBtn.IsClicked(input);
        if (restore) input.ConsumeMouseClick();
        bool resetPanels = _resetPanelsBtn.IsClicked(input);
        if (resetPanels) input.ConsumeMouseClick();

        return new OptionsChanges
        {
            AspectChanged = aspect,
            PlayMusicChanged = playMusic,
            VolumeChanged = volume,
            GamepadChanged = gamepad,
            LanguageChanged = languageChanged,
            BarsChanged = bars,
            CombatNumbersChanged = combat,
            SkipTabChanged = skipTab,
            ShowNpcNamesChanged = showNpcNames,
            ShowBloodChanged = showBlood,
            ShowOtherNamesChanged = showOtherNames,
            ShowPlayerNameChanged = showPlayerName,
            ShowCooldownBarChanged = showCooldownBar,
            ShowOtherCooldownBarsChanged = showOtherCooldownBars,
            ShowChatTimestampsChanged = showChatTimestamps,
            Use24HourClockChanged = use24HourClock,
            ShowChannelLabelsChanged = showChannelLabels,
            RestoreDefaults = restore,
            ResetPanels = resetPanels,
        };
    }

    /// <summary>Paint the window, re-localizing the captions first if the language has changed since the
    /// last draw. <paramref name="isActive"/> marks this as the focused window, which brightens its title
    /// bar.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, bool isActive = false)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _aspectChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_MaintainAspectRatio);
            _alwaysShowBarsChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_AlwaysShowBars);
            _showCombatNumbersChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowCombatNumbers);
            _playMusicChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_PlayMusic);
            _volumeSlider.Label = ClientStrings.Get(ClientStrings.OptionsPanel_MusicVolume);
            _useGamepadChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_UseGamepad);
            _skipPlayersTabChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_SkipPlayersTabTarget);
            _showNpcNamesChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowNpcNames);
            _showOtherNamesChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowOtherPlayerNames);
            _showPlayerNameChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowPlayerName);
            _showCooldownBarChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowCooldownBar);
            _showOtherCooldownBarsChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowOtherCooldownBars);
            _showChatTimestampsChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowChatTimestamps);
            _use24HourClockChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_Use24HourClock);
            _showChannelLabelsChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowChannelLabels);
            _showBloodChk.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ShowBlood);
            _restoreBtn.Label = ClientStrings.Get(ClientStrings.OptionsPanel_RestoreDefaults);
            _resetPanelsBtn.Label = ClientStrings.Get(ClientStrings.OptionsPanel_ResetPanels);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.OptionsPanel_Title), isActive);
        LayoutControls(out var langLabelRect, out var langDropRect);
        _aspectChk.Draw(sb, font, input);
        _showCombatNumbersChk.Draw(sb, font, input);
        _alwaysShowBarsChk.Draw(sb, font, input);
        _playMusicChk.Draw(sb, font, input, disabled: !AudioAvailable);
        _volumeSlider.Draw(sb, font, input, disabled: !AudioAvailable);
        _useGamepadChk.Draw(sb, font, input);
        _skipPlayersTabChk.Draw(sb, font, input);
        _showNpcNamesChk.Draw(sb, font, input);
        _showOtherNamesChk.Draw(sb, font, input);
        _showPlayerNameChk.Draw(sb, font, input);
        _showCooldownBarChk.Draw(sb, font, input);
        _showOtherCooldownBarsChk.Draw(sb, font, input);
        _showChatTimestampsChk.Draw(sb, font, input);
        _use24HourClockChk.Draw(sb, font, input, disabled: !_showChatTimestampsChk.Checked);
        _showChannelLabelsChk.Draw(sb, font, input);
        _showBloodChk.Draw(sb, font, input);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.OptionsPanel_Language) + ":",
            new Vector2(langLabelRect.X, langLabelRect.Y), UiHelper.DlgLabelColor);
        _languageDropDown.DrawHeader(sb, font, langDropRect, input);
        _restoreBtn.Draw(sb, font, input);
        _resetPanelsBtn.Draw(sb, font, input);
        _panel.DrawOverlay(sb);
        _languageDropDown.DrawPopup(sb, font, langDropRect, input);
    }

    /// <summary>Position every control for the window's current size. Shared by Update and Draw so hit
    /// testing and painting can never disagree about where a widget is.</summary>
    private void LayoutControls(out Rectangle langLabelRect, out Rectangle langDropRect)
    {
        var c = _panel.ContentBounds;
        const int RowH = 20;
        const int ChkH = 14;
        const int SldrH = 24;
        const int ColPad = 6;
        int half = c.Width / 2;
        int lx = c.X + ColPad;
        int rx = c.X + half + ColPad;
        int colW = half - ColPad * 2;

        // ── Left column ────────────────────────────────────────────────────────
        _aspectChk.Bounds = new Rectangle(lx, c.Y + ColPad, colW, ChkH);
        _showCombatNumbersChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH, colW, ChkH);
        _alwaysShowBarsChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 2, colW, ChkH);
        _playMusicChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 3, colW, ChkH);
        _volumeSlider.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 4, colW, SldrH);
        _useGamepadChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 4 + SldrH, colW, ChkH);
        _showChatTimestampsChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 5 + SldrH, colW, ChkH);
        _use24HourClockChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 6 + SldrH, colW, ChkH);
        _showChannelLabelsChk.Bounds = new Rectangle(lx, c.Y + ColPad + RowH * 7 + SldrH, colW, ChkH);

        // ── Right column ───────────────────────────────────────────────────────
        _skipPlayersTabChk.Bounds = new Rectangle(rx, c.Y + ColPad, colW, ChkH);
        _showNpcNamesChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH, colW, ChkH);
        _showOtherNamesChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH * 2, colW, ChkH);
        _showPlayerNameChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH * 3, colW, ChkH);
        _showCooldownBarChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH * 4, colW, ChkH);
        _showOtherCooldownBarsChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH * 5, colW, ChkH);
        _showBloodChk.Bounds = new Rectangle(rx, c.Y + ColPad + RowH * 6, colW, ChkH);

        int langY = c.Y + ColPad + RowH * 7 + 4;
        langLabelRect = new Rectangle(rx, langY, colW, ChkH);
        langDropRect = new Rectangle(rx, langY + 16, colW, 20);

        // ── Bottom spanning both columns ───────────────────────────────────────
        // Two buttons side by side across the strip.
        // Restore Defaults resets the OPTIONS above; Reset Panels resets panel geometry and column
        // layouts, which the options have nothing to do with — separate buttons because one is about
        // what the game shows and the other about where the windows sit.
        const int BtnGap = 6;
        int btnY = c.Bottom - 26;
        int btnStripW = c.Width - 40;
        int leftBtnW = (btnStripW - BtnGap) / 2;
        _restoreBtn.Bounds = new Rectangle(c.X + 20, btnY, leftBtnW, 18);
        _resetPanelsBtn.Bounds = new Rectangle(c.X + 20 + leftBtnW + BtnGap, btnY, btnStripW - leftBtnW - BtnGap, 18);
    }
}
