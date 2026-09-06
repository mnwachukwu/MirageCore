using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>Scrolling credits, reachable from the main menu.</summary>
public sealed class CreditsScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly Button _cancelBtn;
    private InputState _input = new();

    /// <summary>The studio name, as a link to the site.
    ///
    /// <para>Stock <see cref="Link"/> styling — bracketed, grey, brightening on hover — because that is
    /// what a link looks like everywhere else in this client ([Mail], [Options], [Help]). Drawn bare and
    /// in the same colour as the copyright line beside it, it reads as more of the sentence.</para>
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

    /// <summary>No setup needed; the scroll position resets with the instance.</summary>
    public void OnEnter() { }
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
        if (_siteLink.IsClicked(input)) UiHelper.OpenUrl(Credits.SiteUrl);
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new MainMenuScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out var content, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.CreditsScreen_Title));

        float lx = Dlg.X + 216f;

        // ── Original VB6 Implementation ──────────────────────────────────────
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_SectionVB6), new Vector2(lx, Dlg.Y + 16), Color.Gold);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_Programming), new Vector2(lx, Dlg.Y + 36), UiHelper.DlgLabelColor);
        sb.DrawString(font, "Chris Kremer", new Vector2(lx, Dlg.Y + 52), Color.LightPink);
        sb.DrawString(font, "(Torquel / Valient / Consty)", new Vector2(lx, Dlg.Y + 64), Color.LightPink);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_ArtMusic), new Vector2(lx, Dlg.Y + 84), UiHelper.DlgLabelColor);
        sb.DrawString(font, "Copyright (c) Square Soft", new Vector2(lx, Dlg.Y + 100), Color.LightPink);

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_GuiArt), new Vector2(lx, Dlg.Y + 120), UiHelper.DlgLabelColor);
        sb.DrawString(font, "Jess Triska (Loken)", new Vector2(lx, Dlg.Y + 136), Color.LightPink);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_GuiArtNote), new Vector2(lx, Dlg.Y + 150), Color.Gray);

        // Divider between the two teams
        UiHelper.DrawFilledRect(sb,
            new Rectangle(content.X + 8, Dlg.Y + 170, content.Width - 16, 1),
            UiHelper.DlgBorderColor);

        // ── C# Implementation ─────────────────────────────────────────────────
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_SectionCSharp), new Vector2(lx, Dlg.Y + 180), Color.Gold);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Credits_CreatorDeveloper), new Vector2(lx, Dlg.Y + 196), UiHelper.DlgLabelColor);
        sb.DrawString(font, Credits.Author, new Vector2(lx, Dlg.Y + 212), Color.LightPink);
        sb.DrawString(font, Credits.AuthorHandles, new Vector2(lx, Dlg.Y + 224), Color.LightPink);

        // ── Copyright ─────────────────────────────────────────────────────────
        // Drawn in two pieces so the studio half can be a link: the prefix, then the link box placed at
        // exactly the prefix's rendered width.
        string prefix = $"Copyright (c) {Credits.CopyrightYears(DateTime.Now.Year)} ";
        float copyrightY = Dlg.Y + 244;
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
