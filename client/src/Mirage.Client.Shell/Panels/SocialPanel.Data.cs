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

/// <summary>Rebuilding the panel's view models from client state when the server pushes new social
/// data, plus the label editor's backing state.</summary>
public sealed partial class SocialPanel : IGamePanel
{
    private void RefreshLabels()
    {
        if (_labelsGeneration == ClientStrings.Generation) return;
        _labelsGeneration = ClientStrings.Generation;
        _removeBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_RemoveButton);
        _kickBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_KickButton);
        _promoteBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_PromoteButton);
        _demoteBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_DemoteButton);
        _transferBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_TransferButton);
        _leaveBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_LeaveButton);
        _disbandBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_DisbandButton);
        _motdBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_MotdButton);
        _labelsBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_LabelsButton);
        _colorBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_ColorButton);
        _createBtn.Label = ClientStrings.Get(ClientStrings.Common_Create);
        _labelSaveBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_SaveButton);
        _labelCancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        _applyBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_ApplyButton);
        _approveBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_ApproveButton);
        _rejectBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_RejectButton);
        _appsBackBtn.Label = ClientStrings.Get(ClientStrings.Common_Back);
        _donateBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_DonateButton);
        _donateValorBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_DonateValorButton);
        _payTaxBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_PayTaxButton);
        _vaultDonationsBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_DonationsTab);
        _vaultSpendingBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_SpendingTab);
        _questAcquireBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_QuestAcquireButton);
        _questAbandonBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_QuestAbandonButton);
        // War page (the peace toggle + requests-count labels are dynamic, set in DrawWars).
        _warDeclareBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarDeclareButton);
        _warRetractBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarRetractButton);
        _warAcceptBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarAcceptButton);
        _warRejectBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarRejectButton);
        // Wager accept/reject are static (the Propose/Withdraw toggle label is set in DrawWars).
        _warWagerAcceptBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarWagerAcceptButton);
        _warWagerRejectBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarWagerRejectButton);
        _warReqAcceptBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarReqAccept);
        _warReqDenyBtn.Label = ClientStrings.Get(ClientStrings.SocialPanel_WarReqDeny);
        _warReqBackBtn.Label = ClientStrings.Get(ClientStrings.Common_Back);
        for (int i = 0; i < _labelBtns.Length; i++)
            _labelBtns[i].Label = LabelName(AllLabels[i]);
        // Roster column headers re-localize automatically (declared as Func<string>, synced by the Table).
    }

    // Rebuild the visible tab's rows, preserving the selected account by login across a server push.
    private void Rebuild(ClientState state)
    {
        if (_activeTab == TabGuild)
        {
            var info = state.GuildInfo;

            // Roster → the data-bound Table: feed the collection; it declares its own columns and follows
            // the selected member by login (WithRowKey) across this wholesale push.
            _rosterTable.Items = info?.Roster ?? new List<SocialEntry>();

            // Territories → its read-only Table (every territory, all guilds).
            _territoryTable.Items = info?.Territories ?? new List<TerritoryView>();

            // Standings → the seasonal leaderboard Table (every guild; server-ordered best-first).
            _standingsTable.Items = state.Leaderboard?.Rows ?? new List<LeaderboardEntry>();
            // Historical browser → the selected archived season's standings.
            _archiveTable.Items = state.SeasonArchive?.Standings ?? new List<SeasonStanding>();

            // Open-guild browser (guildless view).
            _browseList.Items.Clear();
            _browseIndices.Clear();
            foreach (var g in state.GuildBrowse)
            {
                _browseList.Items.Add(ClientStrings.Format(ClientStrings.SocialPanel_BrowseRowFormat,
                    ("Name", g.Name), ("Level", g.Level), ("Members", g.Members)));
                _browseIndices.Add(g.Index);
            }

            // Pending applications (officer review view).
            _appList.Items.Clear();
            _appLogins.Clear();
            foreach (var login in info?.Applications ?? new List<string>())
            {
                _appList.Items.Add(login);
                _appLogins.Add(login);
            }

            // Active wars (war page), preserving the selected war by opponent index — the live attrition
            // push bumps SocialVersion, which would otherwise deselect the row on every war death.
            int prevWarOpp = SelectedWarOpp();
            _warList.Items.Clear();
            _warOpp.Clear();
            foreach (var w in info?.Wars ?? new List<GuildWarView>())
            {
                _warList.Items.Add(ClientStrings.Format(ClientStrings.SocialPanel_WarRowFormat,
                    ("Name", w.OpponentName), ("Status", WarStatusName(w.Status))));
                _warOpp.Add(w.OpponentIndex);
            }
            _warList.SelectedIndex = prevWarOpp == 0 ? -1 : _warOpp.IndexOf(prevWarOpp);

            // Pending officer war-requests (leader review overlay).
            _warReqList.Items.Clear();
            _warReqKeys.Clear();
            foreach (var r in info?.WarRequests ?? new List<GuildWarRequestView>())
            {
                _warReqList.Items.Add(ClientStrings.Format(ClientStrings.SocialPanel_WarReqRowFormat,
                    ("By", r.RequesterName), ("Kind", WarReqKindName(r.Kind)), ("Target", r.TargetName)));
                _warReqKeys.Add((r.Kind, r.TargetIndex));
            }
        }
        else
        {
            string prev = SelectedLogin();
            _list.Items.Clear();
            _rowLogins.Clear();
            foreach (var r in _activeTab == TabFriends ? state.Friends : state.Ignore)
            {
                _list.Items.Add($"{r.Login}  -  {Who(r)}");
                _rowLogins.Add(r.Login);
            }
            int idx = prev.Length == 0 ? -1 : _rowLogins.FindIndex(l => string.Equals(l, prev, StringComparison.OrdinalIgnoreCase));
            _list.SelectedIndex = idx;
        }
    }

    // A row's character columns only mean anything while the account is online — these lists key on
    // accounts, and only the guild roster keeps a snapshot of an offline member's last character.
    private static string Who(SocialEntry r) => r.Online
        ? ClientStrings.Format(ClientStrings.SocialPanel_OnlineFormat, ("Char", r.CharName), ("Level", r.CharLevel))
        : ClientStrings.Get(ClientStrings.SocialPanel_Offline);

    private static string RankName(GuildRank rank) => ClientStrings.Get(rank switch
    {
        GuildRank.Leader => ClientStrings.SocialPanel_RankLeader,
        GuildRank.Officer => ClientStrings.SocialPanel_RankOfficer,
        _ => ClientStrings.SocialPanel_RankMember,
    });

    private static string LabelName(GuildLabel label) => ClientStrings.Get(label switch
    {
        GuildLabel.Pvp => ClientStrings.GuildLabel_Pvp,
        GuildLabel.Pve => ClientStrings.GuildLabel_Pve,
        GuildLabel.Leveling => ClientStrings.GuildLabel_Leveling,
        GuildLabel.CasualSocial => ClientStrings.GuildLabel_CasualSocial,
        GuildLabel.Hardcore => ClientStrings.GuildLabel_Hardcore,
        GuildLabel.OrganizedWars => ClientStrings.GuildLabel_OrganizedWars,
        GuildLabel.ItemFarming => ClientStrings.GuildLabel_ItemFarming,
        GuildLabel.NewbieFocused => ClientStrings.GuildLabel_NewbieFocused,
        _ => ClientStrings.GuildLabel_VeteranFocused,
    });

    private string SelectedLogin()
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _rowLogins.Count ? _rowLogins[_list.SelectedIndex] : "";

    // Roster selection in account terms (the member actions address a login + read a rank).
    private string RosterSelectedLogin() => _rosterTable.SelectedItem?.Login ?? "";
    private GuildRank RosterSelectedRank() => _rosterTable.SelectedItem?.Rank ?? GuildRank.None;

    // Class name for a roster member's active/last character, from the client's class table (empty until the
    // server sends it, or for an undefined class). Read live so a late class-data push fills the column in.
    private string ClassName(int classIndex)
    {
        var classes = _state?.Classes;
        return classes is not null && classIndex > 0 && classIndex < classes.Length
            ? classes[classIndex]?.Name?.TrimEnd() ?? "" : "";
    }

    // The selected war's opponent index (0 = none) — the war list's stable key across rebuilds.
    private int SelectedWarOpp()
        => _warList.SelectedIndex >= 0 && _warList.SelectedIndex < _warOpp.Count ? _warOpp[_warList.SelectedIndex] : 0;

    private GuildWarView? SelectedWar(GuildInfoPacket info)
    {
        int opp = SelectedWarOpp();
        if (opp == 0) return null;
        foreach (var w in info.Wars) if (w.OpponentIndex == opp) return w;
        return null;
    }

    private static string WarStatusName(GuildWarStatus status) => ClientStrings.Get(status switch
    {
        GuildWarStatus.OneSidedAggressor => ClientStrings.SocialPanel_WarStatusAggressor,
        GuildWarStatus.OneSidedDefender => ClientStrings.SocialPanel_WarStatusDefender,
        GuildWarStatus.Mutual => ClientStrings.SocialPanel_WarStatusMutual,
        _ => ClientStrings.SocialPanel_WarStatusWarmup,
    });

    private static string WarReqKindName(GuildWarRequestKind kind) => ClientStrings.Get(kind switch
    {
        GuildWarRequestKind.Retract => ClientStrings.SocialPanel_WarReqKindRetract,
        GuildWarRequestKind.Peace => ClientStrings.SocialPanel_WarReqKindPeace,
        _ => ClientStrings.SocialPanel_WarReqKindDeclare,
    });
}
