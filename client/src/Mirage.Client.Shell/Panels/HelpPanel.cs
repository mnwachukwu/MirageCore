using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Floating, resizeable panel that hosts the chat-command reference, plus a [Controls] link to
/// open the picture-based controls reference.
/// Reuses <see cref="TextArea"/> so it scrolls and selects exactly like the chat window's log.
/// </summary>
public sealed class HelpPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 460, 320), minH: 140);
    private readonly TextArea _log = new() { ReadOnly = true };
    // A tight box (sized to the label) pinned 4 px in from the strip's right edge, so only the
    // link text is clickable — matching the inventory/bank sort links. Label + bounds are
    // refreshed in SyncControlsLink; the Link itself adds the "[...]" brackets so the locale
    // string stays plain.
    private const int LinkRightPad = 4;
    private readonly Link _controlsLink = new();

    private const int LinkStripH = 18;

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public void Toggle() { IsOpen = !IsOpen; }
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    /// <summary>Opens the Controls reference panel — wired by GameplayScreen.</summary>
    public Action? OnToggleControls { get; set; }

    /// <summary>Per-command help entry. <see cref="Syntax"/> is the literal slash-command form
    /// (not localized — slash commands don't translate); <see cref="DescKey"/> looks up the
    /// translated description text; <see cref="Min"/> is the tier at which the entry first appears.
    /// Each tier inherits all rows from lower tiers — see the plan's "Action set & access mapping".</summary>
    private readonly record struct CmdEntry(string Syntax, string DescKey, AdminLevel Min);

    private static readonly CmdEntry[] Commands =
    [
        new("/help",                       ClientStrings.HelpText_Cmd_Help,    AdminLevel.Player),
        new("/info [name]",                ClientStrings.HelpText_Cmd_Info,    AdminLevel.Player),
        new("/played",                     ClientStrings.HelpText_Cmd_Played,  AdminLevel.Player),
        new("/home",                       ClientStrings.HelpText_Cmd_Home,    AdminLevel.Player),
        new("/homecd",                     ClientStrings.HelpText_Cmd_HomeCd,  AdminLevel.Player),
        new("/who",                        ClientStrings.HelpText_Cmd_Who,     AdminLevel.Player),
        new("/fps",                        ClientStrings.HelpText_Cmd_Fps,     AdminLevel.Player),
        new("/inv",                        ClientStrings.HelpText_Cmd_Inv,     AdminLevel.Player),
        new("/stats",                      ClientStrings.HelpText_Cmd_Stats,   AdminLevel.Player),
        new("/train",                      ClientStrings.HelpText_Cmd_Train,   AdminLevel.Player),
        new("/join [name]",                ClientStrings.HelpText_Cmd_Join,    AdminLevel.Player),
        new("/leave",                      ClientStrings.HelpText_Cmd_Leave,   AdminLevel.Player),
        new("/trade [name]",               ClientStrings.HelpText_Cmd_Trade,   AdminLevel.Player),
        new("/roll [N]",                   ClientStrings.HelpText_Cmd_Roll,    AdminLevel.Player),
        new("/r",                          ClientStrings.HelpText_Cmd_Reply,   AdminLevel.Player),
        new("/kick name [minutes]",        ClientStrings.HelpText_Cmd_Kick,          AdminLevel.Monitor),
        new("/ban name",                   ClientStrings.HelpText_Cmd_Ban,           AdminLevel.Monitor),
        new("/mute name [minutes]",        ClientStrings.HelpText_Cmd_Mute,          AdminLevel.Monitor),
        new("/refreshbanlist",             ClientStrings.HelpText_Cmd_RefreshBanList, AdminLevel.Monitor),
        new("/loc",                        ClientStrings.HelpText_Cmd_Loc,           AdminLevel.Mapper),
        new("/debug",                      ClientStrings.HelpText_Cmd_Debug,         AdminLevel.Mapper),
        new("/warpto mapNum",              ClientStrings.HelpText_Cmd_WarpTo,        AdminLevel.Mapper),
        new("/setsprite N",                ClientStrings.HelpText_Cmd_SetSprite,     AdminLevel.Mapper),
        new("/mapreport",                  ClientStrings.HelpText_Cmd_MapReport,     AdminLevel.Mapper),
        new("/respawn",                    ClientStrings.HelpText_Cmd_Respawn,       AdminLevel.Mapper),
        new("/motd text",                  ClientStrings.HelpText_Cmd_Motd,          AdminLevel.Mapper),
        new("/godmode",                    ClientStrings.HelpText_Cmd_GodMode,       AdminLevel.Developer),
        new("/warpmeto name",              ClientStrings.HelpText_Cmd_WarpMeTo,      AdminLevel.Developer),
        new("/warptome name",             ClientStrings.HelpText_Cmd_WarpToMe,      AdminLevel.Developer),
        new("/tod day|dusk|night|dawn",   ClientStrings.HelpText_Cmd_Tod,           AdminLevel.Developer),
        new("/weather clear|rain|snow|heatwave|heavywind", ClientStrings.HelpText_Cmd_Weather, AdminLevel.Developer),
        new("/setaccess level name",       ClientStrings.HelpText_Cmd_SetAccess,     AdminLevel.Creator),
        new("/hwban name",                 ClientStrings.HelpText_Cmd_HwBan,         AdminLevel.Creator),
        new("/hwunban login",              ClientStrings.HelpText_Cmd_HwUnban,       AdminLevel.Creator),
        new("/startwar",                   ClientStrings.HelpText_Cmd_StartWar,      AdminLevel.Creator),
        new("/advancewar",                 ClientStrings.HelpText_Cmd_AdvanceWar,    AdminLevel.Creator),
        new("/endwar",                     ClientStrings.HelpText_Cmd_EndWar,        AdminLevel.Creator),
        new("/guildreset day|week|season", ClientStrings.HelpText_Cmd_GuildReset,    AdminLevel.Creator),
    ];

    // Two-color row: command syntax (Yellow, label-color used elsewhere for items/repair)
    // followed by " = <description>" in a softer body color. The " = " separator matches the
    // existing convention in HelpText_Say/Yell/Tell/etc. so the panel reads as one consistent
    // reference (e.g. "msghere = Say" sits visually with "/help = show this panel").
    private const int CmdSyntaxColor = GameColor.Yellow;
    private const int CmdDescColor = GameColor.White;
    private const string CmdSeparator = " = ";

    // Color for section headers ("Player Commands:", "Admin Commands:") — Pink matches the
    // existing "Social Commands:" header convention in the same panel.
    private const int SectionHeaderColor = GameColor.Pink;

    public void Populate(AdminLevel access, bool inGuild)
    {
        _log.Clear();

        // ── Social section (chat channels — always visible; slash syntax in yellow like the command
        //    tables, except the plain "Enter" line) ──────────────────────────
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_SocialHeader), SectionHeaderColor);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_EnterKey), GameColor.White);
        AddSocialLine(ClientStrings.HelpText_Say);
        AddSocialLine(ClientStrings.HelpText_Yell);
        AddSocialLine(ClientStrings.HelpText_Tell);
        AddSocialLine(ClientStrings.HelpText_Emote);
        AddSocialLine(ClientStrings.HelpText_Broadcast);
        // Guild + officer chat only apply while in a guild — hide both lines otherwise so the list
        // shows only commands the player can actually use.
        if (inGuild)
        {
            AddSocialLine(ClientStrings.HelpText_GuildChat);
            AddSocialLine(ClientStrings.HelpText_OfficerChat);
        }

        // ── Admin social channels (admin-only, styled like the social section) ──
        if (access > AdminLevel.Player)
        {
            _log.AddLine(string.Empty);
            _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_AdminSocialHeader), SectionHeaderColor);
            AddSocialLine(ClientStrings.HelpText_AdminNotice);
            AddSocialLine(ClientStrings.HelpText_AdminMsg);
        }

        // ── Chat tabs how-to ───────────────────────────────────────────────
        _log.AddLine(string.Empty);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_TabsHeader), SectionHeaderColor);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_TabAdd), GameColor.White);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_TabRemove), GameColor.White);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_TabConfig), GameColor.White);

        // ── Player slash commands (always at least one row — /help, /info, ...) ──
        _log.AddLine(string.Empty);
        _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_PlayerCommandsHeader), SectionHeaderColor);
        AppendCommandRows(access, adminTier: false);

        // ── Admin slash commands (only when at least one row will be visible) ──
        if (access > AdminLevel.Player)
        {
            _log.AddLine(string.Empty);
            _log.AddLine(ClientStrings.Get(ClientStrings.HelpText_AdminCommandsHeader), SectionHeaderColor);
            AppendCommandRows(access, adminTier: true);
        }

        // Reset scroll so the freshly-populated panel shows the top of the list. Without this
        // the TextArea opens scrolled to the newest (bottom) line, which is unhelpful for a
        // reference list that reads top-down.
        _log.ScrollToTop();
    }

    /// <summary>Emits the visible subset of <see cref="Commands"/>: player-tier rows when
    /// <paramref name="adminTier"/> is false, admin-tier rows (Min > Player) when true. Each row
    /// paints the slash-command syntax in <see cref="CmdSyntaxColor"/> and the description in
    /// <see cref="CmdDescColor"/> via a single ColorSpan on the prefix.</summary>
    private void AppendCommandRows(AdminLevel access, bool adminTier)
    {
        foreach (var entry in Commands)
        {
            if (access < entry.Min) continue;
            bool isAdminCmd = entry.Min > AdminLevel.Player;
            if (isAdminCmd != adminTier) continue;
            string text = entry.Syntax + CmdSeparator + ClientStrings.Get(entry.DescKey);
            var spans = new[] { new TextArea.ColorSpan(0, entry.Syntax.Length, CmdSyntaxColor) };
            _log.AddLine(text, CmdDescColor, names: null, colors: spans);
        }
    }

    /// <summary>Emits one social-section line ("<syntax> = <description>") with the syntax
    /// (everything before the " = " separator) painted in the command-syntax yellow, matching the command
    /// tables. A line without the separator renders plain (shouldn't happen for social entries).</summary>
    private void AddSocialLine(string key)
    {
        string text = ClientStrings.Get(key);
        int sep = text.IndexOf(CmdSeparator, System.StringComparison.Ordinal);
        if (sep <= 0)
        {
            _log.AddLine(text, CmdDescColor);
            return;
        }
        var spans = new[] { new TextArea.ColorSpan(0, sep, CmdSyntaxColor) };
        _log.AddLine(text, CmdDescColor, names: null, colors: spans);
    }

    // Cached so Draw can hand it to the Link widget. Set in Update each frame; Update always
    // runs before Draw, so the field is non-null by the time Draw reads it.
    private InputState? _lastInput;

    // Font captured in Draw and reused by Update's link hit-test on the next frame (Update runs
    // before Draw, so it can't measure the label itself). Null until the first Draw — the link
    // simply isn't clickable for that one frame, same as the inventory panel.
    private SpriteFont? _lastFont;

    public void Update(InputState input, bool isActive)
    {
        if (!IsOpen) return;
        _lastInput = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        // Refresh link layout each frame so panel-resize and locale changes both flow in, then
        // hit-test the link BEFORE the TextArea so its click doesn't fall through to text
        // selection. The layout needs the font (captured in Draw); on the first frame before any
        // Draw, skip — the link isn't drawn or clickable yet.
        if (_lastFont != null)
        {
            SyncControlsLink(_lastFont);
            if (_controlsLink.IsClicked(input))
            {
                OnToggleControls?.Invoke();
                input.ConsumeMouseClick();
                return;
            }
        }

        _log.SetBounds(LogBounds());
        _log.Update(input, keyboardActive: isActive);
    }

    public void Draw(SpriteBatch sb, SpriteFont font, long nowMs, bool isActive = false)
    {
        if (!IsOpen || _lastInput == null) return;
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.HelpPanel_Title), isActive);

        // [Controls] link at the top of the content area — opens the picture reference panel.
        _lastFont = font;
        SyncControlsLink(font);
        _controlsLink.Draw(sb, font, _lastInput);

        _log.SetBounds(LogBounds());
        _log.Draw(sb, font, nowMs);
        _panel.DrawOverlay(sb);
    }

    // Keeps _controlsLink's label + bounds in sync with the current panel bounds and locale.
    // The bounds are a tight box (label width) pinned to the strip's right edge. Needs the font
    // to measure the label; called from Draw (font in hand) and Update (via the cached font).
    // Idempotent and cheap, so running it twice per frame is fine.
    private void SyncControlsLink(SpriteFont font)
    {
        var strip = LinkStripRect();
        _controlsLink.Label = ClientStrings.Get(ClientStrings.Common_ControlsHeader);
        int w = (int)Math.Ceiling(Link.MeasureSize(font, _controlsLink.Label).X);
        _controlsLink.Bounds = new Rectangle(
            strip.Right - LinkRightPad - w, strip.Y, w, strip.Height);
    }

    private Rectangle LinkStripRect()
    {
        var c = _panel.ContentBounds;
        return new Rectangle(c.X, c.Y, c.Width, LinkStripH);
    }
    private Rectangle LogBounds()
    {
        var c = _panel.ContentBounds;
        return new Rectangle(c.X, c.Y + LinkStripH, c.Width, c.Height - LinkStripH);
    }
}
