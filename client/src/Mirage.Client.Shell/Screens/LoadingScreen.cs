using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Screens;

/// <summary>
/// Shown while waiting for a server response. Replaced automatically by MenuLogic on success,
/// or via the AlertDialog / disconnect detection in MirageGame on failure.
///
/// <para>Also where a player waits out a full server. The queue is not a screen of its own because it is
/// not a different situation from the player's side — they pressed Login and are waiting for the server,
/// which is what this screen already means. The only difference is that the server can say how much
/// longer.</para>
/// </summary>
public sealed class LoadingScreen : IGameScreen
{
    private readonly ShellContext _ctx;

    public LoadingScreen(ShellContext ctx) => _ctx = ctx;

    public void OnEnter() { }

    /// <summary>Clears the queue position on the way out, whichever way the wait ended — let in, refused,
    /// or given up on. Nothing else knows the wait is over.</summary>
    public void OnExit()
    {
        _ctx.State.QueuePosition = 0;
        _ctx.State.QueueTotal = 0;
    }

    public void Update(GameTime gameTime, InputState input)
    {
        // Escape gives up the place. Only while queued: everywhere else this screen is a round trip that
        // is about to end on its own, and letting Escape abandon those would drop connections mid-login.
        if (_ctx.State.QueuePosition > 0 && input.IsKeyPressed(Keys.Escape))
        {
            _ctx.Transport.Disconnect();
            _ctx.Screens.Replace(new LoginScreen(_ctx));
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        var vp = _ctx.Graphics.Viewport.Bounds;
        UiHelper.DrawFilledRect(sb, vp, Color.Black);

        if (_ctx.State.QueuePosition > 0)
        {
            DrawQueue(sb, font, vp);
            return;
        }

        string msg = _ctx.State.LoadingMessage.Length > 0 ? _ctx.State.LoadingMessage : ClientStrings.Get(ClientStrings.LoadingScreen_DefaultMessage);
        var pos = UiHelper.CenterText(font, msg, vp);
        sb.DrawString(font, msg, pos, Color.White);
    }

    /// <summary>The position, and under it what happens next. The numbers arrive from the server; the
    /// sentence around them is written here, in whatever language the menus are in.</summary>
    private void DrawQueue(SpriteBatch sb, SpriteFont font, Rectangle vp)
    {
        string line = ClientStrings.Format(ClientStrings.LoadingScreen_QueuePosition,
            ("Position", _ctx.State.QueuePosition), ("Total", _ctx.State.QueueTotal));
        string hint = ClientStrings.Get(ClientStrings.LoadingScreen_QueueHint);

        var linePos = UiHelper.CenterText(font, line, vp);
        var hintPos = UiHelper.CenterText(font, hint, vp);
        float lineHeight = font.MeasureString(line).Y;

        sb.DrawString(font, line, new Vector2(linePos.X, linePos.Y - lineHeight), Color.White);
        sb.DrawString(font, hint, new Vector2(hintPos.X, hintPos.Y + lineHeight * 0.5f), Color.Gray);
    }
}
