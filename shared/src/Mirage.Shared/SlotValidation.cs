namespace Mirage.Shared;

/// <summary>
/// Bound-check helpers for the 1-based indices used throughout the game. Every helper implements the same
/// `value >= 1 && value <= max` shape, so the guards across server, client and editor read
/// uniformly. Pure functions; no allocations.
///
/// <para><b>The record-family checks take their ceiling as an argument, deliberately.</b> Those ceilings
/// are per-server now (<see cref="RecordLimits"/>), and a helper that read a <c>const</c> would quietly
/// reject a legitimate record on a server configured larger than the caller was compiled against. Passing
/// it in makes a stale ceiling a compile error instead: there is nothing to forget. Server sites pass
/// <c>_world.Limits.X</c>, client sites <c>_state.Limits.X</c>.</para>
///
/// <para>The rest still read constants because they genuinely are fixed: per-character shapes are baked
/// into the save format, and the player/NPC slot checks bound the PROTOCOL — see the warning on
/// <see cref="IsValidPlayerSlot"/>.</para>
/// </summary>
public static class SlotValidation
{
    // ── Player / NPC instance slots (1-based per-server / per-map) ──────────────

    /// <summary><b>Bounds by the PROTOCOL ceiling, not a server's limit.</b> Right for a client deciding
    /// whether a slot number could ever be legal; WRONG anywhere server-side about to index a player —
    /// use <c>PlayerManager.IsValidSlot</c> there, or a slot of 400 passes this and then throws on a
    /// 20-slot world.</summary>
    public static bool IsValidPlayerSlot(int slot) => slot >= 1 && slot <= Constants.MaxPlayers;
    public static bool IsValidNpcSlot(int slot) => slot >= 1 && slot <= Constants.MaxMapNpcs;

    // ── Per-player slots (fixed: these shapes are in the save format) ───────────
    public static bool IsValidInvSlot(int slot) => slot >= 1 && slot <= Constants.MaxInv;
    public static bool IsValidBankSlot(int slot) => slot >= 1 && slot <= Constants.MaxBankSlots;
    public static bool IsValidSpellSlot(int slot) => slot >= 1 && slot <= Constants.MaxPlayerSpells;
    public static bool IsValidClassNum(int num) => num >= 1 && num <= Constants.MaxClasses;

    // ── World-data record numbers (per-server; pass the limit) ──────────────────
    public static bool IsValidMapNum(int mapNum, int maxMaps) => mapNum >= 1 && mapNum <= maxMaps;
    public static bool IsValidItemNum(int num, int maxItems) => num >= 1 && num <= maxItems;
    public static bool IsValidNpcNum(int num, int maxNpcs) => num >= 1 && num <= maxNpcs;
    public static bool IsValidShopNum(int num, int maxShops) => num >= 1 && num <= maxShops;
    public static bool IsValidSpellNum(int num, int maxSpells) => num >= 1 && num <= maxSpells;
    public static bool IsValidQuestNum(int num, int maxQuests) => num >= 1 && num <= maxQuests;
    public static bool IsValidConversationNum(int num, int maxConversations) => num >= 1 && num <= maxConversations;
    public static bool IsValidMapGroupNum(int num, int maxMapGroups) => num >= 1 && num <= maxMapGroups;

}
