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
    // Notes panel — sub-headers, formula lines, and explanatory paragraphs.
    // The section that authors a family a module declared. Only the chrome is here — every field's own
    // caption comes from the schema, in the game's catalog rather than this build's.
    public const string SchemaEditor_SelectPrompt = nameof(SchemaEditor_SelectPrompt);
    public const string SchemaEditor_NoFields = nameof(SchemaEditor_NoFields);
    public const string SchemaEditor_MissingRequired = nameof(SchemaEditor_MissingRequired);
    public const string SchemaEditor_SaveButton = nameof(SchemaEditor_SaveButton);

    public const string ItemEditor_Notes_EquipmentHeader = nameof(ItemEditor_Notes_EquipmentHeader);
    public const string ItemEditor_Notes_EquipmentDurability = nameof(ItemEditor_Notes_EquipmentDurability);
    public const string ItemEditor_Notes_EquipmentPower = nameof(ItemEditor_Notes_EquipmentPower);
    public const string ItemEditor_Notes_EquipmentSlot = nameof(ItemEditor_Notes_EquipmentSlot);
    public const string ItemEditor_Notes_ConsumableHeader = nameof(ItemEditor_Notes_ConsumableHeader);
    public const string ItemEditor_Notes_ConsumableAmount = nameof(ItemEditor_Notes_ConsumableAmount);
    public const string ItemEditor_Notes_KeyHeader = nameof(ItemEditor_Notes_KeyHeader);
    public const string ItemEditor_Notes_KeyId = nameof(ItemEditor_Notes_KeyId);
    public const string ItemEditor_Notes_CurrencyHeader = nameof(ItemEditor_Notes_CurrencyHeader);
    public const string ItemEditor_Notes_CurrencyDesc = nameof(ItemEditor_Notes_CurrencyDesc);

    // ── NpcEditorView ─────────────────────────────────────────────────────────
    public const string NpcEditor_AllBehaviorsFilter = nameof(NpcEditor_AllBehaviorsFilter);
    public const string NpcEditor_SelectPrompt = nameof(NpcEditor_SelectPrompt);
    public const string NpcEditor_SectionTitle = nameof(NpcEditor_SectionTitle);
    public const string NpcEditor_SaysLabel = nameof(NpcEditor_SaysLabel);
    public const string NpcEditor_SpriteSheetLabel = nameof(NpcEditor_SpriteSheetLabel);
    public const string NpcEditor_SpriteLabel = nameof(NpcEditor_SpriteLabel);
    public const string NpcEditor_SizeLabel = nameof(NpcEditor_SizeLabel);
    public const string NpcEditor_SpawnSecsLabel = nameof(NpcEditor_SpawnSecsLabel);
    public const string NpcEditor_BehaviorLabel = nameof(NpcEditor_BehaviorLabel);
    public const string NpcEditor_EmitsLightLabel = nameof(NpcEditor_EmitsLightLabel);
    public const string NpcEditor_LightColorLabel = nameof(NpcEditor_LightColorLabel);
    public const string NpcEditor_LightRadiusLabel = nameof(NpcEditor_LightRadiusLabel);
    public const string NpcEditor_LightIntensityLabel = nameof(NpcEditor_LightIntensityLabel);
    public const string NpcEditor_LightFlickerLabel = nameof(NpcEditor_LightFlickerLabel);
    public const string NpcEditor_GroupLabel = nameof(NpcEditor_GroupLabel);
    public const string NpcEditor_RangeLabel = nameof(NpcEditor_RangeLabel);
    public const string NpcEditor_LightingHeader = nameof(NpcEditor_LightingHeader);
    public const string NpcEditor_SaveNpcButton = nameof(NpcEditor_SaveNpcButton);

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

    // ── Starting loadout ─────────────────────────────────────────────────────
    // Character creation SKIPS a starting line the class cannot use, so an unusable row produces a
    // MISSING item and no explanation in-game. The outcome column below is the only place that mistake
    // is ever visible, which is why it is spelled out per row rather than summarized.
    public const string ShopEditor_TypeName = nameof(ShopEditor_TypeName);         // "Shop"
    public const string ShopEditor_TypeNamePlural = nameof(ShopEditor_TypeNamePlural);   // "Shops"

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
    public const string ConversationEditor_ChoiceActionIdPlaceholder = nameof(ConversationEditor_ChoiceActionIdPlaceholder);
    public const string ConversationEditor_SaveButton = nameof(ConversationEditor_SaveButton);
    // The visual tree: the view picker, the canvas legend, and the node dialog a click opens.
    public const string ConversationEditor_ViewText = nameof(ConversationEditor_ViewText);
    public const string ConversationEditor_ViewGraph = nameof(ConversationEditor_ViewGraph);
    public const string ConversationEditor_GraphHint = nameof(ConversationEditor_GraphHint);
    public const string ConversationEditor_GraphStart = nameof(ConversationEditor_GraphStart);
    public const string ConversationEditor_GraphUnreachable = nameof(ConversationEditor_GraphUnreachable);
    public const string ConversationEditor_GraphEnds = nameof(ConversationEditor_GraphEnds);
    public const string ConversationEditor_GraphOpensShop = nameof(ConversationEditor_GraphOpensShop);
    public const string ConversationEditor_GraphChoiceCount = nameof(ConversationEditor_GraphChoiceCount);
    public const string ConversationEditor_NodeDialogTitle = nameof(ConversationEditor_NodeDialogTitle);
    public const string ConversationEditor_DeleteNode = nameof(ConversationEditor_DeleteNode);

    // ── MapGroupEditor ────────────────────────────────────────────────────────
    public const string MapGroupEditor_TypeName = nameof(MapGroupEditor_TypeName);         // "Map Group"
    public const string MapGroupEditor_TypeNamePlural = nameof(MapGroupEditor_TypeNamePlural);   // "Map Groups"
    public const string MapGroupEditor_SelectPrompt = nameof(MapGroupEditor_SelectPrompt);
    public const string MapGroupEditor_SectionTitle = nameof(MapGroupEditor_SectionTitle);
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
    public const string References_KeepsShop = nameof(References_KeepsShop);
    public const string References_Speaks = nameof(References_Speaks);
    public const string References_SpawnsOn = nameof(References_SpawnsOn);
    public const string References_GroupedWith = nameof(References_GroupedWith);
    public const string References_PrerequisiteFor = nameof(References_PrerequisiteFor);

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
}
