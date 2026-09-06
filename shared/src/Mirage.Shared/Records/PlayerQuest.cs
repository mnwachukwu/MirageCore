namespace Mirage.Shared.Records;

/// <summary>
/// Per-character state for one quest the character has accepted, held in a <see cref="List{T}"/> on
/// <c>PlayerRecord.Quests</c> (persisted whole with the account, like the trade escrow). A quest the character
/// has never accepted has NO entry (== <see cref="QuestStatus.NotStarted"/>); the list holds only InProgress
/// and Done. <see cref="Progress"/> parallels <c>QuestRecord.Objectives</c> by index. <see cref="PeriodKey"/>
/// stamps the completion period for a Repeatable quest so its eligibility re-lights when the period rolls over
/// (empty for a non-repeatable quest, whose Done is permanent).
/// </summary>
public sealed class PlayerQuest
{
    public int QuestNum { get; set; }
    public QuestStatus Status { get; set; }
    public List<int> Progress { get; set; } = new();
    public string PeriodKey { get; set; } = "";

    public PlayerQuest Clone() => new()
    {
        QuestNum = QuestNum,
        Status = Status,
        Progress = new List<int>(Progress),
        PeriodKey = PeriodKey,
    };
}
