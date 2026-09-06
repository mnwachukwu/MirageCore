using Mirage.Shared.Localization;

namespace Mirage.Editor.Localization;

/// <summary>The per-entity editors — item, NPC, spell, class, shop, quest, conversation, and map
/// group — plus the status messages and entity type names they share.</summary>
public static partial class EditorStrings
{
    // ── ItemEditorView ────────────────────────────────────────────────────────
    public const string ItemEditor_AllTypesFilter = nameof(ItemEditor_AllTypesFilter);
    public const string ItemEditor_SelectPrompt = nameof(ItemEditor_SelectPrompt);
    public const string ItemEditor_SectionTitle = nameof(ItemEditor_SectionTitle);
    public const string ItemEditor_PicLabel = nameof(ItemEditor_PicLabel);
    public const string ItemEditor_ItemSheetLabel = nameof(ItemEditor_ItemSheetLabel);
    public const string ItemEditor_RestrictionsLabel = nameof(ItemEditor_RestrictionsLabel);
    public const string ItemEditor_NonTradeable = nameof(ItemEditor_NonTradeable);
    public const string ItemEditor_NonListable = nameof(ItemEditor_NonListable);
    public const string ItemEditor_NonMailable = nameof(ItemEditor_NonMailable);
    public const string ItemEditor_DestroyOnDrop = nameof(ItemEditor_DestroyOnDrop);
    public const string ItemEditor_NonJunkable = nameof(ItemEditor_NonJunkable);
    public const string ItemEditor_PriceLabel = nameof(ItemEditor_PriceLabel);
    public const string ItemEditor_SaveItemButton = nameof(ItemEditor_SaveItemButton);
    public const string ItemEditor_SpellSearchPlaceholder = nameof(ItemEditor_SpellSearchPlaceholder);
    // Notes panel — sub-headers, formula lines, and explanatory paragraphs.
    public const string ItemEditor_Notes_EquipmentHeader = nameof(ItemEditor_Notes_EquipmentHeader);
    public const string ItemEditor_Notes_EquipmentDurability = nameof(ItemEditor_Notes_EquipmentDurability);
    public const string ItemEditor_Notes_EquipmentPower = nameof(ItemEditor_Notes_EquipmentPower);
    public const string ItemEditor_Notes_EquipmentWeapon = nameof(ItemEditor_Notes_EquipmentWeapon);
    public const string ItemEditor_Notes_EquipmentArmor = nameof(ItemEditor_Notes_EquipmentArmor);
    public const string ItemEditor_Notes_EquipmentHelmet = nameof(ItemEditor_Notes_EquipmentHelmet);
    public const string ItemEditor_Notes_EquipmentShield = nameof(ItemEditor_Notes_EquipmentShield);
    public const string ItemEditor_Notes_EquipmentClassReq = nameof(ItemEditor_Notes_EquipmentClassReq);
    public const string ItemEditor_Notes_EquipmentShieldSide = nameof(ItemEditor_Notes_EquipmentShieldSide);
    public const string ItemEditor_Notes_PotionsHeader = nameof(ItemEditor_Notes_PotionsHeader);
    public const string ItemEditor_Notes_PotionsAmount = nameof(ItemEditor_Notes_PotionsAmount);
    public const string ItemEditor_Notes_SpellScrollHeader = nameof(ItemEditor_Notes_SpellScrollHeader);
    public const string ItemEditor_Notes_SpellScrollSpell = nameof(ItemEditor_Notes_SpellScrollSpell);
    public const string ItemEditor_Notes_KeyHeader = nameof(ItemEditor_Notes_KeyHeader);
    public const string ItemEditor_Notes_KeyId = nameof(ItemEditor_Notes_KeyId);
    public const string ItemEditor_Notes_CurrencyHeader = nameof(ItemEditor_Notes_CurrencyHeader);
    public const string ItemEditor_Notes_CurrencyDesc = nameof(ItemEditor_Notes_CurrencyDesc);

    // ── NpcEditorView ─────────────────────────────────────────────────────────
    public const string NpcEditor_AllBehaviorsFilter = nameof(NpcEditor_AllBehaviorsFilter);
    public const string NpcEditor_SelectPrompt = nameof(NpcEditor_SelectPrompt);
    public const string NpcEditor_SectionTitle = nameof(NpcEditor_SectionTitle);
    public const string NpcEditor_AttackSayLabel = nameof(NpcEditor_AttackSayLabel);
    public const string NpcEditor_SpriteSheetLabel = nameof(NpcEditor_SpriteSheetLabel);
    public const string NpcEditor_SpriteLabel = nameof(NpcEditor_SpriteLabel);
    public const string NpcEditor_SizeLabel = nameof(NpcEditor_SizeLabel);
    public const string NpcEditor_SpawnSecsLabel = nameof(NpcEditor_SpawnSecsLabel);
    public const string NpcEditor_BehaviorLabel = nameof(NpcEditor_BehaviorLabel);
    public const string NpcEditor_IsBossLabel = nameof(NpcEditor_IsBossLabel);
    public const string NpcEditor_EmitsLightLabel = nameof(NpcEditor_EmitsLightLabel);
    public const string NpcEditor_LightColorLabel = nameof(NpcEditor_LightColorLabel);
    public const string NpcEditor_LightRadiusLabel = nameof(NpcEditor_LightRadiusLabel);
    public const string NpcEditor_LightIntensityLabel = nameof(NpcEditor_LightIntensityLabel);
    public const string NpcEditor_LightFlickerLabel = nameof(NpcEditor_LightFlickerLabel);
    public const string NpcEditor_GroupLabel = nameof(NpcEditor_GroupLabel);
    public const string NpcEditor_RangeLabel = nameof(NpcEditor_RangeLabel);
    public const string NpcEditor_DropChanceLabel = nameof(NpcEditor_DropChanceLabel);
    public const string NpcEditor_DropItemLabel = nameof(NpcEditor_DropItemLabel);
    public const string NpcEditor_DropValueLabel = nameof(NpcEditor_DropValueLabel);
    public const string NpcEditor_StrLabel = nameof(NpcEditor_StrLabel);
    public const string NpcEditor_DefLabel = nameof(NpcEditor_DefLabel);
    public const string NpcEditor_SpdLabel = nameof(NpcEditor_SpdLabel);
    public const string NpcEditor_IntLabel = nameof(NpcEditor_IntLabel);
    public const string NpcEditor_ExtraHpLabel = nameof(NpcEditor_ExtraHpLabel);
    public const string NpcEditor_ExtraHpNote = nameof(NpcEditor_ExtraHpNote);
    public const string NpcEditor_TotalStatsLabel = nameof(NpcEditor_TotalStatsLabel);
    public const string NpcEditor_EquivLevelLabel = nameof(NpcEditor_EquivLevelLabel);   // "Level:" (the one player-faithful virtual level)
    public const string NpcEditor_LevelNote = nameof(NpcEditor_LevelNote);
    public const string NpcEditor_LightingHeader = nameof(NpcEditor_LightingHeader);
    public const string NpcEditor_VitalsHeader = nameof(NpcEditor_VitalsHeader);
    public const string NpcEditor_RegenHeader = nameof(NpcEditor_RegenHeader);
    public const string NpcEditor_EffectivenessHeader = nameof(NpcEditor_EffectivenessHeader);
    public const string NpcEditor_ChanceHeader = nameof(NpcEditor_ChanceHeader);
    public const string NpcEditor_RewardsHeader = nameof(NpcEditor_RewardsHeader);
    public const string NpcEditor_MaxHpLabel = nameof(NpcEditor_MaxHpLabel);
    public const string NpcEditor_MaxMpLabel = nameof(NpcEditor_MaxMpLabel);
    public const string NpcEditor_MaxSpLabel = nameof(NpcEditor_MaxSpLabel);
    public const string NpcEditor_HpRegenLabel = nameof(NpcEditor_HpRegenLabel);
    public const string NpcEditor_MpRegenLabel = nameof(NpcEditor_MpRegenLabel);
    public const string NpcEditor_SpRegenLabel = nameof(NpcEditor_SpRegenLabel);
    public const string NpcEditor_PCritLabel = nameof(NpcEditor_PCritLabel);
    public const string NpcEditor_MCritLabel = nameof(NpcEditor_MCritLabel);
    public const string NpcEditor_BlockLabel = nameof(NpcEditor_BlockLabel);
    public const string NpcEditor_DodgeLabel = nameof(NpcEditor_DodgeLabel);
    public const string NpcEditor_ExpLabel = nameof(NpcEditor_ExpLabel);
    public const string NpcEditor_PreviewLevelLabel = nameof(NpcEditor_PreviewLevelLabel);
    public const string NpcEditor_SaveNpcButton = nameof(NpcEditor_SaveNpcButton);
    public const string NpcEditor_DropItemSearchPlaceholder = nameof(NpcEditor_DropItemSearchPlaceholder);
    // Formula-notes panel — sub-headers, formula lines, and explanatory paragraphs.
    public const string NpcEditor_Formula_VitalsHeader = nameof(NpcEditor_Formula_VitalsHeader);
    public const string NpcEditor_Formula_VitalsBaseHp = nameof(NpcEditor_Formula_VitalsBaseHp);
    public const string NpcEditor_Formula_VitalsFavorPct = nameof(NpcEditor_Formula_VitalsFavorPct);
    public const string NpcEditor_Formula_VitalsMaxHp = nameof(NpcEditor_Formula_VitalsMaxHp);
    public const string NpcEditor_Formula_VitalsMaxMp = nameof(NpcEditor_Formula_VitalsMaxMp);
    public const string NpcEditor_Formula_VitalsMaxSp = nameof(NpcEditor_Formula_VitalsMaxSp);
    public const string NpcEditor_Formula_VitalsNote = nameof(NpcEditor_Formula_VitalsNote);
    public const string NpcEditor_Formula_RegenHeader = nameof(NpcEditor_Formula_RegenHeader);
    public const string NpcEditor_Formula_RegenHp = nameof(NpcEditor_Formula_RegenHp);
    public const string NpcEditor_Formula_RegenMp = nameof(NpcEditor_Formula_RegenMp);
    public const string NpcEditor_Formula_RegenSp = nameof(NpcEditor_Formula_RegenSp);
    public const string NpcEditor_Formula_RegenNote = nameof(NpcEditor_Formula_RegenNote);
    public const string NpcEditor_Formula_CombatHeader = nameof(NpcEditor_Formula_CombatHeader);
    public const string NpcEditor_Formula_CombatPDmg = nameof(NpcEditor_Formula_CombatPDmg);
    public const string NpcEditor_Formula_CombatMDmg = nameof(NpcEditor_Formula_CombatMDmg);
    public const string NpcEditor_Formula_CombatMit = nameof(NpcEditor_Formula_CombatMit);
    public const string NpcEditor_Formula_CombatFloor = nameof(NpcEditor_Formula_CombatFloor);
    public const string NpcEditor_Formula_CombatNote = nameof(NpcEditor_Formula_CombatNote);
    public const string NpcEditor_Formula_ExpHeader = nameof(NpcEditor_Formula_ExpHeader);
    public const string NpcEditor_Formula_ExpLine1 = nameof(NpcEditor_Formula_ExpLine1);
    public const string NpcEditor_Formula_ExpLine2 = nameof(NpcEditor_Formula_ExpLine2);
    public const string NpcEditor_Formula_ExpNote = nameof(NpcEditor_Formula_ExpNote);
    public const string NpcEditor_Formula_DropChanceHeader = nameof(NpcEditor_Formula_DropChanceHeader);
    public const string NpcEditor_Formula_DropChanceLine1 = nameof(NpcEditor_Formula_DropChanceLine1);
    public const string NpcEditor_Formula_DropChanceLine2 = nameof(NpcEditor_Formula_DropChanceLine2);
    public const string NpcEditor_Formula_DropChanceLine3 = nameof(NpcEditor_Formula_DropChanceLine3);
    public const string NpcEditor_Formula_DropChanceLine4 = nameof(NpcEditor_Formula_DropChanceLine4);
    public const string NpcEditor_Formula_DropChanceLine5 = nameof(NpcEditor_Formula_DropChanceLine5);
    public const string NpcEditor_Formula_DropChanceNote = nameof(NpcEditor_Formula_DropChanceNote);
    public const string NpcEditor_Formula_ChancesHeader = nameof(NpcEditor_Formula_ChancesHeader);
    public const string NpcEditor_Formula_ChancesCrit = nameof(NpcEditor_Formula_ChancesCrit);
    public const string NpcEditor_Formula_ChancesSpellCrit = nameof(NpcEditor_Formula_ChancesSpellCrit);
    public const string NpcEditor_Formula_ChancesBlock = nameof(NpcEditor_Formula_ChancesBlock);
    public const string NpcEditor_Formula_ChancesDodge = nameof(NpcEditor_Formula_ChancesDodge);
    public const string NpcEditor_Formula_ChancesNote = nameof(NpcEditor_Formula_ChancesNote);

    // ── SpellEditorView ───────────────────────────────────────────────────────
    public const string SpellEditor_AllSpellTypesFilter = nameof(SpellEditor_AllSpellTypesFilter);
    public const string SpellEditor_AllClassesFilter = nameof(SpellEditor_AllClassesFilter);
    public const string SpellEditor_SelectPrompt = nameof(SpellEditor_SelectPrompt);
    public const string SpellEditor_SectionTitle = nameof(SpellEditor_SectionTitle);
    public const string SpellEditor_MaxMpCostLabel = nameof(SpellEditor_MaxMpCostLabel);
    public const string SpellEditor_ReagentCostLabel = nameof(SpellEditor_ReagentCostLabel);
    public const string SpellEditor_ReagentChanceLabel = nameof(SpellEditor_ReagentChanceLabel);
    public const string SpellEditor_ReagentChancePercent = nameof(SpellEditor_ReagentChancePercent);   // "{Percent}%"
    public const string SpellEditor_SubHpMpCostValue = nameof(SpellEditor_SubHpMpCostValue);
    public const string SpellEditor_AddMpCostValue = nameof(SpellEditor_AddMpCostValue);
    public const string SpellEditor_MpCostNote = nameof(SpellEditor_MpCostNote);
    public const string SpellEditor_SaveSpellButton = nameof(SpellEditor_SaveSpellButton);
    public const string SpellEditor_GiveItemSearchPlaceholder = nameof(SpellEditor_GiveItemSearchPlaceholder);
    public const string SpellEditor_Formula_MagnitudeIntro = nameof(SpellEditor_Formula_MagnitudeIntro);
    public const string SpellEditor_Formula_MagnitudeBullet1 = nameof(SpellEditor_Formula_MagnitudeBullet1);
    public const string SpellEditor_Formula_MagnitudeBullet2 = nameof(SpellEditor_Formula_MagnitudeBullet2);
    public const string SpellEditor_Formula_MagnitudeBullet3 = nameof(SpellEditor_Formula_MagnitudeBullet3);
    public const string SpellEditor_Formula_ClassIntNote = nameof(SpellEditor_Formula_ClassIntNote);
    public const string SpellEditor_Formula_PlayerIntNote = nameof(SpellEditor_Formula_PlayerIntNote);
    public const string SpellEditor_Formula_MagnitudeHeader = nameof(SpellEditor_Formula_MagnitudeHeader);
    public const string SpellEditor_Formula_MagnitudeRaw = nameof(SpellEditor_Formula_MagnitudeRaw);
    public const string SpellEditor_Formula_MagnitudeContribution = nameof(SpellEditor_Formula_MagnitudeContribution);
    public const string SpellEditor_Formula_MagnitudeActualHit = nameof(SpellEditor_Formula_MagnitudeActualHit);
    public const string SpellEditor_Formula_MagnitudeMitNote = nameof(SpellEditor_Formula_MagnitudeMitNote);
    public const string SpellEditor_Formula_MpCostHeader = nameof(SpellEditor_Formula_MpCostHeader);
    public const string SpellEditor_Formula_MpCostFormula = nameof(SpellEditor_Formula_MpCostFormula);
    public const string SpellEditor_Formula_MpCostNote = nameof(SpellEditor_Formula_MpCostNote);
    public const string SpellEditor_Formula_GiveItemHeader = nameof(SpellEditor_Formula_GiveItemHeader);
    public const string SpellEditor_Formula_GiveItemBullet1 = nameof(SpellEditor_Formula_GiveItemBullet1);
    public const string SpellEditor_Formula_GiveItemBullet2 = nameof(SpellEditor_Formula_GiveItemBullet2);
    public const string SpellEditor_Formula_GiveItemBullet3 = nameof(SpellEditor_Formula_GiveItemBullet3);
    public const string SpellEditor_Formula_MaxMpHeader = nameof(SpellEditor_Formula_MaxMpHeader);
    public const string SpellEditor_Formula_MaxMpFormula = nameof(SpellEditor_Formula_MaxMpFormula);
    public const string SpellEditor_Formula_MaxMpNote = nameof(SpellEditor_Formula_MaxMpNote);
    public const string SpellEditor_Formula_RangeHeader = nameof(SpellEditor_Formula_RangeHeader);
    public const string SpellEditor_Formula_RangeFormula = nameof(SpellEditor_Formula_RangeFormula);
    public const string SpellEditor_Formula_RangeNote = nameof(SpellEditor_Formula_RangeNote);

    // ── ClassEditorView ───────────────────────────────────────────────────────
    public const string ClassEditor_SelectPrompt = nameof(ClassEditor_SelectPrompt);
    public const string ClassEditor_SectionTitle = nameof(ClassEditor_SectionTitle);
    public const string ClassEditor_DescLabel = nameof(ClassEditor_DescLabel);
    public const string ClassEditor_DescHint = nameof(ClassEditor_DescHint);
    public const string ClassEditor_SpriteSheetMaleLabel = nameof(ClassEditor_SpriteSheetMaleLabel);
    public const string ClassEditor_SpriteSheetFemaleLabel = nameof(ClassEditor_SpriteSheetFemaleLabel);
    public const string ClassEditor_SpriteMaleLabel = nameof(ClassEditor_SpriteMaleLabel);
    public const string ClassEditor_SpriteFemaleLabel = nameof(ClassEditor_SpriteFemaleLabel);
    public const string ClassEditor_StrLabel = nameof(ClassEditor_StrLabel);
    public const string ClassEditor_DefLabel = nameof(ClassEditor_DefLabel);
    public const string ClassEditor_SpdLabel = nameof(ClassEditor_SpdLabel);
    public const string ClassEditor_IntLabel = nameof(ClassEditor_IntLabel);
    public const string ClassEditor_MaxHpLabel = nameof(ClassEditor_MaxHpLabel);
    public const string ClassEditor_MaxMpLabel = nameof(ClassEditor_MaxMpLabel);
    public const string ClassEditor_MaxSpLabel = nameof(ClassEditor_MaxSpLabel);
    public const string ClassEditor_StartingStatsNote = nameof(ClassEditor_StartingStatsNote);
    public const string ClassEditor_RegenHeader = nameof(ClassEditor_RegenHeader);
    public const string ClassEditor_CombatHeader = nameof(ClassEditor_CombatHeader);
    public const string ClassEditor_HpRegenLabel = nameof(ClassEditor_HpRegenLabel);
    public const string ClassEditor_MpRegenLabel = nameof(ClassEditor_MpRegenLabel);
    public const string ClassEditor_SpRegenLabel = nameof(ClassEditor_SpRegenLabel);
    public const string ClassEditor_SaveClassButton = nameof(ClassEditor_SaveClassButton);
    public const string ClassEditor_Formula_VitalsHeader = nameof(ClassEditor_Formula_VitalsHeader);
    public const string ClassEditor_Formula_VitalsMaxHp = nameof(ClassEditor_Formula_VitalsMaxHp);
    public const string ClassEditor_Formula_VitalsMaxMp = nameof(ClassEditor_Formula_VitalsMaxMp);
    public const string ClassEditor_Formula_VitalsMaxSp = nameof(ClassEditor_Formula_VitalsMaxSp);
    public const string ClassEditor_Formula_VitalsNote = nameof(ClassEditor_Formula_VitalsNote);
    public const string ClassEditor_Formula_RegenHeader = nameof(ClassEditor_Formula_RegenHeader);
    public const string ClassEditor_Formula_RegenHp = nameof(ClassEditor_Formula_RegenHp);
    public const string ClassEditor_Formula_RegenMp = nameof(ClassEditor_Formula_RegenMp);
    public const string ClassEditor_Formula_RegenSp = nameof(ClassEditor_Formula_RegenSp);
    public const string ClassEditor_Formula_RegenNote = nameof(ClassEditor_Formula_RegenNote);
    public const string ClassEditor_Formula_CombatHeader = nameof(ClassEditor_Formula_CombatHeader);
    public const string ClassEditor_Formula_CombatPDmg = nameof(ClassEditor_Formula_CombatPDmg);
    public const string ClassEditor_Formula_CombatMDmg = nameof(ClassEditor_Formula_CombatMDmg);
    public const string ClassEditor_Formula_CombatMit = nameof(ClassEditor_Formula_CombatMit);
    public const string ClassEditor_Formula_CombatNote = nameof(ClassEditor_Formula_CombatNote);
    public const string ClassEditor_Formula_PreviewNote = nameof(ClassEditor_Formula_PreviewNote);

    // ── ShopEditorView ────────────────────────────────────────────────────────
    public const string ShopEditor_SelectPrompt = nameof(ShopEditor_SelectPrompt);
    public const string ShopEditor_SectionTitle = nameof(ShopEditor_SectionTitle);
    public const string ShopEditor_TypeStore = nameof(ShopEditor_TypeStore);
    public const string ShopEditor_TypeInn = nameof(ShopEditor_TypeInn);
    public const string ShopEditor_FixesItemsLabel = nameof(ShopEditor_FixesItemsLabel);
    public const string ShopEditor_AllowBankingLabel = nameof(ShopEditor_AllowBankingLabel);
    public const string ShopEditor_KeeperLabel = nameof(ShopEditor_KeeperLabel);
    public const string ShopEditor_TradesHeader = nameof(ShopEditor_TradesHeader);
    public const string ShopEditor_TradesColGiveItem = nameof(ShopEditor_TradesColGiveItem);
    public const string ShopEditor_TradesColGiveQty = nameof(ShopEditor_TradesColGiveQty);
    public const string ShopEditor_TradesColGetItem = nameof(ShopEditor_TradesColGetItem);
    public const string ShopEditor_TradesColGetQty = nameof(ShopEditor_TradesColGetQty);
    public const string ShopEditor_SalesHeader = nameof(ShopEditor_SalesHeader);
    public const string ShopEditor_SalesHint = nameof(ShopEditor_SalesHint);
    public const string ShopEditor_SalesColItem = nameof(ShopEditor_SalesColItem);
    public const string ShopEditor_SalesColPrice = nameof(ShopEditor_SalesColPrice);
    public const string ShopEditor_SalesColOrder = nameof(ShopEditor_SalesColOrder);
    public const string ShopEditor_SalesItemPlaceholder = nameof(ShopEditor_SalesItemPlaceholder);
    public const string ShopEditor_SalesNoPrice = nameof(ShopEditor_SalesNoPrice);
    public const string ShopEditor_SalesSummary = nameof(ShopEditor_SalesSummary);
    public const string ShopEditor_SalesWarnDuplicate = nameof(ShopEditor_SalesWarnDuplicate);
    public const string ShopEditor_SalesWarnNoPrice = nameof(ShopEditor_SalesWarnNoPrice);
    public const string ShopEditor_GiveItemPlaceholder = nameof(ShopEditor_GiveItemPlaceholder);
    public const string ShopEditor_GetItemPlaceholder = nameof(ShopEditor_GetItemPlaceholder);
    public const string ShopEditor_SaveShopButton = nameof(ShopEditor_SaveShopButton);

    // ── QuestEditor ───────────────────────────────────────────────────────────
    public const string QuestEditor_SelectPrompt = nameof(QuestEditor_SelectPrompt);
    public const string QuestEditor_SectionTitle = nameof(QuestEditor_SectionTitle);
    public const string QuestEditor_DescriptionLabel = nameof(QuestEditor_DescriptionLabel);
    public const string QuestEditor_GiverLabel = nameof(QuestEditor_GiverLabel);
    public const string QuestEditor_TurnInLabel = nameof(QuestEditor_TurnInLabel);
    public const string QuestEditor_RepeatableLabel = nameof(QuestEditor_RepeatableLabel);
    public const string QuestEditor_CadenceLabel = nameof(QuestEditor_CadenceLabel);
    public const string QuestEditor_RequirementsHeader = nameof(QuestEditor_RequirementsHeader);
    public const string QuestEditor_ReqLevelLabel = nameof(QuestEditor_ReqLevelLabel);
    public const string QuestEditor_ReqStrLabel = nameof(QuestEditor_ReqStrLabel);
    public const string QuestEditor_ReqDefLabel = nameof(QuestEditor_ReqDefLabel);
    public const string QuestEditor_ReqSpdLabel = nameof(QuestEditor_ReqSpdLabel);
    public const string QuestEditor_ReqIntLabel = nameof(QuestEditor_ReqIntLabel);
    public const string QuestEditor_PrereqLabel = nameof(QuestEditor_PrereqLabel);
    public const string QuestEditor_ObjectivesHeader = nameof(QuestEditor_ObjectivesHeader);
    public const string QuestEditor_ObjColKind = nameof(QuestEditor_ObjColKind);
    public const string QuestEditor_ObjColTarget = nameof(QuestEditor_ObjColTarget);
    public const string QuestEditor_ObjColCount = nameof(QuestEditor_ObjColCount);
    public const string QuestEditor_RewardsHeader = nameof(QuestEditor_RewardsHeader);
    public const string QuestEditor_RepeatRewardsHeader = nameof(QuestEditor_RepeatRewardsHeader);
    public const string QuestEditor_RewardExpLabel = nameof(QuestEditor_RewardExpLabel);
    public const string QuestEditor_RepeatRewardExpLabel = nameof(QuestEditor_RepeatRewardExpLabel);
    public const string QuestEditor_RewardColItem = nameof(QuestEditor_RewardColItem);
    public const string QuestEditor_RewardColQty = nameof(QuestEditor_RewardColQty);
    public const string QuestEditor_ItemPlaceholder = nameof(QuestEditor_ItemPlaceholder);
    public const string QuestEditor_TargetPlaceholder = nameof(QuestEditor_TargetPlaceholder);
    public const string QuestEditor_SaveQuestButton = nameof(QuestEditor_SaveQuestButton);

    // ── Shared entity editor status messages ──────────────────────────────────
    public const string EntityEditor_LoadedOffline = nameof(EntityEditor_LoadedOffline);
    public const string EntityEditor_LoadedOnline = nameof(EntityEditor_LoadedOnline);
    public const string EntityEditor_LoadingEntity = nameof(EntityEditor_LoadingEntity);
    public const string EntityEditor_LoadedEntity = nameof(EntityEditor_LoadedEntity);
    public const string EntityEditor_LoadFailed = nameof(EntityEditor_LoadFailed);
    public const string EntityEditor_Saved = nameof(EntityEditor_Saved);
    public const string EntityEditor_SaveFailed = nameof(EntityEditor_SaveFailed);
    public const string EntityEditor_SaveAllSaved = nameof(EntityEditor_SaveAllSaved);
    public const string EntityEditor_Copied = nameof(EntityEditor_Copied);
    public const string EntityEditor_NoEmptySlot = nameof(EntityEditor_NoEmptySlot);
    public const string EntityEditor_NoDirty = nameof(EntityEditor_NoDirty);
    public const string EntityEditor_Discarded = nameof(EntityEditor_Discarded);
    public const string EntityEditor_DiscardFailed = nameof(EntityEditor_DiscardFailed);
    public const string EntityEditor_AllDiscarded = nameof(EntityEditor_AllDiscarded);

    // ── Entity type names (singular/plural) substituted into EntityEditor_* status keys ──────
    public const string ItemEditor_TypeName = nameof(ItemEditor_TypeName);         // "Item"
    public const string ItemEditor_TypeNamePlural = nameof(ItemEditor_TypeNamePlural);   // "Items"
    public const string NpcEditor_TypeName = nameof(NpcEditor_TypeName);          // "NPC"
    public const string NpcEditor_TypeNamePlural = nameof(NpcEditor_TypeNamePlural);    // "NPCs"
    public const string SpellEditor_TypeName = nameof(SpellEditor_TypeName);        // "Spell"
    public const string SpellEditor_TypeNamePlural = nameof(SpellEditor_TypeNamePlural);  // "Spells"
    public const string ClassEditor_TypeName = nameof(ClassEditor_TypeName);        // "Class"

    // ── Starting loadout ─────────────────────────────────────────────────────
    // Character creation SKIPS a starting line the class cannot use, so an unusable row produces a
    // MISSING item and no explanation in-game. The outcome column below is the only place that mistake
    // is ever visible, which is why it is spelled out per row rather than summarized.
    public const string ClassEditor_StartItemsLabel = nameof(ClassEditor_StartItemsLabel);
    public const string ClassEditor_StartSpellsLabel = nameof(ClassEditor_StartSpellsLabel);
    public const string ClassEditor_StartItemPlaceholder = nameof(ClassEditor_StartItemPlaceholder);
    public const string ClassEditor_StartSpellPlaceholder = nameof(ClassEditor_StartSpellPlaceholder);
    public const string ClassEditor_AddStartItem = nameof(ClassEditor_AddStartItem);
    public const string ClassEditor_AddStartSpell = nameof(ClassEditor_AddStartSpell);
    public const string ClassEditor_StartWorn = nameof(ClassEditor_StartWorn);
    public const string ClassEditor_StartCarried = nameof(ClassEditor_StartCarried);
    public const string ClassEditor_StartSkippedClass = nameof(ClassEditor_StartSkippedClass);
    public const string ClassEditor_StartSkippedStat = nameof(ClassEditor_StartSkippedStat);
    public const string ClassEditor_StartSkippedLevel = nameof(ClassEditor_StartSkippedLevel);
    public const string ClassEditor_StartSpellDetail = nameof(ClassEditor_StartSpellDetail);
    public const string ClassEditor_LoadoutSummary = nameof(ClassEditor_LoadoutSummary);
    public const string ClassEditor_StartSkippedWarning = nameof(ClassEditor_StartSkippedWarning);
    public const string ClassEditor_TypeNamePlural = nameof(ClassEditor_TypeNamePlural);  // "Classes"
    public const string ShopEditor_TypeName = nameof(ShopEditor_TypeName);         // "Shop"
    public const string ShopEditor_TypeNamePlural = nameof(ShopEditor_TypeNamePlural);   // "Shops"
    public const string QuestEditor_TypeName = nameof(QuestEditor_TypeName);        // "Quest"
    public const string QuestEditor_TypeNamePlural = nameof(QuestEditor_TypeNamePlural);  // "Quests"

    // ── Conversation editor (NPC conversations) ────────────────────────────────
    public const string ConversationEditor_TypeName = nameof(ConversationEditor_TypeName);
    public const string ConversationEditor_TypeNamePlural = nameof(ConversationEditor_TypeNamePlural);
    public const string ConversationEditor_SectionTitle = nameof(ConversationEditor_SectionTitle);
    public const string ConversationEditor_SelectPrompt = nameof(ConversationEditor_SelectPrompt);
    public const string ConversationEditor_SpeakerLabel = nameof(ConversationEditor_SpeakerLabel);
    public const string ConversationEditor_RootLabel = nameof(ConversationEditor_RootLabel);
    public const string ConversationEditor_NodesHeader = nameof(ConversationEditor_NodesHeader);
    public const string ConversationEditor_RootFirst = nameof(ConversationEditor_RootFirst);
    public const string ConversationEditor_ChoiceEnd = nameof(ConversationEditor_ChoiceEnd);
    public const string ConversationEditor_ChoicesLabel = nameof(ConversationEditor_ChoicesLabel);
    public const string ConversationEditor_NodeSpeakerPlaceholder = nameof(ConversationEditor_NodeSpeakerPlaceholder);
    public const string ConversationEditor_NodeTextPlaceholder = nameof(ConversationEditor_NodeTextPlaceholder);
    public const string ConversationEditor_ChoiceLabelPlaceholder = nameof(ConversationEditor_ChoiceLabelPlaceholder);
    public const string ConversationEditor_ChoiceNextPlaceholder = nameof(ConversationEditor_ChoiceNextPlaceholder);
    public const string ConversationEditor_SaveButton = nameof(ConversationEditor_SaveButton);
    // The visual tree: the view picker, the canvas legend, and the node dialog a click opens.
    public const string ConversationEditor_ViewText = nameof(ConversationEditor_ViewText);
    public const string ConversationEditor_ViewGraph = nameof(ConversationEditor_ViewGraph);
    public const string ConversationEditor_GraphHint = nameof(ConversationEditor_GraphHint);
    public const string ConversationEditor_GraphStart = nameof(ConversationEditor_GraphStart);
    public const string ConversationEditor_GraphUnreachable = nameof(ConversationEditor_GraphUnreachable);
    public const string ConversationEditor_GraphEnds = nameof(ConversationEditor_GraphEnds);
    public const string ConversationEditor_GraphOpensShop = nameof(ConversationEditor_GraphOpensShop);
    public const string ConversationEditor_GraphOpensQuests = nameof(ConversationEditor_GraphOpensQuests);
    public const string ConversationEditor_GraphChoiceCount = nameof(ConversationEditor_GraphChoiceCount);
    public const string ConversationEditor_NodeDialogTitle = nameof(ConversationEditor_NodeDialogTitle);
    public const string ConversationEditor_DeleteNode = nameof(ConversationEditor_DeleteNode);

    // ── MapGroupEditor ────────────────────────────────────────────────────────
    public const string MapGroupEditor_TypeName = nameof(MapGroupEditor_TypeName);         // "Map Group"
    public const string MapGroupEditor_TypeNamePlural = nameof(MapGroupEditor_TypeNamePlural);   // "Map Groups"
    public const string MapGroupEditor_SelectPrompt = nameof(MapGroupEditor_SelectPrompt);
    public const string MapGroupEditor_SectionTitle = nameof(MapGroupEditor_SectionTitle);
    public const string MapGroupEditor_TerritoryLabel = nameof(MapGroupEditor_TerritoryLabel);
    public const string MapGroupEditor_FallbackHeader = nameof(MapGroupEditor_FallbackHeader);
    public const string MapGroupEditor_TriStateHint = nameof(MapGroupEditor_TriStateHint);
    public const string MapGroupEditor_SaveButton = nameof(MapGroupEditor_SaveButton);
    public const string MapGroupEditor_MapsHeader = nameof(MapGroupEditor_MapsHeader);
    public const string MapGroupEditor_NoMaps = nameof(MapGroupEditor_NoMaps);

    // Inbound-reference panel headings, shared by every editor.
    public const string References_Header = nameof(References_Header);
    public const string References_None = nameof(References_None);
    public const string References_DroppedBy = nameof(References_DroppedBy);
    public const string References_SoldBy = nameof(References_SoldBy);
    public const string References_RewardedBy = nameof(References_RewardedBy);
    public const string References_ReagentFor = nameof(References_ReagentFor);
    public const string References_StartingGearFor = nameof(References_StartingGearFor);
    public const string References_TaughtBy = nameof(References_TaughtBy);
    public const string References_StartingSpellFor = nameof(References_StartingSpellFor);
    public const string References_GivesQuest = nameof(References_GivesQuest);
    public const string References_TakesQuest = nameof(References_TakesQuest);
    public const string References_KilledFor = nameof(References_KilledFor);
    public const string References_KeepsShop = nameof(References_KeepsShop);
    public const string References_Speaks = nameof(References_Speaks);
    public const string References_SpawnsOn = nameof(References_SpawnsOn);
    public const string References_GroupedWith = nameof(References_GroupedWith);
    public const string References_PrerequisiteFor = nameof(References_PrerequisiteFor);
    public const string References_RestrictedItems = nameof(References_RestrictedItems);
    public const string References_RestrictedSpells = nameof(References_RestrictedSpells);

    // ── NpcRowViewModel formatted previews (Drop % and Magic Damage) ──────────
    public const string NpcEditor_DropChanceNever = nameof(NpcEditor_DropChanceNever);   // "0% (never drops)"
    public const string NpcEditor_DropChanceAlways = nameof(NpcEditor_DropChanceAlways);  // "100% (always drops)"
    public const string NpcEditor_DropItemPlaceholder = nameof(NpcEditor_DropItemPlaceholder);
    public const string NpcEditor_DropTableLabel = nameof(NpcEditor_DropTableLabel);
    public const string NpcEditor_DropQuantityHeader = nameof(NpcEditor_DropQuantityHeader);
    public const string NpcEditor_DropChanceHeader = nameof(NpcEditor_DropChanceHeader);
    public const string NpcEditor_AddDrop = nameof(NpcEditor_AddDrop);
    // Expected drops per kill = the SUM of the live chances, because drop lines roll independently
    // rather than competing for one slot. Surfaced because that sum is what a long table gets wrong.
    public const string NpcEditor_DropYieldNone = nameof(NpcEditor_DropYieldNone);
    public const string NpcEditor_DropYield = nameof(NpcEditor_DropYield);
    public const string NpcEditor_DropWarnChanceNoItem = nameof(NpcEditor_DropWarnChanceNoItem);  // chance set, no item
    public const string NpcEditor_RangeWarnTooShort = nameof(NpcEditor_RangeWarnTooShort);
    public const string NpcEditor_RangeWarnTooFar = nameof(NpcEditor_RangeWarnTooFar);
    public const string NpcEditor_DropWarnItemNoChance = nameof(NpcEditor_DropWarnItemNoChance);  // item set, 0 chance
    public const string NpcEditor_DropWarnCurrencyQty = nameof(NpcEditor_DropWarnCurrencyQty);     // currency, qty < 1
    public const string NpcEditor_DropWarnNonCurrencyQty = nameof(NpcEditor_DropWarnNonCurrencyQty);  // non-currency, qty > 0
}
