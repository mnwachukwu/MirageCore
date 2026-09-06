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

/// <summary>The per-frame tick: input gating, the panel and world update order, the camera, and the
/// <c>InputSnapshot</c> handed to the shared movement/action processors.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    public void Update(GameTime gameTime, InputState input)
    {
        _lastInput = input;
        float deltaMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
        // The client's one clock. Draw, the chat panel, the bubbles and the floating text all stamp
        // from Environment.TickCount64, and a deadline written here is read over there — the action
        // bar's cooldowns are charged in this method and compared in Draw.
        long nowMs = Environment.TickCount64;

        // Context menu runs FIRST so it can claim mouse clicks before any other panel sees them.
        // While open, every mouse-button event is consumed regardless of where it lands.
        if (_contextMenu.IsOpen && _gameFont != null)
        {
            _contextMenu.Update(input, new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _gameFont);
        }

        // Per-tab chat options panel (right-click on a tab opens it). Runs next so its clicks
        // are claimed before the chat panel below it sees them. Snapshot whether its rename field
        // owns the keyboard BEFORE Update so the commit frame (Enter/Escape blurs the field) is
        // still treated as captured — otherwise that Enter would leak to chat/world the same frame.
        bool chatOptionsTyping = _chatOptions.IsCapturingKeyboard;
        _chatOptions.Update(input, nowMs);

        // Death overlay: while the local player is dead this is a FULL modal — it handles the
        // respawn click and every other gameplay input path below is locked out (panel toggles, HUD
        // buttons/links, item/potion hotkeys, casting, and movement/attack via movementBlocked). Any panels
        // open at the moment of death are closed so nothing lingers behind the overlay.
        bool dead = _death.Update(input, _ctx.State, _ctx.Sender);
        if (dead && !_wasDead) CloseAllPanels();
        _wasDead = dead;

        float dtSec = deltaMs / 1000f;
        var floats = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_floatingTexts);
        for (int fi = floats.Length - 1; fi >= 0; fi--)
        {
            floats[fi].Age += dtSec;
            if (floats[fi].Age >= FloatingText.MaxAge) _floatingTexts.RemoveAt(fi);
        }

        // Keep the lock attached to the same monster across map changes:
        //  • a targeted native NPC that crosses a seam becomes a traversal guest — follow it (the
        //    server transfers its copy too), guest identity = (spawnMap = the cell's map, spawnSlot
        //    = the slot);
        //  • when the entity dies, returns home, or leaves the region, drop and tell the server so a
        //    reused slot/identity can't silently re-bind a stale lock.
        if (!_tabTarget.IsNone)
        {
            if (_tabTarget.Kind == TargetKind.Npc
                && _ctx.State.TraversalNpcs.TryGetValue((_tabTarget.B, _tabTarget.A), out var crossed) && crossed.Num > 0)
            {
                _tabTarget = new TargetRef(TargetKind.Traversal, _tabTarget.B, _tabTarget.A);
            }

            if (!ResolveTargetTile(_tabTarget, out int tMap, out int tX, out int tY)
                || !TryEntityScreen(tMap, tX, tY, 0f, 0f, out _, out _))
            {
                _tabTarget = default;
                _ctx.Sender.SendDropTarget();
            }
        }

        MovementProcessor.Process(_ctx.State, deltaMs);
        AnimationProcessor.Process(_ctx.State, nowMs);
        _camVelX = Math.Clamp(dtSec > 0f ? (_camera.CameraX - _prevCamX) / dtSec : 0f, -MaxCamVelPxSec, MaxCamVelPxSec);
        _camVelY = Math.Clamp(dtSec > 0f ? (_camera.CameraY - _prevCamY) / dtSec : 0f, -MaxCamVelPxSec, MaxCamVelPxSec);
        _prevCamX = _camera.CameraX;
        _prevCamY = _camera.CameraY;
        bool indoors = _ctx.State.IndoorsOf(_ctx.State.Map);
        // Stepping from an outdoor map to an indoor one (seam-cross or warp) instantly kills any weather
        // still in the air, rather than letting rain/snow finish falling inside. Weather-STATE changes are
        // unaffected — those keep the particles and just stop spawning more, so they taper off naturally.
        if (indoors && !_prevIndoors)
            _particles.ClearAll();
        _prevIndoors = indoors;
        if (!indoors)
            _particles.EmitWeather(_ctx.State.Weather, _camera, _camVelY, dtSec);
        _particles.Update(dtSec);
        BloodProcessor.Process(_ctx.State, dtSec);
        ReleaseDeferredHits();

        // Tick gate — all action sends are capped at TickMs intervals so rapid taps
        // can't fire faster than the tick rate and all inputs are checked together.
        _tickAccMs += deltaMs;
        bool onTick = _tickAccMs >= TickMs;
        if (onTick) _tickAccMs -= TickMs;

        if (_ctx.State.ActiveShopNum > 0 && !_shop.IsOpen)
        {
            _shop.Open();
            BringToFront(PanelShop);
        }
        else if (_ctx.State.ActiveShopNum == 0 && _shop.IsOpen)
        {
            _shop.Close();
        }

        if (_ctx.State.BankOpen && !_bank.IsOpen)
        {
            _ctx.State.BankOpen = false;
            if (!_bank.IsOpen) _bank.Toggle();
            BringToFront(PanelBank);
            if (_inn.IsOpen) _inn.Toggle();
        }

        // Server-driven marketplace open (from the Inn panel's Marketplace button), mirroring the bank.
        if (_ctx.State.MarketOpen && !_market.IsOpen)
        {
            _ctx.State.MarketOpen = false;
            _market.Open();
            BringToFront(PanelMarket);
            if (_inn.IsOpen) _inn.Toggle();
        }
        // Tell the server the moment the market panel closes (any path), so it drops us as a live-broadcast
        // recipient. Edge-detected here so every close route — X, Escape, toggle, close-all-on-death — is covered.
        if (_marketWasOpen && !_market.IsOpen) _ctx.Sender.SendMarketClose();
        _marketWasOpen = _market.IsOpen;

        // Server-driven trade window: TradeActive is the single source of truth. It opens when a trade is
        // accepted and closes when the trade ends (completion, cancel, or a proximity/death break), so the
        // client never toggles this panel itself — the server drives both edges.
        if (_ctx.State.TradeActive && !_trade.IsOpen)
        {
            _trade.Open();
            BringToFront(PanelTrade);
        }
        else if (!_ctx.State.TradeActive && _trade.IsOpen)
        {
            _trade.Close();
        }

        bool chatFocused = _chat.IsFocused || _chat.IsLogFocused;
        bool panelOpen = _inv.IsOpen || _spells.IsOpen || _training.IsOpen || _shop.IsOpen || _stats.IsOpen;

        bool mouseOverFloating = MouseOverFloatingAt(input.MousePosition);

        // Hover is intentionally not consumed here so buttons/checkboxes inside panels
        // can still highlight. HUD buttons already blocked from clicks via mouseOwned.

        _hud.Tick(_ctx.State, deltaMs / 1000f);
        _partyOverlay.Tick(_ctx.State, deltaMs / 1000f);
        _partyOverlay.Update(input, _ctx.State, _ctx.Sender);
        WorldBarAnimator.Tick(_ctx.State, deltaMs / 1000f);
        TickChatBubbles(_ctx.State);
        // While dead, keep the HUD live ONLY for the Logout (Quit) button so a corpse can still log out; every
        // other HUD button stays inert. Preserve the mouseOverFloating guard in both cases.
        var hudAction = mouseOverFloating ? HudAction.None : _hud.Update(input);
        if (dead && hudAction != HudAction.Quit) hudAction = HudAction.None;
        switch (hudAction)
        {
            case HudAction.ToggleInventory:
                ActivatePanel(PanelInventory);
                break;
            case HudAction.ToggleSpells:
                ActivatePanel(PanelSpells);
                break;
            case HudAction.ToggleStats:
                ActivatePanel(PanelStats);
                break;
            case HudAction.ToggleTraining:
                ActivatePanel(PanelTraining);
                break;
            case HudAction.ToggleQuestLog:
                ActivatePanel(PanelQuestLog);
                break;
            case HudAction.ToggleSocial:
                ActivatePanel(PanelSocial);
                break;
            case HudAction.Quit:
                _ctx.ShowQuitConfirm();
                break;
        }

        // Panel toggles (I/P/T/O/H/U/C), Escape, and Enter (chat focus, handled by ChatPanel)
        // stay on keyboard regardless of which device owns gameplay — they're convenience UI
        // with no gamepad analog, not the source of the double-input we're suppressing here.
        // Combat/movement/cycle hotkeys that DO have a gamepad equivalent are gated by
        // ActiveDevice so the two devices can't fire the same action at once.
        bool kbActive = input.IsKeyboardActive;
        bool padActive = input.IsGamepadActive;
        // World input (the menu + potion hotkeys below, and the pickup/movement gates further down) is
        // suppressed whenever a UI surface owns typed input: chat focus, a chat-tab rename field, or an
        // open panel's modal sub-surface (number-prompt text box, context menu, confirm overlay). Without
        // the AnyPanelCapturingInput term, typing an amount into a panel's number prompt still fired the
        // 1/2/3 potion and I/P/T/... menu hotkeys underneath it.
        bool worldInputSuppressed = WorldInputGate.IsSuppressed(chatFocused, chatOptionsTyping, AnyPanelCapturingInput);
        // While dead the world-input block below is skipped, but Escape must still reach the quit/logout dialog so
        // a corpse can log out. Panels are force-closed on death, so HandleEscapeKey routes straight to ShowQuitConfirm.
        if (dead && !worldInputSuppressed && input.IsKeyPressed(Keys.Escape))
            HandleEscapeKey();
        if (!worldInputSuppressed && !dead)   // dead: no panel toggles, item/potion hotkeys, or casting
        {
            bool ctrl = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl);
            if (input.IsKeyPressed(Keys.I)) ActivatePanel(PanelInventory);
            if (input.IsKeyPressed(Keys.P)) ActivatePanel(PanelSpells);
            if (input.IsKeyPressed(Keys.T)) ActivatePanel(PanelTraining);
            if (input.IsKeyPressed(Keys.O)) ActivatePanel(PanelOptions);
            if (input.IsKeyPressed(Keys.H)) ActivateHelpPanel();
            if (input.IsKeyPressed(Keys.M)) ActivatePanel(PanelMail);
            if (input.IsKeyPressed(Keys.G)) ActivatePanel(PanelSocial);
            if (input.IsKeyPressed(Keys.J)) ActivatePanel(PanelQuestLog);
            if (kbActive && input.IsKeyPressed(Keys.Tab))
            {
                if (ctrl) TargetSelf();
                else CycleTabTarget(reverse: input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift));
            }
            if (padActive)
            {
                bool lbDown = input.IsGamePadButtonDown(Buttons.LeftShoulder);
                bool rbDown = input.IsGamePadButtonDown(Buttons.RightShoulder);
                bool lbPressed = input.IsGamePadButtonPressed(Buttons.LeftShoulder);
                bool rbPressed = input.IsGamePadButtonPressed(Buttons.RightShoulder);
                // LB+RB held together targets self; the bumper whose press lands second
                // (or both on the same frame) triggers the combo and the single-bumper
                // cycle below is suppressed that frame.
                if ((lbPressed || rbPressed) && lbDown && rbDown)
                {
                    TargetSelf();
                }
                else
                {
                    if (lbPressed) CycleTabTarget(reverse: false);
                    if (rbPressed) CycleTabTarget(reverse: true);
                }
            }
            // Bare C opens the stats panel; guard against Ctrl+C so copying text never toggles it.
            if (input.IsKeyPressed(Keys.C) && !ctrl) ActivatePanel(PanelStats);

            // ── Casting and the action bar ───────────────────────────────────
            // The two split cleanly along the line the caster resource model already draws: SubHp is the
            // caster's WEAPON (its own pool-fraction cost plus reagents), and everything else is utility.
            // So SubHp keeps Q and the prepared slot — one chosen attack spell, swung on the same beat as
            // a melee swing — and the action bar takes every OTHER spell type plus items. Neither can
            // reach into the other's half: the server refuses to prepare a non-SubHp spell or to bind a
            // SubHp one, so "which key casts this" is never ambiguous.
            // EITHER trigger opens the bar; which one decides where it points. LT aims at the target, RT at
            // the caster, so the same four face buttons serve both without a two-trigger grip. Holding both
            // aims at the caster — see IsSelfTargetHeld — so aim can be switched without releasing first.
            bool hotkeyModifier = padActive && (input.IsGamePadLeftTriggerDown() || input.IsGamePadRightTriggerDown());
            // HELD, like the attack key: a prepared spell is the caster's swing, so holding it should keep
            // swinging. TryCastPrepared paces the repeat on the cast beat — both the beat the server confirms
            // and a local one, so a refused cast cannot re-send every frame while the key stays down.
            if ((kbActive && input.IsKeyDown(Keys.Q)) || (padActive && input.IsGamePadButtonDown(Buttons.Y) && !hotkeyModifier))
                _spells.TryCastPrepared(_ctx.State, _ctx.Sender);

            // Each slot answers to the clock its contents keep: a spell to the action beat it shares
            // with attacking, a potion to the slower drinking clock, anything else to neither. Checked
            // per slot rather than per row, because the four can hold four different things. Only a
            // press that did something charges anything — an empty slot or an empty bag costs nothing.
            {
                int fired = 0;
                if (kbActive)
                {
                    if (input.IsKeyPressed(Keys.D1) || input.IsKeyPressed(Keys.NumPad1)) fired = 1;
                    else if (input.IsKeyPressed(Keys.D2) || input.IsKeyPressed(Keys.NumPad2)) fired = 2;
                    else if (input.IsKeyPressed(Keys.D3) || input.IsKeyPressed(Keys.NumPad3)) fired = 3;
                    else if (input.IsKeyPressed(Keys.D4) || input.IsKeyPressed(Keys.NumPad4)) fired = 4;
                }
                if (fired == 0 && hotkeyModifier)
                {
                    // Trigger + face button. The order preserves the old potion layout — X was the HP
                    // potion, Y mana, B stamina — so existing muscle memory still lands on the same
                    // three, and slot 4 takes A. HotkeyBarPanel.GamepadFace draws these same letters.
                    if (input.IsGamePadButtonPressed(Buttons.X)) fired = 1;
                    else if (input.IsGamePadButtonPressed(Buttons.Y)) fired = 2;
                    else if (input.IsGamePadButtonPressed(Buttons.B)) fired = 3;
                    else if (input.IsGamePadButtonPressed(Buttons.A)) fired = 4;
                }
                if (fired > 0 && HotkeySlotReady(fired, nowMs) && TryUseHotkey(fired)) StartHotkeyCooldown(fired, nowMs);
            }
            if (input.IsKeyPressed(Keys.Escape))
                HandleEscapeKey();
        }

        // Left-click an action-bar slot to use it, on the same terms as pressing its key: the row's one
        // shared cooldown, and only a press that did something starts it. The click is consumed for any
        // slot, bound or not, so it never falls through and swings at the world behind the bar.
        if (!mouseOverFloating && !dead && input.IsMouseClicked())
        {
            int barSlot = HotkeyBarPanel.SlotAt(input.MousePosition);
            if (barSlot > 0)
            {
                input.ConsumeMouseClick();
                if (HotkeySlotReady(barSlot, nowMs) && TryUseHotkey(barSlot)) StartHotkeyCooldown(barSlot, nowMs);
            }
        }

        // Right-click a bound action-bar slot to empty it. Only a BOUND slot offers the menu — a menu whose
        // single item does nothing is worse than no menu — and the click is consumed either way so it
        // can't fall through to the world behind the sidebar.
        if (!mouseOverFloating && !dead && input.IsRightMouseClicked() && _gameFont is not null)
        {
            int barSlot = HotkeyBarPanel.SlotAt(input.MousePosition);
            if (barSlot > 0)
            {
                input.ConsumeRightMouseClick();
                var me = _ctx.State.Me;
                if (me?.Hotkeys is not null && barSlot < me.Hotkeys.Length && me.Hotkeys[barSlot].IsBound)
                {
                    int captured = barSlot;
                    _contextMenu.Open(input.MousePosition, "",
                        [new ContextMenu.Item(ClientStrings.Get(ClientStrings.HotkeyBar_Clear),
                            () => AssignHotkey(captured, HotkeyKind.None, 0))],
                        new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH), _gameFont);
                }
            }
        }

        // [Mail (M)] / [Options (O)] / [Help (H)] link clicks — same on-top-toggle behavior as everywhere else.
        // Consuming the click here prevents MirageGame's link handler from firing in gameplay.
        if (!mouseOverFloating && !dead)
        {
            if (HudPanel.HelpLink.IsClicked(input))
            {
                ActivateHelpPanel();
                input.ConsumeMouseClick();
            }
            else if (HudPanel.OptionsLinkInGame.IsClicked(input))
            {
                ActivatePanel(PanelOptions);
                input.ConsumeMouseClick();
            }
            else if (HudPanel.MailLink.IsClicked(input))
            {
                ActivatePanel(PanelMail);
                input.ConsumeMouseClick();
            }
        }

        // Game input (movement, attack, pick up) — tick-gated so all actions are
        // synchronized to the same cadence and holding a key works for all of them.
        bool movementBlocked = dead || AnyPanelBlocksMovement;
        bool worldActionInput = !worldInputSuppressed && !movementBlocked;

        // Latch a pickup press every frame, so a key tap landing between ticks isn't lost.
        if (worldActionInput &&
            ((kbActive && input.IsKeyPressed(Keys.F)) || (padActive && input.IsGamePadButtonPressed(Buttons.A))))
        {
            _pickUpLatched = true;
        }

        // Same latch for the attack press. EITHER trigger reserves X — left because it is the bar's own
        // modifier, right because a self-aimed swing is not a thing and should do nothing rather than attack.
        if (worldActionInput &&
            ((kbActive && input.IsKeyPressed(Keys.E)) ||
             (padActive && input.IsGamePadButtonPressed(Buttons.X)
                        && !input.IsGamePadLeftTriggerDown() && !input.IsGamePadRightTriggerDown())))
        {
            _attackPressLatched = true;
        }

        if (!worldActionInput)
        {
            // The gate closed with a press still latched (chat focus, a blocking panel opened): drop it rather
            // than replaying a stale action the moment the gate reopens.
            _pickUpLatched = false;
            _attackPressLatched = false;
        }
        else
        {
            // Every frame, not only on the tick. The tick gates the ACTION sends inside Process; movement
            // is rate-limited by the slide it is waiting on, and holding it to a tick boundary makes any
            // run faster than one tile per tick finish early and then stand still until the next one.
            var snapshot = BuildInputSnapshot(input, _pickUpLatched, _attackPressLatched);
            if (onTick)
            {
                _pickUpLatched = false;
                _attackPressLatched = false;
            }
            InputProcessor.Process(snapshot, _ctx.State, _ctx.Sender, nowMs, onTick);
            // Core refused a melee-key interact aimed across the two planes. It owns that decision (it resolves the
            // faced NPC) but has no chat, so the refusal is voiced here — drained immediately, never left stale.
            if (_ctx.State.NpcInteractWrongLayer)
            {
                _ctx.State.NpcInteractWrongLayer = false;
                AddChatLine(ClientStrings.Get(ClientStrings.GameplayScreen_NpcOtherLayer), GameColor.BrightRed);
            }
        }

        // Recompute the camera AFTER input: a predicted move or seam-cross re-frames the 3×3 grid and
        // repositions the player this same frame, so the camera must read that new state before Draw —
        // otherwise it lags one frame behind the grid and the world flickers on a border crossing.
        UpdateCamera();

        // On a fresh mouse press, bring the topmost hit open panel to the front and
        // give it keyboard focus. Clicking outside all open panels releases focus.
        if (input.IsMouseJustPressed())
        {
            bool clickedPanel = false;
            for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
            {
                int idx = _zOrder[zi];
                if (PanelIsOpen(idx) && PanelContainsMouse(idx, input.MousePosition))
                {
                    _zOrder.RemoveAt(zi);
                    _zOrder.Add(idx);
                    clickedPanel = true;
                    break;
                }
            }
            _panelFocused = clickedPanel;
        }

        // Determine which panel (if any) has keyboard focus: the topmost open panel
        // while _panelFocused is true, or -1 when no panel has focus.
        _activePanel = -1;
        if (_panelFocused)
        {
            for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
            {
                if (PanelIsOpen(_zOrder[zi]))
                {
                    _activePanel = _zOrder[zi];
                    break;
                }
            }
        }

        // Update panels topmost-first. After the topmost panel that contains the mouse
        // has been updated (including its children), block lower panels from starting new
        // drag/resize or scroll interactions on the same press/scroll event. Hover is also
        // consumed for lower panels so any hover-driven cursor request inside them (e.g.
        // a Link's hand-cursor request) doesn't bleed up through the panel on top.
        bool mouseOwned = false;
        for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
        {
            int idx = _zOrder[zi];
            if (mouseOwned)
            {
                if (input.IsMouseJustPressed()) input.ConsumeMouseDown();
                if (input.IsMouseClicked()) input.ConsumeMouseClick();
                input.ConsumeScrollWheel();
                input.ConsumeMouseHover();
            }
            UpdatePanel(idx, input, idx == _activePanel);
            if (!mouseOwned && PanelIsOpen(idx) && PanelContainsMouse(idx, input.MousePosition))
                mouseOwned = true;
        }

        // If the focused panel closed itself (via its own X button), release focus.
        if (_activePanel >= 0 && !PanelIsOpen(_activePanel))
        {
            _panelFocused = false;
            _activePanel = -1;
        }
        // Prevent scroll/click/press/hover from reaching the chat window when a game panel
        // owns the mouse. Hover consumption stops the chat TextArea from firing the hand
        // cursor for hyperlinks that sit beneath an open panel.
        if (mouseOwned)
        {
            input.ConsumeScrollWheel();
            input.ConsumeMouseClick();
            if (input.IsMouseJustPressed()) input.ConsumeMouseDown();
            input.ConsumeMouseHover();
        }

        // Persist layout the moment a drag or resize completes, so config survives crashes.
        if (_inv.LayoutChanged || _spells.LayoutChanged || _training.LayoutChanged || _shop.LayoutChanged || _bank.LayoutChanged || _inn.LayoutChanged || _stats.LayoutChanged || _help.LayoutChanged || _controls.LayoutChanged || _mail.LayoutChanged || _mail.ColumnsChanged || _market.LayoutChanged || _market.ColumnsChanged || _trade.LayoutChanged || _social.LayoutChanged || _social.TabChanged || _social.ColumnsChanged || _questLog.ColumnsChanged || _death.LayoutChanged)
            SavePanelConfig();
        if (_ctx.OptionsPanel.LayoutChanged) _ctx.SaveSettings();

        // Map click → target search. Fires after panel updates so panel-consumed clicks are ignored.
        if (_ctx.State.InGame && !_ctx.State.GettingMap && !chatFocused && input.IsMouseClicked())
        {
            var mp = input.MousePosition;
            var origin = input.LeftPressOrigin;
            // Press-origin gate: only reach the world if the press AND the release both landed in the
            // viewport and the press did not begin over floating UI — a press that started on a panel and
            // released on the map must not move/target.
            bool releaseInView = mp.X >= 0 && mp.X < Camera.ViewW && mp.Y >= 0 && mp.Y < Camera.ViewH;
            bool originInView = origin.X >= 0 && origin.X < Camera.ViewW && origin.Y >= 0 && origin.Y < Camera.ViewH;
            if (releaseInView && originInView && !MouseOverFloatingAt(origin))
            {
                // The target a click acquires is decided client-side from what the player
                // visually clicked (sprite-pixel hit test).  The clicked TILE is still sent
                // for the server's item-listing scan.  Server validates the proposed identity
                // and emits a ClearTargetPacket if it's stale.
                var gt = _camera.ScreenToGridTile(mp.X, mp.Y);
                if (gt is { } g && g.Col is >= 0 and <= 2 && g.Row is >= 0 and <= 2)
                {
                    int mapNum = _ctx.State.NeighborMapNums[g.Col, g.Row];
                    if (mapNum > 0)
                    {
                        float wx = mp.X + _camera.CameraX;
                        float wy = mp.Y + _camera.CameraY;
                        _tabTarget = FindEntityAtPixel(wx, wy);
                        var pr = ToSearchProposal(_tabTarget, _ctx.State.MyIndex);
                        _ctx.Sender.SendSearch(g.LocalX, g.LocalY, mapNum, pr.type, pr.id, pr.map);
                    }
                }
            }
        }

        // The chat log only owns copy/cut when no floating panel is active; otherwise the
        // active panel's text area takes clipboard priority.
        _chat.Update(input, _ctx.State, _ctx.Sender, nowMs, keyboardActive: _activePanel < 0, suppressKeyboard: chatOptionsTyping);

        // In-world right-click on a player sprite → open the context menu. Skip if a panel is
        // already absorbing the cursor (panels run their own right-click handlers above) or
        // if the menu is already open (it consumes the click itself this frame).
        if (!mouseOverFloating && !MouseOverFloatingAt(input.RightPressOrigin) && !_contextMenu.IsOpen && input.IsRightMouseClicked())
        {
            // ONE menu for the whole square rather than a branch per entity kind. The old form asked
            // "what did I click", which cannot answer for loot (it has no sprite worth aiming at) and
            // is ambiguous across the two planes anyway. Asking "what is HERE" has a single answer,
            // and it is what lets an item be taken without standing on it.
            float rwx = input.MousePosition.X + _camera.CameraX;
            float rwy = input.MousePosition.Y + _camera.CameraY;
            OpenTileContextMenu(FindNpcsAtPixel(rwx, rwy), rwx, rwy, input.MousePosition);
            if (_contextMenu.IsOpen) input.ConsumeRightMouseClick();
        }
    }

    /// <summary>
    /// Recompute the seamless-scroll camera from the local player's position.
    /// The camera follows the player and clamps at any border the center map does not link across;
    /// a map with no links locks the view to itself.
    /// </summary>
    private void UpdateCamera()
    {
        var me = _ctx.State.Me;
        _camera.Update(me.X, me.Y, me.XOffset, me.YOffset, DeclaredNeighbors(),
                       _ctx.State.MapTilesX, _ctx.State.MapTilesY);
    }

    // Reused, because this runs every frame and the frame readout counts the KB/s.
    private readonly int[,] _scrollBounds = new int[3, 3];

    /// <summary>Which sides the camera may scroll toward, taken from the center map's own LINKS rather
    /// than from which neighbor cells have arrived.
    ///
    /// <para>🔴 The difference is a visible slide after every warp. A warp empties the grid and the server
    /// refills it a cell at a time, so the loaded set grows over several frames — and the camera's clamp is
    /// derived from it, so the bounds widen in steps and the view creeps to its final position after the
    /// player has already arrived. It reads as overshooting the destination and settling back.</para>
    ///
    /// <para>A map's links are known the instant the map itself is, so the bounds are right on the first
    /// frame and never move again. They are also the better answer on their own terms: what the camera is
    /// asking is whether there is anywhere to scroll TO, and a declared neighbor is somewhere the player
    /// can walk whether or not its tiles have landed yet.</para>
    ///
    /// <para>Only the four side cells are filled. The clamp asks whether a row or column holds anything at
    /// all, so a side answers for its whole row, and the corners never need setting.</para></summary>
    private int[,] DeclaredNeighbors()
    {
        var map = _ctx.State.NeighborMaps[1, 1];
        _scrollBounds[1, 0] = map?.Up ?? 0;
        _scrollBounds[1, 2] = map?.Down ?? 0;
        _scrollBounds[0, 1] = map?.Left ?? 0;
        _scrollBounds[2, 1] = map?.Right ?? 0;
        return _scrollBounds;
    }

    private InputSnapshot BuildInputSnapshot(InputState input, bool pickUpLatched, bool attackPressLatched)
    {
        const float Deadzone = InputState.GamepadStickDeadzone;
        // Whichever device "owns" gameplay this frame; the other device's contributions are
        // zeroed out so a stray key/button press on the idle device can't double-trigger an action.
        bool kbActive = input.IsKeyboardActive;
        bool padActive = input.IsGamepadActive;

        var stick = padActive ? input.GamePadLeftStick : Vector2.Zero;
        bool stickUp = stick.Y > Deadzone && Math.Abs(stick.Y) >= Math.Abs(stick.X);
        bool stickDown = stick.Y < -Deadzone && Math.Abs(stick.Y) >= Math.Abs(stick.X);
        bool stickLeft = stick.X < -Deadzone && Math.Abs(stick.X) > Math.Abs(stick.Y);
        bool stickRight = stick.X > Deadzone && Math.Abs(stick.X) > Math.Abs(stick.Y);

        // Face-only inputs: Ctrl+WASD on keyboard and the right stick on gamepad rotate the
        // sprite without moving.  Ctrl held also gates WASD out of the movement channel so
        // a single Ctrl+W press faces north instead of stepping north.
        bool ctrl = kbActive && (input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl));
        bool wasdUp = kbActive && input.IsKeyDown(Keys.W);
        bool wasdDown = kbActive && input.IsKeyDown(Keys.S);
        bool wasdLeft = kbActive && input.IsKeyDown(Keys.A);
        bool wasdRight = kbActive && input.IsKeyDown(Keys.D);

        var rstick = padActive ? input.GamePadRightStick : Vector2.Zero;
        bool faceStickUp = rstick.Y > Deadzone && Math.Abs(rstick.Y) >= Math.Abs(rstick.X);
        bool faceStickDown = rstick.Y < -Deadzone && Math.Abs(rstick.Y) >= Math.Abs(rstick.X);
        bool faceStickLeft = rstick.X < -Deadzone && Math.Abs(rstick.X) > Math.Abs(rstick.Y);
        bool faceStickRight = rstick.X > Deadzone && Math.Abs(rstick.X) > Math.Abs(rstick.Y);

        bool dpadUp = padActive && input.IsGamePadButtonDown(Buttons.DPadUp);
        bool dpadDown = padActive && input.IsGamePadButtonDown(Buttons.DPadDown);
        bool dpadLeft = padActive && input.IsGamePadButtonDown(Buttons.DPadLeft);
        bool dpadRight = padActive && input.IsGamePadButtonDown(Buttons.DPadRight);

        // Movement inputs on the gamepad (D-pad or left stick past its deadzone) suppress the
        // right stick entirely — face-aim is a "look around while idle" tool, not something to
        // fight against the movement direction the player is already committing to.
        bool gamepadMovementActive =
            stickUp || stickDown || stickLeft || stickRight ||
            dpadUp || dpadDown || dpadLeft || dpadRight;

        // Ctrl+WASD facing uses the same last-pressed-wins press-order as movement, so Ctrl+W then
        // Ctrl+D faces east and releasing D falls back to north.  Resolved every tick independent of
        // Ctrl (fed the raw WASD held-state, which the movement stack can't provide once Ctrl gates
        // it off) so the key order is already current the instant Ctrl engages.
        Direction? faceWasd = _faceStack.Resolve(wasdUp, wasdDown, wasdLeft, wasdRight);

        Direction? dirFace = null;
        if (ctrl && faceWasd is Direction fw)
        {
            dirFace = fw;
        }
        else if (!gamepadMovementActive)
        {
            if (faceStickUp) dirFace = Direction.Up;
            else if (faceStickDown) dirFace = Direction.Down;
            else if (faceStickLeft) dirFace = Direction.Left;
            else if (faceStickRight) dirFace = Direction.Right;
        }

        // Triggers reserve the face buttons for the potion hotkeys, so B/X don't run/attack while held.
        bool triggerHeld = padActive && (input.IsGamePadLeftTriggerDown() || input.IsGamePadRightTriggerDown());
        // Combined per-direction "held" state across the active device (keyboard WASD, gated out
        // while Ctrl-facing, plus D-pad and left stick). The press-order stack turns this into one
        // dominant direction — last-pressed still-held wins — so it must be fed every tick, even
        // mid-step, to track presses and releases. The stick already yields a single direction.
        bool moveUp = (!ctrl && wasdUp) || dpadUp || stickUp;
        bool moveDown = (!ctrl && wasdDown) || dpadDown || stickDown;
        bool moveLeft = (!ctrl && wasdLeft) || dpadLeft || stickLeft;
        bool moveRight = (!ctrl && wasdRight) || dpadRight || stickRight;
        return new InputSnapshot
        {
            Move = _moveStack.Resolve(moveUp, moveDown, moveLeft, moveRight),
            // Ctrl outranks Shift on keyboard: holding Ctrl turns WASD into a face-only input, so
            // a co-held Shift must not leak a Running intent that the next non-Ctrl frame inherits.
            Running = (!ctrl && kbActive && input.IsKeyDown(Keys.LeftShift)) || (padActive && input.IsGamePadButtonDown(Buttons.B) && !triggerHeld),
            // The press edge comes from the frame-latch, never from this tick's key state — a press landing on a
            // non-tick frame would otherwise be gone by the time the snapshot is built (see _attackPressLatched).
            // A latched press also counts as "attack held" for this tick, so a tap shorter than one tick still acts.
            Attack = attackPressLatched || (kbActive && input.IsKeyDown(Keys.E)) || (padActive && input.IsGamePadButtonDown(Buttons.X) && !triggerHeld),
            AttackPressed = attackPressLatched,
            PickUp = pickUpLatched,
            DirFace = dirFace,
        };
    }
}
