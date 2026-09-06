namespace Mirage.Shared.Records;

/// <summary>A guild's active quest — the guild-layer template atop the shared objective kernel (#5).
/// v1 is always a Kill objective against a random spawning NPC, its count scaled by mob difficulty.
/// Progress is guild-wide (any member's kills advance <see cref="Objective"/>); completion pays the guild
/// <see cref="RewardExp"/> + <see cref="RewardGold"/>. Persisted on the <see cref="GuildRecord"/>; a
/// future player-quest system will define its own QuestDef on the same kernel.</summary>
public sealed class GuildQuestDef
{
    /// <summary>The kill objective (Kind=Kill, Target=NPC number, Count, Progress); advanced directly by
    /// the kill path (guild-wide).</summary>
    public Objective Objective { get; set; } = new();
    /// <summary>Guild XP awarded on completion (scaled by mob difficulty + guild level).</summary>
    public long RewardExp { get; set; }
    /// <summary>Vault gold awarded on completion.</summary>
    public long RewardGold { get; set; }
    /// <summary>UTC-seconds the quest expires (acquired + the 24h limit); dropped unrewarded past this.</summary>
    public long ExpiresUtc { get; set; }

    /// <summary>Deep copy (the mutable <see cref="Objective"/> is cloned) for an off-thread guild save.</summary>
    public GuildQuestDef Clone()
    {
        var c = (GuildQuestDef)MemberwiseClone();
        c.Objective = Objective.Clone();
        return c;
    }
}
