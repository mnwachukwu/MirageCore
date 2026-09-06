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

/// <summary>Per-tab rendering, mirroring the Update partial tab for tab.</summary>
public sealed partial class SocialPanel : IGamePanel
{
    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive = false)
    {
        if (!IsOpen) return;
        _state = state;
        long nowMs = Environment.TickCount64;

        if (_lastSocialVersion != state.SocialVersion || _builtTab != _activeTab)
        {
            _lastSocialVersion = state.SocialVersion;
            _builtTab = _activeTab;
            Rebuild(state);
        }
        RefreshLabels();

        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_Title), isActive);
        DrawTabStrip(sb, font);
        var body = BodyRect();

        if (_activeTab == TabGuild)
        {
            var info = state.GuildInfo;
            if (info is null || !info.InGuild)
            {
                DrawGuildlessView(sb, font, body);
            }
            else
            {
                DrawGuildSubTabStrip(sb, font, body);
                var gbody = GuildContentRect(body);
                switch (_guildSubTab)
                {
                    case GuildSub.Roster:
                        DrawGuildRoster(sb, font, gbody);
                        break;
                    case GuildSub.Vault:
                        DrawGuildVault(sb, font, info, gbody);
                        break;
                    case GuildSub.Quests:
                        DrawGuildQuests(sb, font, info, gbody);
                        break;
                    case GuildSub.Wars:
                        DrawWars(sb, font, state, gbody);
                        break;
                    case GuildSub.Territories:
                        DrawGuildTerritories(sb, font, gbody);
                        break;
                    case GuildSub.Standings:
                        DrawGuildStandings(sb, font, state, gbody);
                        break;
                    case GuildSub.Main:
                    default:
                        DrawGuildMain(sb, font, info, gbody);
                        break;
                }
            }
        }
        else
        {
            DrawSocialList(sb, font, body);
        }

        // Overlays draw last, over the tab body.
        if (_prompt.IsOpen) _prompt.Draw(sb, font, body, nowMs);
        else if (_confirm.IsOpen) _confirm.Draw(sb, font, body);
        else if (_labelEditing) DrawLabelEditor(sb, font, body);
        else if (_colorPicker.IsOpen) _colorPicker.Draw(sb, font, body, nowMs);
        else if (_reviewingApps) DrawAppsReview(sb, font, body);
        else if (_reviewingWarReqs) DrawWarReqs(sb, font, body);

        _panel.DrawOverlay(sb);
    }

    private void DrawSocialList(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        LayoutListTab(body, out var listRect);
        if (_list.Items.Count == 0)
        {
            string empty = ClientStrings.Get(_activeTab == TabFriends
                ? ClientStrings.SocialPanel_NoFriends
                : ClientStrings.SocialPanel_NoIgnored);
            UiHelper.DrawLabel(sb, font, empty, new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _list.Draw(sb, font, listRect);
        }

        // Add-by-name (label follows the tab: "Add Friend" / "Ignore"), then Remove (acts on the selected row).
        _addBtn.Label = ClientStrings.Get(_activeTab == TabFriends ? ClientStrings.ContextMenu_AddFriend : ClientStrings.ContextMenu_Ignore);
        _addBtn.Draw(sb, font, _input);
        _removeBtn.Draw(sb, font, _input,
            normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
    }

    // Main page: guild identity (color swatch + name/level, labels, MOTD), the level-progress bar, and the
    // settings + membership buttons.
    private void DrawGuildMain(SpriteBatch sb, SpriteFont font, GuildInfoPacket info, Rectangle gbody)
    {
        float maxW = gbody.Width - 8;
        float y = gbody.Y + Pad;
        // A swatch of the guild's chosen overhead color sits before the name (skipped while unset).
        int headerX = gbody.X + Pad;
        if (info.Color != 0)
        {
            var swatch = new Rectangle(headerX, (int)y + 2, 12, 12);
            UiHelper.DrawFilledRect(sb, swatch, new Color(GameColor.RedOf(info.Color), GameColor.GreenOf(info.Color), GameColor.BlueOf(info.Color)));
            UiHelper.DrawBorder(sb, swatch, Color.Gray);
            headerX += 16;
        }
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_GuildHeaderFormat,
                ("Name", info.Name), ("Level", info.Level)),
            new Vector2(headerX, y), Color.Gold, gbody.Right - Pad - headerX);
        y += RowH;
        string labels = info.Labels.Count > 0 ? string.Join(", ", info.Labels.Select(LabelName)) : "-";
        UiHelper.DrawLabel(sb, font, labels, new Vector2(gbody.X + Pad, y), Color.LightGray, maxW);
        y += RowH;
        if (info.Motd.Length > 0)
            UiHelper.DrawLabel(sb, font, info.Motd, new Vector2(gbody.X + Pad, y), new Color(140, 200, 140), maxW);

        // Level-progress bar sits below the fixed identity header.
        var bar = new Rectangle(gbody.X + Pad, gbody.Y + Pad + HeaderH + 2, (int)maxW, 16);
        DrawLevelProgress(sb, font, bar, info.Level, info.Exp);

        LayoutGuildMain(gbody);
        // Dynamic labels: the open toggle shows its state; Apps shows the pending count.
        _openBtn.Label = ClientStrings.Get(info.OpenForMembership ? ClientStrings.SocialPanel_OpenOn : ClientStrings.SocialPanel_OpenOff);
        _rankBtn.Label = ClientStrings.Get(info.ShowRankOverhead ? ClientStrings.SocialPanel_StandingOn : ClientStrings.SocialPanel_StandingOff);
        _appsBtn.Label = ClientStrings.Format(ClientStrings.SocialPanel_AppsFormat, ("Count", info.Applications.Count));
        _motdBtn.Draw(sb, font, _input);
        _labelsBtn.Draw(sb, font, _input);
        _colorBtn.Draw(sb, font, _input);
        DrawToggle(sb, font, _openBtn, info.OpenForMembership);
        DrawToggle(sb, font, _rankBtn, info.ShowRankOverhead);
        _leaveBtn.Draw(sb, font, _input);
        _disbandBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _appsBtn.Draw(sb, font, _input);
    }

    // A toggle button whose fill signals state at a glance: green when ON, a neutral gray when OFF. (A
    // non-leader's toggle is disabled and Button.Draw then falls back to the disabled look regardless.)
    private void DrawToggle(SpriteBatch sb, SpriteFont font, Button btn, bool on) => btn.Draw(sb, font, _input,
        normalColor: on ? UiHelper.PrimaryButtonNormal : UiHelper.ToggleOffBg,
        hoverColor: on ? UiHelper.PrimaryButtonHover : UiHelper.ToggleOffHover);

    // The guild's XP toward its next level as a bar with a "cur / span XP to Level N+1" readout (or "Max Level").
    private static void DrawLevelProgress(SpriteBatch sb, SpriteFont font, Rectangle bar, int level, long exp)
    {
        if (level >= Constants.GuildMaxLevel)
        {
            UiHelper.DrawVitalBar(sb, font, bar, 1f, UiHelper.ExpBarColor, Color.Gray,
                ClientStrings.Get(ClientStrings.SocialPanel_LevelMax), Color.White);
            return;
        }
        long cur = GuildLeveling.ExpForLevel(level);
        long next = GuildLeveling.ExpForLevel(level + 1);
        long into = Math.Max(0, exp - cur);
        long span = Math.Max(1, next - cur);
        float frac = Math.Clamp(into / (float)span, 0f, 1f);
        string text = ClientStrings.Format(ClientStrings.SocialPanel_LevelProgressFormat,
            ("Cur", into.ToString("N0")), ("Max", span.ToString("N0")), ("Next", level + 1));
        UiHelper.DrawVitalBar(sb, font, bar, frac, UiHelper.ExpBarColor, Color.Gray, text, Color.White);
    }

    // Roster page: the member Table + the member-action buttons.
    private void DrawGuildRoster(SpriteBatch sb, SpriteFont font, Rectangle gbody)
    {
        LayoutGuildRoster(gbody, out var tableRect);
        _rosterTable.Draw(sb, font, tableRect);
        _kickBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _promoteBtn.Draw(sb, font, _input);
        _demoteBtn.Draw(sb, font, _input);
        _transferBtn.Draw(sb, font, _input);
    }

    // Territories page: every territory, alphabetical — owner, weeks held, previous-week income, contesting.
    private void DrawGuildTerritories(SpriteBatch sb, SpriteFont font, Rectangle gbody)
    {
        LayoutGuildTerritories(gbody, out var tableRect);
        _territoryTable.Draw(sb, font, tableRect);
        _challengeBtn.Draw(sb, font, _input);
    }

    // The standings table sits below a one-line "Season N" header; Update + Draw share this rect so the
    // clickable sort-headers line up with what's drawn.
    private static Rectangle StandingsTableRect(Rectangle gbody)
    {
        int top = gbody.Y + Pad + HeaderH;
        return new Rectangle(gbody.X + Pad, top, gbody.Width - Pad * 2, Math.Max(0, gbody.Bottom - top - Pad));
    }

    private void DrawGuildStandings(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle gbody)
    {
        LayoutStandingsButtons(gbody);
        _historyBtn.Label = ClientStrings.Get(_viewingHistory ? ClientStrings.SocialPanel_Current : ClientStrings.SocialPanel_History);
        _prevSeasonBtn.Label = "<";
        _nextSeasonBtn.Label = ">";

        if (_viewingHistory)
        {
            var arc = state.SeasonArchive;
            string header = arc is { Found: true }
                ? ClientStrings.Format(ClientStrings.SocialPanel_ArchiveHeader, ("Season", arc.Season), ("Date", arc.EndDate))
                : ClientStrings.Get(ClientStrings.SocialPanel_NoArchive);
            sb.DrawString(font, header, new Vector2(gbody.X + Pad, gbody.Y + Pad), Color.White);
            _prevSeasonBtn.Draw(sb, font, _input);
            _nextSeasonBtn.Draw(sb, font, _input);
            if (arc is { Found: true }) _archiveTable.Draw(sb, font, StandingsTableRect(gbody));
        }
        else
        {
            int season = state.Leaderboard?.Season ?? 0;
            sb.DrawString(font, ClientStrings.Format(ClientStrings.SocialPanel_SeasonHeader, ("Season", season)),
                new Vector2(gbody.X + Pad, gbody.Y + Pad), Color.White);
            _standingsTable.Draw(sb, font, StandingsTableRect(gbody));
        }
        _historyBtn.Draw(sb, font, _input);
    }

    // Vault page: gold + valor balances, perk-suspended warning, and the donate / pay-tax actions.
    private void DrawGuildVault(SpriteBatch sb, SpriteFont font, GuildInfoPacket info, Rectangle gbody)
    {
        float maxW = gbody.Width - Pad * 2;
        float x = gbody.X + Pad;
        float y = gbody.Y + Pad;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_VaultHeader), new Vector2(x, y), Color.Gold, maxW);
        y += RowH;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_VaultFormat, ("Gold", info.VaultGold)),
            new Vector2(x, y), Color.LightGray, maxW);
        y += RowH;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_VaultValorFormat, ("Valor", info.VaultValor)),
            new Vector2(x, y), Color.LightGray, maxW);
        y += RowH;
        // Gold earned and not yet banked — what the next daily settlement pays in. Always drawn, including at
        // zero: a row that appears only when it happens to be non-zero cannot be told from one that is missing.
        UiHelper.DrawLabel(sb, font,
            ClientStrings.Format(ClientStrings.SocialPanel_VaultPendingFormat, ("Gold", info.PendingIncomeTotal)),
            new Vector2(x, y), new Color(140, 210, 140), maxW);
        y += RowH;
        if (!info.PerksActive)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_PerksSuspended),
                new Vector2(x, y), new Color(220, 120, 120), maxW);
            y += RowH;
        }

        // Weekly financial health — the SEASON-WEEK running totals (income / donations / war spend this week),
        // inflows green, outflows amber; discrete per-type numbers, no bottom-line total.
        y += RowH / 2;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WeeklyHeader), new Vector2(x, y), Color.Gold, maxW);
        y += RowH;
        var inflow = new Color(140, 210, 140);
        var outflow = new Color(210, 160, 120);
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WeeklyIncomeFormat, ("Gold", info.WeeklyIncome)), new Vector2(x, y), inflow, maxW);
        y += RowH;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WeeklyDonationsFormat, ("Gold", info.WeeklyDonations)), new Vector2(x, y), inflow, maxW);
        y += RowH;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WeeklyWarCostsFormat, ("Gold", info.WeeklyWarCosts)), new Vector2(x, y), outflow, maxW);
        y += RowH;
        // Tax sits on its OWN weekly cadence (the guild's founding weekday), so it shows the amount due + a
        // days-until countdown rather than a season-week running total.
        y += RowH / 2;
        long baseTax = GuildTaxFormulas.WeeklyTax(info.Level);
        long netTax = GuildTaxFormulas.EffectiveTax(info.Level, info.VaultValor);
        // Lead with the base weekly tax; when vault valor will offset some at settlement, also show the net owed.
        string taxText = netTax < baseTax
            ? ClientStrings.Format(ClientStrings.SocialPanel_WeeklyTaxValorFormat,
                ("Gold", baseTax), ("Net", netTax), ("Days", info.DaysUntilTax))
            : ClientStrings.Format(ClientStrings.SocialPanel_WeeklyTaxFormat,
                ("Gold", baseTax), ("Days", info.DaysUntilTax));
        UiHelper.DrawLabel(sb, font, taxText, new Vector2(x, y), outflow, maxW);

        // Vault log with a Donations (incoming) / Spending (outgoing war repairs) toggle over the recent
        // entries, newest first, filling the space down to the button row (older entries clip; server caps the
        // lists at GuildRecentVaultLogMax). The toggle buttons are laid out here at the running y and hit-tested
        // next frame in UpdateGuildVault (the standard cache-layout-from-Draw pattern); the active tab is green.
        y += RowH + RowH / 2;
        int toggleW = 84;
        _vaultDonationsBtn.Bounds = new Rectangle((int)x, (int)y, toggleW, ButtonH);
        _vaultSpendingBtn.Bounds = new Rectangle((int)x + toggleW + Pad, (int)y, toggleW, ButtonH);
        _vaultDonationsBtn.Draw(sb, font, _input,
            normalColor: _vaultShowSpending ? UiHelper.ToggleOffBg : UiHelper.PrimaryButtonNormal,
            hoverColor: _vaultShowSpending ? UiHelper.ToggleOffHover : UiHelper.PrimaryButtonHover);
        _vaultSpendingBtn.Draw(sb, font, _input,
            normalColor: _vaultShowSpending ? UiHelper.PrimaryButtonNormal : UiHelper.ToggleOffBg,
            hoverColor: _vaultShowSpending ? UiHelper.PrimaryButtonHover : UiHelper.ToggleOffHover);
        y += ButtonH + Pad;
        int logBottom = gbody.Bottom - ButtonH - Pad * 2;   // stop above the donate/tax button row
        if (!_vaultShowSpending)
        {
            if (info.RecentDonations.Count == 0)
            {
                UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_DonorLogEmpty), new Vector2(x, y), Color.Gray, maxW);
            }
            else
            {
                foreach (var d in info.RecentDonations)   // account + gold/valor donated (inflow-green)
                {
                    if (y + RowH > logBottom) break;
                    UiHelper.DrawLabel(sb, font, ClientStrings.Format(
                        d.Valor ? ClientStrings.SocialPanel_DonorRowValor : ClientStrings.SocialPanel_DonorRowGold,
                        ("Account", d.Account), ("Amount", d.Amount)), new Vector2(x, y), inflow, maxW);
                    y += RowH;
                }
            }
        }
        else
        {
            if (info.RecentSpending.Count == 0)
            {
                UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_SpendingLogEmpty), new Vector2(x, y), Color.Gray, maxW);
            }
            else
            {
                foreach (var s in info.RecentSpending)   // war-repair the vault absorbed, on behalf of a member (outflow-amber)
                {
                    if (y + RowH > logBottom) break;
                    UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_SpendingRow,
                        ("Account", s.Account), ("Char", s.Character), ("Amount", s.Amount)), new Vector2(x, y), outflow, maxW);
                    y += RowH;
                }
            }
        }

        LayoutGuildVault(gbody);
        _donateBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _donateValorBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _payTaxBtn.Draw(sb, font, _input);
    }

    // Quests page: the active quest (objective / reward / time left) or "none", and the acquire / reroll actions.
    private void DrawGuildQuests(SpriteBatch sb, SpriteFont font, GuildInfoPacket info, Rectangle gbody)
    {
        float maxW = gbody.Width - Pad * 2;
        float x = gbody.X + Pad;
        float y = gbody.Y + Pad;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_QuestsHeader), new Vector2(x, y), Color.Gold, maxW);
        y += RowH + RowH / 2;

        if (info.Quest is { } q)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_QuestObjectiveFormat,
                ("Progress", q.Progress), ("Count", q.Count), ("Mob", q.TargetNpcName)), new Vector2(x, y), Color.White, maxW);
            y += RowH;
            // Visual completion bar (fill = kills done / kills needed), with a percent readout.
            float frac = q.Count > 0 ? Math.Clamp(q.Progress / (float)q.Count, 0f, 1f) : 0f;
            UiHelper.DrawVitalBar(sb, font, new Rectangle((int)x, (int)y + 2, (int)maxW, 14), frac,
                new Color(90, 170, 90), Color.Gray, $"{(int)(frac * 100)}%", Color.White);
            y += RowH + 4;
            // At max guild level XP is eschewed (0), so show gold only rather than a misleading "0 XP".
            string rewardText = q.RewardExp > 0
                ? ClientStrings.Format(ClientStrings.SocialPanel_QuestRewardFormat, ("Exp", q.RewardExp), ("Gold", q.RewardGold))
                : ClientStrings.Format(ClientStrings.SocialPanel_QuestRewardGoldOnlyFormat, ("Gold", q.RewardGold));
            UiHelper.DrawLabel(sb, font, rewardText, new Vector2(x, y), new Color(140, 200, 140), maxW);
            y += RowH;
            long secs = Math.Max(0, q.ExpiresUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_QuestTimeFormat,
                ("Hours", secs / 3600), ("Mins", (secs % 3600) / 60)), new Vector2(x, y), Color.LightGray, maxW);
        }
        else
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_QuestNone), new Vector2(x, y), Color.Gray, maxW);
        }

        LayoutGuildQuests(gbody);
        _questAcquireBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _questAbandonBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
    }

    // Guildless: create-a-guild on-ramp + the open-guild browser with Apply.
    private void DrawGuildlessView(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        LayoutGuildlessView(body, out var browseRect);
        UiHelper.DrawLabelCentered(sb, font,
            ClientStrings.Format(ClientStrings.SocialPanel_CreateCostFormat, ("Cost", Constants.GuildCreationCost)),
            body.X, body.Y + Pad, body.Width, Color.LightGray);
        _createBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_BrowseHeader),
            new Vector2(browseRect.X, browseRect.Y - RowH + 2), Color.Gold, browseRect.Width);
        if (_browseList.Items.Count == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_NoOpenGuilds),
                new Vector2(browseRect.X + 4, browseRect.Y + 4), Color.Gray, browseRect.Width - 8);
        }
        else
        {
            _browseList.Draw(sb, font, browseRect);
        }

        _applyBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
    }

    private void DrawAppsReview(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        var bg = new Rectangle(body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);
        UiHelper.DrawFilledRect(sb, bg, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bg, UiHelper.ConfirmOverlayBorder);

        LayoutAppsReview(body, out var appRect);
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_AppsHeader),
            new Vector2(body.X + 8, body.Y + 6), Color.Yellow, body.Width - 16);
        if (_appList.Items.Count == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_NoApplications),
                new Vector2(appRect.X + 4, appRect.Y + 4), Color.Gray, appRect.Width - 8);
        }
        else
        {
            _appList.Draw(sb, font, appRect);
        }

        _approveBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _rejectBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _appsBackBtn.Draw(sb, font, _input);
    }

    // War page: the war list, the selected war's "War Status" area (favor / direction / scores), and the
    // context-adaptive action buttons.
    private void DrawWars(SpriteBatch sb, SpriteFont font, ClientState state, Rectangle body)
    {
        var info = state.GuildInfo;
        if (info is null || !info.InGuild) return;

        LayoutWars(body, out var listRect, out int statusY);

        if (_warList.Items.Count == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_NoWars),
                new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _warList.Draw(sb, font, listRect);
        }

        float x = body.X + Pad;
        float maxW = body.Width - Pad * 2;
        float y = statusY;
        var sel = SelectedWar(info);
        if (sel is null)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WarsHeader), new Vector2(x, y), Color.Gray, maxW);
        }
        else
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarHeaderFormat, ("Name", sel.OpponentName)),
                new Vector2(x, y), Color.Gold, maxW);
            y += RowH;
            // Daily maintenance for this war (only a one-sided war we declared has upkeep).
            if (sel.DailyCost > 0)
            {
                UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarDailyCostFormat, ("Gold", sel.DailyCost)),
                    new Vector2(x, y), new Color(210, 160, 120), maxW);
                y += RowH;
            }
            switch (sel.Status)
            {
                case GuildWarStatus.Warmup:
                    long toLive = Math.Max(0, sel.GoLiveUtc - now);
                    UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarWarmupFormat,
                        ("Mins", toLive / 60), ("Secs", toLive % 60)), new Vector2(x, y), Color.LightGray, maxW);
                    break;
                case GuildWarStatus.OneSidedAggressor:
                    UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WarSelfLimiting),
                        new Vector2(x, y), Color.LightGray, maxW);
                    y += RowH;
                    long toRetract = sel.DeclaredUtc + Constants.GuildWarRetractionLockSeconds - now;
                    string lockLine = toRetract > 0
                        ? ClientStrings.Format(ClientStrings.SocialPanel_WarRetractLockFormat, ("Mins", toRetract / 60), ("Secs", toRetract % 60))
                        : ClientStrings.Get(ClientStrings.SocialPanel_WarRetractReady);
                    UiHelper.DrawLabel(sb, font, lockLine, new Vector2(x, y), Color.LightGray, maxW);
                    break;
                case GuildWarStatus.OneSidedDefender:
                    UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WarDefenderNote),
                        new Vector2(x, y), new Color(140, 200, 140), maxW);
                    break;
                case GuildWarStatus.Mutual:
                    DrawWarMutualStatus(sb, font, sel, state, x, ref y, maxW);
                    break;
            }
        }

        // Dynamic labels: the peace + wager buttons toggle offer/withdraw; Requests shows the queue count.
        _warPeaceBtn.Label = ClientStrings.Get(sel is { PeaceOfferedByUs: true }
            ? ClientStrings.SocialPanel_WarWithdrawButton : ClientStrings.SocialPanel_WarPeaceButton);
        _warWagerBtn.Label = ClientStrings.Get(sel is { WagerProposedByUs: > 0 }
            ? ClientStrings.SocialPanel_WarWagerWithdrawButton : ClientStrings.SocialPanel_WarWagerButton);
        _warReqsBtn.Label = ClientStrings.Format(ClientStrings.SocialPanel_WarReqsFormat, ("Count", info.WarRequests.Count));

        _warRetractBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _warPeaceBtn.Draw(sb, font, _input);
        _warAcceptBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _warRejectBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _warWagerBtn.Draw(sb, font, _input);
        _warWagerAcceptBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _warWagerRejectBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _warDeclareBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _warReqsBtn.Draw(sb, font, _input);
    }

    // Mutual war "War Status": numeric scores, the advantage + trend line, a center-anchored tug bar, and any
    // pending peace plea. Advances y as it goes.
    private void DrawWarMutualStatus(SpriteBatch sb, SpriteFont font, GuildWarView sel, ClientState state, float x, ref float y, float maxW)
    {
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarScoreFormat,
            ("Ours", sel.Attrition), ("Theirs", sel.OpponentAttrition)), new Vector2(x, y), Color.White, maxW);
        y += RowH;

        bool us = sel.Attrition > sel.OpponentAttrition;
        bool them = sel.Attrition < sel.OpponentAttrition;
        string favor = ClientStrings.Get(us ? ClientStrings.SocialPanel_WarFavorUs
            : them ? ClientStrings.SocialPanel_WarFavorThem : ClientStrings.SocialPanel_WarFavorEven);
        int trend = state.WarTrend(sel.OpponentIndex);
        string dir = ClientStrings.Get(trend > 0 ? ClientStrings.SocialPanel_WarTrendUp
            : trend < 0 ? ClientStrings.SocialPanel_WarTrendDown : ClientStrings.SocialPanel_WarTrendFlat);
        Color favorColor = us ? new Color(140, 200, 140) : them ? new Color(220, 120, 120) : Color.LightGray;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarTrendFormat, ("Favor", favor), ("Dir", dir)),
            new Vector2(x, y), favorColor, maxW);
        y += RowH;

        DrawTugBar(sb, new Rectangle((int)x, (int)y + 2, (int)maxW, 8), sel.Attrition, sel.OpponentAttrition);
        y += RowH;

        if (sel.PeaceOfferedByThem)
        {
            string s = sel.PeaceEscrowByThem > 0
                ? ClientStrings.Format(ClientStrings.SocialPanel_WarPeaceIncomingGold, ("Gold", sel.PeaceEscrowByThem))
                : ClientStrings.Get(ClientStrings.SocialPanel_WarPeaceIncoming);
            UiHelper.DrawLabel(sb, font, s, new Vector2(x, y), Color.Yellow, maxW);
        }
        else if (sel.PeaceOfferedByUs)
        {
            string s = sel.PeaceEscrowByUs > 0
                ? ClientStrings.Format(ClientStrings.SocialPanel_WarPeaceOutgoingGold, ("Gold", sel.PeaceEscrowByUs))
                : ClientStrings.Get(ClientStrings.SocialPanel_WarPeaceOutgoing);
            UiHelper.DrawLabel(sb, font, s, new Vector2(x, y), new Color(140, 200, 220), maxW);
        }
        y += RowH;

        // Wager line: a locked ante, a pending proposal either way, or an open-window invite.
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (sel.AnteEscrow > 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarAnteFormat,
                ("Ante", sel.AnteEscrow), ("Pot", sel.AnteEscrow * 2)), new Vector2(x, y), new Color(220, 200, 120), maxW);
        }
        else if (sel.WagerProposedByThem > 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarWagerIncomingFormat,
                ("Gold", sel.WagerProposedByThem)), new Vector2(x, y), Color.Yellow, maxW);
        }
        else if (sel.WagerProposedByUs > 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarWagerOutgoingFormat,
                ("Gold", sel.WagerProposedByUs)), new Vector2(x, y), new Color(140, 200, 220), maxW);
        }
        else if (now < sel.WagerDeadlineUtc)
        {
            long left = sel.WagerDeadlineUtc - now;
            UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WarWagerWindowFormat,
                ("Mins", left / 60), ("Secs", left % 60)), new Vector2(x, y), Color.Gray, maxW);
        }
    }

    // A tug-of-war bar: our meter (green) grows leftward from the midline, the opponent's (red) rightward,
    // each scaled to the shared attrition pool. Balanced (both full) fills the bar; a losing side recedes.
    private static void DrawTugBar(SpriteBatch sb, Rectangle bar, int ours, int theirs)
    {
        UiHelper.DrawFilledRect(sb, bar, new Color(30, 30, 30));
        int pool = Constants.GuildWarAttritionPool;
        int mid = bar.X + bar.Width / 2;
        int oursW = (int)(bar.Width / 2 * (Math.Clamp(ours, 0, pool) / (float)pool));
        int theirsW = (int)(bar.Width / 2 * (Math.Clamp(theirs, 0, pool) / (float)pool));
        if (oursW > 0) UiHelper.DrawFilledRect(sb, new Rectangle(mid - oursW, bar.Y, oursW, bar.Height), new Color(80, 180, 80));
        if (theirsW > 0) UiHelper.DrawFilledRect(sb, new Rectangle(mid, bar.Y, theirsW, bar.Height), new Color(200, 80, 80));
        UiHelper.DrawBorder(sb, bar, Color.DimGray);
    }

    // War-requests review overlay (leader): the officer queue with Accept / Deny / Back.
    private void DrawWarReqs(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        var bg = new Rectangle(body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);
        UiHelper.DrawFilledRect(sb, bg, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bg, UiHelper.ConfirmOverlayBorder);

        LayoutWarReqs(body, out var listRect);
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WarReqsHeader),
            new Vector2(body.X + 8, body.Y + 6), Color.Yellow, body.Width - 16);
        if (_warReqList.Items.Count == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_NoWarReqs),
                new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _warReqList.Draw(sb, font, listRect);
        }

        _warReqAcceptBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _warReqDenyBtn.Draw(sb, font, _input, normalColor: UiHelper.DangerButtonNormal, hoverColor: UiHelper.DangerButtonHover);
        _warReqBackBtn.Draw(sb, font, _input);
    }

    private void DrawLabelEditor(SpriteBatch sb, SpriteFont font, Rectangle body)
    {
        var bg = new Rectangle(body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);
        UiHelper.DrawFilledRect(sb, bg, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bg, UiHelper.ConfirmOverlayBorder);

        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_LabelsHeader),
            new Vector2(body.X + 8, body.Y + 6), Color.Yellow, body.Width - 16);

        LayoutLabelEditor(body);
        for (int i = 0; i < _labelBtns.Length; i++)
        {
            bool active = _pendingLabels.Contains(AllLabels[i]);
            _labelBtns[i].Draw(sb, font, _input,
                normalColor: active ? UiHelper.PrimaryButtonNormal : UiHelper.ButtonNormalBg,
                hoverColor: active ? UiHelper.PrimaryButtonHover : UiHelper.ButtonHoverBg);
        }
        _labelSaveBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _labelCancelBtn.Draw(sb, font, _input);
    }
}
