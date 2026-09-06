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

/// <summary>Overhead chat bubbles: their lifetime tick and the wrapped, tail-anchored drawing.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // What a truncated bubble ends with. Three periods rather than U+2026, because the SpriteFonts
    // cover ASCII plus Latin-1 and MeasureString THROWS on anything else.
    private const string Ellipsis = "...";

    // ── Chat bubble tick + draw ───────────────────────────────────────────────

    /// <summary>Demote naturally-expired head bubbles into the drifter list, and remove drifters that
    /// have rotated past their float window. Iterates players + center/neighbor NPC arrays — cheap
    /// because active bubbles are infrequent and the per-entity slot check is a null/long comparison.</summary>
    private static void TickChatBubbles(ClientState state)
    {
        long now = Environment.TickCount64;

        for (int i = 1; i <= state.PlayerSlots; i++)
        {
            var p = state.Players[i];
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (p.ChatBubbleText != null && now >= p.ChatBubbleEndMs)
                ChatBubbleManager.NaturallyExpire(p, now);
            if (p.ChatBubbleDrifters is { Count: > 0 } pd)
            {
                while (pd.Count > 0 && now - pd[0].DemotedMs >= ChatBubbleStyle.FloatMs)
                    pd.RemoveAt(0);
            }
        }

        TickNpcArrayBubbles(state.MapNpcs, now);
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1))
                    TickNpcArrayBubbles(state.NeighborNpcs[c, r], now);
            }
        }
        // Traversal (guest) NPCs live in a separate dict outside the cell arrays — without this
        // their head bubbles would never demote to drifters and would just blink off at EndMs.
        foreach (var t in state.TraversalNpcs.Values)
            TickOneNpcBubble(t, now);
    }

    private static void TickOneNpcBubble(ClientMapNpc n, long now)
    {
        if (n.ChatBubbleText == null && (n.ChatBubbleDrifters?.Count ?? 0) == 0) return;
        if (n.ChatBubbleText != null && now >= n.ChatBubbleEndMs)
            ChatBubbleManager.NaturallyExpire(n, now);
        if (n.ChatBubbleDrifters is { Count: > 0 } nd)
        {
            while (nd.Count > 0 && now - nd[0].DemotedMs >= ChatBubbleStyle.FloatMs)
                nd.RemoveAt(0);
        }
    }

    private static void TickNpcArrayBubbles(ClientMapNpc[] arr, long now)
    {
        // Process bubble state regardless of Num — a freshly-killed NPC keeps its preserved
        // "last words" drifters until the float window elapses (see HandleNpcDead).
        for (int i = 1; i < arr.Length; i++)
            TickOneNpcBubble(arr[i], now);
    }

    /// <summary>Draw a stack of chat bubbles for the current frame. Each bubble: word-wrap to N lines,
    /// shadow → rounded background → colored border → white text, all multiplied by Alpha.</summary>
    private void DrawChatBubbles(SpriteBatch sb, SpriteFont font, List<ChatBubbleDrawCmd> bubbles)
    {
        float lineH = font.LineSpacing;
        Color bgBase = new(20, 20, 40, 220);
        Color shadowBase = new(0, 0, 0, 120);

        foreach (var b in bubbles)
        {
            if (b.Alpha <= 0f) continue;
            var lines = WrapBubbleText(font, b.Text, ChatBubbleStyle.MaxWidthPx, ChatBubbleStyle.MaxLines);
            if (lines.Count == 0) continue;

            float maxW = 0f;
            for (int li = 0; li < lines.Count; li++)
            {
                float w = font.MeasureString(lines[li]).X;
                if (w > maxW) maxW = w;
            }
            int panelW = (int)Math.Ceiling(maxW) + ChatBubbleStyle.PadX * 2;
            int panelH = (int)Math.Ceiling(lineH * lines.Count) + ChatBubbleStyle.PadY * 2;
            int panelX = (int)Math.Round(b.CenterX - panelW / 2f);
            // Edge clamp: nudge the panel inward so the whole bubble stays inside the world viewport
            // (the panel plus its shadow). Bubbles wider than the viewport pin to the left edge.
            int maxPanelX = Camera.ViewW - panelW - ChatBubbleStyle.ShadowOffset;
            if (maxPanelX < 0) maxPanelX = 0;
            panelX = Math.Clamp(panelX, 0, maxPanelX);
            // AnchorY is the panel BOTTOM by default, or the panel TOP when AnchorBelow=true
            // (used when the entity's name was flipped below its sprite — see RenderCommandBuilder).
            int panelY = b.AnchorBelow
                ? (int)Math.Round(b.AnchorY)
                : (int)Math.Round(b.AnchorY) - panelH;
            var rect = new Rectangle(panelX, panelY, panelW, panelH);

            // Shadow first (offset down/right), then panel, then border, then text — all alpha-tinted.
            var shadow = new Rectangle(rect.X + ChatBubbleStyle.ShadowOffset, rect.Y + ChatBubbleStyle.ShadowOffset, rect.Width, rect.Height);
            UiHelper.DrawRoundedFilledRect(sb, shadow, ChatBubbleStyle.CornerRadius, shadowBase * b.Alpha);
            UiHelper.DrawRoundedFilledRect(sb, rect, ChatBubbleStyle.CornerRadius, bgBase * b.Alpha);
            UiHelper.DrawRoundedBorder(sb, rect, ChatBubbleStyle.CornerRadius, ChatPanel.GetColor(b.BorderColorIndex) * b.Alpha);

            // Lines centered horizontally inside the panel.
            float ty = rect.Y + ChatBubbleStyle.PadY;
            for (int li = 0; li < lines.Count; li++)
            {
                float lw = font.MeasureString(lines[li]).X;
                var tp = new Vector2(rect.X + (rect.Width - lw) / 2f, ty);
                sb.DrawString(font, lines[li], tp, Color.White * b.Alpha);
                ty += lineH;
            }
        }
    }

    private static readonly List<string> _wrapScratch = new();
    private static readonly StringBuilder _wrapSb = new();
    /// <summary>Word-wrap into at most <paramref name="maxLines"/> lines at <paramref name="maxWidthPx"/>;
    /// last-line overflow gets an ellipsis. Words longer than the wrap width are hard-broken character
    /// by character so they don't escape the bubble. Uses static scratch buffers so steady-state
    /// rendering allocates nothing beyond the per-line strings.</summary>
    private static List<string> WrapBubbleText(SpriteFont font, string text, int maxWidthPx, int maxLines)
    {
        _wrapScratch.Clear();
        _wrapSb.Clear();
        if (string.IsNullOrEmpty(text)) return _wrapScratch;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool truncated = false;

        void PushLine()
        {
            _wrapScratch.Add(_wrapSb.ToString());
            _wrapSb.Clear();
        }

        for (int wi = 0; wi < words.Length; wi++)
        {
            if (_wrapScratch.Count >= maxLines)
            {
                truncated = true;
                break;
            }
            string w = words[wi];

            // Try to append word (with space if line non-empty) to the current line.
            int savedLen = _wrapSb.Length;
            if (_wrapSb.Length > 0) _wrapSb.Append(' ');
            _wrapSb.Append(w);
            if (font.MeasureString(_wrapSb).X <= maxWidthPx)
                continue;

            // Doesn't fit — roll back and push the existing line first.
            _wrapSb.Length = savedLen;
            if (_wrapSb.Length > 0)
            {
                PushLine();
                if (_wrapScratch.Count >= maxLines)
                {
                    truncated = true;
                    break;
                }
            }

            // Word alone exceeds wrap width — hard-break by character.
            if (font.MeasureString(w).X > maxWidthPx)
            {
                for (int ci = 0; ci < w.Length; ci++)
                {
                    _wrapSb.Append(w[ci]);
                    if (font.MeasureString(_wrapSb).X > maxWidthPx)
                    {
                        _wrapSb.Length--; // back out the last char
                        PushLine();
                        if (_wrapScratch.Count >= maxLines)
                        {
                            truncated = true;
                            break;
                        }
                        _wrapSb.Append(w[ci]);
                    }
                }
                if (truncated) break;
            }
            else
            {
                _wrapSb.Append(w);
            }
        }
        if (_wrapSb.Length > 0 && _wrapScratch.Count < maxLines)
            PushLine();
        else if (_wrapSb.Length > 0)
            truncated = true;

        if (truncated && _wrapScratch.Count > 0)
        {
            string last = _wrapScratch[^1];
            // Make room for the ellipsis by trimming the tail until the new line fits the wrap width.
            // Three periods, not U+2026: the SpriteFonts stop at Latin-1 plus the French ligature.
            while (last.Length > 0 && font.MeasureString(last + Ellipsis).X > maxWidthPx)
                last = last[..^1];
            _wrapScratch[^1] = last + Ellipsis;
        }
        return _wrapScratch;
    }

    public void AddChatLine(string text, int colorIndex) => _chat.AddLine(text, colorIndex);
    public void AddChatLine(ChatMsgPacket pkt) => _chat.AddLine(pkt);
    public void OpenShop()
    {
        _shop.Open();
        BringToFront(PanelShop);
    }
    public void OpenInnPanel()
    {
        _inn.Open();
        BringToFront(PanelInn);
    }
    public void SyncPreparedSpell(int slot) => _spells.SetPreparedSlot(slot);
    public void SetTabTarget(TargetRef t) => _tabTarget = t;

    /// <summary>Fire one action-bar slot. The binding names an item or spell by NUMBER, so this resolves
    /// it to a live inventory/spellbook slot at the moment of use — the bar keeps working across a bag
    /// that reorders itself under it.
    /// <para>Returns whether anything was actually sent, which is what starts the shared cooldown: a press
    /// on an empty or unusable slot should not eat the beat.</para></summary>
    // ── The two clocks ────────────────────────────────────────────────────────
    // Attacking and casting share one beat; drinking runs on its own, slower one. Heavy Wind doubles
    // both, exactly as the server doubles them. AttackTimer is the same field the head cooldown bar
    // reads, so the bar over the character and a spell slot's sweep always agree.

    private long WindMult =>
        _ctx.State.Weather == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;

    internal long ActionCooldownMs => Constants.PlayerAttackCooldownMs * WindMult;
    private long PotionCooldownMs => Constants.PotionCooldownMs * WindMult;

    /// <summary>Whether a potion may be drunk. Only potions wait on the drinking clock — the action
    /// beat has nothing to do with reaching into a bag.</summary>
    private bool PotionReady(long nowMs) =>
        _ctx.State.Me is not { } me || nowMs - me.PotionTimer >= PotionCooldownMs;

    /// <summary>Whether a bar slot may fire, asking the clock its contents answer to. A spell waits on
    /// the action beat, a potion on the drinking clock, and anything else on neither. The server decides
    /// either way; this only keeps a slot from looking dead while its own clock runs.</summary>
    private bool HotkeySlotReady(int slot, long nowMs)
    {
        var me = _ctx.State.Me;
        if (me?.Hotkeys is null || slot < 1 || slot >= me.Hotkeys.Length) return true;

        var hk = me.Hotkeys[slot];
        if (hk.Kind == HotkeyKind.Spell) return nowMs - me.AttackTimer >= ActionCooldownMs;
        return !IsPotion(hk.Num) || PotionReady(nowMs);
    }

    /// <summary>Charges the clock the slot's contents answer to. A spell's beat is stamped by the cast
    /// itself, so only drinking is recorded here.
    ///
    /// <para>A sip the server will refuse — a full bar, a vital with nothing left to give — starts no
    /// clock, because the server does not start its own either: it stamps only when the potion was
    /// actually spent. Stamping here regardless would gray the slot for two seconds over a drink that
    /// never happened, and block the next press while the server was still willing.</para></summary>
    private void StartHotkeyCooldown(int slot, long nowMs)
    {
        var me = _ctx.State.Me;
        if (me?.Hotkeys is null || slot < 1 || slot >= me.Hotkeys.Length) return;
        int itemNum = me.Hotkeys[slot].Num;
        if (IsPotion(itemNum) && PotionWouldDoSomething(itemNum)) me.PotionTimer = nowMs;
    }

    /// <summary>Whether drinking this would change anything, read through the same shared rule the server
    /// applies — see <see cref="StatFormulas.PotionWouldDoSomething"/>.</summary>
    private bool PotionWouldDoSomething(int itemNum)
    {
        if (_ctx.State.Me is not { } me) return false;
        var item = itemNum > 0 && itemNum < _ctx.State.Items.Length ? _ctx.State.Items[itemNum] : null;
        return item is not null && StatFormulas.PotionWouldDoSomething(
            item.Type, item.VitalAmount, me.Hp, me.MaxHp, me.Mp, me.MaxMp, me.Sp, me.MaxSp);
    }

    /// <summary>How much of the clock a bound slot answers to is still to run, 1→0. A potion reads the
    /// drinking clock, a spell the action beat, and anything else is never waiting on either.</summary>
    private float HotkeyCooldownFraction(PlayerHotkey hk, long nowMs)
    {
        if (_ctx.State.Me is not { } me) return 0f;

        long stamped, span;
        if (hk.Kind == HotkeyKind.Spell) (stamped, span) = (me.AttackTimer, ActionCooldownMs);
        else if (IsPotion(hk.Num)) (stamped, span) = (me.PotionTimer, PotionCooldownMs);
        else return 0f;

        long elapsed = nowMs - stamped;
        return stamped > 0 && elapsed < span ? 1f - elapsed / (float)span : 0f;
    }

    private bool IsPotion(int itemNum) =>
        itemNum > 0 && itemNum < _ctx.State.Items.Length
        && _ctx.State.Items[itemNum]?.Type is ItemType.PotionAddHp or ItemType.PotionAddMp
            or ItemType.PotionAddSp or ItemType.PotionSubHp or ItemType.PotionSubMp or ItemType.PotionSubSp;

    private bool TryUseHotkey(int slot)
    {
        var state = _ctx.State;
        var me = state.Me;
        if (me?.Hotkeys is null || slot < 1 || slot >= me.Hotkeys.Length) return false;

        var hk = me.Hotkeys[slot];
        if (!hk.IsBound)
        {
            AddChatLine(ClientStrings.Get(ClientStrings.HotkeyBar_NothingBound), GameColor.BrightRed);
            return false;
        }

        if (hk.Kind == HotkeyKind.Item)
        {
            int inv = HotkeyBarPanel.FindInvSlot(state, hk.Num);
            if (inv <= 0)
            {
                string name = (hk.Num < state.Items.Length ? state.Items[hk.Num]?.TrimmedName : null) ?? "?";
                AddChatLine(ClientStrings.Format(ClientStrings.HotkeyBar_ItemGone, ("Item", name)), GameColor.BrightRed);
                return false;
            }
            _ctx.Sender.SendUseItem(inv);
            return true;
        }

        int book = HotkeyBarPanel.FindSpellSlot(state, hk.Num);
        if (book <= 0)
        {
            string name = (hk.Num < state.SpellDefs.Length ? state.SpellDefs[hk.Num]?.TrimmedName : null) ?? "?";
            AddChatLine(ClientStrings.Format(ClientStrings.HotkeyBar_SpellGone, ("Spell", name)), GameColor.BrightRed);
            return false;
        }
        _ctx.Sender.SendCast(book, _lastInput.IsSelfTargetHeld());
        return true;
    }

    /// <summary>Bind or clear an action-bar slot, then let the server echo the whole bar back.</summary>
    public void AssignHotkey(int slot, HotkeyKind kind, int num) => _ctx.Sender.SendSetHotkey(slot, kind, num);
}
