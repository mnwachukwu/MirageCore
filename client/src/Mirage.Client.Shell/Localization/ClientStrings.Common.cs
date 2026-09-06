using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>Words shared across the whole UI: common verbs, stat labels, and tooltip fields.</summary>
public static partial class ClientStrings
{
    // ── Common ────────────────────────────────────────────────────────────────
    public const string Common_Confirm = nameof(Common_Confirm);
    public const string Common_Cancel = nameof(Common_Cancel);
    public const string Common_OK = nameof(Common_OK);
    public const string Common_Drop = nameof(Common_Drop);
    public const string Common_Delete = nameof(Common_Delete);
    public const string Common_Create = nameof(Common_Create);
    public const string Common_Equipped = nameof(Common_Equipped);
    public const string Common_Broken = nameof(Common_Broken);
    public const string Common_Empty = nameof(Common_Empty);
    public const string Common_Prepared = nameof(Common_Prepared);
    // Inventory-panel link labels (Sort/Equipment) + the equipment sub-view's back button; the Link
    // widget adds the "[…]" brackets, so the strings stay plain.
    public const string Common_SortHeader = nameof(Common_SortHeader);
    public const string Common_EquipmentHeader = nameof(Common_EquipmentHeader);
    public const string Common_Back = nameof(Common_Back);
    public const string Common_TotalBonuses = nameof(Common_TotalBonuses);
    public const string Common_LevelFormat = nameof(Common_LevelFormat);
    public const string Common_LevelWithClassFormat = nameof(Common_LevelWithClassFormat);
    public const string Common_GoldLabel = nameof(Common_GoldLabel);
    public const string Common_NameLabel = nameof(Common_NameLabel);
    public const string Common_PasswordLabel = nameof(Common_PasswordLabel);
    public const string Common_CannotConnect = nameof(Common_CannotConnect);
    public const string Common_ConnectionTimedOut = nameof(Common_ConnectionTimedOut);
    public const string Common_ServerIdentityChanged = nameof(Common_ServerIdentityChanged);
    public const string Common_ServerIdentityChangedTitle = nameof(Common_ServerIdentityChangedTitle);
    // Four lines that IdentityChangedPrompt joins into one wrapped block. Separate keys because the
    // SpriteFont draws no newline: a line break inside a value fails the charset guard.
    public const string Common_ServerIdentityChangedDetail = nameof(Common_ServerIdentityChangedDetail); // "{Host}" "{Port}"
    public const string Common_ServerIdentityOnRecord = nameof(Common_ServerIdentityOnRecord);
    public const string Common_ServerIdentityOffered = nameof(Common_ServerIdentityOffered);
    public const string Common_ServerIdentityAdvice = nameof(Common_ServerIdentityAdvice);
    public const string Common_TrustNewCertificate = nameof(Common_TrustNewCertificate);
    public const string Common_ServerPinCleared = nameof(Common_ServerPinCleared);
    public const string Common_Disconnected = nameof(Common_Disconnected);
    public const string Common_Connecting = nameof(Common_Connecting);
    public const string Common_NameTooShort = nameof(Common_NameTooShort);
    public const string Common_PasswordTooShort = nameof(Common_PasswordTooShort);
    public const string Common_PasswordsDoNotMatch = nameof(Common_PasswordsDoNotMatch);

    // ── Shared stat labels ────────────────────────────────────────────────────
    public const string Stats_Hp = nameof(Stats_Hp);
    public const string Stats_Mp = nameof(Stats_Mp);
    public const string Stats_Sp = nameof(Stats_Sp);
    public const string Stats_Exp = nameof(Stats_Exp);
    public const string Stats_Str = nameof(Stats_Str);
    public const string Stats_Def = nameof(Stats_Def);
    public const string Stats_Int = nameof(Stats_Int);
    public const string Stats_Spd = nameof(Stats_Spd);
    public const string Stats_PCrit = nameof(Stats_PCrit);
    public const string Stats_MCrit = nameof(Stats_MCrit);
    public const string Stats_Block = nameof(Stats_Block);
    public const string Stats_Dodge = nameof(Stats_Dodge);
    public const string Stats_PDmg = nameof(Stats_PDmg);
    public const string Stats_MDmg = nameof(Stats_MDmg);
    public const string Stats_MpDmg = nameof(Stats_MpDmg);
    public const string Stats_SpDmg = nameof(Stats_SpDmg);
    public const string Stats_HpRestore = nameof(Stats_HpRestore);
    public const string Stats_MpRestore = nameof(Stats_MpRestore);
    public const string Stats_SpRestore = nameof(Stats_SpRestore);
    public const string Stats_Mit = nameof(Stats_Mit);
    public const string Stats_Sprint = nameof(Stats_Sprint);
    public const string Stats_HpRegen = nameof(Stats_HpRegen);
    public const string Stats_MpRegen = nameof(Stats_MpRegen);
    public const string Stats_SpRegen = nameof(Stats_SpRegen);
    public const string Stats_RegenFormat = nameof(Stats_RegenFormat);
    public const string Stats_PkTimer = nameof(Stats_PkTimer);
    public const string Stats_Points = nameof(Stats_Points);

    // Floating combat text (Block/Dodge over an entity; vital labels reuse Stats_*).
    public const string Combat_Blocked = nameof(Combat_Blocked);
    public const string Combat_Dodged = nameof(Combat_Dodged);
    public const string Combat_Missed = nameof(Combat_Missed);
    public const string Combat_LevelUp = nameof(Combat_LevelUp);
    public const string Combat_EnterCombat = nameof(Combat_EnterCombat);
    public const string Combat_EndCombat = nameof(Combat_EndCombat);

    // ── Tooltip (item/spell hover labels) ───────────────────────────────────────
    public const string Tooltip_Durability = nameof(Tooltip_Durability);
    public const string Tooltip_StrReq = nameof(Tooltip_StrReq);
    public const string Tooltip_DefReq = nameof(Tooltip_DefReq);
    public const string Tooltip_Restores = nameof(Tooltip_Restores);
    public const string Tooltip_Drains = nameof(Tooltip_Drains);
    public const string Tooltip_Quantity = nameof(Tooltip_Quantity);
    public const string Tooltip_ClassReq = nameof(Tooltip_ClassReq);
    public const string NumberPrompt_OverMax = nameof(NumberPrompt_OverMax);
    public const string Tooltip_LevelReq = nameof(Tooltip_LevelReq);
    public const string Tooltip_Teaches = nameof(Tooltip_Teaches);
    public const string Tooltip_MpCost = nameof(Tooltip_MpCost);
    // Action bar
    public const string HotkeyBar_EmptyHint = nameof(HotkeyBar_EmptyHint);
    public const string HotkeyBar_GamepadModifier = nameof(HotkeyBar_GamepadModifier);
    public const string HotkeyBar_AssignSubmenu = nameof(HotkeyBar_AssignSubmenu);
    public const string HotkeyBar_AssignSlot = nameof(HotkeyBar_AssignSlot);
    public const string HotkeyBar_AssignSlotBound = nameof(HotkeyBar_AssignSlotBound);
    public const string HotkeyBar_Clear = nameof(HotkeyBar_Clear);
    public const string HotkeyBar_NothingBound = nameof(HotkeyBar_NothingBound);
    public const string HotkeyBar_ItemGone = nameof(HotkeyBar_ItemGone);
    public const string HotkeyBar_SpellGone = nameof(HotkeyBar_SpellGone);
    public const string Tooltip_ReagentCost = nameof(Tooltip_ReagentCost);
    public const string Tooltip_ReagentCostRained = nameof(Tooltip_ReagentCostRained);   // rain-doubled value: "{Count} (x2)"
    public const string Tooltip_ReagentDepletes = nameof(Tooltip_ReagentDepletes);
    public const string Tooltip_ReagentChancePercent = nameof(Tooltip_ReagentChancePercent);   // how often: "{Percent}%"
    public const string Tooltip_IntReq = nameof(Tooltip_IntReq);
}
