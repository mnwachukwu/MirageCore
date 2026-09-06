using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Sound;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Client.Shell;

/// <summary>The game loop's update half: transport pumping, screen ticks, music, and the combat
/// state transitions that drive the enter/leave-combat effects.</summary>
public sealed partial class MirageGame : Game
{
    /// <summary>Per-frame tick: window state and deferred config saves, input, the network pump, music,
    /// dialogs, and the active screen. Input is swapped for an empty <c>_blockedInput</c> whenever a
    /// modal dialog is up, so screens keep ticking without receiving player input.</summary>
    protected override void Update(GameTime gameTime)
    {
        // Detect window position/size changes and save ~1s after the last one.
        // While the quit confirm is open, lock the window to prevent moving/resizing.
        if (_initialized)
        {
            if (_quitConfirm.IsVisible)
            {
                if (Window.Position != _quitConfirmLockedPos)
                {
                    Window.Position = _quitConfirmLockedPos;
                    _lastTrackedWindowPos = _quitConfirmLockedPos;
                }
                if (SdlInterop.IsMinimized(Window.Handle))
                    SdlInterop.RestoreWindow(Window.Handle);
            }
            else
            {
                var curPos = Window.Position;
                var curSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
                if (curPos != _lastTrackedWindowPos || curSize != _lastTrackedWindowSize)
                {
                    _lastTrackedWindowPos = curPos;
                    _lastTrackedWindowSize = curSize;
                    if (!SdlInterop.IsMaximized(Window.Handle))
                    {
                        _restoreWindowX = curPos.X;
                        _restoreWindowY = curPos.Y;
                        _restoreWindowWidth = curSize.X;
                        _restoreWindowHeight = curSize.Y;
                    }
                    _windowSettleFrames = 60;
                }
                else if (_windowSettleFrames > 0 && --_windowSettleFrames == 0)
                {
                    SaveConfig();
                }
            }
        }

        // Reset stale key-state when re-focusing so we don't replay held keys.
        if (!IsActive)
        {
            if (_wasActive) _input.Reset();
            _wasActive = false;
        }
        else
        {
            // Suppress the spurious scroll-wheel jump that occurs on the first frame after
            // regaining focus: Reset() zeroed _prevMouse.ScrollWheelValue, but the OS
            // accumulator retains its value, producing a large phantom delta.
            if (!_wasActive) _input.NotifyFocusGained();
            _wasActive = true;
        }

        // Counted here rather than in Draw: MonoGame runs EXTRA Updates to catch up after a frame that
        // overran, and how many is the thing worth knowing. Draw reads and clears it.
        _updatesThisFrame++;
        if (gameTime.IsRunningSlowly) _runningSlowly = true;
        long updateStart = Stopwatch.GetTimestamp();

        _fpsFrameCount++;
        float elapsedSec = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _totalTimeSeconds += elapsedSec;
        _fpsAccMs += elapsedSec * 1000f;
        if (_fpsAccMs >= 1000f)
        {
            _state.GameFps = _fpsFrameCount;
            _fpsFrameCount = 0;
            _fpsAccMs -= 1000f;
        }

        while (_transport.TryDequeue(out string line))
            _handler.Handle(line);

        // Detect unexpected disconnects (server drop, network loss, or server shutdown with no alert).
        // The alert path sets _dialog.IsVisible first, so this won't double-fire.
        if (_transport.DroppedUnexpectedly && !_dialog.IsVisible)
        {
            _transport.Disconnect();
            _dialog.Show(ClientStrings.Get(ClientStrings.Common_Disconnected), () => _menu.GoToMainMenu());
        }

        var lb = GetLetterbox();
        float scaleX = lb.Width / (float)RefW;
        float scaleY = lb.Height / (float)RefH;
        _input.SetMouseTransform(lb.X, lb.Y, scaleX, scaleY);
        if (IsActive)
            _input.Update();

        // The console, above every screen so it opens on the login screen and character select as well as
        // in game — the moments worth reading a log for include the ones before a world exists. The key is
        // CONSUMED so nothing downstream also acts on it, and the panel takes no other input.
        if (_input.IsKeyPressed(Keys.OemTilde))
        {
            _input.ConsumeKey(Keys.OemTilde);
            _consolePanel.Toggle();
        }
        _consolePanel.Update(_input);

        // Alt+F4 was intercepted by the SDL filter; show quit confirm directly (bypass open panels).
        if (_pendingAltF4)
        {
            _pendingAltF4 = false;
            if (!_dialog.IsVisible && !_quitConfirm.IsVisible)
            {
                _quitConfirmLockedPos = Window.Position;
                Window.AllowUserResizing = false;
                bool inCombat = _state.Me.LastCombatMs > 0
                    && (Environment.TickCount64 - _state.Me.LastCombatMs) < 10_000
                    && !_state.Me.Dead;  // a corpse isn't in combat (no ghost risk) — always show Logout while dead
                _quitConfirm.Show(Exit, inCombat, onLogout: () =>
                {
                    _ctx!.Sender.SendLogoutToCharSelect();
                    _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.CharSelectScreen_Returning));
                    _screens.Replace(new LoadingScreen(_ctx));
                });
            }
        }

        // Dialog and quit confirm are modal — consume all input while visible; don't update screens.
        if (_dialog.IsVisible)
        {
            _dialog.Update(_input);
        }
        else if (_quitConfirm.IsVisible)
        {
            _quitConfirm.Update(_input);
            if (!_quitConfirm.IsVisible) // Cancel was clicked or ESC pressed
                Window.AllowUserResizing = true;
            _screens.Update(gameTime, _blockedInput);
        }
        else if (_guildOffer.IsVisible)
        {
            _guildOffer.Update(_input);
            _screens.Update(gameTime, _blockedInput);
        }
        else if (_tradeDialog.IsVisible)
        {
            _tradeDialog.Update(_input);
            _screens.Update(gameTime, _blockedInput);
        }
        else
        {
            bool inGame = _screens.Current is GameplayScreen;
            if (!inGame)
            {
                // Focus + Z-order tracking. Click inside the overlap region focuses (and keeps on
                // top) the panel currently on top. Click inside the buried panel — outside the
                // overlap — raises THAT panel and focuses it. Click outside both clears focus.
                if (_input.IsMouseJustPressed())
                {
                    bool inConfig = _configPanel.ContainsMouse(_input.MousePosition);
                    bool inOptions = _optionsPanel.ContainsMouse(_input.MousePosition);
                    if (inConfig && inOptions)
                    {
                        _configPanelFocused = _configOnTop;
                        _optionsPanelFocused = !_configOnTop;
                    }
                    else if (inConfig)
                    {
                        _configOnTop = true;
                        _configPanelFocused = true;
                        _optionsPanelFocused = false;
                    }
                    else if (inOptions)
                    {
                        _configOnTop = false;
                        _optionsPanelFocused = true;
                        _configPanelFocused = false;
                    }
                    else
                    {
                        _configPanelFocused = false;
                        _optionsPanelFocused = false;
                    }
                }

                if (_configOnTop) UpdateConfigPanel();
                else UpdateOptionsPanel();
                if (_configOnTop) UpdateOptionsPanel();
                else UpdateConfigPanel();
            }
            // While ConfigPanel is open, gate screen input — without this, text typed into
            // the panel's Host/Port fields would also bleed into LoginScreen's Name/Password
            // fields underneath. Mirrors the modal pattern used by _dialog and _quitConfirm.
            _screens.Update(gameTime, _configPanel.IsOpen ? _blockedInput : _input);
        }

        // Sidebar link clicks — restructured per screen tier:
        //   Pre-connect:  Configure / Options paired (HudPanel.ConfigureLink / OptionsLinkPregame).
        //   CharSelect/NewChar/Loading: lone Options (HudPanel.OptionsLink).
        //   Gameplay: Options (O) / Help (H) — GameplayScreen handles those itself.
        if (_screens.Current is not GameplayScreen)
        {
            if (IsPreConnectScreen())
            {
                if (HudPanel.OptionsLinkPregame.IsClicked(_input))
                {
                    _optionsPanel.Toggle();
                    _input.ConsumeMouseClick();
                    if (_optionsPanel.IsOpen)
                    {
                        _configOnTop = false; // newly opened panel raises to top
                        _optionsPanelFocused = true;
                    }
                }
                else if (HudPanel.ConfigureLink.IsClicked(_input))
                {
                    if (_configPanel.IsOpen)
                    {
                        _configPanel.Close();
                    }
                    else
                    {
                        _configPanel.Open(_ctx!.ServerHost, _ctx.ServerPort);
                        _configOnTop = true; // newly opened panel raises to top
                        _configPanelFocused = true;
                    }
                    _input.ConsumeMouseClick();
                }
            }
            else if (HudPanel.OptionsLink.IsClicked(_input))
            {
                _optionsPanel.Toggle();
                _input.ConsumeMouseClick();
                if (_optionsPanel.IsOpen)
                    _optionsPanelFocused = true;
            }
        }

        TickCombatStateTransitions();

        base.Update(gameTime);
        _updateTicks += Stopwatch.GetTimestamp() - updateStart;
    }

    /// <summary>Polls the local player + party partner each frame and spawns floating "Enter Combat"
    /// (orange) or "End Combat" (lime) text when LastCombatMs / LastCombatTickMs cross the same 10 s
    /// window used by the world HUD bars (RenderCommandBuilder.IsInCombat). Anchors over the live player
    /// record when visible so the text rides their interp; falls back to the party snapshot's
    /// map/tile when the partner is on a non-loaded map (SpawnFloatingTextAtEntity no-ops off-screen).</summary>
    private void TickCombatStateTransitions()
    {
        if (_screens.Current is not GameplayScreen gs)
        {
            _wasInGameplay = false;
            _meInCombatPrev = false;
            _partyInCombatPrev = false;
            return;
        }

        long now = Environment.TickCount64;
        var me = _state.Me;
        bool meValid = me is not null && !string.IsNullOrEmpty(me.Name);
        bool meIn = meValid && me!.LastCombatMs > 0 && (now - me.LastCombatMs) < 10_000;

        var party = _state.Party;
        bool partyIn = party.Active && party.LastCombatTickMs > 0
            && (now - party.LastCombatTickMs) < 10_000;

        if (!_wasInGameplay)
        {
            _meInCombatPrev = meIn;
            _partyInCombatPrev = partyIn;
            _wasInGameplay = true;
            return;
        }

        if (meValid && meIn != _meInCombatPrev)
        {
            string text = meIn ? ClientStrings.Get(ClientStrings.Combat_EnterCombat) : ClientStrings.Get(ClientStrings.Combat_EndCombat);
            Color color = meIn ? Color.Orange : UiHelper.FloatHealColor;
            gs.SpawnFloatingTextAtEntity(me!.Map, me.X, me.Y, me.XOffset, me.YOffset, text, color, size: 1);
            _meInCombatPrev = meIn;
        }

        if (party.Active && partyIn != _partyInCombatPrev)
        {
            string text = partyIn ? ClientStrings.Get(ClientStrings.Combat_EnterCombat) : ClientStrings.Get(ClientStrings.Combat_EndCombat);
            Color color = partyIn ? Color.Orange : UiHelper.FloatHealColor;
            var p = (party.Index >= 1 && party.Index <= Constants.MaxPlayers)
                ? _state.Players[party.Index] : null;
            if (p is not null && !string.IsNullOrEmpty(p.Name))
                gs.SpawnFloatingTextAtEntity(p.Map, p.X, p.Y, p.XOffset, p.YOffset, text, color, size: 1);
            else
                gs.SpawnFloatingTextAtEntity(party.MapNum, party.X, party.Y, 0f, 0f, text, color, size: 1);
            _partyInCombatPrev = partyIn;
        }
        else if (!party.Active)
        {
            _partyInCombatPrev = false;
        }
    }
}
