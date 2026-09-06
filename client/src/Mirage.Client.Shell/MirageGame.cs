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

/// <summary>
/// The client's root MonoGame <see cref="Game"/>: owns the graphics device, fonts and textures,
/// the screen stack, the network transport, and the shared <see cref="ClientState"/>.
/// <para><b>Rendering.</b> Gameplay does not draw straight to the backbuffer. The scrolling world
/// renders into a supersampled target (<c>_worldRT</c>) that is linear-downscaled on composite, which
/// is what keeps sub-pixel scrolling smooth instead of shimmering. At night a second pass builds a
/// light map that MULTIPLIES the world, and on a two-layer map (a bridge) ground and fringe get their
/// own world and light targets so the deck can occlude what is beneath it. Everything else — HUD,
/// panels, dialogs — draws to the letterboxed reference frame at whole pixels.</para>
/// <para><b>Lifetime.</b> <see cref="Initialize"/> builds the screens and wires events,
/// <see cref="LoadContent"/> loads fonts/atlases and generates the procedural light textures, and
/// <see cref="Update"/> drives input, networking, music and screen ticks.</para>
/// </summary>
public sealed partial class MirageGame : Game
{
    private readonly GraphicsDeviceManager _gfx;
    private SpriteBatch? _sb;
    private SpriteFont? _font;
    private SpriteFont? _gameFont;
    private SpriteFont? _menuFont;
    private SpriteFont? _titleFont;
    private SpriteFont? _bubbleFont;

    // Tileset textures indexed by sheet number (gaps may be null). See LoadTilesets / TileAtlas.
    private Texture2D?[] _tilesets = [];
    private Texture2D?[] _sprites = [];
    private Texture2D?[] _sprites64 = [];
    private Texture2D?[] _sprites96 = [];
    private Texture2D?[] _items = [];
    private Texture2D? _menuArt;
    private RenderTarget2D? _renderTarget;
    // Supersampled target for the scrolling world: rendered at _worldSS× the 512×384 viewport (crisp),
    // then LINEAR-downscaled to the screen, offset by the camera's sub-pixel fraction.  Supersampling +
    // linear gives smooth scrolling with NO shimmer — point-sampling at a moving sub-pixel position makes
    // every pixel flicker as it flips texels, while a plain 1× linear upscale would just be soft.
    // _worldSS is chosen per-window (= ceil(window scale), capped) so the composite is always a sharp
    // DOWNSCALE, never an upscale: 1 at native, 2 at ~1080p, 3 at ~1440p, 4 at ~4K.  The target is
    // (re)created only when the factor changes — see EnsureWorldTarget.
    private const int MaxWorldSS = 4;
    private int _worldSS;
    private RenderTarget2D? _worldRT;
    // Two-light-map occlusion split (night on a bridge): ground-layer content and fringe-layer content render
    // into these separate targets, each pre-multiplied by its OWN light map, then composited into _worldRT (the
    // fringe target is transparent where the deck is open, so ground shows through lit by the ground map). Unused
    // on the flat/daylight single-target path.
    private RenderTarget2D? _worldRTGround;
    private RenderTarget2D? _worldRTFringe;
    // Light-map RT — same dimensions as _worldRT so it composites via the same scale factors. Holds the
    // per-pixel lighting (dark navy ambient + additive warm halos) that MULTIPLIES the world at composite.
    // _lightRT is the ground/whole-view map; _lightRTFringe is the fringe map built only on the split path.
    private RenderTarget2D? _lightRT;
    private RenderTarget2D? _lightRTFringe;
    // Blood metaball accumulation target (same size as _worldRT).  Blood blobs are MAX-blended into this so
    // overlapping pools form a smooth UNION (no darkening seams), then it's composited (tinted) into the
    // world below the entities.
    private RenderTarget2D? _bloodRT;
    private RenderTarget2D? _bloodRTFringe;   // two-layer world: bridge-top blood field (composited on the deck)
    // Procedural radial-gradient light halo textures.  Created once in LoadContent.
    // Warm light halo layers, white-luminance radial gradients. Both drawn per light in the MAX pass
    // (overlapping lights don't additively bleed): a static outer reach + a flickering inner flame core.
    private Texture2D? _lightHaloOuterTex; // 3-tile radius (96 px), smoothstep falloff — stable reach
    private Texture2D? _lightHaloInnerTex; // 2-tile radius (64 px), Gaussian falloff — flickering flame core
    private Effect? _heatEffect;              // HeatWave world-distortion shader (Content/shaders/HeatHaze.fx)
    private Effect? _lightMaskEffect;         // Multiplies a light halo by its reach mask (Content/shaders/LightMask.fx)
    private const float HeatIntensity = 1.0f; // heat-haze strength (shader Intensity param)
    // Soft-edged box light for safe-zone maps (flat interior, feathered border). Created in LoadContent.
    private Texture2D? _mapLightTex;
    // Total elapsed game time in seconds; accumulated in Update() for the flicker animation.
    private float _totalTimeSeconds;
    internal static readonly RasterizerState WorldCompositeRaster = new() { ScissorTestEnable = true };
    // Ambient light color at full night — the factor unlit ground is MULTIPLIED by (so unlit areas go
    // dark and navy-tinted while retaining sprite/text shape). At darkness 0 the map is cleared to white
    // (identity multiply); it lerps toward this at full night.
    internal static readonly Color NightAmbient = new(
        LightModel.NightAmbientR, LightModel.NightAmbientG, LightModel.NightAmbientB, (byte)255);
    private const int LightHaloOuterRadius = Constants.PicX * 3; // 96 px (3 tiles) — outer reach
    private const int LightHaloInnerRadius = Constants.PicX * 2; // 64 px (2 tiles) — inner flame core
    // How far a map area light/dark overlay's soft edge spills past the map boundary. Also
    // the flat-interior box texture's skirt width, so the interior maps exactly onto the map cell.
    internal const int MapAreaBleed = Constants.PicX * Constants.MapAreaBleedTiles; // 64 px
    // Additive accumulation for entity halos: overlapping lights sum and blend smoothly (no dividing-line
    // seams, no dips, flicker rides visibly on top). The 8-bit light map clamps at white, and white = ×1.0
    // in the multiply composite = "fully lit / no darkening" — so light adds up to daylight and stops
    // there (no flooding brighter than that, no detail washout).
    internal static readonly BlendState LightAccumBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.One,
        AlphaBlendFunction = BlendFunction.Add,
    };
    // Max-component blend for the safe-zone map boxes only: contiguous safe maps' flat interiors tile with
    // no seam glow (overlaps take the brighter, not the sum). Entity halos use additive, above.
    internal static readonly BlendState MaxLightBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Max,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.One,
        AlphaBlendFunction = BlendFunction.Max,
    };
    // Multiply composite: backbuffer (world) × light map. Modulates the scene by the lighting instead of
    // painting over it — sprites, names, bars, and floats always read; lit areas warm/brighten, unlit darken.
    internal static readonly BlendState MultiplyBlend = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
    };

    // Two-light-map occlusion split: multiply a world target's RGB by its own light map IN PLACE while PRESERVING
    // the target's own alpha (dest × 1, src × 0). The plain MultiplyBlend above overwrites alpha with the light
    // map's (opaque) alpha — which on the TRANSPARENT fringe target would seal the grate/edge gaps and stop the
    // ground showing through. Here dst alpha survives, so a gap stays see-through after it's lit.
    internal static readonly BlendState LightModulateBlend = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        AlphaBlendFunction = BlendFunction.Add,
    };

    private readonly InputState _input = new();
    private readonly InputState _blockedInput = new(); // never Updated — always empty, used to tick screens without player input
    private readonly ScreenManager _screens = new();
    private readonly TcpClientTransport _transport = new();
    private readonly ClientState _state = new();
    private readonly AlertDialog _dialog = new();
    private readonly QuitConfirmDialog _quitConfirm = new();
    private readonly GuildOfferDialog _guildOffer = new();
    private readonly TradeRequestDialog _tradeDialog = new();
    private readonly ClientPacketSender _sender;
    private readonly ClientPacketHandler _handler;
    private readonly MenuLogic _menu;
    private IMusicPlayer _music;
    private bool _audioAvailable = true;
    private ShellContext? _ctx;
    private bool _wasActive = true;

    // Alt+F4 interception via SDL event filter.
    // The static instance reference lets the filter reach game state without userdata indirection.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlEventFilterDelegate(IntPtr userdata, IntPtr @event);
    private SdlEventFilterDelegate? _sdlFilter; // field keeps delegate alive (prevents GC)
    private static MirageGame? _instance;
    private static volatile bool _pendingAltF4;
    // Localized vital label for floating combat text. Resolved per use (not cached) so a runtime
    // language switch is reflected immediately; values reuse the Stats_* panel labels.
    private static string VitalLabel(VitalType type) => ClientStrings.Get(type switch
    {
        VitalType.Hp => ClientStrings.Stats_Hp,
        VitalType.Mp => ClientStrings.Stats_Mp,
        VitalType.Sp => ClientStrings.Stats_Sp,
        _ => ClientStrings.Stats_Exp,
    });
    private Point _quitConfirmLockedPos; // window position saved when quit confirm opens
    private bool _loginClearName = true;
    private bool _loginClearPassword = true;
    private bool _changePwdClearName = true;
    private bool _deleteAccountClearName = true;

    private int _fpsFrameCount;
    private float _fpsAccMs;

    // Frame-time accounting, gathered across Update and closed off in Draw. Present-to-present is what a
    // player perceives, so the frame is measured between Draws rather than inside one; Update's share is
    // accumulated because a catch-up frame runs it more than once.
    private int _updatesThisFrame;
    private bool _runningSlowly;
    private long _updateTicks;
    private long _lastPresentStamp;
    private int _lastGen0, _lastGen1, _lastGen2;
    private long _lastAllocated;
    // The light-map passes, bracketed separately from the rest of Draw: they are the part that scales with
    // how many emitters are on screen, and a draw that only says "slow" cannot say which half.
    private long _lightStart;
    private long _lightTicks;

    private const int RefW = UiHelper.RefW;   // 800
    private const int RefH = UiHelper.RefH;   // 600
    private bool _maintainAspectRatio = true;
    // These two mirror AccountConfig.CharacterConfig's defaults — they seed the first GameplayScreen
    // and back the pre-login checkboxes, both of which the character's saved prefs overwrite at login.
    private bool _alwaysShowBars = true;
    private bool _showCombatNumbers = true;
    // Per-frame combat-state transition detection for floating "Enter Combat" / "End Combat" text.
    // _wasInGameplay gates the very first in-game frame so we establish a baseline instead of firing
    // "Enter Combat" on a reconnect/ghost-takeover that lands while the timer is still live.
    private bool _wasInGameplay;
    private bool _meInCombatPrev;
    private bool _partyInCombatPrev;
    private bool _useGamepad = false;
    private bool _playMusic = true;
    private int _musicVolume = 100;
    private int _mainMenuMusic = 1;
    private int _currentMusicTrack;
    private readonly OptionsPanel _optionsPanel = new();
    private bool _optionsPanelFocused;
    // Owned here rather than by GameplayScreen, because backtick opens it on every screen.
    private readonly ConsolePanel _consolePanel = new();

    // Holding the world while a map loads: whether there is a complete frame to hold at all, and how the
    // one being held was lit. See the hold in MirageGame.Draw.
    private bool _worldDrawn;
    private bool _heldSplitPath;
    private bool _heldNeedsLightPass;
    private readonly ConfigPanel _configPanel = new();
    private bool _configPanelFocused;
    // Z-order for the two pre-connect panels: true = Config above Options, false = Options above
    // Config. Newly opened or clicked-into panel raises to top, mirroring GameplayScreen's
    // _zOrder/BringToFront behavior.
    private bool _configOnTop = true;
    private int _restoreWindowX = int.MinValue;
    private int _restoreWindowY = int.MinValue;
    private int _restoreWindowWidth = RefW;
    private int _restoreWindowHeight = RefH;
    private bool _savedWindowMaximized;
    // Debounce: save ~1s after the last position/size change.
    private bool _initialized;
    private Point _lastTrackedWindowPos;
    private Point _lastTrackedWindowSize;
    private int _windowSettleFrames;

    private readonly Queue<(string text, int color)> _pendingChat = new();

    private string _serverHost;
    private int _serverPort;
    private readonly bool _rememberLogin;
    private readonly string _rememberedLogin;
    private string _language = "en";
    private string _langDir = "lang";

    public MirageGame()
    {
        var cfg = ReadConfig();
        _serverHost = cfg.ServerHost;
        _serverPort = cfg.ServerPort;
        _rememberLogin = cfg.RememberLogin;
        _rememberedLogin = cfg.RememberedLogin;
        _maintainAspectRatio = cfg.MaintainAspectRatio;
        _playMusic = cfg.PlayMusic;
        _musicVolume = cfg.MusicVolume;
        _mainMenuMusic = cfg.MainMenuMusic;
        _useGamepad = cfg.UseGamepad;
        _restoreWindowX = cfg.WindowX;
        _restoreWindowY = cfg.WindowY;
        _restoreWindowWidth = cfg.WindowWidth;
        _restoreWindowHeight = cfg.WindowHeight;
        _savedWindowMaximized = cfg.WindowMaximized;
        _language = cfg.Language;
        if (cfg.OptionsPanelBounds is { } optionsBounds) _optionsPanel.SetBounds(optionsBounds);

        _gfx = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = RefW,
            PreferredBackBufferHeight = RefH,
        };
        InactiveSleepTime = TimeSpan.Zero;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _sender = new ClientPacketSender(_transport);
        // Lazy locale getter — read at packet-send time so a pre-session language change on the
        // login screen is reflected on the next outgoing Login/NewAccount/etc.
        _sender.SetLocaleProvider(() => _language);
        // Cached inside MachineKey after the first call, so the OS is read (and on macOS ioreg spawned)
        // once, on the first login attempt, rather than during startup.
        _sender.SetMachineKeyProvider(MachineKey.Compute);
        _handler = new ClientPacketHandler(_state, _sender, new DiskMapCache(AppPaths.Cache("maps")));
        _menu = new MenuLogic(_handler);
        _music = new OggMusicPlayer();
        _instance = this;
        // _audioAvailable is probed lazily on first use in Initialize()
    }
}
