using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>One progress-tracked goal in the shared objective kernel: "do <see cref="Kind"/> to
/// <see cref="Target"/>, <see cref="Count"/> times." Deliberately scope-agnostic — it carries no notion
/// of who owns it — so the guild-quest layer (<c>GuildQuestDef</c>, its first customer) and a future
/// player-quest system (<c>QuestRecord</c>) both ride it without the kernel knowing about either. v1 only
/// wires <see cref="ObjectiveKind.Kill"/>; the other kinds are declared plumbing. A plain POCO: it
/// embeds in a persisted record (a guild's active quest) and travels whole on the wire to drive a quest
/// board, so keep it serializable.</summary>
public sealed class Objective
{
    /// <summary>What action counts. <see cref="ObjectiveKind.None"/> = an empty/unset objective.</summary>
    public ObjectiveKind Kind { get; set; }
    /// <summary>What the action targets (e.g. the NPC number for a Kill objective). 0 = "any target of
    /// this kind" (a wildcard), following the 1-based-with-0-sentinel convention.</summary>
    public int Target { get; set; }
    /// <summary>How many actions complete the objective.</summary>
    public int Count { get; set; }
    /// <summary>How many have been done so far (clamped to 0..<see cref="Count"/>).</summary>
    public int Progress { get; set; }

    /// <summary>True once enough progress has accrued. Derived, so it is neither persisted nor sent —
    /// a get-only property can never round-trip anyway; the receiver recomputes it from
    /// <see cref="Progress"/> and <see cref="Count"/>, which do travel.</summary>
    [JsonIgnore]
    public bool IsComplete => Progress >= Count;

    /// <summary>Advance this objective if <paramref name="kind"/> and <paramref name="target"/> match
    /// (a <see cref="Target"/> of 0 matches any target), clamping <see cref="Progress"/> at
    /// <see cref="Count"/>. Returns true ONLY on the call that pushes it from incomplete to complete —
    /// the single completion edge a caller keys its reward off — so a matching hit on an
    /// already-complete objective returns false.</summary>
    public bool TryAdvance(ObjectiveKind kind, int target, int amount = 1)
    {
        if (amount <= 0 || IsComplete) return false;
        if (Kind != kind) return false;
        if (Target != 0 && Target != target) return false;
        Progress = Math.Min(Progress + amount, Count);
        return IsComplete;
    }

    /// <summary>Shallow copy is a full copy (all fields are value types) — snapshot for an off-thread
    /// account/guild write.</summary>
    public Objective Clone() => (Objective)MemberwiseClone();
}
