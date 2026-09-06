using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// An editor-authored player-quest definition — 1-based, held in <c>GameWorld.Quests[]</c> and persisted per
/// entry as JSON, mirroring items/npcs/shops. The FIRST real customer of the shared objective kernel: an
/// accepted quest registers each of its <see cref="Objectives"/> with <c>ObjectiveSystem.Track</c>. NPC roles
/// (giver / turn-in) live HERE, keyed by NPC number, so <c>NpcRecord</c> is never touched. Rewards are granted
/// on TURN-IN at the turn-in NPC (the overhead "!"). A plain serializable POCO.
/// </summary>
public sealed class QuestRecord
{
    private string _name = "";
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — names come padded; message sites trim.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    /// <summary>Journal / flavor text shown in the quest dialog and log.</summary>
    public string Description { get; set; } = "";

    /// <summary>Goals to complete (ALL must complete to turn in), parallel to a character's progress list.
    /// Reuses the shared <see cref="Objective"/> kernel; v1 wires <see cref="ObjectiveKind.Kill"/>. Capped at
    /// <c>Constants.MaxQuestObjectives</c> by the editor.</summary>
    public List<Objective> Objectives { get; set; } = new();

    // ── Requirements to accept (0 = no requirement) ─────────────────────────────
    public int ReqLevel { get; set; }
    public int ReqStr { get; set; }
    public int ReqDef { get; set; }
    public int ReqSpd { get; set; }
    public int ReqInt { get; set; }
    /// <summary>The classes allowed to accept it (1-based ids). Empty or absent = every class. Unlike the
    /// scalar requirements above this is a set, not a threshold — a class-locked quest is invisible to
    /// every other class rather than merely ungrantable. Ask <see cref="ClassGate"/>; the editor's save
    /// path canonicalizes it through <see cref="ClassGate.Normalize"/>.</summary>
    public List<short>? AllowedClasses { get; set; }
    /// <summary>A quest number that must be Done before this one can be accepted (0 = none) — enables chains.</summary>
    public int PrereqQuest { get; set; }

    // ── Rewards (granted on turn-in) ────────────────────────────────────────────
    // Gold is NOT a separate field — it's just item #1 (Constants.GoldItemIndex) in RewardItems, like anywhere
    // else in the engine. Currency stacks, so a gold reward never hits the bag-full mail fallback.
    public long RewardExp { get; set; }
    public List<QuestReward> RewardItems { get; set; } = new();
    // Repeat rewards for a Repeatable quest: the FIRST completion pays the main rewards above; SUBSEQUENT
    // completions pay these instead — UNLESS this set is empty (no repeat exp AND no repeat items), in which
    // case every completion keeps paying the main rewards.
    public long RepeatRewardExp { get; set; }
    public List<QuestReward> RepeatRewardItems { get; set; } = new();

    /// <summary>Whether a distinct repeat-reward set is defined (else subsequent completions pay the main set).</summary>
    [JsonIgnore]
    public bool HasRepeatRewards => RepeatRewardExp > 0 || RepeatRewardItems.Count > 0;

    /// <summary>Whether a run held at <paramref name="status"/> pays the repeat set rather than the main one.
    ///
    /// <para>🔴 The single rule behind every place a reward amount is named — the turn-in that grants it, the
    /// journal, and the giver NPC's offer. <see cref="QuestStatus.Done"/> counts because accepting from Done
    /// starts a repeat run, so an offer to re-run a finished quest quotes what accepting it will actually
    /// pay. Anything else — never accepted, or a first run in progress — pays the main set.</para></summary>
    public bool PaysRepeatRewards(QuestStatus status)
        => HasRepeatRewards && status is QuestStatus.InProgressRepeat or QuestStatus.Done;

    // ── NPC roles (kept here, NOT on NpcRecord) ─────────────────────────────────
    /// <summary>NPC number that offers this quest (0 = not offered in-world).</summary>
    public int GiverNpc { get; set; }
    /// <summary>NPC number to turn in at; 0 = same as <see cref="GiverNpc"/>.</summary>
    public int TurnInNpc { get; set; }

    // ── Repeatability ───────────────────────────────────────────────────────────
    public bool Repeatable { get; set; }
    public QuestCadence Cadence { get; set; }

    /// <summary>Where to turn the quest in — the turn-in NPC, falling back to the giver.</summary>
    [JsonIgnore]
    public int EffectiveTurnInNpc => TurnInNpc != 0 ? TurnInNpc : GiverNpc;

    /// <summary>Deep copy for an off-thread snapshot / broadcast (the lists are mutable references).</summary>
    public QuestRecord Clone()
    {
        var c = (QuestRecord)MemberwiseClone();
        // AllowedClasses is a list too, so the shallow copy would alias it into the snapshot — the exact
        // bug the objective/reward copies below exist to avoid.
        c.AllowedClasses = AllowedClasses is null ? null : new List<short>(AllowedClasses);
        c.Objectives = new List<Objective>(Objectives.Count);
        foreach (var o in Objectives) c.Objectives.Add(o.Clone());
        c.RewardItems = new List<QuestReward>(RewardItems.Count);
        foreach (var r in RewardItems) c.RewardItems.Add(r.Clone());
        c.RepeatRewardItems = new List<QuestReward>(RepeatRewardItems.Count);
        foreach (var r in RepeatRewardItems) c.RepeatRewardItems.Add(r.Clone());
        return c;
    }
}

/// <summary>One item reward on a quest (item number + amount). Gold rides <c>RewardGold</c>, EXP rides
/// <c>RewardExp</c>; gold is item #1 so it could also ride here, but the dedicated fields keep the common
/// case simple.</summary>
public sealed class QuestReward
{
    public int ItemNum { get; set; }
    public int Quantity { get; set; }
    public QuestReward Clone() => (QuestReward)MemberwiseClone();
}
