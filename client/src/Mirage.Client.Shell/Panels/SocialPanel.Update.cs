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

/// <summary>Per-tab input handling: the friends/ignore list, the guild tabs (main, roster,
/// territories, vault, quests, standings), wars, and the application and war-request queues.</summary>
public sealed partial class SocialPanel : IGamePanel
{
    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive = false)
    {
        ColumnsChanged = false;
        if (!IsOpen) return;
        _input = input;
        _lastMousePos = input.MousePosition;
        TabChanged = false;
        long nowMs = Environment.TickCount64;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            _prompt.Close();
            _labelEditing = false;
            _colorPicker.Close();
            _confirm.Close();
            _reviewingApps = false;
            return;
        }

        // Opened directly on the restored Guild tab: pull a fresh roster + open-guild list (the tab-switch
        // path below does the same, but a restore-on-open never runs it).
        if (_pendingGuildRefresh)
        {
            _pendingGuildRefresh = false;
            sender.SendGuildInfoRequest();
            sender.SendGuildBrowseRequest();
        }

        var body = BodyRect();

        // Modal sub-surfaces take precedence over everything else in the panel.
        if (_prompt.IsOpen)
        {
            _prompt.Update(input, body, nowMs);
            return;
        }
        if (_confirm.IsOpen)
        {
            _confirm.Update(input);
            return;
        }
        if (_labelEditing)
        {
            UpdateLabelEditor(input, sender, body);
            return;
        }
        if (_colorPicker.IsOpen)
        {
            _colorPicker.Update(input, body, nowMs);
            return;
        }
        if (_reviewingApps)
        {
            UpdateAppsReview(input, sender, body);
            return;
        }

        var tabs = ComputeTabRects();
        for (int i = 0; i < tabs.Length; i++)
        {
            if (!input.IsClickIn(tabs[i]) || i == _activeTab) continue;
            _activeTab = i;
            TabChanged = true;   // let the host persist the new tab
            _list.SelectedIndex = -1;
            Invalidate();
            // Re-request the guild data + the open-guild browser whenever the Guild tab opens: the
            // roster's live online column and the browser list have no push, so a refresh keeps them
            // current. (A guildless player uses the browser; a member's client just ignores it.)
            if (i == TabGuild)
            {
                sender.SendGuildInfoRequest();
                sender.SendGuildBrowseRequest();
            }
        }

        if (_activeTab == TabGuild) UpdateGuild(input, state, sender, body);
        else UpdateSocialList(input, sender, body);
    }

    private void UpdateSocialList(InputState input, ClientPacketSender sender, Rectangle body)
    {
        LayoutListTab(body, out var listRect);
        _list.Update(input, listRect, keyboardActive: false);

        // Add a friend / ignored account BY NAME (parity with the right-click menu): the shared name prompt feeds
        // the add for the active tab. The server resolves the name + validates (self/unknown/dupes).
        if (_addBtn.IsClicked(input))
        {
            bool friends = _activeTab == TabFriends;
            _prompt.Open(ClientStrings.Get(ClientStrings.Common_NameLabel), "", Constants.NameLength, allowEmpty: false,
                name => { if (friends) sender.SendSocialAddFriend(name); else sender.SendSocialAddIgnore(name); });
            return;
        }

        string login = SelectedLogin();
        _removeBtn.Enabled = login.Length > 0;
        if (!_removeBtn.IsClicked(input) || login.Length == 0) return;
        if (_activeTab == TabFriends) sender.SendSocialRemoveFriend(login);
        else sender.SendSocialRemoveIgnore(login);
        _list.SelectedIndex = -1;
    }

    // Guild tab: the guildless create/browse on-ramp, or (in-guild) a sub-tab strip over the active page.
    private void UpdateGuild(InputState input, ClientState state, ClientPacketSender sender, Rectangle body)
    {
        var info = state.GuildInfo;
        if (info is null || !info.InGuild)
        {
            // Guildless: create-a-guild button on top, then the open-guild browser with Apply.
            LayoutGuildlessView(body, out var browseRect);
            if (_createBtn.IsClicked(input))
            {
                _prompt.Open(ClientStrings.Get(ClientStrings.SocialPanel_CreateNamePrompt), "",
                    Constants.NameLength, allowEmpty: false, name => sender.SendGuildCreate(name));
            }

            _browseList.Update(input, browseRect, keyboardActive: false);
            int bsel = _browseList.SelectedIndex;
            _applyBtn.Enabled = bsel >= 0 && bsel < _browseIndices.Count;
            if (_applyBtn.IsClicked(input) && _applyBtn.Enabled)
            {
                sender.SendGuildApply(_browseIndices[bsel]);
                _browseList.SelectedIndex = -1;
            }
            return;
        }

        // In-guild: hit-test the sub-tab strip, then drive the active page.
        var subtabs = ComputeGuildSubTabRects(body);
        for (int i = 0; i < subtabs.Length; i++)
        {
            var tab = (GuildSub)i;
            if (!input.IsClickIn(subtabs[i]) || tab == _guildSubTab) continue;
            _guildSubTab = tab;
            sender.SendGuildInfoRequest();   // refresh live data (the roster's online column has no push)
        }

        var gbody = GuildContentRect(body);
        switch (_guildSubTab)
        {
            case GuildSub.Roster:
                UpdateGuildRoster(input, state, sender, gbody, info);
                break;
            case GuildSub.Vault:
                UpdateGuildVault(input, sender, gbody, info);
                break;
            case GuildSub.Main:
            default:
                UpdateGuildMain(input, sender, gbody, info);
                break;
        }
    }

    // Main page: leader settings (MOTD / labels / color / open) + membership (leave / disband) + the apps review.
    private void UpdateGuildMain(InputState input, ClientPacketSender sender, Rectangle gbody, GuildInfoPacket info)
    {
        LayoutGuildMain(gbody);
        var myRank = info.MyRank;
        bool canEdit = GuildActionGate.CanEditSettings(myRank);
        _motdBtn.Enabled = _labelsBtn.Enabled = _colorBtn.Enabled = _openBtn.Enabled = _rankBtn.Enabled = canEdit;
        _leaveBtn.Enabled = GuildActionGate.CanLeave(myRank);
        _disbandBtn.Enabled = GuildActionGate.CanDisband(myRank, info.Roster.Count);
        _appsBtn.Enabled = myRank >= GuildRank.Officer && info.Applications.Count > 0;

        if (_motdBtn.IsClicked(input) && _motdBtn.Enabled)
        {
            _prompt.Open(ClientStrings.Get(ClientStrings.SocialPanel_MotdPrompt), info.Motd,
                Constants.GuildMotdMaxLength, allowEmpty: true, motd => sender.SendGuildSetMotd(motd));
        }
        else if (_labelsBtn.IsClicked(input) && _labelsBtn.Enabled)
        {
            _pendingLabels.Clear();
            _pendingLabels.AddRange(info.Labels);
            _labelEditing = true;
        }
        else if (_colorBtn.IsClicked(input) && _colorBtn.Enabled)
        {
            _colorPicker.Open(ClientStrings.Get(ClientStrings.SocialPanel_ColorPrompt), info.Color,
                rgb => GuildColorPolicy.IsReserved(rgb) ? ClientStrings.Get(ClientStrings.SocialPanel_ColorReserved) : null,
                rgb => sender.SendGuildSetColor(rgb));
        }
        else if (_openBtn.IsClicked(input) && _openBtn.Enabled)
        {
            sender.SendGuildSetOpen(!info.OpenForMembership);
        }
        else if (_rankBtn.IsClicked(input) && _rankBtn.Enabled)
        {
            sender.SendGuildSetShowRank(!info.ShowRankOverhead);
        }
        else if (_leaveBtn.IsClicked(input) && _leaveBtn.Enabled)
        {
            sender.SendGuildLeave();
        }
        else if (_disbandBtn.IsClicked(input) && _disbandBtn.Enabled)
        {
            sender.SendGuildDisband();
        }
        else if (_appsBtn.IsClicked(input) && _appsBtn.Enabled)
        {
            _reviewingApps = true;
            _appList.SelectedIndex = -1;
        }
    }

    // Roster page: the member Table + the rank-gated member actions on the selected row.
    private void UpdateGuildRoster(InputState input, ClientState state, ClientPacketSender sender, Rectangle gbody, GuildInfoPacket info)
    {
        LayoutGuildRoster(gbody, out var tableRect);
        _rosterTable.Update(input, tableRect, keyboardActive: false);
        ColumnsChanged |= _rosterTable.LayoutChanged;   // persisted by the host when set

        // Rank gates mirror the server's: officers manage lower-ranked members, only the leader
        // promotes/demotes/transfers, and nobody can act on themselves through the roster.
        string login = RosterSelectedLogin();
        bool isSelf = string.Equals(login, state.AccountName, StringComparison.OrdinalIgnoreCase);
        bool hasTarget = login.Length > 0 && !isSelf;
        var targetRank = RosterSelectedRank();
        var myRank = info.MyRank;

        _kickBtn.Enabled = GuildActionGate.CanKick(myRank, targetRank, hasTarget);
        _promoteBtn.Enabled = GuildActionGate.CanPromote(myRank, targetRank, hasTarget);
        _demoteBtn.Enabled = GuildActionGate.CanDemote(myRank, targetRank, hasTarget);
        _transferBtn.Enabled = GuildActionGate.CanTransfer(myRank, targetRank, hasTarget);

        if (_kickBtn.IsClicked(input) && _kickBtn.Enabled)
        {
            sender.SendGuildKick(login);
            _rosterTable.ClearSelection();
        }
        else if (_promoteBtn.IsClicked(input) && _promoteBtn.Enabled)
        {
            sender.SendGuildPromote(login);
        }
        else if (_demoteBtn.IsClicked(input) && _demoteBtn.Enabled)
        {
            sender.SendGuildDemote(login);
        }
        else if (_transferBtn.IsClicked(input) && _transferBtn.Enabled)
        {
            sender.SendGuildTransfer(login);
        }
    }

    private void UpdateGuildVault(InputState input, ClientPacketSender sender, Rectangle gbody, GuildInfoPacket info)
    {
        LayoutGuildVault(gbody);
        _donateBtn.Enabled = true;   // any member

        // Log-view toggle (Bounds set last frame in DrawGuildVault): switch the recent-entries list.
        if (_vaultDonationsBtn.IsClicked(input))
        {
            _vaultShowSpending = false;
        }
        else if (_vaultSpendingBtn.IsClicked(input))
        {
            _vaultShowSpending = true;
        }
        else if (_donateBtn.IsClicked(input))
        {
            _prompt.Open(ClientStrings.Get(ClientStrings.SocialPanel_DonatePrompt), "", maxLength: 9, allowEmpty: false,
                s => { if (int.TryParse(s, out int amt) && amt > 0) sender.SendGuildDonate(amt); });
        }
    }

    // Open a numeric prompt for a positive gold amount and hand the parsed value to <paramref name="onGold"/>.
    // Reused by the wager-propose and no-ante peace-offering flows.
    private void PromptGold(string promptKey, Action<long> onGold) =>
        _prompt.Open(ClientStrings.Get(promptKey), "", maxLength: 12, allowEmpty: false,
            s => { if (long.TryParse(s, out long amt) && amt > 0) onGold(amt); });

    private void UpdateAppsReview(InputState input, ClientPacketSender sender, Rectangle body)
    {
        LayoutAppsReview(body, out var appRect);
        _appList.Update(input, appRect, keyboardActive: false);
        int sel = _appList.SelectedIndex;
        string login = sel >= 0 && sel < _appLogins.Count ? _appLogins[sel] : "";
        _approveBtn.Enabled = login.Length > 0;
        _rejectBtn.Enabled = login.Length > 0;

        if (_approveBtn.IsClicked(input) && login.Length > 0)
        {
            sender.SendGuildReviewApplication(login, accept: true);
            _appList.SelectedIndex = -1;
        }
        else if (_rejectBtn.IsClicked(input) && login.Length > 0)
        {
            sender.SendGuildReviewApplication(login, accept: false);
            _appList.SelectedIndex = -1;
        }
        else if (_appsBackBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            _reviewingApps = false;
        }
    }

    private void UpdateLabelEditor(InputState input, ClientPacketSender sender, Rectangle body)
    {
        LayoutLabelEditor(body);

        for (int i = 0; i < _labelBtns.Length; i++)
        {
            if (!_labelBtns[i].IsClicked(input)) continue;
            var label = AllLabels[i];
            if (_pendingLabels.Contains(label)) _pendingLabels.Remove(label);
            else if (_pendingLabels.Count < Constants.MaxGuildLabels) _pendingLabels.Add(label);
        }

        if (_labelSaveBtn.IsClicked(input))
        {
            sender.SendGuildSetLabels(_pendingLabels);
            _labelEditing = false;
        }
        else if (_labelCancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            _labelEditing = false;
        }
    }

}
