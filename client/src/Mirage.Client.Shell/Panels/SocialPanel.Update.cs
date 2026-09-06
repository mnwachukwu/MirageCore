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
        _state = state;
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
            _reviewingWarReqs = false;
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
        if (_reviewingWarReqs)
        {
            UpdateWarReqs(input, state, sender, body);
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
            sender.SendGuildInfoRequest();   // refresh live data (roster online column, quest progress, wars)
            if (tab == GuildSub.Standings)
            {
                _viewingHistory = false;
                sender.SendGuildLeaderboardRequest();
            }  // open on the current season
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
            case GuildSub.Quests:
                UpdateGuildQuests(input, sender, gbody, info);
                break;
            case GuildSub.Wars:
                UpdateWars(input, state, sender, gbody);
                break;
            case GuildSub.Territories:
                UpdateGuildTerritories(input, sender, info, gbody);
                break;
            case GuildSub.Standings:
                UpdateGuildStandings(input, state, sender, gbody);
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

    // Vault page: donate gold, donate valor, pay late tax.
    // Territories page: the territory Table + a Challenge/Withdraw action on the selected territory.
    private void UpdateGuildTerritories(InputState input, ClientPacketSender sender, GuildInfoPacket info, Rectangle gbody)
    {
        LayoutGuildTerritories(gbody, out var tableRect);
        _territoryTable.Update(input, tableRect, keyboardActive: false);
        ColumnsChanged |= _territoryTable.LayoutChanged;   // persisted by the host when set

        var sel = _territoryTable.SelectedItem;
        bool officer = GuildActionGate.CanChallengeTerritory(info.MyRank);
        bool isOwn = sel is not null && !string.IsNullOrEmpty(sel.Owner) &&
                     string.Equals(sel.Owner, info.Name, StringComparison.OrdinalIgnoreCase);
        bool withdrawing = sel is { ChallengedByUs: true };
        _challengeBtn.Label = ClientStrings.Get(withdrawing
            ? ClientStrings.SocialPanel_WithdrawChallengeButton : ClientStrings.SocialPanel_ChallengeButton);
        // Challenge any territory we don't own; withdraw one we're already challenging. Officer+ only.
        _challengeBtn.Enabled = sel is not null && officer && (withdrawing || !isOwn);

        if (_challengeBtn.IsClicked(input) && _challengeBtn.Enabled && sel is not null)
        {
            // Challenging spends from the vault and withdrawing does NOT give it back, so the spend is
            // confirmed before it happens. Withdrawing is not confirmed: it costs nothing, and the money
            // it fails to return was already gone before the button was reached.
            if (withdrawing)
            {
                sender.SendGuildTerritoryWithdraw(sel.Index);
            }
            else
            {
                int territory = sel.Index;
                _confirm.Open(
                    ClientStrings.Format(ClientStrings.SocialPanel_ChallengeConfirmFormat, ("Territory", sel.Name)),
                    () => sender.SendGuildTerritoryChallenge(territory));
            }
        }
    }

    private void UpdateGuildVault(InputState input, ClientPacketSender sender, Rectangle gbody, GuildInfoPacket info)
    {
        LayoutGuildVault(gbody);
        _donateBtn.Enabled = true;                                                    // any member
        _donateValorBtn.Enabled = true;
        _payTaxBtn.Enabled = GuildActionGate.CanPayTax(info.MyRank, info.PerksActive);

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
        else if (_donateValorBtn.IsClicked(input))
        {
            _prompt.Open(ClientStrings.Get(ClientStrings.SocialPanel_DonateValorPrompt), "", maxLength: 9, allowEmpty: false,
                s => { if (int.TryParse(s, out int amt) && amt > 0) sender.SendGuildDonateValor(amt); });
        }
        else if (_payTaxBtn.IsClicked(input) && _payTaxBtn.Enabled)
        {
            sender.SendGuildPayTax();
        }
    }

    // Quests page: the active quest board + Acquire / Abandon (Leader-only, gated), each behind a
    // confirmation — Acquire shows the gold cost; Abandon warns that progress + gold are forfeit.
    private void UpdateGuildQuests(InputState input, ClientPacketSender sender, Rectangle gbody, GuildInfoPacket info)
    {
        LayoutGuildQuests(gbody);
        _questAcquireBtn.Enabled = GuildActionGate.CanAcquireQuest(info.MyRank, info.Quest is not null);
        _questAbandonBtn.Enabled = GuildActionGate.CanAbandonQuest(info.MyRank, info.Quest is not null);

        if (_questAcquireBtn.IsClicked(input) && _questAcquireBtn.Enabled)
        {
            _confirm.Open(ClientStrings.Format(ClientStrings.SocialPanel_QuestAcquireConfirmFormat,
                ("Cost", GuildQuests.AcquireCost(info.Level))), sender.SendGuildQuestAcquire);
        }
        else if (_questAbandonBtn.IsClicked(input) && _questAbandonBtn.Enabled)
        {
            _confirm.Open(ClientStrings.Get(ClientStrings.SocialPanel_QuestAbandonConfirm), sender.SendGuildQuestAbandon);
        }
    }

    // Wars page: a war list + the selected war's action buttons. Declare (by name) and Requests are
    // list-independent; the rest act on the selected war and adapt to its status/peace state.
    private void UpdateWars(InputState input, ClientState state, ClientPacketSender sender, Rectangle body)
    {
        var info = state.GuildInfo;
        if (info is null || !info.InGuild) return;

        LayoutWars(body, out var listRect, out _);
        _warList.Update(input, listRect, keyboardActive: false);

        var sel = SelectedWar(info);
        var myRank = info.MyRank;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool oneSidedAggr = sel is { Status: GuildWarStatus.OneSidedAggressor };
        bool mutual = sel is { Status: GuildWarStatus.Mutual };
        bool retractReady = sel is not null && now >= sel.DeclaredUtc + Constants.GuildWarRetractionLockSeconds;
        bool peaceByUs = sel is { PeaceOfferedByUs: true };
        bool peaceByThem = sel is { PeaceOfferedByThem: true };
        // Wager state: leader-only, mutual, before the window closes and while no ante is locked.
        bool anteLocked = sel is { AnteEscrow: > 0 };
        bool wagerWindow = sel is not null && now < sel.WagerDeadlineUtc;
        bool wagerByUs = sel is { WagerProposedByUs: > 0 };
        bool wagerByThem = sel is { WagerProposedByThem: > 0 };
        bool canWager = mutual && !anteLocked && wagerWindow && GuildActionGate.CanWager(myRank);

        _warDeclareBtn.Enabled = GuildActionGate.CanDeclareWar(myRank, info.Level);
        _warReqsBtn.Enabled = GuildActionGate.CanResolveWar(myRank) && info.WarRequests.Count > 0;
        _warRetractBtn.Enabled = oneSidedAggr && retractReady && GuildActionGate.CanRequestWar(myRank);
        // One toggle button: withdraw our pending plea (leader) or sue for peace (officer+), never while they
        // already hold an offer on the table (accept/reject that instead).
        _warPeaceBtn.Enabled = mutual && (peaceByUs ? GuildActionGate.CanResolveWar(myRank)
                                                    : (!peaceByThem && GuildActionGate.CanRequestWar(myRank)));
        _warAcceptBtn.Enabled = mutual && peaceByThem && GuildActionGate.CanResolveWar(myRank);
        _warRejectBtn.Enabled = mutual && peaceByThem && GuildActionGate.CanResolveWar(myRank);
        _warWagerBtn.Enabled = canWager;                          // toggles Propose / Withdraw
        _warWagerAcceptBtn.Enabled = canWager && wagerByThem;
        _warWagerRejectBtn.Enabled = canWager && wagerByThem;

        if (_warDeclareBtn.IsClicked(input) && _warDeclareBtn.Enabled)
        {
            _prompt.Open(ClientStrings.Get(ClientStrings.SocialPanel_WarDeclarePrompt), "",
                Constants.NameLength, allowEmpty: false, name => sender.SendGuildWarDeclareByName(name));
        }
        else if (_warReqsBtn.IsClicked(input) && _warReqsBtn.Enabled)
        {
            _reviewingWarReqs = true;
            _warReqList.SelectedIndex = -1;
        }
        else if (sel is null)
        {
            return;
        }
        else if (_warRetractBtn.IsClicked(input) && _warRetractBtn.Enabled)
        {
            sender.SendGuildWarRetract(sel.OpponentIndex);
        }
        else if (_warPeaceBtn.IsClicked(input) && _warPeaceBtn.Enabled)
        {
            int opp = sel.OpponentIndex;   // captured for the offering-prompt closure below
            if (peaceByUs)
            {
                sender.SendGuildWarPeace(opp, GuildWarPeaceAction.Withdraw);
            }
            else if (anteLocked)
            {
                sender.SendGuildWarPeace(opp, GuildWarPeaceAction.Offer);   // concede the ante — no offering
            }
            else
            {
                PromptGold(ClientStrings.SocialPanel_WarPeaceOfferPrompt,   // no ante: the plea must carry a pot
                amt => sender.SendGuildWarPeace(opp, GuildWarPeaceAction.Offer, amt));
            }
        }
        else if (_warAcceptBtn.IsClicked(input) && _warAcceptBtn.Enabled)
        {
            sender.SendGuildWarPeace(sel.OpponentIndex, GuildWarPeaceAction.Accept);
        }
        else if (_warRejectBtn.IsClicked(input) && _warRejectBtn.Enabled)
        {
            sender.SendGuildWarPeace(sel.OpponentIndex, GuildWarPeaceAction.Reject);
        }
        else if (_warWagerBtn.IsClicked(input) && _warWagerBtn.Enabled)
        {
            int opp = sel.OpponentIndex;   // captured for the ante-prompt closure below
            if (wagerByUs)
            {
                sender.SendGuildWarWager(opp, GuildWarWagerAction.Withdraw);
            }
            else
            {
                PromptGold(ClientStrings.SocialPanel_WarWagerPrompt,
                amt => sender.SendGuildWarWager(opp, GuildWarWagerAction.Propose, amt));
            }
        }
        else if (_warWagerAcceptBtn.IsClicked(input) && _warWagerAcceptBtn.Enabled)
        {
            sender.SendGuildWarWager(sel.OpponentIndex, GuildWarWagerAction.Accept);
        }
        else if (_warWagerRejectBtn.IsClicked(input) && _warWagerRejectBtn.Enabled)
        {
            sender.SendGuildWarWager(sel.OpponentIndex, GuildWarWagerAction.Reject);
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

    // War-requests review overlay (leader): accept/deny the officer queue, addressed by (kind, target).
    private void UpdateWarReqs(InputState input, ClientState state, ClientPacketSender sender, Rectangle body)
    {
        LayoutWarReqs(body, out var listRect);
        _warReqList.Update(input, listRect, keyboardActive: false);
        int s = _warReqList.SelectedIndex;
        bool has = s >= 0 && s < _warReqKeys.Count;
        _warReqAcceptBtn.Enabled = has;
        _warReqDenyBtn.Enabled = has;

        if (_warReqAcceptBtn.IsClicked(input) && has)
        {
            var (k, t) = _warReqKeys[s];
            sender.SendGuildWarReviewRequest(k, t, accept: true);
            _warReqList.SelectedIndex = -1;
        }
        else if (_warReqDenyBtn.IsClicked(input) && has)
        {
            var (k, t) = _warReqKeys[s];
            sender.SendGuildWarReviewRequest(k, t, accept: false);
            _warReqList.SelectedIndex = -1;
        }
        else if (_warReqBackBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            _reviewingWarReqs = false;
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

    private void UpdateGuildStandings(InputState input, ClientState state, ClientPacketSender sender, Rectangle gbody)
    {
        LayoutStandingsButtons(gbody);
        if (_historyBtn.IsClicked(input))
        {
            _viewingHistory = !_viewingHistory;
            if (_viewingHistory) sender.SendSeasonArchiveRequest(0);   // 0 = latest archived season
        }
        if (_viewingHistory)
        {
            var seasons = state.SeasonArchive?.AvailableSeasons ?? new List<int>();
            int idx = seasons.IndexOf(state.SeasonArchive?.Season ?? 0);
            _prevSeasonBtn.Enabled = idx > 0;                                 // an older season exists
            _nextSeasonBtn.Enabled = idx >= 0 && idx < seasons.Count - 1;    // a newer season exists
            if (_prevSeasonBtn.IsClicked(input) && _prevSeasonBtn.Enabled) sender.SendSeasonArchiveRequest(seasons[idx - 1]);
            else if (_nextSeasonBtn.IsClicked(input) && _nextSeasonBtn.Enabled) sender.SendSeasonArchiveRequest(seasons[idx + 1]);
            _archiveTable.Update(input, StandingsTableRect(gbody), keyboardActive: false);
            ColumnsChanged |= _archiveTable.LayoutChanged;
        }
        else
        {
            _standingsTable.Update(input, StandingsTableRect(gbody), keyboardActive: false);
            ColumnsChanged |= _standingsTable.LayoutChanged;
        }
    }
}
