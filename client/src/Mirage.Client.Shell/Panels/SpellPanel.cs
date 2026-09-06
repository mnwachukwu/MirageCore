using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Panels;

/// <summary>Spell panel: the player's known spells, with cast and forget actions.</summary>
public sealed class SpellPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 280, 300), minH: 120);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    private const string TooltipScope = "Spell";

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _stateDirty = true;
        }
        else
        {
            _confirmState = ConfirmState.None;
            Tooltip.CloseScope(TooltipScope);
        }
    }

    // True while the forget-confirmation overlay is showing; used by GameplayScreen
    // to suppress Escape-closes-panel so Escape cancels the forget instead.
    public bool IsCapturingInput => _confirmState != ConfirmState.None || _contextMenu.IsOpen;

    private readonly ListBox _list = new() { ShowTruncationTooltip = false };   // rows show the richer spell tooltip instead
    // Right-click a known spell to bind it to the action bar — the spellbook's half of the same submenu
    // the inventory offers, so both kinds of hotkey are assigned the same way.
    private readonly ContextMenu _contextMenu = new();
    private SpriteFont? _cachedFont;
    private readonly Button _castBtn = new();
    private readonly Button _prepareBtn = new();
    private readonly Button _forgetBtn = new();
    private readonly Button _forgetConfirmBtn = new();
    private readonly Button _forgetCancelBtn = new();
    private int _labelsGeneration = -1;
    private InputState _input = new();

    // 1-based slot index of the prepared spell; 0 = none.
    private int _preparedSlot;
    public void SetPreparedSlot(int slot)
    {
        _preparedSlot = slot;
        _stateDirty = true;
    }

    // Forget confirmation state. Inline rather than a ConfirmDialog: forgetting is a two-click
    // confirm on the button itself, so it needs no modal.
    private enum ConfirmState { None, Confirming }
    private ConfirmState _confirmState;
    private int _forgetSlot;

    private int _stateHash;
    private bool _stateDirty = true;

    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        if (!IsOpen) return;
        _input = input;

        // The menu runs first and claims every mouse event while open, so a click meant for it can't also
        // land on the row underneath — the same ordering InventoryPanel uses.
        if (_contextMenu.IsOpen && _cachedFont is not null)
        {
            _contextMenu.Update(input, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
            return;
        }

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            _confirmState = ConfirmState.None;
            _contextMenu.Close();
            Tooltip.CloseScope(TooltipScope);
            return;
        }

        var c = _panel.ContentBounds;

        if (_confirmState == ConfirmState.Confirming)
        {
            UpdateForgetConfirm(input, state, sender, c);
            return;
        }

        LayoutButtons(c);

        // Combat lock — matches the server gate so the player gets feedback before the round-trip.
        // 10s window mirrors MirageGame.cs quit/logout check.
        bool inCombat = state.Me is { } me0 && me0.LastCombatMs > 0
            && (Environment.TickCount64 - me0.LastCombatMs) < 10_000L;
        _forgetBtn.Enabled = !inCombat;
        // Preparing is for SubHp only — the prepared slot is the caster's weapon, cast with Q, while every
        // other spell type belongs to the action bar. Greying the button says so before the click rather
        // than after a silent server refusal.
        _prepareBtn.Enabled = _list.SelectedIndex >= 0 && IsSubHp(state, _list.SelectedIndex + 1);

        _list.Update(input, ListBoundsOf(c), keyboardActive: isActive);

        // Right-click a known, NON-SubHp spell → the assign submenu. SubHp is deliberately unbindable: it
        // has Q, and offering both would make "which key casts this" ambiguous.
        int rcRow = _list.ConsumeRightClickedRow(input);
        if (rcRow > 0 && _cachedFont is not null && !IsSubHp(state, rcRow))
        {
            int spellNum = state.Me?.Spell?[rcRow] ?? 0;
            if (spellNum > 0 && spellNum < state.SpellDefs.Length && state.SpellDefs[spellNum] is { } def)
            {
                _contextMenu.Open(input.MousePosition, def.TrimmedName,
                    HotkeyAssignMenu.BuildFor(state, sender, HotkeyKind.Spell, spellNum),
                    new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _cachedFont);
            }
        }

        // Toggle prepared status for the selected spell.
        if (_prepareBtn.IsClicked(input) && _list.SelectedIndex >= 0)
        {
            int slot = _list.SelectedIndex + 1;
            if ((state.Me?.Spell?[slot] ?? 0) > 0 && IsSubHp(state, slot))
            {
                _preparedSlot = (_preparedSlot == slot) ? 0 : slot;
                sender.SendSetPreparedSpell(_preparedSlot);
            }
        }

        if (_forgetBtn.IsClicked(input) && _list.SelectedIndex >= 0)
        {
            int slot = _list.SelectedIndex + 1;
            if ((state.Me?.Spell?[slot] ?? 0) > 0)
            {
                _forgetSlot = slot;
                _confirmState = ConfirmState.Confirming;
                return;
            }
        }

        bool cast = _castBtn.IsClicked(input);

        if (cast && _list.SelectedIndex >= 0 && state.Me is { } me)
        {
            if (Environment.TickCount64 - me.AttackTimer < 1000L)
                return;
            // Ctrl+click casts on the caster, mirroring Ctrl+Q.
            AttemptCast(sender, _list.SelectedIndex + 1, input.IsSelfTargetHeld());
        }
    }

    private void UpdateForgetConfirm(InputState input, ClientState state, ClientPacketSender sender, Rectangle c)
    {
        LayoutForgetButtons(c);

        if (_forgetConfirmBtn.IsClicked(input))
        {
            // Re-validate defensively in case server state shifted while the overlay was up.
            if ((state.Me?.Spell?[_forgetSlot] ?? 0) > 0)
                sender.SendForgetSpell(_forgetSlot);
            _confirmState = ConfirmState.None;
            return;
        }

        if (_forgetCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            _confirmState = ConfirmState.None;
        }
    }

    /// <summary>Cast the prepared spell via Q key. Silently does nothing if no spell is prepared.
    /// <paramref name="self"/> is set when Ctrl is held (Ctrl+Cast) to target the caster.</summary>
    /// <summary>Cast the prepared spell at the current target. NEVER at the caster: the prepared slot only
    /// ever holds a SubHp spell — the server refuses to prepare anything else — so aiming it inward is
    /// asking to be damaged. Self-casting belongs to the action bar and the book, which hold the rest.</summary>
    /// <summary>The cast beat — how often a prepared spell may go off, and how often the key may be
    /// re-tried while it is HELD.</summary>
    private const long CastBeatMs = 1000L;

    /// <summary>When the last attempt was sent, whatever became of it.
    ///
    /// <para>🔴 <see cref="PlayerRecord.AttackTimer"/> is stamped only when the server CONFIRMS a cast, so
    /// it does not pace a cast that gets refused — no target, no MP, out of range. On a rising edge that is
    /// fine and deliberate (a refusal must never lock out the retry), but a held key with only that gate
    /// would re-send every frame for as long as the refusal lasts.</para></summary>
    private long _lastAttemptMs;

    public void TryCastPrepared(ClientState state, ClientPacketSender sender)
    {
        if (_preparedSlot <= 0) return;
        if (state.Me is not { } me) return;

        long now = Environment.TickCount64;
        if (now - me.AttackTimer < CastBeatMs) return;   // the last cast the server accepted
        if (now - _lastAttemptMs < CastBeatMs) return;   // and the last one it did not

        _lastAttemptMs = now;
        AttemptCast(sender, _preparedSlot);
    }

    // AttackTimer is only stamped when the server confirms a cast via PlayerCastPacket, so
    // failed casts (no target, illegal target, not enough MP) never lock the user out of a
    // retry. Input is already rising-edge so we don't need a separate local spam guard.
    private static void AttemptCast(ClientPacketSender sender, int slot, bool self = false) => sender.SendCast(slot, self);

    public void Refresh(ClientState state)
    {
        _list.Items.Clear();

        var me = state.Me;

        // Clear prepared slot if the spell was removed from that slot.
        if (_preparedSlot > 0 && (me?.Spell?[_preparedSlot] ?? 0) == 0)
            _preparedSlot = 0;

        for (int i = 1; i <= Constants.MaxPlayerSpells; i++)
        {
            int spellId = me?.Spell?[i] ?? 0;
            if (spellId > 0 && spellId <= state.Limits.Spells)
            {
                var spell = state.SpellDefs[spellId];
                string name = spell?.Name.TrimEnd() ?? $"Spell {spellId}";
                bool prepared = _preparedSlot == i;
                _list.Items.Add(prepared
                    ? $"{i}: {name} {ClientStrings.Get(ClientStrings.Common_Prepared)}"
                    : $"{i}: {name}");
            }
            else
            {
                _list.Items.Add($"{i}: {ClientStrings.Get(ClientStrings.Common_Empty)}");
            }
        }
    }

    private int ComputeStateHash(ClientState state)
    {
        var me = state.Me;
        if (me is null) return 0;
        var h = new HashCode();
        h.Add(_preparedSlot);
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++)
            h.Add(me.Spell?[i] ?? 0);
        return h.ToHashCode();
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive = false, bool canHover = true)
    {
        if (!IsOpen) return;
        _cachedFont = font;   // the context menu needs a font in Update, which has none of its own
        int hash = ComputeStateHash(state);
        if (_stateDirty || hash != _stateHash)
        {
            _stateHash = hash;
            _stateDirty = false;
            Refresh(state);
        }

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _castBtn.Label = ClientStrings.Get(ClientStrings.SpellPanel_CastButton);
            _prepareBtn.Label = ClientStrings.Get(ClientStrings.SpellPanel_PrepareButton);
            _forgetBtn.Label = ClientStrings.Get(ClientStrings.SpellPanel_ForgetButton);
            _forgetConfirmBtn.Label = ClientStrings.Get(ClientStrings.SpellPanel_ForgetButton);
            _forgetCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.SpellPanel_Title), isActive);

        var c = _panel.ContentBounds;

        if (_confirmState == ConfirmState.Confirming)
        {
            DrawForgetConfirm(sb, font, state, c);
            _panel.DrawOverlay(sb);
            return;
        }

        LayoutButtons(c);

        _list.Draw(sb, font, ListBoundsOf(c));
        _castBtn.Draw(sb, font, _input);
        _prepareBtn.Draw(sb, font, _input);
        _forgetBtn.Draw(sb, font, _input,
            normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _panel.DrawOverlay(sb);
        _contextMenu.Draw(sb, font);

        if (canHover && !_contextMenu.IsOpen) NotifyHover(state);
    }

    /// <summary>Whether the spell in this 1-based book slot is a SubHp — the one type that is prepared and
    /// cast with Q rather than bound to the action bar. Mirrors the server's split in HandleSetPreparedSpell
    /// / HandleSetHotkey; an empty slot is not SubHp, so it neither prepares nor binds.</summary>
    private static bool IsSubHp(ClientState state, int bookSlot)
    {
        int spellNum = state.Me?.Spell?[bookSlot] ?? 0;
        return spellNum > 0 && spellNum < state.SpellDefs.Length
            && state.SpellDefs[spellNum]?.Type == SpellType.SubHp;
    }

    private void NotifyHover(ClientState state)
    {
        int hovered = _list.HoveredIndex;
        if (hovered < 0) return;
        int slot = hovered + 1;
        int spellId = state.Me?.Spell?[slot] ?? 0;
        if (spellId <= 0 || spellId > state.Limits.Spells) return;
        var spell = state.SpellDefs[spellId];
        if (spell is null) return;
        var key = (TooltipScope, slot, spellId);
        Tooltip.NotifyHoverSpell(TooltipScope, key, spell, state.Me, state.Classes, state.Items, state.Weather, _input.MousePosition);
    }

    private void DrawForgetConfirm(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle c)
    {
        int spellId = state.Me?.Spell?[_forgetSlot] ?? 0;
        string spellName = (spellId > 0 && spellId <= state.Limits.Spells)
            ? (state.SpellDefs[spellId]?.Name.TrimEnd() ?? $"Spell {spellId}")
            : "(unknown)";

        LayoutForgetButtons(c);

        var bgRect = new Rectangle(c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
        UiHelper.DrawFilledRect(sb, bgRect, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bgRect, UiHelper.ConfirmOverlayBorder);

        float textY = c.Y + 12;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SpellPanel_ForgetPrompt), new Vector2(c.X + 8, textY), Color.LightGray, c.Width - 16);
        textY += 20;
        UiHelper.DrawLabel(sb, font, spellName, new Vector2(c.X + 8, textY), Color.White, c.Width - 16);
        textY += 28;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SpellPanel_ForgetHint1), new Vector2(c.X + 8, textY), Color.Yellow, c.Width - 16);
        textY += 18;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SpellPanel_ForgetHint2), new Vector2(c.X + 8, textY), Color.Yellow, c.Width - 16);

        _forgetConfirmBtn.Draw(sb, font, _input,
            normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _forgetCancelBtn.Draw(sb, font, _input);
    }

    private void LayoutButtons(Rectangle c)
    {
        int thirdW = (c.Width - 16) / 3;
        int y = c.Bottom - 34;
        _castBtn.Bounds = new Rectangle(c.X + 4, y, thirdW, 26);
        _prepareBtn.Bounds = new Rectangle(c.X + 8 + thirdW, y, thirdW, 26);
        _forgetBtn.Bounds = new Rectangle(c.X + 12 + thirdW * 2, y, thirdW, 26);
    }

    private void LayoutForgetButtons(Rectangle c)
    {
        _forgetConfirmBtn.Bounds = UiHelper.PanelBottomButton(c, 0);
        _forgetCancelBtn.Bounds = UiHelper.PanelBottomButton(c, 1);
    }

    private static Rectangle ListBoundsOf(Rectangle c) =>
        new(c.X + 4, c.Y + 2, c.Width - 8, Math.Max(0, c.Height - 44));
}
