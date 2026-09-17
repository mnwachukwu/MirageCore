using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>The credits, reachable from the main menu.</summary>
public sealed class CreditsScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly Button _cancelBtn;
    private InputState _input = new();

    /// <summary>The studio name, as a link to the site.
    ///
    /// <para>Stock <see cref="Link"/> styling — bracketed, gray, brightening on hover — because that is
    /// what a link looks like everywhere else in this client ([Mail], [Options], [Help]). Drawn bare and
    /// in the same color as the copyright line beside it, it reads as more of the sentence.</para>
    ///
    /// <para>Its box is measured and positioned in <see cref="Draw"/>: it sits immediately after the
    /// copyright prefix, so its left edge depends on the rendered width of text in whatever font and
    /// language are current. Update click-tests the box Draw last set, which costs the first frame and
    /// nothing after it.</para></summary>
    private readonly Link _siteLink = new() { Label = Credits.Studio };
    // The close button's caption is captured in the constructor, so a language switch made while
    // this screen is showing would leave it stale. The credit lines themselves are fetched inline
    // at draw time and need no refresh.
    private int _labelsGeneration = -1;

    private void RefreshLabels()
        => _cancelBtn.Label = ClientStrings.Get(ClientStrings.CreditsScreen_CloseButton);

    // frmCredits coordinates (twips / 15, offset by dialog 127, 148).
    // All labels: Left=3360=224px, Width=4455=297px.
    private static readonly Rectangle Dlg = new(127, 148, 546, 304);

    public CreditsScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _cancelBtn = new Button { Bounds = new Rectangle(399, 412, 200, 34), Label = ClientStrings.Get(ClientStrings.CreditsScreen_CloseButton) };
    }

    /// <summary>No setup needed; the screen holds no state beyond its fields.</summary>
    public void OnEnter() { }
    /// <summary>Nothing to release — the screen holds no resources beyond its fields.</summary>
    public void OnExit() { }

    /// <summary>Refresh the close button after a language switch, then handle clicks on the studio
    /// link and the close button.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        if (_siteLink.IsClicked(input)) UiHelper.OpenUrl(Credits.SiteUrl);
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new MainMenuScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, the credit lines, the copyright with its studio link, and the
    /// close button.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.CreditsScreen_Title));

        float lx = Dlg.X + 216f;

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_CreatorDeveloper), new Vector2(lx, Dlg.Y + 16), UiHelper.DlgLabelColor);
        sb.DrawString(font, Credits.Author, new Vector2(lx, Dlg.Y + 32), Color.LightPink);
        sb.DrawString(font, Credits.AuthorHandles, new Vector2(lx, Dlg.Y + 44), Color.LightPink);

        // ── Copyright ─────────────────────────────────────────────────────────
        // Drawn in two pieces so the studio half can be a link: the prefix, then the link box placed at
        // exactly the prefix's rendered width.
        string prefix = $"Copyright (c) {Credits.CopyrightYears(DateTime.Now.Year)} ";
        float copyrightY = Dlg.Y + 64;
        sb.DrawString(font, prefix, new Vector2(lx, copyrightY), Color.LightPink);

        // Measured off DisplayText, so the brackets are inside the clickable box rather than beside it.
        var linkSize = font.MeasureString(_siteLink.DisplayText);
        _siteLink.Bounds = new Rectangle(
            (int)(lx + font.MeasureString(prefix).X), (int)copyrightY,
            (int)linkSize.X, (int)linkSize.Y);
        _siteLink.Draw(sb, font, _input);

        _cancelBtn.Draw(sb, font, _input);
    }
}
