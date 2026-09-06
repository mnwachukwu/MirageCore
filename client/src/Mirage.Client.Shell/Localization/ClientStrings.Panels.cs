using Mirage.Shared.Localization;

namespace Mirage.Client.Shell.Localization;

/// <summary>The trading and inventory panels — inn, market, direct trade, bank, shop, inventory,
/// spells, stats, training — plus the HUD.</summary>
public static partial class ClientStrings
{
    // ── InnPanel ──────────────────────────────────────────────────────────────
    public const string InnPanel_SetSpawnButton = nameof(InnPanel_SetSpawnButton);
    public const string InnPanel_AccessBankButton = nameof(InnPanel_AccessBankButton);
    public const string InnPanel_MarketplaceButton = nameof(InnPanel_MarketplaceButton);

    // ── MarketPanel ─────────────────────────────────────────────────────────────
    public const string MarketPanel_Title = nameof(MarketPanel_Title);
    public const string ConsolePanel_Title = nameof(ConsolePanel_Title);
    public const string MarketPanel_TabBrowse = nameof(MarketPanel_TabBrowse);
    public const string MarketPanel_TabMine = nameof(MarketPanel_TabMine);
    public const string MarketPanel_ColItem = nameof(MarketPanel_ColItem);
    public const string MarketPanel_ColSeller = nameof(MarketPanel_ColSeller);
    public const string MarketPanel_ColPrice = nameof(MarketPanel_ColPrice);
    public const string MarketPanel_ColTimeLeft = nameof(MarketPanel_ColTimeLeft);
    public const string MarketPanel_Buy = nameof(MarketPanel_Buy);
    public const string MarketPanel_ConfirmBuy = nameof(MarketPanel_ConfirmBuy);
    public const string MarketPanel_CancelListing = nameof(MarketPanel_CancelListing);
    public const string MarketPanel_ListItem = nameof(MarketPanel_ListItem);
    public const string MarketPanel_Empty = nameof(MarketPanel_Empty);
    public const string MarketPanel_EmptyMine = nameof(MarketPanel_EmptyMine);
    public const string MarketPanel_ListTitle = nameof(MarketPanel_ListTitle);
    public const string MarketPanel_PriceLabel = nameof(MarketPanel_PriceLabel);
    public const string MarketPanel_TaxPreview = nameof(MarketPanel_TaxPreview);
    public const string MarketPanel_List = nameof(MarketPanel_List);
    public const string MarketPanel_TabSales = nameof(MarketPanel_TabSales);
    public const string MarketPanel_ColBuyer = nameof(MarketPanel_ColBuyer);
    public const string MarketPanel_ColNet = nameof(MarketPanel_ColNet);
    public const string MarketPanel_ColDate = nameof(MarketPanel_ColDate);
    public const string MarketPanel_EmptySales = nameof(MarketPanel_EmptySales);
    public const string MarketPanel_PriceLabelPerUnit = nameof(MarketPanel_PriceLabelPerUnit);
    public const string MarketPanel_TaxPreviewPerUnit = nameof(MarketPanel_TaxPreviewPerUnit);
    public const string MarketPanel_PricePerUnitFormat = nameof(MarketPanel_PricePerUnitFormat);
    public const string MarketPanel_Refresh = nameof(MarketPanel_Refresh);
    public const string MarketPanel_QtyPrompt = nameof(MarketPanel_QtyPrompt);

    // ── Direct trade window + incoming-invite dialog ────────────────────────
    public const string TradePanel_TitleFormat = nameof(TradePanel_TitleFormat);
    public const string TradePanel_YourOffer = nameof(TradePanel_YourOffer);
    public const string TradePanel_TheirOfferFormat = nameof(TradePanel_TheirOfferFormat);
    public const string TradePanel_YourInventory = nameof(TradePanel_YourInventory);
    public const string TradePanel_OfferButton = nameof(TradePanel_OfferButton);
    public const string TradePanel_RemoveButton = nameof(TradePanel_RemoveButton);
    public const string TradePanel_Confirm = nameof(TradePanel_Confirm);
    public const string TradePanel_Unconfirm = nameof(TradePanel_Unconfirm);
    public const string TradePanel_StatusConfirmed = nameof(TradePanel_StatusConfirmed);
    public const string TradePanel_StatusWaiting = nameof(TradePanel_StatusWaiting);
    public const string TradePanel_YouLabel = nameof(TradePanel_YouLabel);
    public const string TradeRequest_Format = nameof(TradeRequest_Format);

    public const string InnPanel_SetSpawnPrompt = nameof(InnPanel_SetSpawnPrompt);
    public const string InnPanel_YourGoldLabel = nameof(InnPanel_YourGoldLabel);
    public const string InnPanel_CostLabel = nameof(InnPanel_CostLabel);
    public const string InnPanel_MainPrompt = nameof(InnPanel_MainPrompt);

    // ── BankPanel ─────────────────────────────────────────────────────────────
    public const string BankPanel_Title = nameof(BankPanel_Title);
    public const string BankPanel_InventoryHeader = nameof(BankPanel_InventoryHeader);
    public const string BankPanel_BankHeader = nameof(BankPanel_BankHeader);
    public const string BankPanel_DepositButton = nameof(BankPanel_DepositButton);
    public const string BankPanel_WithdrawButton = nameof(BankPanel_WithdrawButton);
    public const string BankPanel_DepositItemLabel = nameof(BankPanel_DepositItemLabel);
    public const string BankPanel_WithdrawItemLabel = nameof(BankPanel_WithdrawItemLabel);
    public const string BankPanel_AmountPrompt = nameof(BankPanel_AmountPrompt);

    // ── ShopPanel ─────────────────────────────────────────────────────────────
    public const string ShopPanel_LevelReq = nameof(ShopPanel_LevelReq);
    public const string ShopPanel_TradeButton = nameof(ShopPanel_TradeButton);
    public const string ShopPanel_FixItemButton = nameof(ShopPanel_FixItemButton);
    public const string ShopPanel_BuyTab = nameof(ShopPanel_BuyTab);
    public const string ShopPanel_TradeTab = nameof(ShopPanel_TradeTab);
    public const string ShopPanel_SellTab = nameof(ShopPanel_SellTab);
    public const string ShopPanel_BuyButton = nameof(ShopPanel_BuyButton);
    public const string ShopPanel_BuyHowMany = nameof(ShopPanel_BuyHowMany);
    public const string ShopPanel_SellHowMany = nameof(ShopPanel_SellHowMany);
    public const string ShopPanel_EachPrice = nameof(ShopPanel_EachPrice);
    public const string ShopPanel_BarterHowMany = nameof(ShopPanel_BarterHowMany);
    public const string ShopPanel_PerHelping = nameof(ShopPanel_PerHelping);
    public const string ShopPanel_SellButton = nameof(ShopPanel_SellButton);
    public const string ShopPanel_SalesRow = nameof(ShopPanel_SalesRow);
    public const string ShopPanel_SellRow = nameof(ShopPanel_SellRow);
    public const string ShopPanel_SellItemLabel = nameof(ShopPanel_SellItemLabel);
    public const string ShopPanel_SellOffer = nameof(ShopPanel_SellOffer);
    public const string ShopPanel_SellForNothing = nameof(ShopPanel_SellForNothing);
    public const string ShopPanel_FixButton = nameof(ShopPanel_FixButton);
    public const string ShopPanel_RepairItemLabel = nameof(ShopPanel_RepairItemLabel);
    public const string ShopPanel_DurabilityLabel = nameof(ShopPanel_DurabilityLabel);
    public const string ShopPanel_PerfectCondition = nameof(ShopPanel_PerfectCondition);
    public const string ShopPanel_FullRepairCost = nameof(ShopPanel_FullRepairCost);
    public const string ShopPanel_PartialRepairCost = nameof(ShopPanel_PartialRepairCost);
    public const string ShopPanel_DurabilityGain = nameof(ShopPanel_DurabilityGain);
    public const string ShopPanel_InsufficientGold = nameof(ShopPanel_InsufficientGold);
    public const string ShopPanel_TeachesSpell = nameof(ShopPanel_TeachesSpell);
    public const string ShopPanel_MpCost = nameof(ShopPanel_MpCost);
    public const string ShopPanel_ReagentCost = nameof(ShopPanel_ReagentCost);
    public const string ShopPanel_ReagentDepletes = nameof(ShopPanel_ReagentDepletes);
    public const string ShopPanel_PotionEffect = nameof(ShopPanel_PotionEffect);
    public const string ShopPanel_TradeCost = nameof(ShopPanel_TradeCost);
    public const string ShopPanel_StatRequirement = nameof(ShopPanel_StatRequirement);
    public const string ShopPanel_IntRequirement = nameof(ShopPanel_IntRequirement);
    public const string ShopPanel_ClassRequirement = nameof(ShopPanel_ClassRequirement);
    public const string ShopPanel_AlreadyKnowSpell = nameof(ShopPanel_AlreadyKnowSpell);
    public const string ShopPanel_RequirementsNotMet = nameof(ShopPanel_RequirementsNotMet);
    public const string ShopPanel_CannotLearnSpell = nameof(ShopPanel_CannotLearnSpell);

    // ── InventoryPanel ────────────────────────────────────────────────────────
    public const string InventoryPanel_Title = nameof(InventoryPanel_Title);
    public const string InventoryPanel_UseItemButton = nameof(InventoryPanel_UseItemButton);
    public const string InventoryPanel_DropItemButton = nameof(InventoryPanel_DropItemButton);
    public const string InventoryPanel_DropItemLabel = nameof(InventoryPanel_DropItemLabel);
    public const string InventoryPanel_DestroyDropWarn = nameof(InventoryPanel_DestroyDropWarn);
    public const string InventoryPanel_AmountPrompt = nameof(InventoryPanel_AmountPrompt);
    public const string InventoryPanel_HpPotionsLong = nameof(InventoryPanel_HpPotionsLong);
    public const string InventoryPanel_MpPotionsLong = nameof(InventoryPanel_MpPotionsLong);
    public const string InventoryPanel_SpPotionsLong = nameof(InventoryPanel_SpPotionsLong);
    public const string InventoryPanel_HpPotionsShort = nameof(InventoryPanel_HpPotionsShort);
    public const string InventoryPanel_MpPotionsShort = nameof(InventoryPanel_MpPotionsShort);
    public const string InventoryPanel_SpPotionsShort = nameof(InventoryPanel_SpPotionsShort);

    // ── SpellPanel ────────────────────────────────────────────────────────────
    public const string SpellPanel_Title = nameof(SpellPanel_Title);
    public const string SpellPanel_CastButton = nameof(SpellPanel_CastButton);
    public const string SpellPanel_PrepareButton = nameof(SpellPanel_PrepareButton);
    public const string SpellPanel_ForgetButton = nameof(SpellPanel_ForgetButton);
    public const string SpellPanel_ForgetPrompt = nameof(SpellPanel_ForgetPrompt);
    public const string SpellPanel_ForgetHint1 = nameof(SpellPanel_ForgetHint1);
    public const string SpellPanel_ForgetHint2 = nameof(SpellPanel_ForgetHint2);

    // ── StatsPanel ────────────────────────────────────────────────────────────
    public const string StatsPanel_Title = nameof(StatsPanel_Title);
    public const string StatsPanel_TotalExpFormat = nameof(StatsPanel_TotalExpFormat);
    public const string StatsPanel_MaxVitalFormat = nameof(StatsPanel_MaxVitalFormat);

    // ── TrainingPanel ─────────────────────────────────────────────────────────
    public const string TrainingPanel_Title = nameof(TrainingPanel_Title);
    public const string TrainingPanel_StrFormat = nameof(TrainingPanel_StrFormat);
    public const string TrainingPanel_DefFormat = nameof(TrainingPanel_DefFormat);
    public const string TrainingPanel_SpdFormat = nameof(TrainingPanel_SpdFormat);
    public const string TrainingPanel_IntFormat = nameof(TrainingPanel_IntFormat);
    public const string TrainingPanel_PointsFormat = nameof(TrainingPanel_PointsFormat);
    public const string TrainingPanel_ResetButton = nameof(TrainingPanel_ResetButton);

    // ── ModerationPanel (Creator only) ────────────────────────────────────────
    public const string ModerationPanel_Title = nameof(ModerationPanel_Title);
    public const string ModerationPanel_TabBans = nameof(ModerationPanel_TabBans);
    public const string ModerationPanel_TabPenalties = nameof(ModerationPanel_TabPenalties);
    public const string ModerationPanel_TabMachines = nameof(ModerationPanel_TabMachines);
    public const string ModerationPanel_NoMachines = nameof(ModerationPanel_NoMachines);
    public const string ModerationPanel_MachineMode = nameof(ModerationPanel_MachineMode);
    public const string ModerationPanel_MachineModeSignal = nameof(ModerationPanel_MachineModeSignal);
    public const string ModerationPanel_MachineModeBlock = nameof(ModerationPanel_MachineModeBlock);
    public const string ModerationPanel_Refresh = nameof(ModerationPanel_Refresh);
    public const string ModerationPanel_Lift = nameof(ModerationPanel_Lift);
    public const string ModerationPanel_NoBans = nameof(ModerationPanel_NoBans);
    public const string ModerationPanel_NoPenalties = nameof(ModerationPanel_NoPenalties);
    public const string ModerationPanel_NotLoaded = nameof(ModerationPanel_NotLoaded);
    public const string ModerationPanel_Scanned = nameof(ModerationPanel_Scanned);
    public const string ModerationPanel_PenaltyDetail = nameof(ModerationPanel_PenaltyDetail);
    public const string ModerationPanel_PlayingAs = nameof(ModerationPanel_PlayingAs);

    // ── HudPanel ──────────────────────────────────────────────────────────────
    public const string HudPanel_InventoryButton = nameof(HudPanel_InventoryButton);
    public const string HudPanel_SpellsButton = nameof(HudPanel_SpellsButton);
    public const string HudPanel_StatsButton = nameof(HudPanel_StatsButton);
    public const string HudPanel_TrainingButton = nameof(HudPanel_TrainingButton);
    public const string HudPanel_QuestLogButton = nameof(HudPanel_QuestLogButton);
    public const string HudPanel_SocialButton = nameof(HudPanel_SocialButton);
    public const string HudPanel_LogoutButton = nameof(HudPanel_LogoutButton);
    public const string HudPanel_OptionsLinkInGame = nameof(HudPanel_OptionsLinkInGame);
    public const string HudPanel_HelpLink = nameof(HudPanel_HelpLink);
    public const string HudPanel_MailLinkInGame = nameof(HudPanel_MailLinkInGame);
    public const string HudPanel_OptionsLinkPregame = nameof(HudPanel_OptionsLinkPregame);
    public const string HudPanel_ConfigureLink = nameof(HudPanel_ConfigureLink);
    // Sidebar map-name fallback shown when a map has neither a DisplayName nor an internal Name.
    public const string HudPanel_MapNameFallbackFormat = nameof(HudPanel_MapNameFallbackFormat);
    // Time-of-Day status line (between map name and HP bar)
    public const string HudPanel_TimeDay = nameof(HudPanel_TimeDay);
    public const string HudPanel_TimeDusk = nameof(HudPanel_TimeDusk);
    public const string HudPanel_TimeNight = nameof(HudPanel_TimeNight);
    public const string HudPanel_TimeDawn = nameof(HudPanel_TimeDawn);
    public const string HudPanel_TimeToNight = nameof(HudPanel_TimeToNight); // tooltip: "Night in {Time}"
    public const string HudPanel_TimeToDay = nameof(HudPanel_TimeToDay);   // tooltip: "Day in {Time}"
    // Weather adjectives, prefixed onto the time-of-day label (e.g. "Windy Night")
    public const string HudPanel_WeatherClear = nameof(HudPanel_WeatherClear);
    public const string HudPanel_WeatherRainy = nameof(HudPanel_WeatherRainy);
    public const string HudPanel_WeatherHot = nameof(HudPanel_WeatherHot);
    public const string HudPanel_WeatherSnowy = nameof(HudPanel_WeatherSnowy);
    public const string HudPanel_WeatherWindy = nameof(HudPanel_WeatherWindy);
}
