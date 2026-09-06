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

/// <summary>Graphics-device setup and asset loading: tilesets, sprite and item sheets, the color
/// key, and the procedurally generated light-halo and box textures.</summary>
public sealed partial class MirageGame : Game
{
    /// <summary>Loads the localized strings, applies window settings, installs the Alt+F4 filter, and
    /// <summary>Absolute path to a numbered music track.</summary>
    private static string MusicPath(int track) => AppPaths.Asset("assets", "music", $"music{track}.ogg");

    /// builds the screen stack and event wiring. Runs before <see cref="LoadContent"/>.</summary>
    protected override void Initialize()
    {
        _langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        ClientStrings.Load(_langDir, _language);
        var languages = ClientStrings.GetAvailableLanguages(_langDir);
        _optionsPanel.SetLanguages(languages, _language);
        // The ENGINE's name at startup — no server has named a world yet. See ClientState.GameName.
        Window.Title = _state.GameName;
        Window.AllowUserResizing = true;
        Window.TextInput += (_, e) => _input.Accumulate(e.Character);
        Window.ClientSizeChanged += OnClientSizeChanged;

        UiHelper.Init(GraphicsDevice);

        _ctx = new ShellContext
        {
            Screens = _screens,
            State = _state,
            Sender = _sender,
            Menu = _menu,
            Transport = _transport,
            Graphics = GraphicsDevice,
            ExitGame = Exit,
            ServerHost = _serverHost,
            ServerPort = _serverPort,
            RememberLogin = _rememberLogin,
            RememberedLogin = _rememberedLogin,
            Dialog = _dialog,
            OptionsPanel = _optionsPanel,
            ConsolePanel = _consolePanel,
            OnAspectRatioChanged = v => { _maintainAspectRatio = v; SaveConfig(); },
            OnAlwaysShowBarsChanged = v => { _alwaysShowBars = v; },
            OnShowCombatNumbersChanged = v => { _showCombatNumbers = v; },
            OnUseGamepadChanged = v => { _useGamepad = v; _input.UseGamepad = v; SaveConfig(); },
            OnRestoreDefaults = RestoreWindowDefaults,
            SaveSettings = SaveConfig,
            ShowQuitConfirm = () =>
            {
                if (_dialog.IsVisible || _quitConfirm.IsVisible) return;
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
            },
            OnPlayMusicChanged = v =>
            {
                _playMusic = v;
                ApplyMusicEnabled();
                SaveConfig();
            },
            OnMusicVolumeChanged = v => { _musicVolume = v; _music.Volume = v / 100f; SaveConfig(); },
            PlayMenuMusic = () =>
            {
                if (_playMusic && _mainMenuMusic > 0)
                {
                    _music.Play(MusicPath(_mainMenuMusic));
                    _currentMusicTrack = _mainMenuMusic;
                }
            },
            // Routed through the one funnel so the in-game options panel also re-syncs the server session.
            OnLanguageChanged = ApplyLanguage,
        };

        try { _music.Volume = _musicVolume / 100f; }
        catch (NoAudioHardwareException) { HandleNoAudio(); }
        _optionsPanel.PlayMusic = _playMusic;
        _optionsPanel.MusicVolume = _musicVolume;

        WireMenuEvents();
        WireGameEvents();

        try { _screens.Push(new MainMenuScreen(_ctx)); }
        catch (NoAudioHardwareException)
        {
            HandleNoAudio();
            _screens.Push(new MainMenuScreen(_ctx));
        }
        base.Initialize();

        // Apply saved window geometry now that the SDL window exists.
        SdlInterop.SetMinimumSize(Window.Handle, RefW, RefH);
        if (_restoreWindowX != int.MinValue)
            Window.Position = new Point(_restoreWindowX, _restoreWindowY);
        if (_restoreWindowWidth != RefW || _restoreWindowHeight != RefH)
        {
            _gfx.PreferredBackBufferWidth = _restoreWindowWidth;
            _gfx.PreferredBackBufferHeight = _restoreWindowHeight;
            _gfx.ApplyChanges();
        }
        if (_savedWindowMaximized)
        {
            SdlInterop.MaximizeWindow(Window.Handle);
            // SDL_MaximizeWindow is synchronous on Windows, but the ClientSizeChanged event
            // isn't fired until the first SDL event pump (which runs after Initialize returns).
            // Read the actual post-maximize size now so the back buffer is correct on frame 1.
            SdlInterop.GetWindowSize(Window.Handle, out int mw, out int mh);
            if (mw > 0 && mh > 0 && (mw != _gfx.PreferredBackBufferWidth || mh != _gfx.PreferredBackBufferHeight))
            {
                _gfx.PreferredBackBufferWidth = mw;
                _gfx.PreferredBackBufferHeight = mh;
                _gfx.ApplyChanges();
            }
        }
        // Capture the OS-assigned position if nothing was saved.
        if (_restoreWindowX == int.MinValue)
        {
            _restoreWindowX = Window.Position.X;
            _restoreWindowY = Window.Position.Y;
        }
        // Seed the debounce with current state so we don't fire immediately.
        _lastTrackedWindowPos = Window.Position;
        _lastTrackedWindowSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
        _initialized = true;

        // Install SDL event filter so Alt+F4 shows the quit confirm instead of closing immediately.
        // The filter returns 0 (suppress) for SDL_QUIT when the alt modifier is held and we're
        // in gameplay with no dialog open; all other SDL_QUIT events pass through normally.
        _sdlFilter = SdlEventFilterFunc;
        SdlInterop.SetEventFilter(Marshal.GetFunctionPointerForDelegate(_sdlFilter));
    }

    /// <summary>Re-derives the letterbox and notifies input of the new window size after a resize.</summary>
    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        int w = Window.ClientBounds.Width;
        int h = Window.ClientBounds.Height;
        if (w != _gfx.PreferredBackBufferWidth || h != _gfx.PreferredBackBufferHeight)
        {
            _gfx.PreferredBackBufferWidth = w;
            _gfx.PreferredBackBufferHeight = h;
            _gfx.ApplyChanges();
        }
        // Discard stale mouse button state so the first Update() after a resize
        // doesn't report a phantom click from the OS resize interaction.
        _input.NotifyResize();
    }

    /// <summary>Loads fonts, tilesets, sprite and item atlases, and the heat-haze shader, then generates
    /// the procedural light textures (radial halos and the soft-edged safe-zone box).</summary>
    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("fonts/DefaultFont");
        HudPanel.ComputeLinkLayout(_font);  // center the in-game [Options (O)] / [Help (H)] pair for this font
        HudPanel.ComputePregameLinkLayout(_font);  // center the pre-connect [Configure] / [Options] pair
        _gameFont = Content.Load<SpriteFont>("fonts/GameFont");
        _menuFont = Content.Load<SpriteFont>("fonts/MenuFont");
        _titleFont = Content.Load<SpriteFont>("fonts/TitleFont");
        _bubbleFont = Content.Load<SpriteFont>("fonts/BubbleFont");
        _heatEffect = Content.Load<Effect>("shaders/HeatHaze");
        _lightMaskEffect = Content.Load<Effect>("shaders/LightMask");
        if (_ctx is not null)
        {
            _ctx.MenuFont = _menuFont;
            _ctx.TitleFont = _titleFont;
        }
        _renderTarget = new RenderTarget2D(GraphicsDevice, RefW, RefH);
        // _worldRT is created/resized on demand in EnsureWorldTarget (its size depends on the window).
        _optionsPanel.MaintainAspectRatio = _maintainAspectRatio;
        _optionsPanel.AlwaysShowBars = _alwaysShowBars;
        _optionsPanel.ShowCombatNumbers = _showCombatNumbers;
        _optionsPanel.UseGamepad = _useGamepad;
        _input.UseGamepad = _useGamepad;

        _tilesets = LoadTilesets();
        var tilesetWidths = new int[_tilesets.Length];
        for (int i = 0; i < _tilesets.Length; i++) tilesetWidths[i] = _tilesets[i]?.Width ?? 0;
        TileAtlas.Init(tilesetWidths);
        _sprites = LoadSpriteSheetsForCell(Constants.PicX);        // 32x32: players + size-1 NPCs
        _sprites64 = LoadSpriteSheetsForCell(Constants.PicX * 2);  // 64x64: size-2 NPCs (empty until art added)
        _sprites96 = LoadSpriteSheetsForCell(Constants.PicX * 3);  // 96x96: size-3 NPCs (empty until art added)
        _items = LoadSheetSet(Constants.ItemsAssetSubfolder);
        _menuArt = LoadTexture(AppPaths.Asset("assets", "graphics", "MenuArt.jpg"));
        if (_ctx is not null)
        {
            _ctx.MenuArt = _menuArt;
            _ctx.Sprites = _sprites;
            _ctx.Sprites64 = _sprites64;
            _ctx.Sprites96 = _sprites96;
            _ctx.Items = _items;
        }

        _lightHaloOuterTex = MakeRadialHaloTex(GraphicsDevice, LightHaloOuterRadius,
            dist => (byte)(LightModel.OuterFalloff(dist) * 255));
        _lightHaloInnerTex = MakeRadialHaloTex(GraphicsDevice, LightHaloInnerRadius,
            dist => (byte)(LightModel.InnerFalloff(dist) * 255)); // Gaussian, sigma ≈ radius/2.5
        _mapLightTex = MakeBoxLightTex(GraphicsDevice, Camera.ViewW, Camera.ViewH, MapAreaBleed);
    }

    /// <summary>Smoothstep easing on [0,1], used for the light halo falloff curves.</summary>
    private static float Smoothstep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    // Feathered-box light sized to one map cell inflated by a skirt on every side. The interior is a
    // FLAT 1 covering exactly the inner (cell) region; only the outer `skirt` px on each side feathers
    // 1→0. Drawn per safe cell with the interior aligned to the map, so contiguous safe cells' interiors
    // tile edge-to-edge (seamless under max blend) while the skirts spill past the block's outer boundary.
    // Luminance lives in RGB+A (like the halos) so it multiplies/max-blends by color.
    /// <summary>Generates the safe-zone map light: a flat interior with a feathered skirt, so adjacent
    /// safe maps tile without a seam.</summary>
    private static Texture2D MakeBoxLightTex(GraphicsDevice gd, int innerW, int innerH, int skirt)
    {
        int w = innerW + skirt * 2, h = innerH + skirt * 2;
        var tex = new Texture2D(gd, w, h);
        var pixels = new Color[w * h];
        for (int py = 0; py < h; py++)
        {
            float ey = EdgeSkirt(py, h, skirt);
            for (int px = 0; px < w; px++)
            {
                float ex = EdgeSkirt(px, w, skirt);
                byte a = (byte)(ex * ey * 255f);
                pixels[py * w + px] = new Color(a, a, a, a);
            }
        }
        tex.SetData(pixels);
        return tex;
    }

    // 1 across the interior; smoothstep 1→0 over only the outermost `skirt` px on each side.
    /// <summary>Feather weight for one axis of the box light's skirt; 1 inside the flat interior.</summary>
    private static float EdgeSkirt(int i, int size, int skirt)
    {
        float d = Math.Min(i, size - 1 - i); // distance to the nearest edge
        return Smoothstep(d / skirt);
    }

    // White-luminance radial halo: alpha/RGB carry the falloff, tinted to a warm color at draw time.
    /// <summary>Generates a radial light halo of the given radius, with per-distance alpha from
    /// <paramref name="alphaAt"/> (smoothstep for the outer reach, Gaussian for the flame core).</summary>
    private static Texture2D MakeRadialHaloTex(GraphicsDevice gd, int radius, Func<float, byte> alphaAt)
    {
        int size = radius * 2;
        var tex = new Texture2D(gd, size, size);
        var pixels = new Color[size * size];
        float cx = size / 2f, cy = size / 2f;
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float dx = px - cx, dy = py - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy) / radius;
                byte a = dist >= 1f ? (byte)0 : alphaAt(dist);
                pixels[py * size + px] = new Color(a, a, a, a);
            }
        }

        tex.SetData(pixels);
        return tex;
    }

    /// <summary>Loads a texture from an absolute path, returning null when the file is missing.</summary>
    private Texture2D? LoadTexture(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }
        catch { return null; }
    }

    /// <summary>
    /// Loads a graphics sheet under the format contract, returning null when the file is missing.
    ///
    /// <para>A BMP names its transparent color with its top-left pixel; a PNG carries its own alpha. The
    /// extension decides, and it decides the same way here as it does in the editor — see
    /// <see cref="SheetFile.UsesColorKey"/>.</para>
    /// </summary>
    private Texture2D? LoadSheet(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var tex = Texture2D.FromStream(GraphicsDevice, stream);
            if (SheetFile.UsesColorKey(path)) ApplyColorKey(tex);
            else Premultiply(tex);
            return tex;
        }
        catch { return null; }
    }

    /// <summary>Premultiplies a decoded sheet, which is what a PNG needs before a premultiplied blend can
    /// draw it. See <see cref="SheetPixels.Premultiply"/>.</summary>
    private static void Premultiply(Texture2D tex)
    {
        var pixels = new Color[tex.Width * tex.Height];
        tex.GetData(pixels);
        SheetPixels.Premultiply(pixels);
        tex.SetData(pixels);
    }

    // Scans assets/graphics/tiles/ for numbered tile sheets (0_*.bmp, 1_*.bmp, ...). The leading number
    // in each filename is the stable sheet index, so adding/removing files never reshuffles existing maps.
    /// <summary>Loads every numbered tile sheet into an array indexed by sheet number. Gaps stay null,
    /// so a missing sheet renders as blank rather than shifting every later sheet's index.</summary>
    private Texture2D?[] LoadTilesets()
    {
        string dir = AppPaths.Asset("assets", "graphics", Constants.TilesAssetSubfolder);
        var byIndex = new Dictionary<int, string>();
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                if (!SheetFile.IsSupported(path)) continue;
                int idx = SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(path));
                if (idx >= 0 && idx < Constants.MaxTilesets) byIndex[idx] = path;
            }
        }
        if (byIndex.Count == 0) return [];
        int max = 0;
        foreach (int k in byIndex.Keys) if (k > max) max = k;
        var sheets = new Texture2D?[max + 1];
        TileOpacity.Reset();
        foreach (var kv in byIndex)
        {
            var tex = LoadSheet(kv.Value);
            sheets[kv.Key] = tex;
            if (tex is not null) ReadTileCoverage(kv.Key, tex);
        }

        return sheets;
    }

    /// <summary>Hands one loaded sheet's alpha to <see cref="TileOpacity"/>, which keeps eight bytes a tile
    /// and nothing else. Read AFTER the color key, so the art's transparent color counts as transparent —
    /// the shadow a tile casts is the shape the player sees.</summary>
    private static void ReadTileCoverage(int sheet, Texture2D tex)
    {
        var pixels = new Color[tex.Width * tex.Height];
        tex.GetData(pixels);
        var alpha = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].A;
        TileOpacity.SetSheet(sheet, alpha, tex.Width, tex.Height);
    }


    // Single-sheet load for sprites/items: the first image file in assets/graphics/<subfolder>
    // (alphabetical), else the legacy flat path. Multi-file handling is intentionally deferred.
    /// <summary>Loads the single sheet an asset folder is expected to hold (sprites, items), falling back
    /// <summary>
    /// Every numbered sheet in one asset folder, indexed by the number in its filename.
    /// </summary>
    /// <remarks>
    /// Gaps stay null, so a missing sheet draws nothing rather than shifting every later sheet's index —
    /// the number is what records store, and it has to mean the same thing whatever else is on disk.
    /// </remarks>
    private Texture2D?[] LoadSheetSet(string subfolder)
    {
        string dir = AppPaths.Asset("assets", "graphics", subfolder);
        var byIndex = new Dictionary<int, string>();
        if (Directory.Exists(dir))
        {
            foreach (string path in Directory.EnumerateFiles(dir))
            {
                if (!SheetFile.IsSupported(path)) continue;
                int idx = SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(path));
                if (idx >= 0 && idx < Constants.MaxTilesets) byIndex[idx] = path;
            }
        }
        if (byIndex.Count == 0) return [];

        int max = 0;
        foreach (int k in byIndex.Keys) if (k > max) max = k;
        var sheets = new Texture2D?[max + 1];
        foreach (var kv in byIndex) sheets[kv.Key] = LoadSheet(kv.Value);
        return sheets;
    }

    /// <summary>The sprite sheets for one NPC footprint size class (32/64/96 px cells).
    ///
    /// <para>A sheet number means the same character at all three sizes, so index 1 is <c>1_*</c> in each
    /// size folder. A size with no such sheet leaves that entry null and those NPCs draw no sprite; their
    /// bars, name and collision still work.</para></summary>
    private Texture2D?[] LoadSpriteSheetsForCell(int cell) =>
        LoadSheetSet(Path.Combine(Constants.SpritesAssetSubfolder, $"{cell}x{cell}"));

    // Replace the top-left pixel's color with transparent throughout the texture.
    // Color-key convention: the top-left pixel defines the transparent color.
    /// <summary>Makes every pixel matching the top-left pixel fully transparent, the color-key
    /// convention the .bmp art is authored against.</summary>
    private static void ApplyColorKey(Texture2D tex)
    {
        var pixels = new Color[tex.Width * tex.Height];
        tex.GetData(pixels);
        SheetPixels.ApplyColorKey(pixels);
        tex.SetData(pixels);
    }
}
