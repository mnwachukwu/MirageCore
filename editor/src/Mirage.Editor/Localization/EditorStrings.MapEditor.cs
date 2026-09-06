using Mirage.Shared.Localization;

namespace Mirage.Editor.Localization;

/// <summary>The map editor view, its attribute dialogs (warp, item spawn, key, key-open, light,
/// NPC-spawn pin), and the status messages and validation those produce.</summary>
public static partial class EditorStrings
{
    // ── MapEditorView ─────────────────────────────────────────────────────────
    public const string MapEditor_ModeHeader = nameof(MapEditor_ModeHeader);
    public const string MapEditor_ModeTile = nameof(MapEditor_ModeTile);
    public const string MapEditor_ModeAttribute = nameof(MapEditor_ModeAttribute);
    public const string MapEditor_ModeLight = nameof(MapEditor_ModeLight);
    public const string MapEditor_ActionHeader = nameof(MapEditor_ActionHeader);
    public const string MapEditor_ActionPlace = nameof(MapEditor_ActionPlace);
    public const string MapEditor_ActionSelect = nameof(MapEditor_ActionSelect);
    public const string MapEditor_ActionDelete = nameof(MapEditor_ActionDelete);
    public const string MapEditor_LayerHeader = nameof(MapEditor_LayerHeader);
    public const string MapEditor_AnimLayer = nameof(MapEditor_AnimLayer);
    public const string MapEditor_AnimLayerTooltip = nameof(MapEditor_AnimLayerTooltip);
    public const string MapEditor_AnimDialogTitle = nameof(MapEditor_AnimDialogTitle);
    public const string MapEditor_TilesetHeader = nameof(MapEditor_TilesetHeader);
    public const string MapEditor_TilesetSearchPlaceholder = nameof(MapEditor_TilesetSearchPlaceholder);
    public const string MapEditor_FillButton = nameof(MapEditor_FillButton);
    public const string MapEditor_FillTooltip = nameof(MapEditor_FillTooltip);
    public const string MapEditor_ClearLayerButton = nameof(MapEditor_ClearLayerButton);
    public const string MapEditor_ClearLayerTooltip = nameof(MapEditor_ClearLayerTooltip);
    public const string MapEditor_AttributeHeader = nameof(MapEditor_AttributeHeader);
    public const string MapEditor_ClearAttrButton = nameof(MapEditor_ClearAttrButton);
    public const string MapEditor_ClearAttrTooltip = nameof(MapEditor_ClearAttrTooltip);
    public const string MapEditor_AltClickHint = nameof(MapEditor_AltClickHint);
    public const string MapEditor_LightHeader = nameof(MapEditor_LightHeader);
    public const string MapEditor_LightModeHint = nameof(MapEditor_LightModeHint);
    public const string MapEditor_ClearLightsButton = nameof(MapEditor_ClearLightsButton);
    public const string MapEditor_ClearLightsTooltip = nameof(MapEditor_ClearLightsTooltip);
    public const string MapEditor_LightText = nameof(MapEditor_LightText);
    public const string MapEditor_LightText_None = nameof(MapEditor_LightText_None);
    public const string MapEditor_HiddenLayerPrompt = nameof(MapEditor_HiddenLayerPrompt);
    public const string MapEditor_HiddenLayerTitle = nameof(MapEditor_HiddenLayerTitle);
    public const string MapEditor_HiddenLayerShow = nameof(MapEditor_HiddenLayerShow);
    public const string MapEditor_AttrLabel = nameof(MapEditor_AttrLabel);
    public const string MapEditor_BrushSizeHeader = nameof(MapEditor_BrushSizeHeader);
    public const string MapEditor_BrushW = nameof(MapEditor_BrushW);
    public const string MapEditor_BrushH = nameof(MapEditor_BrushH);
    public const string MapEditor_PaletteHeader = nameof(MapEditor_PaletteHeader);
    public const string MapEditor_PropertiesHeader = nameof(MapEditor_PropertiesHeader);
    public const string MapEditor_SelectMapPrompt = nameof(MapEditor_SelectMapPrompt);
    public const string MapEditor_MoralLabel = nameof(MapEditor_MoralLabel);
    // The three titled groups. Their member labels drop the prefix the header now carries — Boot Map
    // reads "Map" under Respawn, and the greeting fields read plainly under Greeting.
    public const string MapEditor_MapLinksHeader = nameof(MapEditor_MapLinksHeader);
    public const string MapEditor_RespawnHeader = nameof(MapEditor_RespawnHeader);
    public const string MapEditor_GreetingHeader = nameof(MapEditor_GreetingHeader);
    public const string MapEditor_UpLabel = nameof(MapEditor_UpLabel);
    public const string MapEditor_DownLabel = nameof(MapEditor_DownLabel);
    public const string MapEditor_LeftLabel = nameof(MapEditor_LeftLabel);
    public const string MapEditor_RightLabel = nameof(MapEditor_RightLabel);
    public const string MapEditor_ClearTooltip = nameof(MapEditor_ClearTooltip);
    public const string MapEditor_MusicLabel = nameof(MapEditor_MusicLabel);
    public const string MapEditor_BootMapLabel = nameof(MapEditor_BootMapLabel);
    public const string MapEditor_BootXLabel = nameof(MapEditor_BootXLabel);
    public const string MapEditor_BootYLabel = nameof(MapEditor_BootYLabel);
    // Map-enter/leave greeting; reused by the MapGroup editor's shared fallback fields.
    public const string MapEditor_GreetingSpeakerLabel = nameof(MapEditor_GreetingSpeakerLabel);
    // Shown in a blank greeting box: the value the map would inherit from its group, or this hint.
    public const string MapEditor_GreetingPlaceholder = nameof(MapEditor_GreetingPlaceholder);
    // The generic form, for a blank field whose group has nothing to hand down either.
    public const string MapEditor_InheritsPlaceholder = nameof(MapEditor_InheritsPlaceholder);
    public const string MapEditor_JoinSayLabel = nameof(MapEditor_JoinSayLabel);
    public const string MapEditor_LeaveSayLabel = nameof(MapEditor_LeaveSayLabel);
    public const string MapEditor_MapGroupLabel = nameof(MapEditor_MapGroupLabel);
    public const string MapEditor_InheritHint = nameof(MapEditor_InheritHint);
    public const string MapEditor_IndoorsLabel = nameof(MapEditor_IndoorsLabel);
    public const string MapEditor_AlwaysLitLabel = nameof(MapEditor_AlwaysLitLabel);
    public const string MapEditor_AlwaysDarkLabel = nameof(MapEditor_AlwaysDarkLabel);
    public const string MapEditor_NpcSlotsLabel = nameof(MapEditor_NpcSlotsLabel);
    public const string MapEditor_AnimPreviewStart = nameof(MapEditor_AnimPreviewStart);
    public const string MapEditor_AnimPreviewStop = nameof(MapEditor_AnimPreviewStop);
    public const string MapEditor_AnimPreviewTooltip = nameof(MapEditor_AnimPreviewTooltip);
    public const string MapEditor_DoorPreviewClosed = nameof(MapEditor_DoorPreviewClosed);
    public const string MapEditor_DoorPreviewOpen = nameof(MapEditor_DoorPreviewOpen);
    public const string MapEditor_DoorPreviewTooltip = nameof(MapEditor_DoorPreviewTooltip);
    public const string MapEditor_NightPreviewStart = nameof(MapEditor_NightPreviewStart);
    public const string MapEditor_NightPreviewStop = nameof(MapEditor_NightPreviewStop);
    public const string MapEditor_NightPreviewTooltip = nameof(MapEditor_NightPreviewTooltip);
    public const string MapEditor_ZoomOutButton = nameof(MapEditor_ZoomOutButton);
    public const string MapEditor_ZoomOutTooltip = nameof(MapEditor_ZoomOutTooltip);
    public const string MapEditor_ZoomInButton = nameof(MapEditor_ZoomInButton);
    public const string MapEditor_ZoomInTooltip = nameof(MapEditor_ZoomInTooltip);
    public const string MapEditor_ResetZoomButton = nameof(MapEditor_ResetZoomButton);
    public const string MapEditor_ResetZoomTooltip = nameof(MapEditor_ResetZoomTooltip);
    public const string MapEditor_UndoButton = nameof(MapEditor_UndoButton);
    public const string MapEditor_RedoButton = nameof(MapEditor_RedoButton);
    public const string MapEditor_BackButton = nameof(MapEditor_BackButton);
    public const string MapEditor_ForwardButton = nameof(MapEditor_ForwardButton);
    public const string MapEditor_SaveMapButton = nameof(MapEditor_SaveMapButton);
    public const string MapEditor_StatusMode = nameof(MapEditor_StatusMode);
    public const string MapEditor_StatusAction = nameof(MapEditor_StatusAction);
    public const string MapEditor_StatusLayer = nameof(MapEditor_StatusLayer);
    public const string MapEditor_TilePreviewHeader = nameof(MapEditor_TilePreviewHeader);
    public const string MapEditor_RevisionLabel = nameof(MapEditor_RevisionLabel);
    public const string MapEditor_UsedTilesheets = nameof(MapEditor_UsedTilesheets);
    public const string MapEditor_ExportMapButton = nameof(MapEditor_ExportMapButton);
    public const string MapEditor_ExportAreaButton = nameof(MapEditor_ExportAreaButton);
    public const string MapEditor_ExportWorldButton = nameof(MapEditor_ExportWorldButton);
    public const string MapEditor_ExportMapTooltip = nameof(MapEditor_ExportMapTooltip);
    public const string MapEditor_ExportAreaTooltip = nameof(MapEditor_ExportAreaTooltip);
    public const string MapEditor_ExportWorldTooltip = nameof(MapEditor_ExportWorldTooltip);
    public const string MapEditor_ExportPngDialogTitle = nameof(MapEditor_ExportPngDialogTitle);
    public const string MapEditorStatus_ExportDiscovering = nameof(MapEditorStatus_ExportDiscovering);
    public const string MapEditorStatus_ExportRendering = nameof(MapEditorStatus_ExportRendering);
    public const string MapEditorStatus_ExportedMap = nameof(MapEditorStatus_ExportedMap);
    public const string MapEditorStatus_ExportedWorld = nameof(MapEditorStatus_ExportedWorld);
    public const string MapEditorStatus_ExportFailed = nameof(MapEditorStatus_ExportFailed);
    public const string MapEditorStatus_ExportFailed_NoMap = nameof(MapEditorStatus_ExportFailed_NoMap);
    public const string MapEditor_SearchMapsPlaceholder = nameof(MapEditor_SearchMapsPlaceholder);
    public const string MapEditor_SearchMapGroupsPlaceholder = nameof(MapEditor_SearchMapGroupsPlaceholder);
    public const string MapEditor_SearchNpcsPlaceholder = nameof(MapEditor_SearchNpcsPlaceholder);
    public const string MapEditor_SearchItemsPlaceholder = nameof(MapEditor_SearchItemsPlaceholder);
    public const string MapEditor_UndoTooltip = nameof(MapEditor_UndoTooltip);
    public const string MapEditor_RedoTooltip = nameof(MapEditor_RedoTooltip);
    public const string MapEditor_TileCoords = nameof(MapEditor_TileCoords);
    public const string MapEditor_TileCoordsEmpty = nameof(MapEditor_TileCoordsEmpty);
    public const string MapEditor_AttrDesc_Blocked = nameof(MapEditor_AttrDesc_Blocked);
    public const string MapEditor_AttrDesc_Warp = nameof(MapEditor_AttrDesc_Warp);
    public const string MapEditor_AttrDesc_Item = nameof(MapEditor_AttrDesc_Item);
    public const string MapEditor_AttrDesc_NpcAvoid = nameof(MapEditor_AttrDesc_NpcAvoid);
    public const string MapEditor_AttrDesc_Key = nameof(MapEditor_AttrDesc_Key);
    public const string MapEditor_AttrDesc_KeyOpen = nameof(MapEditor_AttrDesc_KeyOpen);
    public const string MapEditor_AttrDesc_NpcSpawn = nameof(MapEditor_AttrDesc_NpcSpawn);
    public const string MapEditor_AttrDesc_LayerRamp = nameof(MapEditor_AttrDesc_LayerRamp);
    public const string MapEditor_AttrDesc_FringeSurface = nameof(MapEditor_AttrDesc_FringeSurface);
    public const string MapEditor_LayerRampDir = nameof(MapEditor_LayerRampDir);
    public const string MapEditor_AttrLayer = nameof(MapEditor_AttrLayer);
    public const string MapEditor_AttrText_None = nameof(MapEditor_AttrText_None);
    public const string MapEditor_InboundWarpText = nameof(MapEditor_InboundWarpText);

    // The exploded view states what a wall stops, always — including when the answer is nothing.
    public const string MapEditor_AttrText_BlockedStops = nameof(MapEditor_AttrText_BlockedStops);
    public const string MapEditor_Blocked_Light = nameof(MapEditor_Blocked_Light);
    public const string MapEditor_Blocked_Sight = nameof(MapEditor_Blocked_Sight);
    public const string MapEditor_Blocked_LightAndSight = nameof(MapEditor_Blocked_LightAndSight);
    public const string MapEditor_Blocked_Nothing = nameof(MapEditor_Blocked_Nothing);
    public const string MapEditor_AttrText_Warp = nameof(MapEditor_AttrText_Warp);
    public const string MapEditor_AttrText_Item = nameof(MapEditor_AttrText_Item);
    public const string MapEditor_AttrText_Key = nameof(MapEditor_AttrText_Key);
    public const string MapEditor_AttrText_KeyOpen = nameof(MapEditor_AttrText_KeyOpen);
    public const string MapEditor_AttrText_RespawnDefault = nameof(MapEditor_AttrText_RespawnDefault);
    public const string MapEditor_AttrText_RespawnSeconds = nameof(MapEditor_AttrText_RespawnSeconds);
    public const string MapEditor_AttrText_KeyTake = nameof(MapEditor_AttrText_KeyTake);
    public const string MapEditor_AttrText_KeyKeep = nameof(MapEditor_AttrText_KeyKeep);
    public const string MapEditor_AttrText_LayerRamp = nameof(MapEditor_AttrText_LayerRamp);
    public const string MapEditor_AttrText_NpcSpawn = nameof(MapEditor_AttrText_NpcSpawn);

    // ── Warp Attribute Dialog ─────────────────────────────────────────────────
    public const string WarpDialog_Title = nameof(WarpDialog_Title);
    public const string WarpDialog_MapLabel = nameof(WarpDialog_MapLabel);
    public const string WarpDialog_XLabel = nameof(WarpDialog_XLabel);
    public const string WarpDialog_YLabel = nameof(WarpDialog_YLabel);

    // ── Item Spawn Attribute Dialog ───────────────────────────────────────────
    public const string ItemSpawnDialog_ItemLabel = nameof(ItemSpawnDialog_ItemLabel);
    public const string ItemSpawnDialog_ValueLabel = nameof(ItemSpawnDialog_ValueLabel);
    public const string ItemSpawnDialog_RespawnLabel = nameof(ItemSpawnDialog_RespawnLabel);
    public const string ItemSpawnDialog_RespawnTooltip = nameof(ItemSpawnDialog_RespawnTooltip);

    // ── Key Tile Attribute Dialog ─────────────────────────────────────────────
    public const string BlockedTileDialog_Title = nameof(BlockedTileDialog_Title);
    public const string BlockedTileDialog_Description = nameof(BlockedTileDialog_Description);
    public const string BlockedTileDialog_BlocksLight = nameof(BlockedTileDialog_BlocksLight);
    public const string BlockedTileDialog_BlocksLightTooltip = nameof(BlockedTileDialog_BlocksLightTooltip);
    public const string BlockedTileDialog_BlocksSight = nameof(BlockedTileDialog_BlocksSight);
    public const string BlockedTileDialog_BlocksSightTooltip = nameof(BlockedTileDialog_BlocksSightTooltip);
    public const string KeyTileDialog_Title = nameof(KeyTileDialog_Title);
    public const string KeyTileDialog_Description = nameof(KeyTileDialog_Description);
    public const string KeyTileDialog_KeyItemLabel = nameof(KeyTileDialog_KeyItemLabel);
    public const string KeyTileDialog_KeyItemTooltip = nameof(KeyTileDialog_KeyItemTooltip);
    public const string KeyTileDialog_TakeKeyCheckbox = nameof(KeyTileDialog_TakeKeyCheckbox);
    public const string KeyTileDialog_TakeKeyTooltip = nameof(KeyTileDialog_TakeKeyTooltip);

    // ── KeyOpen Trigger Attribute Dialog ──────────────────────────────────────
    public const string KeyOpenDialog_Title = nameof(KeyOpenDialog_Title);
    public const string KeyOpenDialog_Description = nameof(KeyOpenDialog_Description);
    public const string KeyOpenDialog_XLabel = nameof(KeyOpenDialog_XLabel);
    public const string KeyOpenDialog_YLabel = nameof(KeyOpenDialog_YLabel);
    public const string KeyOpenDialog_XTooltip = nameof(KeyOpenDialog_XTooltip);
    public const string KeyOpenDialog_YTooltip = nameof(KeyOpenDialog_YTooltip);

    // ── Light Source Dialog ───────────────────────────────────────────────────
    public const string LightDialog_Title = nameof(LightDialog_Title);
    public const string LightDialog_ColorLabel = nameof(LightDialog_ColorLabel);
    public const string LightDialog_RadiusLabel = nameof(LightDialog_RadiusLabel);
    public const string LightDialog_IntensityLabel = nameof(LightDialog_IntensityLabel);
    public const string LightDialog_FlickerLabel = nameof(LightDialog_FlickerLabel);

    // ── Map editor status messages ────────────────────────────────────────────
    public const string MapEditorStatus_Filled = nameof(MapEditorStatus_Filled);
    public const string MapEditorStatus_AssetsReloaded = nameof(MapEditorStatus_AssetsReloaded);
    public const string MapEditorStatus_ClearedLayer = nameof(MapEditorStatus_ClearedLayer);
    public const string MapEditorStatus_TileRenderFailed = nameof(MapEditorStatus_TileRenderFailed);
    public const string MapEditorStatus_ClearedAttributes = nameof(MapEditorStatus_ClearedAttributes);
    public const string MapEditorStatus_AutoLinked = nameof(MapEditorStatus_AutoLinked);
    public const string MapEditorStatus_AutoLinkedConflict = nameof(MapEditorStatus_AutoLinkedConflict);
    public const string MapEditorStatus_AutoUnlinked = nameof(MapEditorStatus_AutoUnlinked);
    public const string MapEditorStatus_NothingToCopy = nameof(MapEditorStatus_NothingToCopy);
    public const string MapEditorStatus_CopiedTiles = nameof(MapEditorStatus_CopiedTiles);
    public const string MapEditorStatus_NothingToCopyAttr = nameof(MapEditorStatus_NothingToCopyAttr);
    public const string MapEditorStatus_CopiedAttributes = nameof(MapEditorStatus_CopiedAttributes);
    public const string MapEditorStatus_NothingToCopyLights = nameof(MapEditorStatus_NothingToCopyLights);
    public const string MapEditorStatus_CopiedLights = nameof(MapEditorStatus_CopiedLights);
    public const string MapEditorStatus_ClearedLights = nameof(MapEditorStatus_ClearedLights);
    public const string MapEditorStatus_NoEligibleNpcSlots = nameof(MapEditorStatus_NoEligibleNpcSlots);
    public const string MapEditorStatus_PlaceOffMap = nameof(MapEditorStatus_PlaceOffMap);
    public const string MapEditorStatus_PlaceOnBlocked = nameof(MapEditorStatus_PlaceOnBlocked);
    public const string MapEditorStatus_PlaceOverlap = nameof(MapEditorStatus_PlaceOverlap);
    public const string MapEditorStatus_AttrUnderNpc = nameof(MapEditorStatus_AttrUnderNpc);
    public const string MapEditorStatus_FringeDialogAttrGroundOnly = nameof(MapEditorStatus_FringeDialogAttrGroundOnly);
    public const string MapEditor_PlaceNpcTooltip = nameof(MapEditor_PlaceNpcTooltip);
    public const string MapEditorStatus_PlaceNeedsNpc = nameof(MapEditorStatus_PlaceNeedsNpc);
    public const string MapEditorStatus_PlacePrompt = nameof(MapEditorStatus_PlacePrompt);
    public const string MapEditorStatus_PlaceCanceled = nameof(MapEditorStatus_PlaceCanceled);
    public const string MapEditorStatus_PlaceDone = nameof(MapEditorStatus_PlaceDone);
    public const string MapEditorStatus_LoadingOffline = nameof(MapEditorStatus_LoadingOffline);
    public const string MapEditorStatus_LoadedOffline = nameof(MapEditorStatus_LoadedOffline);
    public const string MapEditorStatus_LoadingOnline = nameof(MapEditorStatus_LoadingOnline);
    public const string MapEditorStatus_LoadedOnline = nameof(MapEditorStatus_LoadedOnline);
    public const string MapEditorStatus_LoadingMap = nameof(MapEditorStatus_LoadingMap);
    public const string MapEditorStatus_LoadedMap = nameof(MapEditorStatus_LoadedMap);
    public const string MapEditorStatus_LoadMapFailed = nameof(MapEditorStatus_LoadMapFailed);
    public const string MapEditorStatus_NoDirtyMaps = nameof(MapEditorStatus_NoDirtyMaps);
    public const string MapEditorStatus_AllDiscarded = nameof(MapEditorStatus_AllDiscarded);
    public const string MapEditor_ConfirmClearLayer = nameof(MapEditor_ConfirmClearLayer);
    public const string MapEditor_ConfirmClearAttrs = nameof(MapEditor_ConfirmClearAttrs);
    public const string MapEditor_ConfirmClearLights = nameof(MapEditor_ConfirmClearLights);
    public const string MapEditorStatus_SavedCount = nameof(MapEditorStatus_SavedCount);
    public const string MapEditorStatus_MapSaved = nameof(MapEditorStatus_MapSaved);
    public const string MapEditorStatus_MapCopied = nameof(MapEditorStatus_MapCopied);
    public const string MapEditorStatus_NoEmptyMapSlot = nameof(MapEditorStatus_NoEmptyMapSlot);
    public const string MapEditorStatus_SaveFailed = nameof(MapEditorStatus_SaveFailed);
    public const string MapEditorStatus_SaveError = nameof(MapEditorStatus_SaveError);
    public const string MapEditorStatus_MapDiscarded = nameof(MapEditorStatus_MapDiscarded);
    public const string MapEditorStatus_DiscardError = nameof(MapEditorStatus_DiscardError);
    public const string MapEditor_ConflictHeader = nameof(MapEditor_ConflictHeader);
    public const string MapEditor_ConflictRow = nameof(MapEditor_ConflictRow);
    public const string MapEditor_MapNone = nameof(MapEditor_MapNone);
    public const string MapEditor_MapWithId = nameof(MapEditor_MapWithId);
    public const string MapEditor_MapWithName = nameof(MapEditor_MapWithName);

    // ── Attribute dialog validation ───────────────────────────────────────────
    public const string AttrDialog_SelectMap = nameof(AttrDialog_SelectMap);
    public const string AttrDialog_SelectItem = nameof(AttrDialog_SelectItem);
    public const string AttrDialog_ValueAtLeastOne = nameof(AttrDialog_ValueAtLeastOne);
    public const string AttrDialog_SelectNpcSlot = nameof(AttrDialog_SelectNpcSlot);

    // ── NPC-spawn pin dialog ──────────────────────────────────────────────────
    public const string NpcSpawnDialog_Title = nameof(NpcSpawnDialog_Title);
    public const string NpcSpawnDialog_SlotLabel = nameof(NpcSpawnDialog_SlotLabel);
    public const string AttrDialog_NonCurrencyQtyOne = nameof(AttrDialog_NonCurrencyQtyOne);
    public const string AttrDialog_SelectKeyItem = nameof(AttrDialog_SelectKeyItem);
    public const string AttrDialog_InvalidColor = nameof(AttrDialog_InvalidColor);
    public const string AttrDialog_RadiusPositive = nameof(AttrDialog_RadiusPositive);

    // ── Map size ──────────────────────────────────────────────────────────────
    // The Properties panel's read-out and the resize dialog: what the map is now, what a new size would
    // discard, why a linked map is refused, and the line saying none of it can be taken back.
    public const string MapEditor_SizeLabel = nameof(MapEditor_SizeLabel);
    public const string MapEditor_ResizeButton = nameof(MapEditor_ResizeButton);
    public const string MapEditor_SizeText = nameof(MapEditor_SizeText);                 // "{Width}", "{Height}"
    public const string MapResize_DialogTitle = nameof(MapResize_DialogTitle);
    public const string MapResize_Intro = nameof(MapResize_Intro);
    public const string MapResize_CurrentSize = nameof(MapResize_CurrentSize);           // "{Width}", "{Height}"
    public const string MapResize_WidthLabel = nameof(MapResize_WidthLabel);
    public const string MapResize_HeightLabel = nameof(MapResize_HeightLabel);
    public const string MapResize_LinkedRefusal = nameof(MapResize_LinkedRefusal);       // "{Maps}"
    public const string MapResize_SoftCapWarning = nameof(MapResize_SoftCapWarning);     // "{Cap}"
    public const string MapEditor_LinkSizeMismatch = nameof(MapEditor_LinkSizeMismatch);   // "{Target}" "{TargetSize}" "{Size}"
    public const string MapResize_Irreversible = nameof(MapResize_Irreversible);
    public const string MapResize_LossTiles = nameof(MapResize_LossTiles);               // "{Count}"
    public const string MapResize_LossLights = nameof(MapResize_LossLights);             // "{Count}"
    public const string MapResize_LossPins = nameof(MapResize_LossPins);                 // "{Count}"
    public const string MapResize_LossWarps = nameof(MapResize_LossWarps);               // "{Count}"
    public const string MapEditorStatus_Resized = nameof(MapEditorStatus_Resized);       // "{Width}", "{Height}"
}
