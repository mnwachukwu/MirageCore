using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Shops, banks, and inventory — buying, selling, depositing, equipping and using.</summary>
public static partial class ServerStrings
{
    // ── ShopSystem ────────────────────────────────────────────────────────────
    public const string ShopSystem_NotNearShop = nameof(ShopSystem_NotNearShop);
    public const string ShopSystem_NotEnoughTrade = nameof(ShopSystem_NotEnoughTrade);
    public const string ShopSystem_TradedWith = nameof(ShopSystem_TradedWith);
    public const string ShopSystem_NotNearRepairShop = nameof(ShopSystem_NotNearRepairShop);
    public const string ShopSystem_NoRepairType = nameof(ShopSystem_NoRepairType);
    public const string ShopSystem_NoItemInSlot = nameof(ShopSystem_NoItemInSlot);
    public const string ShopSystem_CannotRepair = nameof(ShopSystem_CannotRepair);
    public const string ShopSystem_PerfectCond = nameof(ShopSystem_PerfectCond);
    public const string ShopSystem_InsufficientGold = nameof(ShopSystem_InsufficientGold);
    public const string ShopSystem_FullyRestored = nameof(ShopSystem_FullyRestored);
    public const string ShopSystem_PartiallyFixed = nameof(ShopSystem_PartiallyFixed);
    public const string ShopSystem_Bought = nameof(ShopSystem_Bought);
    public const string ShopSystem_BoughtMany = nameof(ShopSystem_BoughtMany);
    public const string ShopSystem_NotForSale = nameof(ShopSystem_NotForSale);
    public const string ShopSystem_Sold = nameof(ShopSystem_Sold);
    public const string ShopSystem_SoldMany = nameof(ShopSystem_SoldMany);
    public const string ShopSystem_SoldForNothing = nameof(ShopSystem_SoldForNothing);
    public const string ShopSystem_CannotSell = nameof(ShopSystem_CannotSell);
    public const string ShopSystem_UnequipFirst = nameof(ShopSystem_UnequipFirst);

    // ── BankSystem ────────────────────────────────────────────────────────────
    public const string BankSystem_NotNearBank = nameof(BankSystem_NotNearBank);
    public const string BankSystem_BankFull = nameof(BankSystem_BankFull);
    public const string BankSystem_UnequipFirst = nameof(BankSystem_UnequipFirst);
    public const string BankSystem_DepositPartial = nameof(BankSystem_DepositPartial);
    public const string BankSystem_WithdrawPartial = nameof(BankSystem_WithdrawPartial);

    // ── ItemSystem ────────────────────────────────────────────────────────────
    public const string ItemSystem_TooManyOnGround = nameof(ItemSystem_TooManyOnGround);
    public const string ItemSystem_DropPartial = nameof(ItemSystem_DropPartial);
    public const string ItemSystem_VitalFull = nameof(ItemSystem_VitalFull);
    public const string ItemSystem_UsedPotion = nameof(ItemSystem_UsedPotion);
    public const string ItemSystem_CantUsePotion = nameof(ItemSystem_CantUsePotion);
    public const string ItemSystem_LootClaimed = nameof(ItemSystem_LootClaimed);
    public const string ItemSystem_LootGone = nameof(ItemSystem_LootGone);
    public const string ItemSystem_LootTooFar = nameof(ItemSystem_LootTooFar);
    public const string ItemSystem_LootLeftBehind = nameof(ItemSystem_LootLeftBehind);
    public const string ItemSystem_PickedUpMultiple = nameof(ItemSystem_PickedUpMultiple);
    public const string ItemSystem_PickedUp = nameof(ItemSystem_PickedUp);
    public const string ItemSystem_DropMultiple = nameof(ItemSystem_DropMultiple);
    public const string ItemSystem_DropWithDurability = nameof(ItemSystem_DropWithDurability);
    public const string ItemSystem_Drop = nameof(ItemSystem_Drop);
    public const string ItemSystem_ItemLostOnDeath = nameof(ItemSystem_ItemLostOnDeath);
    public const string ItemSystem_CurrencyLostOnDeath = nameof(ItemSystem_CurrencyLostOnDeath);
    public const string ItemSystem_ItemDestroyed = nameof(ItemSystem_ItemDestroyed);
    public const string ItemSystem_CurrencyDestroyed = nameof(ItemSystem_CurrencyDestroyed);
    public const string ItemSystem_ScrollNoSpell = nameof(ItemSystem_ScrollNoSpell);
    public const string ItemSystem_SpellWrongClass = nameof(ItemSystem_SpellWrongClass);
    public const string ItemSystem_SpellIntReq = nameof(ItemSystem_SpellIntReq);
    public const string ItemSystem_LevelReq = nameof(ItemSystem_LevelReq);
    public const string ItemSystem_SpellLevelReq = nameof(ItemSystem_SpellLevelReq);
    public const string ItemSystem_SpellBookFull = nameof(ItemSystem_SpellBookFull);
    public const string ItemSystem_SpellAlreadyKnown = nameof(ItemSystem_SpellAlreadyKnown);
    public const string ItemSystem_StudyingSpell = nameof(ItemSystem_StudyingSpell);
    public const string ItemSystem_LearnedSpell = nameof(ItemSystem_LearnedSpell);
    public const string ItemSystem_WeaponStrReq = nameof(ItemSystem_WeaponStrReq);
    public const string ItemSystem_ArmorDefReq = nameof(ItemSystem_ArmorDefReq);
    public const string ItemSystem_HelmetDefReq = nameof(ItemSystem_HelmetDefReq);
    public const string ItemSystem_ShieldDefReq = nameof(ItemSystem_ShieldDefReq);
    public const string ItemSystem_GearUnequippedDelevel = nameof(ItemSystem_GearUnequippedDelevel);
    public const string ItemSystem_WrongClass = nameof(ItemSystem_WrongClass);
    public const string ItemSystem_ItemBroken = nameof(ItemSystem_ItemBroken);
    public const string ItemSystem_GearSwapCombat = nameof(ItemSystem_GearSwapCombat);
    public const string ItemSystem_KeyDissolves = nameof(ItemSystem_KeyDissolves);
}
