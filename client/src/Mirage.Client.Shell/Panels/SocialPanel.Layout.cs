using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>Tab strips and the per-tab rectangle math — where each list, table and button row sits
/// inside the panel body. Shared by the Update and Draw passes so hit-testing and drawing can never
/// disagree about a control's position.</summary>
public sealed partial class SocialPanel : IGamePanel
{
    private void DrawTabStrip(SpriteBatch sb, SpriteFont font)
    {
        var content = _panel.ContentBounds;
        UiHelper.DrawFilledRect(sb, new Rectangle(content.X, content.Y, content.Width, TabStripH), UiHelper.PanelTitleBg);

        var tabs = ComputeTabRects();
        for (int i = 0; i < tabs.Length; i++)
        {
            var r = tabs[i];
            bool active = i == _activeTab;
            bool hovered = !active && r.Contains(_lastMousePos);
            TabStrip.DrawCenteredTab(sb, font, r, ClientStrings.Get(TabKey(i)), active, hovered);
        }
    }

    // The guild sub-tab strip (second level), pinned to the top of the guild body when in-guild.
    private void DrawGuildSubTabStrip(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        UiHelper.DrawFilledRect(sb, new Rectangle(body.X, body.Y, body.Width, TabStripH), UiHelper.PanelTitleBg);
        var tabs = ComputeGuildSubTabRects(body);
        for (int i = 0; i < tabs.Length; i++)
        {
            var r = tabs[i];
            bool active = (GuildSub)i == _guildSubTab;
            bool hovered = !active && r.Contains(_lastMousePos);
            TabStrip.DrawCenteredTab(sb, font, r, ClientStrings.Get(GuildSubTabKey((GuildSub)i)), active, hovered);
        }
    }

    private static string TabKey(int tab) => tab switch
    {
        TabIgnore => ClientStrings.SocialPanel_IgnoreTab,
        TabGuild => ClientStrings.SocialPanel_GuildTab,
        _ => ClientStrings.SocialPanel_FriendsTab,
    };

    private static string GuildSubTabKey(GuildSub tab) => tab switch
    {
        GuildSub.Main => ClientStrings.SocialPanel_SubTabMain,
        GuildSub.Roster => ClientStrings.SocialPanel_SubTabRoster,
        GuildSub.Vault => ClientStrings.SocialPanel_SubTabVault,
        GuildSub.Quests => ClientStrings.SocialPanel_SubTabQuests,
        GuildSub.Wars => ClientStrings.SocialPanel_SubTabWars,
        GuildSub.Territories => ClientStrings.SocialPanel_SubTabTerritories,
        GuildSub.Standings => ClientStrings.SocialPanel_SubTabStandings,
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    private Rectangle[] ComputeTabRects()
    {
        var content = _panel.ContentBounds;
        int availW = content.Width - TabGap * (TabCount + 1);
        int tabW = Math.Max(MinTabW, availW / TabCount);
        int totalW = tabW * TabCount + TabGap * (TabCount - 1);
        int startX = content.X + (content.Width - totalW) / 2;
        var rects = new Rectangle[TabCount];
        for (int i = 0; i < TabCount; i++)
            rects[i] = new Rectangle(startX + i * (tabW + TabGap), content.Y + 2, tabW, TabStripH - 4);
        return rects;
    }

    // The guild sub-tab strip's rects (5 tabs across the guild body's top).
    private Rectangle[] ComputeGuildSubTabRects(Rectangle body)
    {
        int availW = body.Width - TabGap * (AllGuildSubs.Length + 1);
        int tabW = Math.Max(MinSubTabW, availW / AllGuildSubs.Length);
        int totalW = tabW * AllGuildSubs.Length + TabGap * (AllGuildSubs.Length - 1);
        int startX = body.X + (body.Width - totalW) / 2;
        var rects = new Rectangle[AllGuildSubs.Length];
        for (int i = 0; i < AllGuildSubs.Length; i++)
            rects[i] = new Rectangle(startX + i * (tabW + TabGap), body.Y + 2, tabW, TabStripH - 4);
        return rects;
    }

    private Rectangle BodyRect()
    {
        var c = _panel.ContentBounds;
        return new Rectangle(c.X, c.Y + TabStripH, c.Width, c.Height - TabStripH);
    }

    // The guild page's content area, below the sub-tab strip.
    private static Rectangle GuildContentRect(Rectangle body)
        => new(body.X, body.Y + TabStripH, body.Width, Math.Max(0, body.Height - TabStripH));

    // Friends/Ignore: a list filling the body above a single Remove button.
    private void LayoutListTab(Rectangle body, out Rectangle listRect)
    {
        int btnY = body.Bottom - ButtonH - Pad;
        _removeBtn.Bounds = new Rectangle(body.Right - Pad - 90, btnY, 90, ButtonH);
        _addBtn.Bounds = new Rectangle(_removeBtn.Bounds.Left - Pad - 90, btnY, 90, ButtonH);
        listRect = new Rectangle(body.X + Pad, body.Y + Pad, body.Width - Pad * 2, Math.Max(0, btnY - body.Y - Pad * 2));
    }

    // Main page: two button rows at the bottom — leader settings, then membership + apps.
    private void LayoutGuildMain(Rectangle gbody)
    {
        // Three bottom-anchored rows grouping the buttons cleanly: settings / toggles / membership.
        int rowCY = gbody.Bottom - ButtonH - Pad;
        int rowBY = rowCY - ButtonH - 2;
        int rowAY = rowBY - ButtonH - 2;
        LayoutRow(gbody, rowAY, _motdBtn, _labelsBtn, _colorBtn);
        LayoutRow(gbody, rowBY, _openBtn, _rankBtn);
        LayoutRow(gbody, rowCY, _leaveBtn, _disbandBtn, _appsBtn);
    }

    // Roster page: the Table fills to a single member-action button row.
    private void LayoutGuildRoster(Rectangle gbody, out Rectangle tableRect)
    {
        int rowY = gbody.Bottom - ButtonH - Pad;
        LayoutRow(gbody, rowY, _kickBtn, _promoteBtn, _demoteBtn, _transferBtn);
        int top = gbody.Y + Pad;
        tableRect = new Rectangle(gbody.X + Pad, top, gbody.Width - Pad * 2, Math.Max(0, rowY - top - Pad));
    }

    // Vault page: one button row (donate gold / donate valor / pay tax).
    private void LayoutGuildVault(Rectangle gbody)
    {
        int rowY = gbody.Bottom - ButtonH - Pad;
        LayoutRow(gbody, rowY, _donateBtn, _donateValorBtn, _payTaxBtn);
    }

    // Quests page: one button row (acquire / abandon).
    private void LayoutGuildQuests(Rectangle gbody)
    {
        int rowY = gbody.Bottom - ButtonH - Pad;
        LayoutRow(gbody, rowY, _questAcquireBtn, _questAbandonBtn);
    }

    // Guildless: a centered Create button up top, then the open-guild browser filling to an Apply button.
    private void LayoutGuildlessView(Rectangle body, out Rectangle browseRect)
    {
        int createY = body.Y + Pad + RowH;   // a header line sits above
        _createBtn.Bounds = new Rectangle(body.X + (body.Width - 130) / 2, createY, 130, ButtonH);
        int btnY = body.Bottom - ButtonH - Pad;
        _applyBtn.Bounds = new Rectangle(body.Right - Pad - 90, btnY, 90, ButtonH);
        int listTop = createY + ButtonH + RowH;   // gap + an "Open guilds:" header line
        browseRect = new Rectangle(body.X + Pad, listTop, body.Width - Pad * 2, Math.Max(0, btnY - listTop - Pad));
    }

    // Applications review overlay: a header line, the applicant list, then Approve / Reject / Back.
    private void LayoutAppsReview(Rectangle body, out Rectangle appRect)
    {
        int btnY = body.Bottom - ButtonH - Pad;
        LayoutRow(body, btnY, _approveBtn, _rejectBtn, _appsBackBtn);
        int top = body.Y + Pad + RowH;
        appRect = new Rectangle(body.X + Pad, top, body.Width - Pad * 2, Math.Max(0, btnY - top - Pad));
    }

    // War page: the war list up top, the selected war's status area beneath it, then three button rows
    // (peace actions, wager actions, then declare/requests).
    private void LayoutWars(Rectangle body, out Rectangle listRect, out int statusY)
    {
        int rowCY = body.Bottom - ButtonH - Pad;
        int rowBY = rowCY - ButtonH - 2;
        int rowAY = rowBY - ButtonH - 2;
        LayoutRow(body, rowAY, _warRetractBtn, _warPeaceBtn, _warAcceptBtn, _warRejectBtn);
        LayoutRow(body, rowBY, _warWagerBtn, _warWagerAcceptBtn, _warWagerRejectBtn);
        LayoutRow(body, rowCY, _warDeclareBtn, _warReqsBtn);
        int statusH = RowH * 6;   // header + score + favor/trend + bar + peace + wager line
        statusY = rowAY - 2 - statusH;
        int listTop = body.Y + Pad;
        listRect = new Rectangle(body.X + Pad, listTop, body.Width - Pad * 2, Math.Max(0, statusY - listTop - Pad));
    }

    // War-requests overlay: a header line, the request list, then Accept / Deny / Back.
    private void LayoutWarReqs(Rectangle body, out Rectangle listRect)
    {
        int btnY = body.Bottom - ButtonH - Pad;
        LayoutRow(body, btnY, _warReqAcceptBtn, _warReqDenyBtn, _warReqBackBtn);
        int top = body.Y + Pad + RowH;
        listRect = new Rectangle(body.X + Pad, top, body.Width - Pad * 2, Math.Max(0, btnY - top - Pad));
    }

    private static void LayoutRow(Rectangle body, int y, params Button[] buttons)
    {
        int n = buttons.Length;
        int w = (body.Width - Pad * (n + 1)) / n;
        for (int i = 0; i < n; i++)
            buttons[i].Bounds = new Rectangle(body.X + Pad + i * (w + Pad), y, w, ButtonH);
    }

    private void LayoutLabelEditor(Rectangle body)
    {
        const int cols = 3;
        int rows = (AllLabels.Length + cols - 1) / cols;
        int gridTop = body.Y + 24;
        int cancelY = body.Bottom - ButtonH - Pad;
        int cellW = (body.Width - Pad * (cols + 1)) / cols;
        int cellH = Math.Max(ButtonH, (cancelY - Pad - gridTop) / rows - Pad);
        for (int i = 0; i < _labelBtns.Length; i++)
        {
            int col = i % cols, row = i / cols;
            _labelBtns[i].Bounds = new Rectangle(body.X + Pad + col * (cellW + Pad), gridTop + row * (cellH + Pad), cellW, cellH);
        }
        _labelSaveBtn.Bounds = UiHelper.PanelBottomButton(body, 0);
        _labelCancelBtn.Bounds = UiHelper.PanelBottomButton(body, 1);
    }

    // Standings header buttons (right-aligned on the header row): History toggle + prev/next season paging.
    private void LayoutStandingsButtons(Rectangle gbody)
    {
        int y = gbody.Y + Pad;
        _historyBtn.Bounds = new Rectangle(gbody.Right - Pad - HistoryBtnW, y, HistoryBtnW, ButtonH);
        _nextSeasonBtn.Bounds = new Rectangle(_historyBtn.Bounds.Left - Pad - SeasonNavW, y, SeasonNavW, ButtonH);
        _prevSeasonBtn.Bounds = new Rectangle(_nextSeasonBtn.Bounds.Left - Pad - SeasonNavW, y, SeasonNavW, ButtonH);
    }

    private void LayoutGuildTerritories(Rectangle gbody, out Rectangle tableRect)
    {
        int rowY = gbody.Bottom - ButtonH - Pad;
        _challengeBtn.Bounds = new Rectangle(gbody.Right - Pad - 130, rowY, 130, ButtonH);
        int top = gbody.Y + Pad;
        tableRect = new Rectangle(gbody.X + Pad, top, gbody.Width - Pad * 2, Math.Max(0, rowY - top - Pad));
    }
}
