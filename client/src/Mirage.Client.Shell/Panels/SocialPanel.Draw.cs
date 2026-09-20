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
                DrawGuildlessView(sb, font, body, state.GuildCost);
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
        UiHelper.DrawLabel(sb, font, info.Name, new Vector2(headerX, y), Color.Gold, gbody.Right - Pad - headerX);
        y += RowH;
        string labels = info.Labels.Count > 0 ? string.Join(", ", info.Labels.Select(LabelName)) : "-";
        UiHelper.DrawLabel(sb, font, labels, new Vector2(gbody.X + Pad, y), Color.LightGray, maxW);
        y += RowH;
        if (info.Motd.Length > 0)
            UiHelper.DrawLabel(sb, font, info.Motd, new Vector2(gbody.X + Pad, y), new Color(140, 200, 140), maxW);

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

        // Where the vault's gold came from — inflows green, outflows amber; discrete per-type numbers, no
        // bottom-line total.
        y += RowH / 2;
        UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.SocialPanel_WeeklyHeader), new Vector2(x, y), Color.Gold, maxW);
        y += RowH;
        var inflow = new Color(140, 210, 140);
        var outflow = new Color(210, 160, 120);
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WeeklyIncomeFormat, ("Gold", info.Income)), new Vector2(x, y), inflow, maxW);
        y += RowH;
        UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_WeeklyDonationsFormat, ("Gold", info.Donations)), new Vector2(x, y), inflow, maxW);

        // Vault log with a Donations (incoming) / Spending (outgoing) toggle over the recent
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
                foreach (var d in info.RecentDonations)   // account + gold donated (inflow-green)
                {
                    if (y + RowH > logBottom) break;
                    UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.SocialPanel_DonorRowGold,
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
                foreach (var s in info.RecentSpending)   // a payment the vault absorbed for a member (outflow-amber)
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
    }

    // Guildless: create-a-guild on-ramp + the open-guild browser with Apply.
    private void DrawGuildlessView(SpriteBatch sb, SpriteFont font, Rectangle body, int guildCost)
    {
        LayoutGuildlessView(body, out var browseRect);
        UiHelper.DrawLabelCentered(sb, font,
            ClientStrings.Format(ClientStrings.SocialPanel_CreateCostFormat, ("Cost", guildCost)),
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
