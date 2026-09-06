using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The player's quest journal — a sortable table of EVERY quest the player's class can take, each tagged by state:
/// In Progress (yellow), Available (gray), Ineligible (gray — right class but unmet level/stat/prereq), Complete
/// (green), or Repeatable (blue — a completed repeatable). Quests locked to another class are omitted entirely.
/// Selecting a quest shows its objectives with live progress; hovering an Ineligible quest shows the acceptance
/// requirements; an active quest can be abandoned (two-click confirm). Accept / turn-in happen at the NPC, not here.
/// </summary>
public sealed class QuestLogPanel : IGamePanel
{
    // Status doubles as the default sort order (actionable first, finished last).
    private enum RowKind { InProgress = 0, Available = 1, Ineligible = 2, Complete = 3, Repeatable = 4 }
    private sealed record Row(int QuestNum, RowKind Kind);   // class (not struct) so the table's nullable Selected/Hovered work

    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 320, 320), minH: 220, minW: 280);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    /// <summary>Persisted table (column widths + sort), keyed for the host's generic save/restore.</summary>
    public IReadOnlyDictionary<string, IColumnLayoutTable> ColumnTables { get; }
    /// <summary>True for the frame after the user resized/reordered/sorted a column, so the host persists it.</summary>
    public bool ColumnsChanged { get; private set; }

    private readonly Table<Row> _table = new();
    private int _rowCount;                  // current row count (Items are rebuilt only on QuestVersion change)
    private int _lastVersion = -1;
    private int _lastSelected;              // quest the abandon-confirm is tied to (reset when the selection changes)
    private bool _confirmingAbandon;
    private readonly Button _abandonBtn = new();
    private InputState _input = new();
    private int _labelsGeneration = -1;
    private string _titleLabel = "Quests";
    private ClientState? _state;            // captured each frame for the column selectors + the tooltip

    private const int LineH = 14, QuestColStatus = 1;

    public QuestLogPanel()
    {
        _table.AllowReorder = true;
        _table.Column(() => ClientStrings.Get(ClientStrings.QuestPanel_ColQuest), r => QuestName(r.QuestNum), width: 176, minWidth: 80)
              .Column(() => ClientStrings.Get(ClientStrings.QuestPanel_ColStatus), r => (int)r.Kind,
                          r => ClientStrings.Get(StatusKey(r.Kind)), width: 84, minWidth: 56)
              .WithRowKey(r => r.QuestNum)                 // selection follows a quest across rebuilds
              .WithRowColor(r => RowColor(r.Kind));
        _table.SortBy(QuestColStatus);                     // group by status (actionable first) by default
        ColumnTables = new Dictionary<string, IColumnLayoutTable> { ["quest.log"] = _table };
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _lastVersion = -1;
            _confirmingAbandon = false;
        }
    }

    public void Update(InputState input, ClientState state, ClientPacketSender sender, bool isActive)
    {
        ColumnsChanged = false;
        if (!IsOpen) return;
        _input = input;
        _state = state;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        SyncItems(state);
        var c = _panel.ContentBounds;
        _table.Update(input, ListRect(c), keyboardActive: isActive);
        ColumnsChanged |= _table.LayoutChanged;

        var sel = _table.SelectedItem;
        int selQuest = sel?.QuestNum ?? 0;
        if (selQuest != _lastSelected)
        {
            _confirmingAbandon = false;
            _lastSelected = selQuest;
        }

        // Abandon (an active quest only), two-click confirm.
        bool selActive = sel is { Kind: RowKind.InProgress };
        _abandonBtn.Enabled = selActive;
        _abandonBtn.Bounds = UiHelper.PanelBottomButton(c, 0, 1);
        if (selActive && _abandonBtn.IsClicked(input))
        {
            if (_confirmingAbandon)
            {
                sender.SendQuestAbandon(selQuest);
                _confirmingAbandon = false;
            }
            else
            {
                _confirmingAbandon = true;
            }
        }
        if (!selActive) _confirmingAbandon = false;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive)
    {
        if (!IsOpen) return;
        _state = state;
        SyncItems(state);
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _titleLabel = ClientStrings.Get(ClientStrings.QuestPanel_Title);
        }
        _abandonBtn.Label = ClientStrings.Get(_confirmingAbandon
            ? ClientStrings.QuestPanel_AbandonConfirm : ClientStrings.QuestPanel_AbandonButton);

        _panel.Draw(sb, font, _titleLabel, isActive);
        var c = _panel.ContentBounds;
        var listRect = ListRect(c);

        if (_rowCount == 0)
        {
            UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.QuestPanel_Empty),
                new Vector2(listRect.X + 4, listRect.Y + 4), Color.Gray, listRect.Width - 8);
        }
        else
        {
            _table.Draw(sb, font, listRect);
        }

        // Detail: the selected quest's objectives with live progress.
        var sel = _table.SelectedItem;
        int detailY = listRect.Bottom + 4;
        UiHelper.DrawFilledRect(sb, new Rectangle(c.X, detailY - 2, c.Width, 1), Color.DimGray);
        if (sel is { } r && r.QuestNum < state.QuestDefs.Length && state.QuestDefs[r.QuestNum] is { } def)
        {
            var pq = state.FindQuest(r.QuestNum);
            for (int k = 0; k < def.Objectives.Count; k++)
            {
                var o = def.Objectives[k];
                int prog = pq is not null && k < pq.Progress.Count ? pq.Progress[k] : 0;
                string tgt = o.Target >= 1 && o.Target < state.NpcDefs.Length
                    ? (state.NpcDefs[o.Target]?.Name?.TrimEnd() ?? "?") : "?";
                string line = ClientStrings.Format(ClientStrings.QuestDialog_ObjectiveKill, ("Target", tgt), ("Have", prog), ("Need", o.Count));
                UiHelper.DrawLabel(sb, font, line, new Vector2(c.X + 6, detailY), prog >= o.Count ? Color.LightGreen : Color.White, c.Width - 12);
                detailY += LineH;
            }

            // What this run pays, off the same rule the server grants by.
            bool useRepeat = def.PaysRepeatRewards(pq?.Status ?? QuestStatus.NotStarted);
            long rewardExp = useRepeat ? def.RepeatRewardExp : def.RewardExp;
            var rewardItems = useRepeat ? def.RepeatRewardItems : def.RewardItems;
            if (rewardExp > 0 || rewardItems.Count > 0)
            {
                detailY += 4;
                UiHelper.DrawLabel(sb, font, ClientStrings.Get(ClientStrings.QuestDialog_RewardsHeader),
                    new Vector2(c.X + 6, detailY), UiHelper.DlgLabelColor, c.Width - 12);
                detailY += LineH;
                if (rewardExp > 0)
                {
                    UiHelper.DrawLabel(sb, font, ClientStrings.Format(ClientStrings.QuestDialog_RewardExp, ("Exp", rewardExp)),
                        new Vector2(c.X + 12, detailY), Color.White, c.Width - 18);
                    detailY += LineH;
                }

                foreach (var reward in rewardItems)
                {
                    if (reward.ItemNum < 1 || reward.ItemNum >= state.Items.Length) continue;
                    string item = state.Items[reward.ItemNum]?.Name?.TrimEnd() ?? "?";
                    UiHelper.DrawLabel(sb, font,
                        ClientStrings.Format(ClientStrings.QuestDialog_RewardItem, ("Item", item), ("Qty", reward.Quantity)),
                        new Vector2(c.X + 12, detailY), Color.White, c.Width - 18);
                    detailY += LineH;
                }
            }
        }

        if (_abandonBtn.Enabled) _abandonBtn.Draw(sb, font, _input);

        // Requirements tooltip when hovering a quest that can't be accepted right now: an Ineligible one (right
        // class, but unmet level/stat/prereq), or a Repeatable one still inside the period it was last finished in
        // — that row reads as plain "Repeatable", so the cooldown is the only thing explaining the wait.
        if (_table.HoveredItem is { } hov && (hov.Kind == RowKind.Ineligible
                || (hov.Kind == RowKind.Repeatable && state.IsQuestOnRepeatCooldown(hov.QuestNum))))
        {
            DrawRequirementsTooltip(sb, font, state, hov.QuestNum);
        }

        _panel.DrawOverlay(sb);
    }

    // The scrollless table occupies the top ~55% of the content; the rest is the objective detail + abandon button.
    private static Rectangle ListRect(Rectangle content)
        => new(content.X, content.Y + 2, content.Width, (int)(content.Height * 0.55f));

    private void SyncItems(ClientState state)
    {
        if (_lastVersion == state.QuestVersion) return;
        _lastVersion = state.QuestVersion;
        int myClass = state.Me.Class;
        var rows = new List<Row>();
        for (int q = 1; q < state.QuestDefs.Length; q++)
        {
            var def = state.QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            // Class-locked quests the player can never take are omitted entirely.
            if (myClass > 0 && !ClassGate.Allows(def.AllowedClasses, myClass)) continue;
            var pq = state.FindQuest(q);
            RowKind kind =
                pq is { Status: QuestStatus.InProgress or QuestStatus.InProgressRepeat } ? RowKind.InProgress
              : pq is { Status: QuestStatus.Done } ? (def.Repeatable ? RowKind.Repeatable : RowKind.Complete)
              : state.IsQuestEligible(q) ? RowKind.Available
              : RowKind.Ineligible;                        // right class, but unmet level/stat/prereq
            rows.Add(new Row(q, kind));
        }
        _rowCount = rows.Count;
        _table.Items = rows;
    }

    private string QuestName(int q)
        => _state is { } s && q >= 1 && q < s.QuestDefs.Length ? (s.QuestDefs[q]?.TrimmedName ?? "?") : "?";

    // A bordered requirements box near the cursor: each requirement green (met) or red (unmet).
    private void DrawRequirementsTooltip(SpriteBatch sb, SpriteFont font, ClientState state, int q)
    {
        var def = q >= 1 && q < state.QuestDefs.Length ? state.QuestDefs[q] : null;
        if (def is null) return;
        var lines = QuestRequirements.Build(state, q, def);
        if (lines.Count == 0) return;

        string header = ClientStrings.Get(ClientStrings.QuestPanel_ReqHeader);
        int lineH = font.LineSpacing;
        float w = font.MeasureString(header).X;
        foreach (var (t, _) in lines) w = Math.Max(w, font.MeasureString(t).X);
        int boxW = (int)w + 12, boxH = (lines.Count + 1) * lineH + 8;
        int bx = Math.Min(_input.MousePosition.X + 14, UiHelper.RefW - boxW - 2);
        int by = Math.Min(_input.MousePosition.Y + 14, UiHelper.RefH - boxH - 2);
        var box = new Rectangle(bx, by, boxW, boxH);
        UiHelper.DrawFilledRect(sb, box, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, box, UiHelper.ConfirmOverlayBorder);
        float ty = by + 4;
        UiHelper.DrawLabel(sb, font, header, new Vector2(bx + 6, ty), Color.White, boxW - 12);
        ty += lineH;
        foreach (var (t, met) in lines)
        {
            UiHelper.DrawLabel(sb, font, t, new Vector2(bx + 6, ty), met ? Color.LightGreen : Color.OrangeRed, boxW - 12);
            ty += lineH;
        }
    }

    private static Color RowColor(RowKind k) => k switch
    {
        RowKind.InProgress => Color.Yellow,
        RowKind.Complete => Color.LightGreen,
        RowKind.Repeatable => Color.CornflowerBlue,
        _ => Color.Gray,                                   // Available + Ineligible (told apart by the Status tag)
    };

    private static string StatusKey(RowKind k) => k switch
    {
        RowKind.InProgress => ClientStrings.QuestPanel_StateInProgress,
        RowKind.Available => ClientStrings.QuestPanel_StateAvailable,
        RowKind.Ineligible => ClientStrings.QuestPanel_StateIneligible,
        RowKind.Complete => ClientStrings.QuestPanel_StateComplete,
        _ => ClientStrings.QuestPanel_StateRepeatable,
    };
}
