using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>Character slot picker for the logged-in account: play an existing character, create one in
/// an empty slot, or delete one (which routes through <see cref="DeleteConfirmScreen"/>).</summary>
public sealed class CharSelectScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly ListBox _charList = new();
    private readonly Button _playBtn;
    private readonly Button _newCharBtn;
    private readonly Button _deleteBtn;
    private readonly Button _logoutBtn;
    private readonly Button _quitBtn;
    private InputState _input = new();
    private long _lastAnimToggleMs;
    private int _animFrame;
    private bool _pendingAction;
    // Captions are captured once in the constructor, so a language switch made while this screen
    // is already showing would otherwise leave them stale — a state transition rebuilds the
    // screen, but sitting on it does not. Trailing Generation re-labels once, as the panels do.
    private int _labelsGeneration = -1;

    private static readonly Rectangle Dlg = new(127, 148, 546, 304);
    private static readonly Rectangle ListBounds = new(335, 192, 305, 60);

    public CharSelectScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _playBtn = new Button { Bounds = new Rectangle(399, 270, 200, 34), Label = ClientStrings.Get(ClientStrings.CharSelectScreen_PlayButton) };
        _newCharBtn = new Button { Bounds = new Rectangle(399, 314, 200, 34), Label = ClientStrings.Get(ClientStrings.CharSelectScreen_NewCharButton) };
        _deleteBtn = new Button { Bounds = new Rectangle(399, 358, 200, 34), Label = ClientStrings.Get(ClientStrings.CharSelectScreen_DeleteCharButton) };
        _logoutBtn = new Button { Bounds = new Rectangle(399, 402, 94, 34), Label = ClientStrings.Get(ClientStrings.CharSelectScreen_LogoutButton) };
        _quitBtn = new Button { Bounds = new Rectangle(505, 402, 94, 34), Label = ClientStrings.Get(ClientStrings.CharSelectScreen_QuitButton) };
    }

    /// <summary>Renders the slot rows from live state. Split out of <see cref="OnEnter"/> so a
    /// language switch can re-render them — the empty-slot placeholder and the "Lv." format are
    /// both localized. The caller owns the selection: OnEnter picks the last-played slot, a
    /// re-label keeps whatever the player had highlighted.</summary>
    private void RebuildCharList()
    {
        _charList.Items.Clear();
        var slots = _ctx.State.CharSlots;
        for (int i = 0; i < Constants.MaxChars; i++)
        {
            var slot = i < slots.Length ? slots[i] : null;
            if (slot is not null && slot.Name.Length > 0)
            {
                string cls = slot.ClassName.Length > 0 ? $" {slot.ClassName}" : "";
                _charList.Items.Add(ClientStrings.Format(ClientStrings.CharSelectScreen_CharFormat, ("Name", slot.Name), ("Level", slot.Level), ("Class", cls)));
            }
            else
            {
                _charList.Items.Add(ClientStrings.Get(ClientStrings.Common_Empty));
            }
        }
    }

    private void RefreshLabels()
    {
        _playBtn.Label = ClientStrings.Get(ClientStrings.CharSelectScreen_PlayButton);
        _newCharBtn.Label = ClientStrings.Get(ClientStrings.CharSelectScreen_NewCharButton);
        _deleteBtn.Label = ClientStrings.Get(ClientStrings.CharSelectScreen_DeleteCharButton);
        _logoutBtn.Label = ClientStrings.Get(ClientStrings.CharSelectScreen_LogoutButton);
        _quitBtn.Label = ClientStrings.Get(ClientStrings.CharSelectScreen_QuitButton);
        int selected = _charList.SelectedIndex;
        RebuildCharList();
        _charList.SelectedIndex = selected;
    }

    /// <summary>Request the account's character list and reset the slot selection.</summary>
    public void OnEnter()
    {
        _ctx.PlayMenuMusic();
        RebuildCharList();
        var slots = _ctx.State.CharSlots;

        // Pre-select the last played character for this account.
        _charList.SelectedIndex = 0;
        string account = _ctx.State.AccountName;
        if (account.Length > 0)
        {
            int lastSlot = AccountConfig.Load(account).LastCharSlot;
            if (lastSlot >= 1 && lastSlot <= slots.Length)
                _charList.SelectedIndex = lastSlot - 1;
        }

        _pendingAction = false;
    }

    /// <summary>Nothing to release — the screen holds no resources beyond its fields.</summary>
    public void OnExit() { }

    /// <summary>Handle typing, field focus, link clicks, and the submit key; also completes any
    /// in-flight connection attempt started by the submit handler.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        _charList.Update(input, ListBounds);

        bool hasChar = IsSlotFilled(_charList.SelectedIndex);
        _playBtn.Enabled = hasChar && !_pendingAction;
        _newCharBtn.Enabled = !_pendingAction;
        _deleteBtn.Enabled = hasChar && !_pendingAction;

        if ((_playBtn.IsClicked(input) || (_playBtn.Enabled && input.IsKeyPressed(Keys.Enter))) && hasChar)
        {
            var selectedSlot = _ctx.State.CharSlots[_charList.SelectedIndex];
            _ctx.State.CurrentCharName = selectedSlot.Name;
            string account = _ctx.State.AccountName;
            if (account.Length > 0)
            {
                var cfg = AccountConfig.Load(account);
                cfg.LastCharSlot = _charList.SelectedIndex + 1;
                cfg.Save(account);
            }
            _ctx.Sender.SendUseChar(_charList.SelectedIndex + 1);
            _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.CharSelectScreen_EnteringWorld));
            _pendingAction = true;
            _ctx.Screens.Replace(new LoadingScreen(_ctx));
        }

        if (_newCharBtn.IsClicked(input))
        {
            _ctx.Sender.SendGetClasses();
            _ctx.Menu.GoToLoadingForNewChar(ClientStrings.Get(ClientStrings.CharSelectScreen_LoadingClasses));
            _pendingAction = true;
            _ctx.Screens.Replace(new LoadingScreen(_ctx));
        }

        if (_deleteBtn.IsClicked(input) && hasChar)
        {
            int slot = _charList.SelectedIndex + 1;
            string charName = _ctx.State.CharSlots[_charList.SelectedIndex].Name.Trim();
            _ctx.Screens.Push(new DeleteConfirmScreen(_ctx, slot, charName));
        }

        if (_logoutBtn.IsClicked(input))
        {
            _ctx.Transport.Disconnect();
            _ctx.Screens.Replace(new LoginScreen(_ctx));
        }

        if (_quitBtn.IsClicked(input))
            _ctx.ExitGame();
    }

    /// <summary>Whether the slot holds a character, which decides Play/Delete versus Create.</summary>
    private bool IsSlotFilled(int listIndex)
    {
        if (listIndex < 0) return false;
        var slots = _ctx.State.CharSlots;
        return listIndex < slots.Length && slots[listIndex].Name.Length > 0;
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.CharSelectScreen_Title));

        sb.DrawString(font, ClientStrings.Get(ClientStrings.CharSelectScreen_Instruction),
            new Vector2(Dlg.X + 216, Dlg.Y + 20), UiHelper.DlgLabelColor);

        _charList.Draw(sb, font, ListBounds);

        long nowMs = Environment.TickCount64;
        long animInterval = _playBtn.IsHovered(_input) ? 100L : 250L;
        if (nowMs - _lastAnimToggleMs >= animInterval)
        {
            _animFrame ^= 1;
            _lastAnimToggleMs = nowMs;
        }
        int sel = _charList.SelectedIndex;
        var slots = _ctx.State.CharSlots;
        if (sel >= 0 && sel < slots.Length && slots[sel].Name.Length > 0 && slots[sel].Sprite >= 0)
        {
            var pb = _playBtn.Bounds;
            var spriteDest = new Rectangle(pb.Right + 8, pb.Y + (pb.Height - Constants.PicY) / 2,
                Constants.PicX, Constants.PicY);
            var dir = _playBtn.IsHovered(_input) ? Direction.Right : Direction.Down;
            UiHelper.DrawMenuSpritePreview(sb, _ctx.Sprites, slots[sel].Sprite, slots[sel].SpriteSheet, _animFrame, spriteDest, dir);
        }

        _playBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _newCharBtn.Draw(sb, font, _input);
        _deleteBtn.Draw(sb, font, _input);
        _logoutBtn.Draw(sb, font, _input);
        _quitBtn.Draw(sb, font, _input, UiHelper.DangerButtonNormal, UiHelper.DangerButtonHover);
    }
}
