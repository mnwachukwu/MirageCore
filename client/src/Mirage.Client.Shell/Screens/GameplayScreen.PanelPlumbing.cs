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

/// <summary>The generic panel plumbing the registry made possible: z-order, hit-testing, the
/// capturing/movement/escape queries, and the update+draw fan-out.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Panel Z-order helpers — slot indices come from the Panel* consts at the top of this file ──

    private void BringToFront(int idx)
    {
        _zOrder.Remove(idx);
        _zOrder.Add(idx);
    }

    private bool PanelIsOpen(int i) => _panels[i].Panel.IsOpen;

    // A panel is "capturing input" while it shows a modal sub-surface that owns the keyboard/mouse:
    // a number-prompt text box (Inventory drop X, Bank deposit/withdraw X), a right-click context
    // menu, or a confirm overlay (Spell forget, Shop repair/trade). Single source of truth for both
    // the world-input gate in Update and the Escape handler below, so they never disagree that a
    // panel is modal — a new capturing panel only has to declare Capturing in the registry.
    private bool AnyPanelCapturingInput
    {
        get
        {
            foreach (var slot in _panels)
                if (slot.Capturing is not null && slot.Capturing()) return true;
            return false;
        }
    }

    private bool PanelContainsMouse(int i, Point p) => _panels[i].Panel.ContainsMouse(p);

    // Whether Escape should close a panel rather than raise the quit confirm. Delegates to
    // PanelPolicies so the live path and the tested path are the same code — an equivalent loop here
    // would be a second implementation that could drift from the one the tests cover.
    private bool AnyPanelOpenForEscape => PanelPolicies.AnyOpenForEscape(_isPanelOpen);

    private void UpdatePanel(int i, InputState input, bool isActive)
    {
        _panels[i].Update(input, isActive);
    }

    // The options panel is the one panel whose Update is not a plain "tick yourself" call: it returns
    // a tuple of which settings the user just changed, and every one of them has to be applied and
    // persisted here. That is why it is a method rather than a registry lambda like the others.
    private void UpdateOptionsPanel(InputState input)
    {
        var ch = _ctx.OptionsPanel.Update(input);
        if (ch.AspectChanged) _ctx.OnAspectRatioChanged(_ctx.OptionsPanel.MaintainAspectRatio);
        if (ch.BarsChanged)
        {
            AlwaysShowBars = _ctx.OptionsPanel.AlwaysShowBars;
            _ctx.OnAlwaysShowBarsChanged(AlwaysShowBars);
            SaveCharPrefs();
        }
        if (ch.CombatNumbersChanged)
        {
            _ctx.OnShowCombatNumbersChanged(_ctx.OptionsPanel.ShowCombatNumbers);
            SaveCharPrefs();
        }
        if (ch.PlayMusicChanged) _ctx.OnPlayMusicChanged(_ctx.OptionsPanel.PlayMusic);
        if (ch.VolumeChanged) _ctx.OnMusicVolumeChanged(_ctx.OptionsPanel.MusicVolume);
        if (ch.GamepadChanged) _ctx.OnUseGamepadChanged(_ctx.OptionsPanel.UseGamepad);
        if (ch.SkipTabChanged)
        {
            _skipPlayersWithTabTarget = _ctx.OptionsPanel.SkipPlayersWithTabTarget;
            SaveCharPrefs();
        }
        if (ch.ShowNpcNamesChanged)
        {
            _showNpcNames = _ctx.OptionsPanel.ShowNpcNames;
            SaveCharPrefs();
        }
        if (ch.ShowBloodChanged)
        {
            _showBlood = _ctx.OptionsPanel.ShowBlood;
            SaveCharPrefs();
        }
        if (ch.ShowOtherNamesChanged)
        {
            _showOtherPlayerNames = _ctx.OptionsPanel.ShowOtherPlayerNames;
            SaveCharPrefs();
        }
        if (ch.ShowPlayerNameChanged)
        {
            _showPlayerName = _ctx.OptionsPanel.ShowPlayerName;
            SaveCharPrefs();
        }
        if (ch.ShowCooldownBarChanged)
        {
            _showCooldownBar = _ctx.OptionsPanel.ShowCooldownBar;
            SaveCharPrefs();
        }
        if (ch.ShowOtherCooldownBarsChanged)
        {
            _showOtherCooldownBars = _ctx.OptionsPanel.ShowOtherCooldownBars;
            SaveCharPrefs();
        }
        if (ch.ShowChatTimestampsChanged || ch.Use24HourClockChanged || ch.ShowChannelLabelsChanged)
        {
            _chat.SetChatDisplayOptions(_ctx.OptionsPanel.ShowChatTimestamps, _ctx.OptionsPanel.Use24HourClock, _ctx.OptionsPanel.ShowChannelLabels);
            SaveCharPrefs();
        }
        if (ch.LanguageChanged is not null) _ctx.OnLanguageChanged(ch.LanguageChanged);
        if (ch.RestoreDefaults)
        {
            // Two non-overlapping halves: the shell owns the global/window settings, this screen owns the
            // per-character display options. If both wrote AlwaysShowBars they could write it in opposite
            // directions, saving the option as on while the renderer kept it off.
            _ctx.OnRestoreDefaults();
            ApplyCharPrefs(new AccountConfig.CharacterConfig());
            _tabTarget = default;   // the tab-cycling rule just changed under it
            SaveCharPrefs();
        }
        if (ch.ResetPanels) ResetPanelLayout();
    }

    private void DrawPanel(int i, SpriteBatch sb, SpriteFont font, long nowMs, bool isActive, bool canHover)
        => _panels[i].Draw(sb, font, nowMs, isActive, canHover);

    private void HandleEscapeKey()
    {
        // The trade window is modal-ish (it locks movement) and server-driven: Escape cancels the trade,
        // and the server's end-of-trade sync flips TradeActive false, which closes the panel via the poll.
        if (_trade.IsOpen)
        {
            if (!_trade.IsCapturingInput) _ctx.Sender.SendTradeCancel();   // an open amount prompt owns Escape
            return;
        }
        bool anyPanelOpen = AnyPanelOpenForEscape;
        if (anyPanelOpen && !AnyPanelCapturingInput)
            CloseTopPanel();
        else if (!anyPanelOpen)
            _ctx.ShowQuitConfirm();
    }

    // Leaving the screen shuts every panel the client owns. Market and Trade are deliberately exempt
    // (ClosesOnLeave: false in the registry): both are server-driven sessions, and closing them here
    // without telling the server would leave the two sides disagreeing about whether the window is up.
    private void CloseAllPanels()
    {
        foreach (var slot in _panels)
            if (slot.Policy.ClosesOnLeave && slot.Panel.IsOpen) slot.Close();
    }

    // Escape closes the topmost open panel. Unlike CloseAllPanels this reaches Market and Trade too —
    // the player dismissing one window on purpose is a different act from the screen tearing down.
    private void CloseTopPanel()
    {
        for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
        {
            int idx = _zOrder[zi];
            if (!PanelIsOpen(idx)) continue;
            // Dismissing the shop also clears the server-pushed shop number: the ActiveShopNum poll in
            // Update would otherwise reopen the panel on the very next frame.
            if (idx == PanelShop) _ctx.State.ActiveShopNum = 0;
            _panels[idx].Close();
            return;
        }
    }

    private InputState _lastInput = new();
}
