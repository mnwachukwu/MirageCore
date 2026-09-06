using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
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

/// <summary>User settings: the appsettings load/save round-trip, and the options and config panels
/// that edit them live.</summary>
public sealed partial class MirageGame : Game
{

    // User settings persist in the per-user config dir. appsettings.json is not bundled; until the
    // player changes a setting the app falls back to the in-code defaults in ReadConfig.
    private static string UserSettingsPath => AppPaths.Config("appsettings.json");

    /// <summary>Loads the global settings, falling back to the in-code defaults for anything the file does
    /// not carry. A key that fails to parse aborts the rest of the read but keeps what was already applied,
    /// so one bad value costs only itself and whatever follows it, not the whole file.</summary>
    private static ClientSettings ReadConfig()
    {
        var s = new ClientSettings();
        try
        {
            string cfgPath = UserSettingsPath;
            if (File.Exists(cfgPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("Server", out var srv))
                {
                    if (srv.TryGetProperty("Host", out var h) && h.GetString() is { } host) s.ServerHost = host;
                    if (srv.TryGetProperty("Port", out var p)) s.ServerPort = p.GetInt32();
                }
                if (root.TryGetProperty("MaintainAspectRatio", out var mar))
                    s.MaintainAspectRatio = mar.GetBoolean();
                if (root.TryGetProperty("PlayMusic", out var pm))
                    s.PlayMusic = pm.GetBoolean();
                if (root.TryGetProperty("MusicVolume", out var mv))
                    s.MusicVolume = Math.Clamp(mv.GetInt32(), 0, 100);
                if (root.TryGetProperty("MainMenuMusic", out var mmm))
                    s.MainMenuMusic = mmm.GetInt32();
                if (root.TryGetProperty("UseGamepad", out var ugp))
                    s.UseGamepad = ugp.GetBoolean();
                if (root.TryGetProperty("Language", out var lang) && lang.GetString() is { } ls)
                    s.Language = ls;
                if (root.TryGetProperty("RememberLogin", out var rl))
                    s.RememberLogin = rl.GetBoolean();
                // Capped on the way in so a hand-edited file cannot overflow the field it lands in.
                if (root.TryGetProperty("RememberedLogin", out var rln) && rln.GetString() is { } login)
                    s.RememberedLogin = login.Length > Constants.NameLength ? login[..Constants.NameLength] : login;
                if (root.TryGetProperty("OptionsPanel", out var op) &&
                    op.TryGetProperty("x", out var ox) && op.TryGetProperty("y", out var oy) &&
                    op.TryGetProperty("width", out var ow) && op.TryGetProperty("height", out var oh))
                {
                    s.OptionsPanelBounds = new Rectangle(ox.GetInt32(), oy.GetInt32(), ow.GetInt32(), oh.GetInt32());
                }
                if (root.TryGetProperty("WindowX", out var wxn)) s.WindowX = wxn.GetInt32();
                if (root.TryGetProperty("WindowY", out var wyn)) s.WindowY = wyn.GetInt32();
                if (root.TryGetProperty("WindowWidth", out var ww))
                    s.WindowWidth = Math.Max(ww.GetInt32(), RefW);
                if (root.TryGetProperty("WindowHeight", out var wh))
                    s.WindowHeight = Math.Max(wh.GetInt32(), RefH);
                if (root.TryGetProperty("WindowMaximized", out var wm))
                    s.WindowMaximized = wm.GetBoolean();
            }
        }
        catch { /* keep whatever parsed, defaults for the rest */ }
        return s;
    }

    /// <summary>Writes the client config, preserving any keys this build doesn't know about so a
    /// downgrade or a hand-edited file isn't stripped.</summary>
    private void SaveConfig()
    {
        try
        {
            string path = UserSettingsPath;
            // Seed from the existing user file so keys we don't manage here survive the round-trip.
            var root = File.Exists(path)
                ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? new JsonObject()
                : new JsonObject();
            root["Server"] = new JsonObject { ["Host"] = _serverHost, ["Port"] = _serverPort };
            root["MaintainAspectRatio"] = _maintainAspectRatio;
            root["Language"] = _language;
            // Read back off the context: the login screen owns these, and it is the only writer.
            root["RememberLogin"] = _ctx?.RememberLogin ?? false;
            root["RememberedLogin"] = _ctx?.RememberedLogin ?? "";
            root["PlayMusic"] = _playMusic;
            root["MusicVolume"] = _musicVolume;
            root["MainMenuMusic"] = _mainMenuMusic;
            root["UseGamepad"] = _useGamepad;
            var b = _optionsPanel.Bounds;
            root["OptionsPanel"] = new JsonObject { ["x"] = b.X, ["y"] = b.Y, ["width"] = b.Width, ["height"] = b.Height };
            if (Window.Handle != IntPtr.Zero)
            {
                bool maximized = SdlInterop.IsMaximized(Window.Handle);
                root["WindowX"] = _restoreWindowX;
                root["WindowY"] = _restoreWindowY;
                root["WindowWidth"] = _restoreWindowWidth;
                root["WindowHeight"] = _restoreWindowHeight;
                root["WindowMaximized"] = maximized;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal: preference just won't persist */ }
    }

    /// <summary>Starts or stops music to match the option, from either place that can flip it.
    ///
    /// <para>Switching it on resumes what SHOULD be playing rather than what happened to be playing
    /// when it went off. Leaving a character clears the remembered track, and the menu only claims one
    /// while music is already on — so turning music off in game, logging out, and turning it back on at
    /// the menu left nothing to resume and the toggle looked dead until the next map load handed it a
    /// track. Outside gameplay the menu track is the answer; inside it, a map with no music is silent
    /// on purpose.</para></summary>
    private void ApplyMusicEnabled()
    {
        if (!_playMusic)
        {
            _music.Stop();
            return;
        }
        if (_currentMusicTrack <= 0 && _screens.Current is not GameplayScreen)
            _currentMusicTrack = _mainMenuMusic;
        if (_currentMusicTrack > 0)
            _music.Play(MusicPath(_currentMusicTrack));
    }

    // Pre-connect screens — those where the user has not yet committed to a server.
    // [Configure] is only shown here; on CharSelect/NewChar/Loading the server choice
    // is already locked in for this session.
    /// <summary>Whether the active screen is one of the pre-connect screens, which show the Configure
    /// link and allow editing the server address.</summary>
    private bool IsPreConnectScreen() =>
        _screens.Current is MainMenuScreen or LoginScreen or NewAccountScreen
                          or ChangePasswordScreen or DeleteAccountScreen;

    /// <summary>Pushes the current host/port into the pre-connect config panel.</summary>
    private void UpdateConfigPanel()
    {
        var (configSaved, _) = _configPanel.Update(_input);
        if (_configPanel.ContainsMouse(_input.MousePosition))
        {
            _input.ConsumeMouseClick();
            _input.ConsumeMouseDown();
        }
        if (configSaved)
        {
            _serverHost = _configPanel.Host;
            _serverPort = _configPanel.PortValue;
            _ctx!.ServerHost = _serverHost;
            _ctx.ServerPort = _serverPort;
            SaveConfig();
        }
    }

    /// <summary>Pushes the current option values into the options panel.</summary>
    private void UpdateOptionsPanel()
    {
        var ch = _optionsPanel.Update(_input);
        if (_optionsPanel.ContainsMouse(_input.MousePosition))
        {
            _input.ConsumeMouseClick();
            _input.ConsumeMouseDown();
            // Note: hover is not consumed so checkboxes can highlight in Draw.
        }
        if (ch.AspectChanged)
        {
            _maintainAspectRatio = _optionsPanel.MaintainAspectRatio;
            SaveConfig();
        }
        if (ch.BarsChanged) _alwaysShowBars = _optionsPanel.AlwaysShowBars;
        if (ch.CombatNumbersChanged) _showCombatNumbers = _optionsPanel.ShowCombatNumbers;
        if (ch.GamepadChanged)
        {
            _useGamepad = _optionsPanel.UseGamepad;
            _input.UseGamepad = _useGamepad;
            SaveConfig();
        }
        if (ch.PlayMusicChanged)
        {
            _playMusic = _optionsPanel.PlayMusic;
            ApplyMusicEnabled();
            SaveConfig();
        }
        if (ch.VolumeChanged)
        {
            _musicVolume = _optionsPanel.MusicVolume;
            _music.Volume = _musicVolume / 100f;
            SaveConfig();
        }
        if (ch.LanguageChanged is not null) ApplyLanguage(ch.LanguageChanged);
        if (_optionsPanel.LayoutChanged) SaveConfig();
        if (ch.RestoreDefaults)
        {
            RestoreWindowDefaults();
            // The per-character options have no character to belong to on this screen, so there is
            // nothing to persist and no renderer reading them yet — but the checkboxes are on show, and
            // leaving them at whatever the player last toggled makes the button look half-broken.
            // GameplayScreen.OnEnter overwrites all of these from the character's saved prefs at login;
            // the two fields below are only what seeds the first GameplayScreen until it does.
            var defaults = new AccountConfig.CharacterConfig();
            _optionsPanel.ApplyCharPrefs(defaults);
            _alwaysShowBars = defaults.AlwaysShowBars;
            _showCombatNumbers = defaults.ShowCombatNumbers;
        }
        // Pre-login the options window is the only floating panel whose bounds are persisted, so it is
        // all there is here for Reset Panels to put back.
        if (ch.ResetPanels)
        {
            _optionsPanel.ResetBounds();
            SaveConfig();
        }
    }

    /// <summary>
    /// The single place a language change is applied. BOTH entry points funnel here — the pre-game
    /// options panel (via <c>UpdateOptionsPanel</c>) and the in-game one (GameplayScreen through
    /// <c>ShellContext.OnLanguageChanged</c>) — so the client and the SERVER SESSION can never
    /// disagree about the locale.
    /// <para>The send is gated on the TRANSPORT, not on being in-game: a change made at character
    /// select is post-login but pre-game, and the Login packet that carried the old locale has
    /// already gone. An early send is harmless — the server drops a SetLanguage from an
    /// unauthenticated connection — and a pre-login change needs no send at all, because the next
    /// Login/NewAccount/DelAccount/ChangePassword packet carries the locale itself.</para>
    /// </summary>
    private void ApplyLanguage(string locale)
    {
        _language = locale;
        ClientStrings.Load(_langDir, _language);
        SaveConfig();
        if (_transport.IsConnected) _sender.SendSetLanguage(_language);
    }
}
