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

/// <summary>
/// Active while the player is in the game world.
/// Owns all in-game panels; drives the core processors each frame.
/// </summary>
public sealed partial class GameplayScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly HudPanel _hud = new();
    private readonly PartyOverlayPanel _partyOverlay = new();
    private readonly ContestHudPanel _contestHud = new();
    private const float TickMs = 100f;   // action-send gate; ~10 ticks/s
    private float _tickAccMs;

    // ── In-world overlay constants (HUD bars, target arrows, floating text) ───
    // Per-vital row height (px) inside the small bar stack above a sprite.
    private const int InWorldBarH = 3;
    // Target-arrow glyph dimensions and gap above/below the name line.
    private const int TargetArrowW = 9;
    private const int TargetArrowH = 5;
    private const int TargetArrowGap = 2;
    // Target arrow hover: sin period (ms) and amplitude (px) for the gentle bob.
    private const double TargetArrowHoverPeriodMs = 350.0;
    private const float TargetArrowHoverAmplitude = 2.5f;
    // Floating combat-text vertical drift spd (px/sec).
    private const float FloatingTextDriftSpeed = 25f;
    // Vertical gap between a floating-text anchor and its source sprite: above the sprite top for normal
    // text, or below the sprite bottom when the label is flipped under (see SpawnFloatingTextAtEntity).
    // Also used to recover the sprite's screen position for the on-screen visibility gate at render time.
    private const float FloatTextGapAbove = 32f;
    private const float FloatTextGapBelow = 30f;

    // Allocation-free version of `_zOrder.Exists(i => PanelIsOpen(i) && PanelContainsMouse(i, p))`
    // — the lambda captured `p` and allocated a closure every Update tick.
    private bool MouseOverAnyOpenPanel(Point p)
    {
        for (int i = 0; i < _zOrder.Count; i++)
        {
            int panel = _zOrder[i];
            if (PanelIsOpen(panel) && PanelContainsMouse(panel, p)) return true;
        }
        return false;
    }

    // True when point p sits over any open panel, the chat window, or the chat-options popup — the
    // "floating UI" that intercepts the pointer before the world. Checked for BOTH the release position
    // and the press-origin so a click that began over floating UI can't leak through to the world.
    private bool MouseOverFloatingAt(Point p)
        => _chat.ContainsMouse(p) || MouseOverAnyOpenPanel(p) || _chatOptions.ContainsMouse(p);

    // Pickup is edge-triggered so one press = one item, but action input is tick-gated:
    // if the press happens on a non-tick frame the edge is gone before the snapshot fires.
    // Latch the press across frames and consume it on the next tick.
    private bool _pickUpLatched;
    // The attack key's PRESS edge needs the same latch. Its held-state survives to the next tick on its own,
    // but the edge does not — and the edge is the only thing that fires the talk-first NPC interact (shop /
    // quest / conversation). At 100 ms ticks and 60 fps only ~1 press in 6 landed on a tick frame, so opening
    // a keeper's menu took several taps. Latched here and consumed on the next tick, exactly like pickup.
    private bool _attackPressLatched;
    // Press-order for the movement keys, so the most-recently-pressed still-held direction wins
    // and releasing it falls back to whatever is still held (an input stack, not a fixed priority).
    private readonly MovementInputStack _moveStack = new();
    // Same press-order behavior for the Ctrl+WASD face-only channel — a separate stack because the
    // movement one sees Ctrl-gated inputs (Ctrl+W faces north, it doesn't step north).
    private readonly MovementInputStack _faceStack = new();
    private readonly ChatPanel _chat;
    private readonly ChatOptionsPanel _chatOptions = new();
    private readonly InventoryPanel _inv = new();
    private readonly SpellPanel _spells = new();
    private readonly TrainingPanel _training = new();
    private readonly ShopPanel _shop = new();
    private readonly BankPanel _bank = new();
    private readonly InnPanel _inn = new();
    private readonly MarketPanel _market = new();
    private readonly TradePanel _trade = new();
    private readonly StatsPanel _stats = new();
    private readonly HelpPanel _help = new();
    private readonly ControlsPanel _controls;
    private readonly MailPanel _mail = new();
    private readonly SocialPanel _social = new();
    private readonly QuestLogPanel _questLog = new();
    private readonly QuestDialogPanel _questDialog = new();
    private readonly ConversationPanel _conversation = new();
    private readonly DeathPanel _death = new();   // uncloseable death overlay
    private readonly ModerationPanel _moderation = new();   // Creator only; gated in the panel and again on the server
    private bool _wasDead;                          // alive→dead edge, to close open panels once on death
    private bool _marketWasOpen;                    // market open→closed edge, to tell the server to stop live broadcasts
    private readonly ContextMenu _contextMenu = new();

    // Panel slot indices, used throughout this file as z-order entries and registry indices.
    // Aliases for PanelSlots, which is the single source of truth shared with the policy table —
    // aliased rather than referenced directly so the ~83 call sites here stay short.
    private const int PanelInventory = PanelSlots.Inventory;
    private const int PanelSpells = PanelSlots.Spells;
    private const int PanelTraining = PanelSlots.Training;
    private const int PanelShop = PanelSlots.Shop;
    private const int PanelOptions = PanelSlots.Options;
    private const int PanelStats = PanelSlots.Stats;
    private const int PanelHelp = PanelSlots.Help;
    private const int PanelControls = PanelSlots.Controls;
    private const int PanelBank = PanelSlots.Bank;
    private const int PanelInn = PanelSlots.Inn;
    private const int PanelMail = PanelSlots.Mail;
    private const int PanelSocial = PanelSlots.Social;
    private const int PanelMarket = PanelSlots.Market;
    private const int PanelTrade = PanelSlots.Trade;
    private const int PanelQuestLog = PanelSlots.QuestLog;
    private const int PanelQuestDialog = PanelSlots.QuestDialog;
    private const int PanelConversation = PanelSlots.Conversation;
    private const int PanelModeration = PanelSlots.Moderation;

    // ── Panel registry ────────────────────────────────────────────────────────
    //
    // One row per floating panel, indexed by the Panel* slot consts above. It carries only what needs
    // the LIVE panel and the frame:
    //
    //   Update / Draw   which slice of the frame that panel's methods take
    //   Close           what dismissing it means (most Toggle, shop + conversation Close)
    //   Toggle          null for the server-driven panels, which have no player toggle entry point
    //   Capturing       null when the panel has no modal sub-surface that owns the keyboard
    //
    // The panel's POLICY — config key, movement lock, close-on-leave, escape participation — is not
    // here. It needs none of the frame, so it lives in PanelPolicies where a headless test can read
    // it, and this record exposes it through Policy rather than restating it. That split is what makes
    // the quirks in that table assertable (see PanelPolicyTests).
    //
    // Before the registry, these facts were spread across twelve separate switches and boolean chains
    // in this file; missing one when adding a panel failed silently rather than at build time.
    private sealed record PanelSlot(
        int Slot,
        IGamePanel Panel,
        Action<InputState, bool> Update,
        Action<SpriteBatch, SpriteFont, long, bool, bool> Draw,
        Action Close,
        Action? Toggle = null,
        Func<bool>? Capturing = null)
    {
        public PanelPolicy Policy => PanelPolicies.BySlot[Slot];
    }

    private PanelSlot[] _panels = [];

    // Built once in the constructor, after every panel field is initialized.
    private void BuildPanelRegistry()
    {
        var options = _ctx.OptionsPanel;
        _panels = new PanelSlot[PanelSlots.Count];
        _isPanelOpen = slot => _panels[slot].Panel.IsOpen;   // allocated once; see the field's note

        // A potion spends the global beat, so the panel's Use button follows the same clock the action
        // bar's sweep does.
        _inv.CanUsePotion = () => PotionReady(Environment.TickCount64);

        _panels[PanelInventory] = new(PanelInventory, _inv,
            (input, active) => _inv.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, now, active, hover) => _inv.Draw(sb, font, _ctx.State, now, _items, active, hover),
            () => _inv.Toggle(), () => _inv.Toggle(), () => _inv.IsCapturingInput);

        _panels[PanelSpells] = new(PanelSpells, _spells,
            (input, active) => _spells.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, hover) => _spells.Draw(sb, font, _ctx.State, active, hover),
            () => _spells.Toggle(), () => _spells.Toggle(), () => _spells.IsCapturingInput);

        _panels[PanelTraining] = new(PanelTraining, _training,
            (input, _) => _training.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, _) => _training.Draw(sb, font, _ctx.State, active),
            () => _training.Toggle(), () => _training.Toggle());

        _panels[PanelShop] = new(PanelShop, _shop,
            (input, _) => _shop.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, hover) => _shop.Draw(sb, font, _ctx.State, _items, active, hover),
            () => _shop.Close(), Capturing: () => _shop.IsCapturingInput);

        _panels[PanelOptions] = new(PanelOptions, options,
            (input, _) => UpdateOptionsPanel(input),
            (sb, font, _, active, _) => options.Draw(sb, font, _lastInput, active),
            () => options.Toggle(), () => options.Toggle());

        _panels[PanelStats] = new(PanelStats, _stats,
            (input, _) => _stats.Update(input, _ctx.State),
            (sb, font, _, active, _) => _stats.Draw(sb, font, _ctx.State, _lastInput, _sprites,
                                                    _hud.DispHp, _hud.DispMp, _hud.DispSp, _hud.DispExp, active),
            () => _stats.Toggle(), () => _stats.Toggle());

        _panels[PanelHelp] = new(PanelHelp, _help,
            (input, active) => _help.Update(input, active),
            (sb, font, now, active, _) => _help.Draw(sb, font, now, active),
            () => _help.Toggle(), () => _help.Toggle());

        _panels[PanelControls] = new(PanelControls, _controls,
            (input, active) => _controls.Update(input, active, _lastInput.IsGamePadConnected, _lastInput.IsPlayStationController),
            (sb, font, now, active, _) => _controls.Draw(sb, font, now, _lastInput.IsGamePadConnected,
                                                         _lastInput.IsPlayStationController, active),
            () => _controls.Toggle(), () => _controls.Toggle());

        _panels[PanelBank] = new(PanelBank, _bank,
            (input, _) => _bank.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, hover) => _bank.Draw(sb, font, _ctx.State, _items, active, hover),
            () => _bank.Toggle(), () => _bank.Toggle(), () => _bank.IsCapturingInput);

        _panels[PanelInn] = new(PanelInn, _inn,
            (input, _) => _inn.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, _) => _inn.Draw(sb, font, _ctx.State, active),
            () => _inn.Toggle(), () => _inn.Toggle());

        _panels[PanelMail] = new(PanelMail, _mail,
            (input, active) => _mail.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, _) => _mail.Draw(sb, font, _ctx.State, _items, active),
            () => _mail.Toggle(), () => _mail.Toggle(), () => _mail.IsCapturingInput);

        _panels[PanelSocial] = new(PanelSocial, _social,
            (input, active) => _social.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, _) => _social.Draw(sb, font, _ctx.State, active),
            () => _social.Toggle(), () => _social.Toggle(), () => _social.IsCapturingInput);

        _panels[PanelMarket] = new(PanelMarket, _market,
            (input, active) => _market.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, _) => _market.Draw(sb, font, _ctx.State, _items, active),
            () => _market.Toggle(), () => _market.Toggle(), () => _market.IsCapturingInput);

        _panels[PanelTrade] = new(PanelTrade, _trade,
            (input, active) => _trade.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, hover) => _trade.Draw(sb, font, _ctx.State, _items, active, hover),
            () => _trade.Close(), Capturing: () => _trade.IsCapturingInput);

        _panels[PanelQuestLog] = new(PanelQuestLog, _questLog,
            (input, active) => _questLog.Update(input, _ctx.State, _ctx.Sender, active),
            (sb, font, _, active, _) => _questLog.Draw(sb, font, _ctx.State, active),
            () => _questLog.Toggle(), () => _questLog.Toggle());

        _panels[PanelQuestDialog] = new(PanelQuestDialog, _questDialog,
            (input, _) => _questDialog.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, _) => _questDialog.Draw(sb, font, _ctx.State, active),
            () => _questDialog.Toggle());

        _panels[PanelConversation] = new(PanelConversation, _conversation,
            (input, _) => _conversation.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, _) => _conversation.Draw(sb, font, _ctx.State, active),
            () => _conversation.Close());

        // Toggling asks the server for a fresh report as it opens, so the panel is never up with nothing
        // in it — see ModerationPanel.Open.
        _panels[PanelModeration] = new(PanelModeration, _moderation,
            (input, _) => _moderation.Update(input, _ctx.State, _ctx.Sender),
            (sb, font, _, active, _) => _moderation.Draw(sb, font, _ctx.State, active),
            () => _moderation.Close(), () => _moderation.Toggle(_ctx.Sender));
    }

    // Reusable "is slot N open?" predicate for the PanelPolicies queries. Allocated ONCE alongside the
    // registry, not per call: AnyPanelBlocksMovement runs every frame in the movement gate, and a
    // lambda (or a method group) capturing `this` at each call site would put a closure allocation there.
    private Func<int, bool> _isPanelOpen = _ => false;

    // Whether any open panel locks world movement. Membership is the BlocksMovement flag in
    // PanelPolicies: the shop/bank/inn/market/trade/mail/training counters PLUS the quest log/dialog
    // and the conversation panel (user: quest + conversation panels lock movement).
    private bool AnyPanelBlocksMovement => PanelPolicies.AnyBlocksMovement(_isPanelOpen);

    // Z-order for floating panels: index 0 = bottom, last index = topmost.
    private readonly List<int> _zOrder = new()
    {
        PanelInventory, PanelSpells, PanelTraining, PanelShop,
        PanelOptions, PanelStats, PanelHelp, PanelControls, PanelBank, PanelInn, PanelMail, PanelSocial, PanelMarket, PanelTrade,
        PanelQuestLog, PanelQuestDialog, PanelConversation
    };

    // Keyboard focus tracking. _panelFocused is set when a panel is clicked or opened
    // via keyboard; cleared when the user clicks outside all panels. _activePanel is
    // the topmost open panel index while focused, or -1 when no panel has focus.
    private bool _panelFocused;
    private int _activePanel = -1;

    private AccountConfig? _config;

    private readonly Texture2D?[] _tilesets;
    private readonly IReadOnlyList<Texture2D?> _sprites;
    private readonly IReadOnlyList<Texture2D?> _items;
    private readonly SpriteFont? _gameFont;
    private readonly SpriteFont? _bubbleFont;

    // ── In-game toggle state ──────────────────────────────────────────────────
    public bool AlwaysShowBars { get; set; } = true;
    private TargetRef _tabTarget;
    private bool _skipPlayersWithTabTarget = true;
    private bool _showNpcNames = true;
    // Blood-pool decals + hit droplet burst.  Own toggle, independent of the damage-numbers toggle.
    private bool _showBlood = true;
    /// <summary>Whether blood pools + the hit droplet burst render (its own option, not the damage-numbers toggle).</summary>
    public bool ShowBlood => _showBlood;
    private bool _showOtherPlayerNames = true;
    private bool _showPlayerName = true;
    private bool _showCooldownBar = true;
    private bool _showOtherCooldownBars = false;
    // /debug — local-only Mapper+ toggle for the occupied-cell outline overlay.
    private bool _debugOverlay;

    // ── Reusable render frame — cleared and refilled each Draw call ───────────
    private readonly RenderFrame _renderFrame = new();

    // ── Seamless-scroll camera (updated once per frame in Update) ─────────────
    private readonly Camera _camera = new();
    // Camera velocity (px/s), tracked per frame for the weather droplet-angle + gentle vertical stabilization.
    private float _prevCamX, _prevCamY, _camVelX, _camVelY;
    // Camera velocity feeds weather stabilization + streak angles. A seam-cross re-anchors the camera by a whole
    // observable-block (hundreds of px in one frame); clamp well above real walk/run pan so that jump can't spike
    // the weather. ShiftParticles also rebases _prevCam on the cross, so normally the clamp never even engages.
    private const float MaxCamVelPxSec = 500f;

    // ── Particle FX (weather + combat) ────────────────────────────────────────
    private readonly ParticleSystem _particles = new();
    // Tracks the current map's Indoors flag between frames so we can detect the Outdoor→Indoor edge and
    // instantly clear lingering weather when stepping inside (distinct from a weather-state change, which
    // stops spawning and lets existing particles animate out). Default false: spawning onto an indoor map
    // fires a harmless no-op clear on the first frame.
    private bool _prevIndoors;
    private Texture2D? _particleDotTex;   // soft premultiplied radial dot, generated lazily; streaks stretch it
    private Texture2D? _particlePixelTex; // 1x1 white, for the item-cube box + drop shadow
    private Texture2D? _swooshTex;        // procedural crescent blade-arc for the melee swoosh

    // Spell hit-timing deferral: a cast's projectile takes ~flight time to land, but the server resolves
    // damage/death instantly. Hold the target's damage/heal number until the bolt would land (time-based)
    // so it reads in sync with the visible projectile. The dying sprite is held the same way (_delayedDeaths).
    private readonly List<PendingHit> _pendingHits = new();
    private readonly List<DeferredFloat> _deferredFloats = new();
    private readonly List<DelayedDeath> _delayedDeaths = new();
    private const long PendingHitGraceMs = 500;   // keep a pending hit this long past its release for late packets
    private const long ClaimReuseWindowMs = 50;   // window for a death to reuse its number's just-claimed bolt
    // The last in-flight hit a damage NUMBER claimed — a same-hit death reuses it (they're one hit) instead of
    // consuming a second bolt. Lets N bolts on one target stagger their numbers/deaths (FIFO) rather than bunch.
    private TargetRef _lastClaimTarget;
    private long _lastClaimRelease;
    private long _lastClaimTick;
    private bool _lastClaimConsumed;

    private struct PendingHit
    {
        public TargetRef Target;
        public long ReleaseMs;
        public bool Claimed;
    }
    private struct DeferredFloat
    {
        public float WorldX, WorldY;
        public string? Text;
        public Color Color;
        public long ReleaseMs;
        public float BloodIntensity;
        public WorldLayer Layer;
    }
    private struct DelayedDeath
    {
        public float WorldX, WorldY;
        public int SpriteRow;
        public Direction Dir;
        public long ReleaseMs;
        public int Size;
    }

    // ── Floating combat text ──────────────────────────────────────────────────
    private readonly List<FloatingText> _floatingTexts = new();

    // Mutable struct (not a record) so the per-frame Age update happens in-place via
    // CollectionsMarshal.AsSpan(_floatingTexts)[i].Age += dtSec rather than allocating a
    // new record. Hot path: every visible damage/heal float updates every frame.
    private struct FloatingText
    {
        public float X;
        public float Y;
        public string Text;
        public Color Color;
        public float Age;
        // Signed screen-px nudge so floats that pop in the SAME frame over one spot fan into a readable
        // column instead of overlapping: +y (down) for normal text, -y (up) when flipped below the sprite.
        public float StackOffset;
        public bool FloatDown;
        public const float MaxAge = 1.5f;
        public FloatingText(float x, float y, string text, Color color, float stackOffset = 0f, bool floatDown = false)
        {
            X = x;
            Y = y;
            Text = text;
            Color = color;
            Age = 0f;
            StackOffset = stackOffset;
            FloatDown = floatDown;
        }
    }

    // Bar and sidebar colors live in UiHelper.

    public GameplayScreen(ShellContext ctx, Texture2D?[] tilesets, IReadOnlyList<Texture2D?> sprites, IReadOnlyList<Texture2D?> items, SpriteFont? gameFont = null, SpriteFont? bubbleFont = null)
    {
        _ctx = ctx;
        _tilesets = tilesets;
        _sprites = sprites;
        _items = items;
        _gameFont = gameFont;
        _bubbleFont = bubbleFont;

        _controls = new ControlsPanel(_ctx.Graphics);
        PrewarmReachTextures(_ctx.Graphics);

        // Every panel field is initialized by now (the rest are field initializers; _controls is
        // above), so the registry can capture them.
        BuildPanelRegistry();

        _chat = new ChatPanel(0, Camera.ViewH, Camera.ViewW, UiHelper.RefH - Camera.ViewH);
        _chat.OnToggleInventory = () => ActivatePanel(PanelInventory);
        _chat.OnToggleTraining = () => ActivatePanel(PanelTraining);
        _chat.OnToggleStats = () => ActivatePanel(PanelStats);
        _chat.OnToggleHelp = () => ActivateHelpPanel();
        _chat.OnActiveChannelChanged = SaveCharPrefs;
        _help.OnToggleControls = () => ActivatePanel(PanelControls);
        _chat.OnToggleDebug = () =>
        {
            _debugOverlay = !_debugOverlay;
            _chat.AddLine(_debugOverlay ? ClientStrings.Get(ClientStrings.GameplayScreen_DebugOverlayOn) : ClientStrings.Get(ClientStrings.GameplayScreen_DebugOverlayOff), GameColor.Pink);
        };
        _chat.OnToggleModeration = () => ActivatePanel(PanelModeration);
        _chat.OnPlayerRightClicked = (name, at) => OpenPlayerContextMenu(name, at);
        _chat.OnTabRightClicked = (tabIndex, _) =>
            _chatOptions.Open(_chat, tabIndex, _ctx.State.Me.Access > AdminLevel.Player, _ctx.State.GuildInfo?.InGuild ?? false);
    }
}
