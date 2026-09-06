namespace Mirage.Server.Core.World;

/// <summary>Runtime projection of one live territory contest, published into <see cref="GameWorld.ContestZones"/>
/// by GuildTerritorySystem so MovementSystem (non-participant entry warnings) and SpawnSystem (NPC spawn
/// suppression) can read the war state WITHOUT referencing GuildTerritorySystem — that keeps those systems
/// acyclic (GuildTerritorySystem already depends on SpawnSystem for the despawns).
/// Runtime-only, like the contest itself; the list is empty whenever no contest runs.</summary>
public sealed class ContestZone
{
    /// <summary>The contested territory (MapGroup) index.</summary>
    public int TerritoryIndex;
    /// <summary>The territory's display name, for the non-participant entry warning.</summary>
    public string Name = "";
    /// <summary>The participating guild indices (defender + challengers); non-participants are warned on entry.</summary>
    public HashSet<int> Participants = new();
    /// <summary>Every map in the territory — NPC-spawn suppression + entry-crossing membership.</summary>
    public List<int> Maps = new();
    /// <summary>Which phase the contest is in, mirrored from <see cref="TerritoryContest.Phase"/> as it rolls.
    ///
    /// <para>The zone outlives the scoring: it stands through Setup, Contest AND Cooldown, because NPC
    /// suppression has to. What a bystander should be told does NOT stand through all three — during
    /// Cooldown the territory has already changed hands and there is nothing left to take part in.</para></summary>
    public ContestPhase Phase = ContestPhase.Setup;
}
