using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Panels;

/// <summary>Uncloseable "You have died" overlay: a mm:ss countdown, a bar that FILLS as the
/// timer empties, and a Respawn button that unlocks at 0. Shown while the local player is dead. The server
/// owns the timer (<see cref="Mirage.Shared.Records.PlayerRecord.RespawnReadyUtc"/>) and ignores an early
/// respawn request, so this is presentation + the button send.
///
/// It reuses the shared <see cref="DraggablePanel"/> chrome — title-bar drag, and position persisted
/// per-character (config panel "Death") through the same GameplayScreen plumbing as every other panel —
/// with the close button and resize handle disabled (it must not close, and is fixed-size). It
/// deliberately does NOT dim the screen: a dead player keeps watching the action and chatting, so it's a
/// small banner (default: centered on the screen) the player can drag out of the way.</summary>
public sealed class DeathPanel
{
    private const int PanelW = 280;
    private const int PanelH = 90;

    private readonly DraggablePanel _panel = new(
        new Rectangle((UiHelper.RefW - PanelW) / 2, (UiHelper.RefH - PanelH) / 2, PanelW, PanelH),
        minH: PanelH, minW: PanelW, showClose: false, resizable: false);
    private readonly Button _respawnBtn = new();
    private long _trackedReadyUtc;   // the RespawnReadyUtc the captured total corresponds to
    private int _totalSecs = 1;      // countdown length, captured on a fresh death so the bar can fill 0..full

    public Rectangle Bounds => _panel.Bounds;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();

    /// <summary>True for one frame after a drag completes, so the caller persists the new position.</summary>
    public bool LayoutChanged { get; private set; }

    /// <summary>Returns true while the local player is dead — the caller uses this to lock out gameplay input
    /// (a corpse can't act). Handles the title-bar drag and fires the respawn request when the timer is up.</summary>
    public bool Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        LayoutChanged = false;
        if (!state.Me.Dead) return false;

        _panel.Update(input);                    // title-bar drag (close + resize disabled)
        LayoutChanged = _panel.LayoutChanged;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now >= state.Me.RespawnReadyUtc && _respawnBtn.IsClicked(input))
            sender.SendRespawnRequest();
        return true;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, ClientState state)
    {
        if (!state.Me.Dead) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long remaining = Math.Max(0, state.Me.RespawnReadyUtc - now);
        bool ready = remaining <= 0;
        // Capture the total on a fresh death (or a relogin's remaining) so the bar can fill 0 → full.
        if (state.Me.RespawnReadyUtc != _trackedReadyUtc)
        {
            _trackedReadyUtc = state.Me.RespawnReadyUtc;
            _totalSecs = (int)Math.Max(1, state.Me.RespawnReadyUtc - now);
        }
        float fill = Math.Clamp(1f - remaining / (float)_totalSecs, 0f, 1f);

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.DeathPanel_Title), isActive: true);
        var c = _panel.ContentBounds;

        string time = $"{remaining / 60:00}:{remaining % 60:00}";
        var cSize = font.MeasureString(time);
        sb.DrawString(font, time, new Vector2(c.X + (c.Width - cSize.X) / 2f, c.Y + 4), Color.White);

        // Progress bar — fills as the countdown empties (empty at death, full when ready).
        var barOuter = new Rectangle(c.X + 20, c.Y + 26, c.Width - 40, 12);
        UiHelper.DrawFilledRect(sb, barOuter, new Color(30, 30, 30));
        UiHelper.DrawFilledRect(sb, new Rectangle(barOuter.X, barOuter.Y, (int)(barOuter.Width * fill), barOuter.Height),
            ready ? new Color(90, 200, 90) : new Color(180, 90, 90));
        UiHelper.DrawBorder(sb, barOuter, Color.Gray);

        const int BtnW = 120, BtnH = 26;
        _respawnBtn.Bounds = new Rectangle(c.X + (c.Width - BtnW) / 2, c.Bottom - 6 - BtnH, BtnW, BtnH);
        _respawnBtn.Label = ClientStrings.Get(ClientStrings.DeathPanel_Respawn);
        _respawnBtn.Enabled = ready;
        _respawnBtn.Draw(sb, font, input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
    }
}
