namespace Mirage.Client.Shell.Panels;

/// <summary>Z-order slot numbers for the floating in-game panels — the single source of truth that
/// <see cref="Screens.GameplayScreen"/>'s <c>Panel*</c> constants alias and
/// <see cref="PanelPolicies.BySlot"/> is indexed by.</summary>
public static class PanelSlots
{
    public const int Inventory = 0;
    public const int Spells = 1;
    public const int Training = 2;
    public const int Shop = 3;
    public const int Options = 4;
    public const int Stats = 5;
    public const int Help = 6;
    public const int Controls = 7;
    public const int Bank = 8;
    public const int Inn = 9;
    public const int Mail = 10;
    public const int Social = 11;
    public const int Market = 12;
    public const int Trade = 13;
    public const int QuestLog = 14;
    public const int QuestDialog = 15;
    public const int Conversation = 16;
    public const int Moderation = 17;

    /// <summary>Number of slots — the registry and the policy table are both this long.</summary>
    public const int Count = Moderation + 1;
}

/// <summary>
/// What a panel IS, as opposed to how it is driven.
///
/// <para>GameplayScreen's registry pairs each panel with delegates that need the live panel instance,
/// the graphics device and the frame — none of which exist in a headless test. These five facts need
/// none of that, so keeping them separate is what makes the panel POLICY assertable while the wiring
/// stays where it has to be.</para>
/// </summary>
/// <param name="ConfigKey">Stable key its position persists under, or null when the panel's position
/// is not saved (the server-driven dialogs, which appear where the game puts them).</param>
/// <param name="PlayerToggleable">Whether a keybind or chat command may open/close it. False for the
/// server-driven panels — shop, trade, quest dialog, conversation — which appear only when the server
/// says so, and therefore have no toggle entry point.</param>
/// <param name="BlocksMovement">Whether world movement is locked while it is open.</param>
/// <param name="ClosesOnLeave">Whether leaving the screen closes it. False for the server-driven
/// SESSIONS (market, trade): closing those client-side without telling the server would leave the two
/// ends disagreeing about whether the window is up.</param>
/// <param name="CountsAsOpenForEscape">Whether its being open makes Escape close a panel rather than
/// raise the quit dialog.</param>
public readonly record struct PanelPolicy(
    string? ConfigKey,
    bool PlayerToggleable,
    bool BlocksMovement,
    bool ClosesOnLeave,
    bool CountsAsOpenForEscape);

/// <summary>
/// The per-panel policy table, indexed by <see cref="PanelSlots"/>.
///
/// <para>Five facts in ONE row per panel. Spread across separate switches in GameplayScreen they have to
/// be edited in lockstep whenever a panel is added, and missing one fails silently at runtime rather
/// than at build time. PanelPolicyTests asserts the table's shape, so a missing or default row fails the
/// build instead of the game.</para>
/// </summary>
public static class PanelPolicies
{
    public static readonly PanelPolicy[] BySlot = BuildTable();

    static PanelPolicy[] BuildTable()
    {
        var t = new PanelPolicy[PanelSlots.Count];

        // ── Player-opened panels ──────────────────────────────────────────────
        t[PanelSlots.Inventory] = new("Inventory", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Spells] = new("Spells", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Stats] = new("Stats", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Help] = new("Help", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Social] = new("Social", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // The options panel has no saved position (it centers itself) but is otherwise ordinary.
        t[PanelSlots.Options] = new(null, PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // Controls counts for Escape like every other player-opened panel. It did not until this table
        // existed: it was the one panel missing from a fourteen-term boolean chain, so Escape with only
        // the Controls panel open raised the QUIT dialog instead of closing it — even though
        // CloseTopPanel already knew how to close it. Its sibling Help was present, which is what
        // marked the omission as an oversight rather than a decision.
        t[PanelSlots.Controls] = new("Controls", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // Moderation is Creator-only, but its POLICY is ordinary: an admin reading a list should still be
        // able to walk away from a fight while it is up, and Escape should close it like anything else.
        // The access check lives in the panel and again on the server, not here — this table is about
        // what a panel IS, and gating it here would put the rule in a third place.
        t[PanelSlots.Moderation] = new("Moderation", PlayerToggleable: true, BlocksMovement: false, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // ── Movement-locking counters and modals ──────────────────────────────
        t[PanelSlots.Training] = new("Training", PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Bank] = new("Bank", PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Inn] = new("Inn", PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Mail] = new("Mail", PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.QuestLog] = new(null, PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // ── Server-driven ─────────────────────────────────────────────────────
        // Opened by the server pushing state, so no player toggle. Shop closes on leave; Market and
        // Trade are live SESSIONS the server also tracks, so tearing the screen down must not close
        // them behind its back.
        t[PanelSlots.Shop] = new("Shop", PlayerToggleable: false, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Market] = new("Market", PlayerToggleable: true, BlocksMovement: true, ClosesOnLeave: false, CountsAsOpenForEscape: true);
        t[PanelSlots.QuestDialog] = new(null, PlayerToggleable: false, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);
        t[PanelSlots.Conversation] = new(null, PlayerToggleable: false, BlocksMovement: true, ClosesOnLeave: true, CountsAsOpenForEscape: true);

        // Trade is handled ahead of the generic Escape path (Escape CANCELS the trade rather than
        // closing the window), so it never reaches the open-for-escape check.
        t[PanelSlots.Trade] = new("Trade", PlayerToggleable: false, BlocksMovement: true, ClosesOnLeave: false, CountsAsOpenForEscape: false);

        return t;
    }

    /// <summary>Whether any open panel locks world movement.</summary>
    public static bool AnyBlocksMovement(Func<int, bool> isOpen) => AnyWhere(isOpen, p => p.BlocksMovement);

    /// <summary>Whether Escape should close a panel rather than raise the quit dialog.</summary>
    public static bool AnyOpenForEscape(Func<int, bool> isOpen) => AnyWhere(isOpen, p => p.CountsAsOpenForEscape);

    static bool AnyWhere(Func<int, bool> isOpen, Func<PanelPolicy, bool> pick)
    {
        for (int slot = 0; slot < BySlot.Length; slot++)
            if (pick(BySlot[slot]) && isOpen(slot)) return true;
        return false;
    }
}
