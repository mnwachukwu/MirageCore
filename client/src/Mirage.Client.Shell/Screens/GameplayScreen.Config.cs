using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>Persisting per-character UI state — panel positions via the registry, table column
/// layouts, and the display preferences the options panel writes.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Config persistence ────────────────────────────────────────────────────

    private void SavePanelConfig()
    {
        if (_config is null) return;
        string charName = _ctx.State.CurrentCharName;
        if (charName.Length == 0) return;

        foreach (var slot in _panels)
            if (slot.Policy.ConfigKey is not null) _config.SetPanelBounds(charName, slot.Policy.ConfigKey, slot.Panel.Bounds);
        _config.SetSocialTab(charName, _social.ActiveTab);
        foreach (var (id, table) in AllColumnTables())
            _config.SetTableColumns(charName, id, table.AllowReorder ? table.ColumnOrder : null, table.ColumnWidths, table.SortColumn, table.SortAscending);
        _config.SetPanelBounds(charName, "Death", _death.Bounds);
        _config.Save(_ctx.State.AccountName);
    }

    // Every persisted table across the panels, keyed by a stable id — saved/restored + change-watched generically.
    private IEnumerable<KeyValuePair<string, IColumnLayoutTable>> AllColumnTables()
    {
        foreach (var kv in _social.ColumnTables) yield return kv;
        foreach (var kv in _mail.ColumnTables) yield return kv;
        foreach (var kv in _market.ColumnTables) yield return kv;
        foreach (var kv in _questLog.ColumnTables) yield return kv;
    }

    /// <summary>Push a set of per-character display preferences into the screen fields, the options panel
    /// widgets, and the shell — everything that has to move for the setting to take effect on the next
    /// frame.
    ///
    /// <para>Shared by world entry (the character's saved prefs) and Restore Defaults (a fresh
    /// <see cref="AccountConfig.CharacterConfig"/>). A second copy of these assignments is the hazard:
    /// two writers can disagree about <see cref="AlwaysShowBars"/> — one setting the screen field while
    /// the other sets the checkbox — leaving the persisted value different from the one the renderer
    /// reads. One method means a restored option cannot be saved without also being applied.</para></summary>
    private void ApplyCharPrefs(AccountConfig.CharacterConfig prefs)
    {
        AlwaysShowBars = prefs.AlwaysShowBars;
        _skipPlayersWithTabTarget = prefs.SkipPlayersWithTabTarget;
        _showNpcNames = prefs.ShowNpcNames;
        _showBlood = prefs.ShowBlood;
        _showOtherPlayerNames = prefs.ShowOtherPlayerNames;
        _showPlayerName = prefs.ShowPlayerName;
        _showCooldownBar = prefs.ShowCooldownBar;
        _showOtherCooldownBars = prefs.ShowOtherCooldownBars;
        _ctx.OptionsPanel.ApplyCharPrefs(prefs);
        // The two the shell also carries (the damage-number popups are drawn by MirageGame, not here).
        _ctx.OnAlwaysShowBarsChanged(prefs.AlwaysShowBars);
        _ctx.OnShowCombatNumbersChanged(prefs.ShowCombatNumbers);
        _chat.SetChatDisplayOptions(prefs.ShowChatTimestamps, prefs.Use24HourClock, prefs.ShowChannelLabels);
    }

    /// <summary>Return every floating panel to the position and size it was declared with, and every table
    /// to its declared column widths, order and sort — the Options panel's Reset Panels button. Both saves
    /// are needed because panel bounds are per-character but the Options window's own bounds are global
    /// (see <c>PanelPolicies.BySlot[PanelSlots.Options]</c>, whose ConfigKey is null).</summary>
    private void ResetPanelLayout()
    {
        foreach (var slot in _panels) slot.Panel.ResetBounds();
        _death.ResetBounds();
        foreach (var (_, table) in AllColumnTables()) table.ResetColumnLayout();
        SavePanelConfig();
        _ctx.SaveSettings();
    }

    private void SaveCharPrefs()
    {
        if (_config is null) return;
        string charName = _ctx.State.CurrentCharName;
        string accountName = _ctx.State.AccountName;
        if (charName.Length == 0 || accountName.Length == 0) return;
        if (!_config.Characters.TryGetValue(charName, out var cc))
            _config.Characters[charName] = cc = new AccountConfig.CharacterConfig();
        cc.AlwaysShowBars = _ctx.OptionsPanel.AlwaysShowBars;
        cc.ShowCombatNumbers = _ctx.OptionsPanel.ShowCombatNumbers;
        cc.SkipPlayersWithTabTarget = _ctx.OptionsPanel.SkipPlayersWithTabTarget;
        cc.ShowNpcNames = _ctx.OptionsPanel.ShowNpcNames;
        cc.ShowBlood = _ctx.OptionsPanel.ShowBlood;
        cc.ShowOtherPlayerNames = _ctx.OptionsPanel.ShowOtherPlayerNames;
        cc.ShowPlayerName = _ctx.OptionsPanel.ShowPlayerName;
        cc.ShowCooldownBar = _ctx.OptionsPanel.ShowCooldownBar;
        cc.ShowOtherCooldownBars = _ctx.OptionsPanel.ShowOtherCooldownBars;
        cc.ShowChatTimestamps = _ctx.OptionsPanel.ShowChatTimestamps;
        cc.Use24HourClock = _ctx.OptionsPanel.Use24HourClock;
        cc.ShowChannelLabels = _ctx.OptionsPanel.ShowChannelLabels;
        cc.ActiveChatChannel = _chat.GetActiveChannel();
        _config.Save(accountName);
    }
}
