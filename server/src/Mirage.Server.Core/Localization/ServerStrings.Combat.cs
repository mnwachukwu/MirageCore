using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Everything a fight says: hits, blocks, dodges, criticals, death, PK flagging, and
/// the level-up and EXP lines a kill produces.</summary>
public static partial class ServerStrings
{
    // ── CombatSystem ──────────────────────────────────────────────────────────
    // Spoken on respawn when the player's respawn point names no real tile.
    public const string NpcAiSystem_NpcSays = nameof(NpcAiSystem_NpcSays);
}
