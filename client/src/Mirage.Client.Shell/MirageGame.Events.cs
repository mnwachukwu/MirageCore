using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Client.Shell;

/// <summary>Shutdown, window handling, and the menu/game event wiring that connects the network
/// layer's events to the screens.</summary>
public sealed partial class MirageGame : Game
{
    /// <summary>Persists the config on the way out, so window geometry and options survive a quit.</summary>
    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        SaveConfig();
        base.OnExiting(sender, args);
    }

    /// <summary>Falls back to a null music player when the audio device is unavailable, and disables the
    /// audio options, so a machine with no sound output still runs.</summary>
    private void HandleNoAudio()
    {
        _audioAvailable = false;
        _music = new NullMusicPlayer();
        _optionsPanel.AudioAvailable = false;
    }

    /// <summary>Resets the GLOBAL options — aspect ratio, gamepad, music, window geometry — to their
    /// shipped defaults, mirrors them into the options panel, and persists them.
    ///
    /// <para>Deliberately does not touch the per-character display options, even though the same button
    /// resets those too. Each caller pairs this with an <c>ApplyCharPrefs</c> immediately after —
    /// GameplayScreen's in-game, which owns both the widgets and the screen fields that render from them,
    /// and the panel's own pre-login, where there is no character yet. When this method wrote those
    /// options as well, the two halves disagreed about AlwaysShowBars and the write the renderer read was
    /// the one that lost.</para></summary>
    private void RestoreWindowDefaults()
    {
        _maintainAspectRatio = true;
        _useGamepad = false;
        _optionsPanel.MaintainAspectRatio = true;
        _optionsPanel.UseGamepad = false;
        _input.UseGamepad = false;
        if (_audioAvailable)
        {
            _playMusic = true;
            _musicVolume = 100;
            _mainMenuMusic = 1;
            _optionsPanel.PlayMusic = true;
            _optionsPanel.MusicVolume = 100;
            _music.Volume = 1f;
            if (_screens.Current is GameplayScreen)
            {
                if (_currentMusicTrack > 0)
                    _music.Play(MusicPath(_currentMusicTrack));
                else
                    _music.Stop();
            }
            else
            {
                if (_mainMenuMusic > 0)
                    _music.Play(MusicPath(_mainMenuMusic));
                else
                    _music.Stop();
            }
        }
        _restoreWindowWidth = RefW;
        _restoreWindowHeight = RefH;
        SdlInterop.RestoreWindow(Window.Handle);
        // Center on whichever display the window is currently on.
        int display = SdlInterop.GetWindowDisplayIndex(Window.Handle);
        SdlInterop.GetDisplayBounds(display < 0 ? 0 : display, out var screen);
        _restoreWindowX = screen.X + (screen.W - RefW) / 2;
        _restoreWindowY = screen.Y + (screen.H - RefH) / 2;
        Window.Position = new Point(_restoreWindowX, _restoreWindowY);
        _gfx.PreferredBackBufferWidth = RefW;
        _gfx.PreferredBackBufferHeight = RefH;
        _gfx.ApplyChanges();
        // Reseed debounce so the forced resize doesn't queue another save.
        _lastTrackedWindowPos = new Point(_restoreWindowX, _restoreWindowY);
        _lastTrackedWindowSize = new Point(RefW, RefH);
        _windowSettleFrames = 0;
        SaveConfig();
    }

    /// <summary>Disposes the render targets, procedural textures, and the music player.</summary>
    protected override void UnloadContent()
    {
        _transport.Disconnect();
        foreach (var t in _tilesets) t?.Dispose();
        foreach (var s in _sprites) s?.Dispose();
        foreach (var s in _items) s?.Dispose();
        _menuArt?.Dispose();
        _renderTarget?.Dispose();
        _worldRT?.Dispose();
        _worldRTGround?.Dispose();
        _worldRTFringe?.Dispose();
        _lightRTFringe?.Dispose();
        _music.Stop();
    }

    // ── Event wiring ──────────────────────────────────────────────────────────

    /// <summary>Subscribes to <see cref="MenuLogic"/> so menu-state changes drive screen transitions,
    /// alerts, and loading messages.</summary>
    private void WireMenuEvents()
    {
        _menu.StateChanged += OnMenuStateChanged;
        _menu.LoadingMessageChanged += msg => _state.LoadingMessage = msg;
        _menu.AlertReceived += (msg, code) =>
        {
            _dialog.Show(msg, () =>
            {
                _transport.Disconnect();
                var flow = _menu.LastAuthFlow;
                _menu.LastAuthFlow = AuthFlow.None;
                switch (flow)
                {
                    case AuthFlow.ChangePassword:
                        // Successful change → bounce to Login keyed to the account name.
                        // Any other error → stay on the change-password form so the user can retry.
                        if (code == AlertCode.PasswordChanged)
                        {
                            (_loginClearName, _loginClearPassword) = (false, true);
                            _menu.GoToLogin();
                        }
                        else
                        {
                            // Wrong name → clear name; everything else (wrong pwd, in-use) → keep name.
                            _changePwdClearName = code == AlertCode.AccountNotFound;
                            _menu.GoToChangePassword();
                        }
                        break;
                    case AuthFlow.DeleteAccount:
                        if (code == AlertCode.AccountDeleted)
                        {
                            _menu.GoToMainMenu();
                        }
                        else
                        {
                            _deleteAccountClearName = code == AlertCode.AccountNotFound;
                            _menu.GoToDeleteAccount();
                        }
                        break;
                    case AuthFlow.NewAccount:
                        // Success drops the credentials into Login (password isn't preserved across
                        // the screen swap, but the user has the name in front of them); any error
                        // stays on the new-account form.
                        if (code == AlertCode.AccountCreated)
                        {
                            (_loginClearName, _loginClearPassword) = (false, false);
                            _menu.GoToLogin();
                        }
                        else
                        {
                            _menu.GoToNewAccount();
                        }

                        break;
                    default:
                        if (code == AlertCode.IncorrectPassword)
                            (_loginClearName, _loginClearPassword) = (false, true);
                        else if (code == AlertCode.AccountNotFound)
                            (_loginClearName, _loginClearPassword) = (true, true);
                        else if (code == AlertCode.PasswordChanged)
                            (_loginClearName, _loginClearPassword) = (false, true);
                        else
                            (_loginClearName, _loginClearPassword) = (false, false);
                        _menu.GoToLogin();
                        break;
                }
            });
        };
    }

    /// <summary>Swaps the active screen for the new menu state and carries over whatever that screen
    /// needs (fonts, atlases, and the shell context).</summary>
    private void OnMenuStateChanged(MenuState state)
    {
        switch (state)
        {
            case MenuState.Loading:
                _pendingChat.Clear();
                _screens.Replace(new LoadingScreen(_ctx!));
                break;
            case MenuState.Login:
                _screens.Replace(new LoginScreen(_ctx!, _loginClearName, _loginClearPassword));
                (_loginClearName, _loginClearPassword) = (true, true);
                break;
            case MenuState.ChangePassword:
                _screens.Replace(new ChangePasswordScreen(_ctx!, _changePwdClearName));
                _changePwdClearName = true;
                break;
            case MenuState.DeleteAccount:
                _screens.Replace(new DeleteAccountScreen(_ctx!, _deleteAccountClearName));
                _deleteAccountClearName = true;
                break;
            case MenuState.NewAccount:
                _screens.Replace(new NewAccountScreen(_ctx!));
                break;
            case MenuState.CharSelect:
                _currentMusicTrack = 0;
                _screens.Replace(new CharSelectScreen(_ctx!));
                break;
            case MenuState.NewChar:
                _screens.Replace(new NewCharScreen(_ctx!));
                break;
            case MenuState.MainMenu:
                _screens.Replace(new MainMenuScreen(_ctx!));
                break;
            case MenuState.InGame:
                var gameplay = new GameplayScreen(_ctx!, _tilesets, _sprites, _items, _gameFont, _bubbleFont)
                {
                    AlwaysShowBars = _alwaysShowBars,
                };
                _screens.Replace(gameplay);
                while (_pendingChat.TryDequeue(out var msg))
                    gameplay.AddChatLine(msg.text, msg.color);
                break;
        }
    }

    private static class SdlInterop
    {
        private const string Lib = "SDL2";
        private const uint SDL_WINDOW_MAXIMIZED = 0x00000080;
        private const uint SDL_WINDOW_MINIMIZED = 0x00000020;

        [StructLayout(LayoutKind.Sequential)]
        public struct SdlRect { public int X, Y, W, H; }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetWindowFlags(IntPtr window);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_MaximizeWindow(IntPtr window);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_RestoreWindow(IntPtr window);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_SetWindowMinimumSize(IntPtr window, int minW, int minH);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_GetWindowDisplayIndex(IntPtr window);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_GetDisplayBounds(int displayIndex, out SdlRect rect);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_SetEventFilter(IntPtr filter, IntPtr userdata);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint SDL_GetModState();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_GetWindowSize(IntPtr window, out int w, out int h);

        public static bool IsMaximized(IntPtr handle) => (SDL_GetWindowFlags(handle) & SDL_WINDOW_MAXIMIZED) != 0;
        public static bool IsMinimized(IntPtr handle) => (SDL_GetWindowFlags(handle) & SDL_WINDOW_MINIMIZED) != 0;
        public static void MaximizeWindow(IntPtr handle) => SDL_MaximizeWindow(handle);
        public static void GetWindowSize(IntPtr handle, out int w, out int h) => SDL_GetWindowSize(handle, out w, out h);
        public static void RestoreWindow(IntPtr handle) => SDL_RestoreWindow(handle);
        public static void SetMinimumSize(IntPtr handle, int w, int h) => SDL_SetWindowMinimumSize(handle, w, h);
        public static int GetWindowDisplayIndex(IntPtr handle) => SDL_GetWindowDisplayIndex(handle);
        public static void GetDisplayBounds(int display, out SdlRect rect) => SDL_GetDisplayBounds(display, out rect);
        public static void SetEventFilter(IntPtr filter) => SDL_SetEventFilter(filter, IntPtr.Zero);
        public static uint GetModState() => SDL_GetModState();
    }

    // SDL event filter: intercepts window-close events before MonoGame processes them.
    // Returns 0 to suppress the event (preventing game exit) when Alt is held in gameplay.
    // X-button close (no alt modifier) passes through and exits immediately.
    // We must intercept BOTH SDL_WINDOWEVENT_CLOSE and SDL_QUIT: MonoGame calls Exit() from
    // the WINDOWEVENT_CLOSE handler, so filtering only SDL_QUIT is too late.
    /// <summary>SDL event filter that intercepts Alt+F4 so it raises the quit-confirm dialog instead of
    /// closing the window outright. Runs on SDL's thread, so it only sets a flag
    /// (<c>_pendingAltF4</c>) that <see cref="Update"/> acts on.</summary>
    private static int SdlEventFilterFunc(IntPtr userdata, IntPtr @event)
    {
        const uint SdlQuitEvent = 0x100;
        const uint SdlWindowEvent = 0x200;
        const byte SdlWindowEventClose = 14;
        const uint KmodAlt = 0x0300; // KMOD_LALT | KMOD_RALT
        uint type = (uint)Marshal.ReadInt32(@event);
        bool isClose = type == SdlQuitEvent
            || (type == SdlWindowEvent && Marshal.ReadByte(@event, 12) == SdlWindowEventClose);
        if (isClose
            && _instance is { } inst
            && inst._screens.Current is GameplayScreen
            && !inst._dialog.IsVisible
            && (SdlInterop.GetModState() & KmodAlt) != 0)
        {
            _pendingAltF4 = true;
            return 0;
        }
        return 1;
    }

    /// <summary>Subscribes to the packet handler's gameplay events — chat, floating text, sounds,
    /// dialogs, and panel refreshes — translating them into shell-side effects.</summary>
    private void WireGameEvents()
    {
        _handler.PlayersOnlineChanged += count =>
            Window.Title = $"{_state.GameName} - Players Online: {count}";

        // A server just told us what its world is called; the title bar is the one place that shows it
        // and cannot read it for itself. It also names this address in the server list.
        _handler.GameNameChanged += name =>
        {
            Window.Title = name;
            ServerBookStore.Book.Remember(name, _serverHost, _serverPort);
        };

        _handler.ChatMessage += pkt =>
        {
            if (_screens.Current is GameplayScreen gs)
                gs.AddChatLine(pkt);
            else
                _pendingChat.Enqueue((pkt.Msg, pkt.Color));
        };

        _handler.GuildOffer += p =>
            _guildOffer.Show(p.GuildName, p.OtherName, p.Kind,
                accept => _sender.SendGuildOfferResponse(accept));

        _handler.TradeInvite += from =>
            _tradeDialog.Show(from, accept => _sender.SendTradeRespond(accept));

        // Re-anchor world-pixel UI (floating combat text) onto the new center map's coord frame
        // when the player crosses a seamless seam, so floats spawned just before the cross don't
        // drift off-screen by one full map width/height.
        _state.GridShifted += (dx, dy) =>
        {
            if (_screens.Current is GameplayScreen g)
            {
                g.ShiftFloatingTexts(dx, dy);
                g.ShiftParticles(dx, dy);
            }
        };

        // A warp/teleport replaces the whole map; floats anchored in the old map's coord frame
        // would otherwise hang at the destination until they age out, so drop them on the clear.
        _state.MapStateCleared += () =>
        {
            if (_screens.Current is GameplayScreen g)
            {
                g.ClearFloatingTexts();
                g.ClearParticles();
            }
        };

        _handler.MeleeSwing += (map, x, y, xo, yo, dir, sparks) =>
            (_screens.Current as GameplayScreen)?.SpawnMeleeSwing(map, x, y, xo, yo, dir, sparks);

        _handler.SpellCast += fx =>
            (_screens.Current as GameplayScreen)?.SpawnSpellCast(fx);

        _handler.EntityDied += fx =>
            (_screens.Current as GameplayScreen)?.OnEntityDied(fx);

        _handler.ShopOpened += _ =>
            (_screens.Current as GameplayScreen)?.OpenShop();

        _handler.OpenInn += () =>
            (_screens.Current as GameplayScreen)?.OpenInnPanel();

        _handler.OpenNpcQuestMenu += (map, slot) =>
            (_screens.Current as GameplayScreen)?.OpenNpcQuestMenuAt(map, slot);

        _handler.OpenNpcConversation += (map, slot, conv) =>
            (_screens.Current as GameplayScreen)?.OpenConversationAt(map, slot, conv);

        _handler.PreparedSpellReceived += slot =>
            (_screens.Current as GameplayScreen)?.SyncPreparedSpell(slot);

        _handler.TargetAssigned += t =>
            (_screens.Current as GameplayScreen)?.SetTabTarget(t);

        _handler.MapReady += () =>
        {
            // Same track (e.g. a seamless crossing within one music zone) — don't restart it. Music is the
            // effective value resolved against the map's group client-side (0 = none), so a live group-music
            // edit swaps the track on the next MapReady without a reload.
            int track = _state.MusicOf(_state.Map);
            if (track == _currentMusicTrack) return;
            _currentMusicTrack = track;
            if (_playMusic && track > 0)
                _music.Play(MusicPath(track));
            else
                _music.Stop();
        };

        _handler.VitalDelta += (idx, delta, type, isNpc, isCrit, npcMap) =>
        {
            if (_screens.Current is not GameplayScreen gs) return;
            // Blood is independent of the damage-numbers toggle — proceed if EITHER is on.
            if (!_showCombatNumbers && !gs.ShowBlood) return;

            // Resolve the entity's map + local tile, then let GameplayScreen do the
            // camera-aware screen-coord conversion + below-sprite flip via SpawnFloatingTextAtEntity.
            if (type == VitalType.Exp)
            {
                if (!_showCombatNumbers) return;   // EXP is a number, never blood
                if (delta <= 0 || idx != _state.MyIndex) return;
                var me = _state.Players[_state.MyIndex];
                if (string.IsNullOrEmpty(me.Name)) return;
                gs.SpawnFloatingTextAtEntity(me.Map, me.X, me.Y, me.XOffset, me.YOffset,
                    $"+{delta:N0} {VitalLabel(VitalType.Exp)}", UiHelper.ExpBarColor, size: 1);   // over ME
                return;
            }

            int mapNum, lx, ly, maxHp;
            float xoff, yoff;
            if (isNpc)
            {
                // Resolve the NPC on its OWN map (center or a neighbor) so the number floats over it
                // wherever it stands, not only on the center map.
                var npcs = _state.NpcsForMap(npcMap);
                if (npcs is null) return;
                var n = npcs[idx];
                if (n.Num == 0) return;
                mapNum = npcMap;
                lx = n.X;
                ly = n.Y;
                xoff = n.XOffset;
                yoff = n.YOffset;
                maxHp = n.MaxHp;
            }
            else
            {
                var p = _state.Players[idx];
                if (string.IsNullOrEmpty(p.Name)) return;
                mapNum = p.Map;
                lx = p.X;
                ly = p.Y;
                xoff = p.XOffset;
                yoff = p.YOffset;
                maxHp = p.MaxHp;
            }

            bool healing = delta > 0;
            int amount = Math.Abs(delta);
            // Blood: HP damage only; the shared concave curve boosts low hits so they still spray droplets.
            float bloodIntensity = (gs.ShowBlood && !healing && type == VitalType.Hp)
                ? Constants.BloodStrength(amount, maxHp) : 0f;
            // The damage number is gated separately by the combat-numbers toggle.
            string? text = null;
            Color color = UiHelper.FloatDmgColor;
            if (_showCombatNumbers)
            {
                string critSuffix = isCrit ? "!!" : "";
                text = healing
                    ? $"+{amount:N0} {VitalLabel(type)}{critSuffix}"
                    : $"-{amount:N0} {VitalLabel(type)}{critSuffix}";
                color = healing ? UiHelper.FloatHealColor : UiHelper.FloatDmgColor;
            }
            if (text is null && bloodIntensity <= 0f) return;
            // Defer the number + blood burst to the spell projectile's arrival when this hit belongs to an in-flight bolt.
            gs.SpawnOrDeferVitalFloat(isNpc, idx, npcMap, mapNum, lx, ly, xoff, yoff, text, color, bloodIntensity);
        };

        _handler.NpcWorldDamage += (mapNum, x, y, delta, isCrit, spawnMap, spawnSlot) =>
        {
            if (_screens.Current is not GameplayScreen gs) return;
            if (!_showCombatNumbers && !gs.ShowBlood) return;
            // Traversal NPCs have no slot — position the number by world tile, and defer it to a spell bolt
            // via the (spawnMap, spawnSlot) identity when one is in flight.
            bool healing = delta > 0;
            int amount = Math.Abs(delta);
            // Blood: HP damage only, sized by damage vs the guest's max HP (looked up by its spawn identity).
            float bloodIntensity = 0f;
            if (gs.ShowBlood && !healing && amount > 0
                && _state.TraversalNpcs.TryGetValue((spawnMap, spawnSlot), out var tn))
            {
                bloodIntensity = Constants.BloodStrength(amount, tn.MaxHp);
            }

            string? text = null;
            Color color = UiHelper.FloatDmgColor;
            if (_showCombatNumbers)
            {
                string critSuffix = isCrit ? "!!" : "";
                text = healing
                    ? $"+{amount:N0} {VitalLabel(VitalType.Hp)}{critSuffix}"
                    : $"-{amount:N0} {VitalLabel(VitalType.Hp)}{critSuffix}";
                color = healing ? UiHelper.FloatHealColor : UiHelper.FloatDmgColor;
            }
            if (text is null && bloodIntensity <= 0f) return;
            gs.SpawnOrDeferTraversalFloat(spawnMap, spawnSlot, mapNum, x, y, text, color, bloodIntensity);
        };

        _handler.LevelUp += () =>
        {
            if (_screens.Current is not GameplayScreen gs) return;
            var me = _state.Players[_state.MyIndex];
            if (string.IsNullOrEmpty(me.Name)) return;
            gs.SpawnFloatingTextAtEntity(me.Map, me.X, me.Y, me.XOffset, me.YOffset, ClientStrings.Get(ClientStrings.Combat_LevelUp), Color.Yellow, size: 1);   // over ME
        };

        _handler.CombatText += p =>
        {
            if (!_showCombatNumbers) return;
            if (_screens.Current is not GameplayScreen gs) return;
            if (p.Kind == CombatTextKind.None) return;
            string text;
            Color color;
            if (p.Kind == CombatTextKind.ZeroHit)
            {
                // A hit that mitigated to 0 — gray "0 HP/MP/SP" (vital set by the source: melee = HP,
                // Sub spell = its drained vital). "0" is a number + a reused Stats_* label, so no new key.
                VitalType vt = p.Vital switch
                {
                    CombatVital.Mp => VitalType.Mp,
                    CombatVital.Sp => VitalType.Sp,
                    _ => VitalType.Hp,
                };
                text = $"0 {VitalLabel(vt)}...";
                color = UiHelper.FloatZeroColor;
            }
            else
            {
                // The three no-damage outcomes share a colour and a phrasing pattern — one glance says
                // "nothing landed", and the word says why.
                text = ClientStrings.Get(p.Kind switch
                {
                    CombatTextKind.Block => ClientStrings.Combat_Blocked,
                    CombatTextKind.Miss => ClientStrings.Combat_Missed,
                    _ => ClientStrings.Combat_Dodged,
                });
                color = UiHelper.FloatBlockColor;
            }
            int mapNum = p.MapNum, lx, ly;
            float xoff = 0f, yoff = 0f;
            if (p.IsNpc && p.Index == 0)
            {
                // Traversal guest — no slot; position by world tile (like NpcWorldDamage).
                lx = p.X;
                ly = p.Y;
            }
            else if (p.IsNpc)
            {
                var npcs = _state.NpcsForMap(p.MapNum);
                if (npcs is null) return;
                var n = npcs[p.Index];
                if (n.Num == 0) return;
                lx = n.X;
                ly = n.Y;
                xoff = n.XOffset;
                yoff = n.YOffset;
            }
            else
            {
                var pl = _state.Players[p.Index];
                if (string.IsNullOrEmpty(pl.Name)) return;
                mapNum = pl.Map;
                lx = pl.X;
                ly = pl.Y;
                xoff = pl.XOffset;
                yoff = pl.YOffset;
            }
            // Centred on the subject's BODY, the way the damage numbers are: Blocked / Missed / Dodged over
            // a 3x3 mob belongs to the mob, not to whatever is standing on its top-left tile.
            gs.SpawnFloatingTextAtEntity(mapNum, lx, ly, xoff, yoff, text, color,
                gs.PopupFootprint(p.IsNpc, p.Index, p.MapNum, lx, ly));
        };
    }
}
