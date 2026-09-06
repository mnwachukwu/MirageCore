using Mirage.Shared.Localization;

namespace Mirage.Editor.Localization;

/// <summary>The application shell: main window, connect/disconnect and push-changes dialogs,
/// shared data-field labels, the tile palette, and the help dialog.</summary>
public static partial class EditorStrings
{
    public const string LanguageName = nameof(LanguageName);

    // ── Common ────────────────────────────────────────────────────────────────
    // Shared across editors and dialogs — single source of truth for these values.
    public const string Editor_SheetUnnamed = nameof(Editor_SheetUnnamed);   // "(unnamed)" - any art sheet
    public const string Common_NameLabel = nameof(Common_NameLabel);          // "Name:"
    public const string Common_DisplayNameLabel = nameof(Common_DisplayNameLabel);   // "Display Name:"
    public const string Common_TypeLabel = nameof(Common_TypeLabel);          // "Type:"
    public const string Common_Cancel = nameof(Common_Cancel);             // "Cancel"
    public const string Common_Close = nameof(Common_Close);              // "Close"
    public const string Common_Confirm = nameof(Common_Confirm);            // "Confirm"
    public const string Common_Connect = nameof(Common_Connect);            // "Connect"
    public const string PushChangesDialog_UnsavedSwitchWorld = nameof(PushChangesDialog_UnsavedSwitchWorld);
    public const string PushChangesDialog_SaveAndContinue = nameof(PushChangesDialog_SaveAndContinue);
    // The lock tooltip on a record list, when the holder is another window signed in as you. Any other
    // holder is shown by account name and needs no wording.
    public const string Common_LockHeldByYourOtherSession = nameof(Common_LockHeldByYourOtherSession);
    public const string Common_Discard = nameof(Common_Discard);            // "Discard"
    public const string Common_DiscardAll = nameof(Common_DiscardAll);         // "Discard All"
    public const string Common_SaveAll = nameof(Common_SaveAll);            // "Save All"
    public const string Common_Copy = nameof(Common_Copy);                  // "Copy"
    public const string Common_CopyTooltip = nameof(Common_CopyTooltip);
    public const string Common_CopyNeedsSelection = nameof(Common_CopyNeedsSelection);
    public const string Common_CopyNeedsRecord = nameof(Common_CopyNeedsRecord);

    public const string Common_Notes = nameof(Common_Notes);              // "Notes"
    public const string Common_RetainOnAltClick = nameof(Common_RetainOnAltClick);
    public const string Common_FillConnectedRun = nameof(Common_FillConnectedRun);
    public const string Common_FillConnectedRunTooltip = nameof(Common_FillConnectedRunTooltip);   // "Retain values for Alt+Click"
    public const string Common_FilterByName = nameof(Common_FilterByName);       // "Filter by name…"
    public const string Common_Filter = nameof(Common_Filter);             // "Filter…"
    public const string Common_PhysDmgAbbrev = nameof(Common_PhysDmgAbbrev);      // "P-DMG"
    public const string Common_MagDmgAbbrev = nameof(Common_MagDmgAbbrev);       // "M-DMG"
    public const string Common_MitAbbrev = nameof(Common_MitAbbrev);          // "MIT" (one universal axis)
    public const string Common_EmptyName = nameof(Common_EmptyName);          // "(empty)"
    public const string Common_Inherit = nameof(Common_Inherit);            // "(Inherit)"
    public const string Common_AddRow = nameof(Common_AddRow);             // "+ Add row" (dynamic table add button)
    public const string Common_NoRowsHint = nameof(Common_NoRowsHint);         // "No rows yet - click + to add." (empty-table hint)

    // ── Auto-save ─────────────────────────────────────────────────────────────
    public const string AutoSave_Menu = nameof(AutoSave_Menu);
    public const string AutoSave_ConfigureItem = nameof(AutoSave_ConfigureItem);
    public const string AutoSave_DisabledOnline = nameof(AutoSave_DisabledOnline);
    public const string AutoSave_DialogTitle = nameof(AutoSave_DialogTitle);
    public const string AutoSave_DialogIntro = nameof(AutoSave_DialogIntro);
    public const string AutoSave_OfflineOnlyNotice = nameof(AutoSave_OfflineOnlyNotice);
    public const string AutoSave_ColumnEditor = nameof(AutoSave_ColumnEditor);
    public const string AutoSave_ColumnEnabled = nameof(AutoSave_ColumnEnabled);
    public const string AutoSave_ColumnInterval = nameof(AutoSave_ColumnInterval);
    public const string AutoSave_ColumnReach = nameof(AutoSave_ColumnReach);
    public const string AutoSave_ReachOpenRecord = nameof(AutoSave_ReachOpenRecord);
    public const string AutoSave_ReachAllDirty = nameof(AutoSave_ReachAllDirty);
    public const string AutoSave_IntervalMinutes = nameof(AutoSave_IntervalMinutes);
    public const string AutoSave_Saved = nameof(AutoSave_Saved);
    public const string AutoSave_RecordCount = nameof(AutoSave_RecordCount);
    public const string AutoSave_Failed = nameof(AutoSave_Failed);

    // ── MainWindow ────────────────────────────────────────────────────────────
    public const string MainWindow_Title = nameof(MainWindow_Title);
    public const string MainWindow_LanguageMenu = nameof(MainWindow_LanguageMenu);
    public const string MainWindow_HelpMenu = nameof(MainWindow_HelpMenu);
    public const string MainWindow_HelpGeneral = nameof(MainWindow_HelpGeneral);
    public const string MainWindow_HelpMapEditor = nameof(MainWindow_HelpMapEditor);
    public const string MainWindow_HelpAbout = nameof(MainWindow_HelpAbout);
    public const string About_Title = nameof(About_Title);
    public const string About_Version = nameof(About_Version);
    public const string About_CreatorDeveloper = nameof(About_CreatorDeveloper);
    public const string MainWindow_ExportMenu = nameof(MainWindow_ExportMenu);
    public const string MainWindow_ViewMenu = nameof(MainWindow_ViewMenu);

    // ── Asset manager ─────────────────────────────────────────────────────────
    public const string MainWindow_AssetsMenu = nameof(MainWindow_AssetsMenu);
    public const string AssetManager_MenuItem = nameof(AssetManager_MenuItem);
    public const string AssetManager_Title = nameof(AssetManager_Title);
    public const string AssetManager_Intro = nameof(AssetManager_Intro);
    public const string AssetManager_Summary = nameof(AssetManager_Summary);
    public const string AssetManager_Empty = nameof(AssetManager_Empty);
    public const string AssetManager_SheetDetail = nameof(AssetManager_SheetDetail);
    public const string AssetManager_SheetDetailUnknown = nameof(AssetManager_SheetDetailUnknown);
    public const string AssetManager_Usage = nameof(AssetManager_Usage);
    public const string AssetManager_UsagePartial = nameof(AssetManager_UsagePartial);
    public const string AssetManager_UsageNone = nameof(AssetManager_UsageNone);
    public const string AssetManager_UsageNonePartial = nameof(AssetManager_UsageNonePartial);
    public const string AssetManager_Import = nameof(AssetManager_Import);
    public const string AssetManager_OpenFolder = nameof(AssetManager_OpenFolder);
    public const string AssetManager_Rename = nameof(AssetManager_Rename);
    public const string AssetManager_Replace = nameof(AssetManager_Replace);
    public const string AssetManager_Delete = nameof(AssetManager_Delete);
    public const string AssetManager_Restore = nameof(AssetManager_Restore);
    public const string AssetManager_Repair = nameof(AssetManager_Repair);
    public const string AssetManager_ProblemsHeader = nameof(AssetManager_ProblemsHeader);
    public const string AssetManager_RecycleHeader = nameof(AssetManager_RecycleHeader);
    public const string AssetManager_ProblemDuplicate = nameof(AssetManager_ProblemDuplicate);
    public const string AssetManager_ProblemNoIndex = nameof(AssetManager_ProblemNoIndex);
    public const string AssetManager_ProblemOutOfRange = nameof(AssetManager_ProblemOutOfRange);
    public const string AssetManager_ProblemNotAligned = nameof(AssetManager_ProblemNotAligned);
    public const string AssetManager_ProblemNoAlpha = nameof(AssetManager_ProblemNoAlpha);
    public const string AssetManager_ProblemMissingSize = nameof(AssetManager_ProblemMissingSize);
    public const string AssetManager_ProblemSizeRows = nameof(AssetManager_ProblemSizeRows);
    public const string AssetManager_CategoryTiles = nameof(AssetManager_CategoryTiles);
    public const string AssetManager_CategorySprites = nameof(AssetManager_CategorySprites);
    public const string AssetManager_CategoryItems = nameof(AssetManager_CategoryItems);
    public const string AssetManager_CategoryLabel = nameof(AssetManager_CategoryLabel);
    public const string AssetManager_SizeLabel = nameof(AssetManager_SizeLabel);
    public const string AssetManager_ConsequenceTiles = nameof(AssetManager_ConsequenceTiles);
    public const string AssetManager_ConsequenceSprites = nameof(AssetManager_ConsequenceSprites);
    public const string AssetManager_ConsequenceItems = nameof(AssetManager_ConsequenceItems);
    public const string AssetManager_UsageSprites = nameof(AssetManager_UsageSprites);
    public const string AssetManager_UsageItems = nameof(AssetManager_UsageItems);
    public const string AssetManager_UsageNoneRecords = nameof(AssetManager_UsageNoneRecords);
    public const string AssetManager_TransparencyKey = nameof(AssetManager_TransparencyKey);
    public const string AssetManager_TransparencyAlpha = nameof(AssetManager_TransparencyAlpha);
    public const string AssetManager_TransparencyNone = nameof(AssetManager_TransparencyNone);
    public const string AssetManager_ConfirmDelete = nameof(AssetManager_ConfirmDelete);
    public const string AssetManager_ConfirmRestoreMoved = nameof(AssetManager_ConfirmRestoreMoved);
    public const string AssetManager_Imported = nameof(AssetManager_Imported);
    public const string AssetManager_Renamed = nameof(AssetManager_Renamed);
    public const string AssetManager_Replaced = nameof(AssetManager_Replaced);
    public const string AssetManager_Deleted = nameof(AssetManager_Deleted);
    public const string AssetManager_Restored = nameof(AssetManager_Restored);
    public const string AssetManager_Repaired = nameof(AssetManager_Repaired);
    public const string AssetManager_Failed = nameof(AssetManager_Failed);
    public const string AssetManager_FullNoIndex = nameof(AssetManager_FullNoIndex);
    public const string AssetManager_PickTitle = nameof(AssetManager_PickTitle);
    public const string AssetManager_PickFilter = nameof(AssetManager_PickFilter);

    // ── World Preview ─────────────────────────────────────────────────────────
    // ── Layer Visibility ──────────────────────────────────────────────────────
    public const string LayerVisibility_MenuItem = nameof(LayerVisibility_MenuItem);
    public const string LayerVisibility_Title = nameof(LayerVisibility_Title);
    public const string LayerVisibility_Intro = nameof(LayerVisibility_Intro);

    // The Console window: the third of the View menu's modeless windows.
    public const string Console_MenuItem = nameof(Console_MenuItem);
    public const string Console_Title = nameof(Console_Title);
    public const string Console_Intro = nameof(Console_Intro);
    public const string Console_Clear = nameof(Console_Clear);
    public const string Console_OpenFolder = nameof(Console_OpenFolder);
    public const string Console_Status = nameof(Console_Status);
    public const string LayerVisibility_ShowAll = nameof(LayerVisibility_ShowAll);
    public const string LayerVisibility_HideAll = nameof(LayerVisibility_HideAll);
    public const string LayerVisibility_AllShown = nameof(LayerVisibility_AllShown);
    public const string LayerVisibility_SomeHidden = nameof(LayerVisibility_SomeHidden);

    public const string WorldPreview_MenuItem = nameof(WorldPreview_MenuItem);
    public const string WorldPreview_Title = nameof(WorldPreview_Title);
    public const string WorldPreview_NoMaps = nameof(WorldPreview_NoMaps);
    public const string WorldPreview_Count = nameof(WorldPreview_Count);
    public const string WorldPreview_CountTruncated = nameof(WorldPreview_CountTruncated);
    public const string WorldPreview_Hint = nameof(WorldPreview_Hint);
    public const string WorldPreview_WarpsLabel = nameof(WorldPreview_WarpsLabel);
    public const string WarpTargets_Title = nameof(WarpTargets_Title);
    public const string WarpTargets_Intro = nameof(WarpTargets_Intro);
    public const string WarpTargets_None = nameof(WarpTargets_None);
    public const string WarpTargets_EntryCount = nameof(WarpTargets_EntryCount);
    public const string MainWindow_DisconnectButton = nameof(MainWindow_DisconnectButton);
    public const string MainWindow_Section_Maps = nameof(MainWindow_Section_Maps);
    public const string MainWindow_Section_MapGroups = nameof(MainWindow_Section_MapGroups);
    public const string MainWindow_Section_Items = nameof(MainWindow_Section_Items);
    public const string MainWindow_Section_Npcs = nameof(MainWindow_Section_Npcs);
    public const string MainWindow_Section_Shops = nameof(MainWindow_Section_Shops);
    public const string MainWindow_Section_Spells = nameof(MainWindow_Section_Spells);
    public const string MainWindow_Section_Classes = nameof(MainWindow_Section_Classes);
    public const string MainWindow_Section_Quests = nameof(MainWindow_Section_Quests);
    public const string MainWindow_Section_Conversations = nameof(MainWindow_Section_Conversations);
    public const string MainWindow_Section_Accounts = nameof(MainWindow_Section_Accounts);

    // ── AccountEditor (Creator only, online only) ─────────────────────────────
    public const string AccountEditor_SearchPlaceholder = nameof(AccountEditor_SearchPlaceholder);
    public const string AccountEditor_OfflineNotice = nameof(AccountEditor_OfflineNotice);
    public const string AccountEditor_SelectPrompt = nameof(AccountEditor_SelectPrompt);
    public const string AccountEditor_AccessLabel = nameof(AccountEditor_AccessLabel);
    public const string AccountEditor_GuildLabel = nameof(AccountEditor_GuildLabel);
    public const string AccountEditor_GuildFormat = nameof(AccountEditor_GuildFormat);
    public const string AccountEditor_NoGuild = nameof(AccountEditor_NoGuild);
    public const string AccountEditor_CharactersHeader = nameof(AccountEditor_CharactersHeader);
    public const string AccountEditor_NoCharacters = nameof(AccountEditor_NoCharacters);
    public const string AccountEditor_PageOf = nameof(AccountEditor_PageOf);
    public const string AccountEditor_PrevPage = nameof(AccountEditor_PrevPage);
    public const string AccountEditor_NextPage = nameof(AccountEditor_NextPage);
    public const string AccountEditor_Reload = nameof(AccountEditor_Reload);
    public const string AccountEditor_Save = nameof(AccountEditor_Save);
    public const string AccountEditor_Saved = nameof(AccountEditor_Saved);
    public const string AccountEditor_SelfAccessHint = nameof(AccountEditor_SelfAccessHint);
    public const string AccountEditor_AnyAccess = nameof(AccountEditor_AnyAccess);
    public const string AccountEditor_StatBudget = nameof(AccountEditor_StatBudget);
    public const string AccountEditor_StatBudgetOver = nameof(AccountEditor_StatBudgetOver);
    public const string AccountEditor_SaveBlockedBudget = nameof(AccountEditor_SaveBlockedBudget);
    public const string AccountEditor_Rename = nameof(AccountEditor_Rename);
    public const string AccountEditor_RenamePlaceholder = nameof(AccountEditor_RenamePlaceholder);
    public const string AccountEditor_BagHeader = nameof(AccountEditor_BagHeader);
    public const string AccountEditor_BagEmpty = nameof(AccountEditor_BagEmpty);
    public const string AccountEditor_Give = nameof(AccountEditor_Give);
    public const string AccountEditor_Take = nameof(AccountEditor_Take);
    public const string AccountEditor_ItemPlaceholder = nameof(AccountEditor_ItemPlaceholder);
    public const string AccountEditor_Worn = nameof(AccountEditor_Worn);
    public const string AccountEditor_BookHeader = nameof(AccountEditor_BookHeader);
    public const string AccountEditor_BookEmpty = nameof(AccountEditor_BookEmpty);
    public const string AccountEditor_Teach = nameof(AccountEditor_Teach);
    public const string AccountEditor_SpellPlaceholder = nameof(AccountEditor_SpellPlaceholder);
    public const string AccountEditor_VaultHeader = nameof(AccountEditor_VaultHeader);
    public const string AccountEditor_VaultEmpty = nameof(AccountEditor_VaultEmpty);
    public const string AccountEditor_LogHeader = nameof(AccountEditor_LogHeader);
    public const string AccountEditor_LogEmpty = nameof(AccountEditor_LogEmpty);
    public const string AccountEditor_SetQuest = nameof(AccountEditor_SetQuest);
    public const string AccountEditor_QuestPlaceholder = nameof(AccountEditor_QuestPlaceholder);
    public const string AccountEditor_Ineligible = nameof(AccountEditor_Ineligible);
    public const string MainWindow_StatusOffline = nameof(MainWindow_StatusOffline);
    public const string MainWindow_StatusOnline = nameof(MainWindow_StatusOnline);
    public const string MainWindow_RailCollapse = nameof(MainWindow_RailCollapse);
    public const string MainWindow_RailExpand = nameof(MainWindow_RailExpand);
    public const string MainWindow_LoadingSection = nameof(MainWindow_LoadingSection);
    public const string MainWindow_LoadingData = nameof(MainWindow_LoadingData);
    public const string MainWindow_LoadingAssets = nameof(MainWindow_LoadingAssets);
    public const string MainWindow_LoadingSectionProgress = nameof(MainWindow_LoadingSectionProgress);
    public const string MainWindow_DisconnectedAlert = nameof(MainWindow_DisconnectedAlert);

    // ── ConnectDialog (in-dialog header + error; ConnectDialog_Title is the window title, exists) ──
    public const string ConnectDialog_Header = nameof(ConnectDialog_Header);
    public const string ConnectDialog_ConnectionError = nameof(ConnectDialog_ConnectionError);
    public const string ConnectDialog_IdentityChanged = nameof(ConnectDialog_IdentityChanged); // "{Host}" "{Port}"
    // "{Host}" "{Port}" "{Expected}" "{Actual}"
    public const string ConnectDialog_IdentityChangedDetail = nameof(ConnectDialog_IdentityChangedDetail);
    public const string ConnectDialog_TrustNewCertificate = nameof(ConnectDialog_TrustNewCertificate);
    public const string ConnectDialog_ClearPin = nameof(ConnectDialog_ClearPin);
    public const string ConnectDialog_PinCleared = nameof(ConnectDialog_PinCleared);           // "{Server}"
    public const string ConnectDialog_KnownServers = nameof(ConnectDialog_KnownServers);
    public const string ConnectDialog_Forget = nameof(ConnectDialog_Forget);
    public const string ConnectDialog_Add = nameof(ConnectDialog_Add);
    public const string ConnectDialog_ServerName = nameof(ConnectDialog_ServerName);
    // The dialog's three groups: who you are, where the server is, which saved entry.
    public const string ConnectDialog_SignInHeader = nameof(ConnectDialog_SignInHeader);
    public const string ConnectDialog_ServerHeader = nameof(ConnectDialog_ServerHeader);
    public const string ConnectDialog_SavedServersHeader = nameof(ConnectDialog_SavedServersHeader);

    // ── DisconnectDialog (connection-lost body + reconnect outcomes) ──
    public const string DisconnectDialog_ConnectionLostBody = nameof(DisconnectDialog_ConnectionLostBody);
    public const string DisconnectDialog_ReconnectFailed = nameof(DisconnectDialog_ReconnectFailed);
    public const string DisconnectDialog_ReconnectCanceled = nameof(DisconnectDialog_ReconnectCanceled);

    // ── PushChangesDialog dirty-entry labels (Unsaved*/SaveAnd*/Saving/Pushing keys already exist) ──
    public const string PushChangesDialog_DirtyItem = nameof(PushChangesDialog_DirtyItem);
    public const string PushChangesDialog_DirtyNpc = nameof(PushChangesDialog_DirtyNpc);
    public const string PushChangesDialog_DirtyShop = nameof(PushChangesDialog_DirtyShop);
    public const string PushChangesDialog_DirtyQuest = nameof(PushChangesDialog_DirtyQuest);
    public const string PushChangesDialog_DirtyConversation = nameof(PushChangesDialog_DirtyConversation);
    public const string PushChangesDialog_DirtySpell = nameof(PushChangesDialog_DirtySpell);
    public const string PushChangesDialog_DirtyMap = nameof(PushChangesDialog_DirtyMap);
    public const string PushChangesDialog_DirtyMapGroup = nameof(PushChangesDialog_DirtyMapGroup);
    public const string PushChangesDialog_DirtyClass = nameof(PushChangesDialog_DirtyClass);
    public const string PushChangesDialog_DirtyUnknown = nameof(PushChangesDialog_DirtyUnknown);

    // ── Data field labels (shared by Item/Spell row editors) ──────────────────
    public const string DataLabel_Durability = nameof(DataLabel_Durability);
    public const string DataLabel_HpAmount = nameof(DataLabel_HpAmount);
    public const string DataLabel_MpAmount = nameof(DataLabel_MpAmount);
    public const string DataLabel_SpAmount = nameof(DataLabel_SpAmount);
    public const string DataLabel_SpellNumber = nameof(DataLabel_SpellNumber);
    public const string DataLabel_ItemNumber = nameof(DataLabel_ItemNumber);
    public const string DataLabel_Damage = nameof(DataLabel_Damage);
    public const string DataLabel_Defense = nameof(DataLabel_Defense);
    public const string DataLabel_MpDrain = nameof(DataLabel_MpDrain);
    public const string DataLabel_SpDrain = nameof(DataLabel_SpDrain);
    public const string DataLabel_Quantity = nameof(DataLabel_Quantity);
    public const string DataLabel_IntReq = nameof(DataLabel_IntReq);
    public const string DataLabel_AllowedClasses = nameof(DataLabel_AllowedClasses);
    // The class multi-select shared by the item, spell and quest editors.
    public const string ClassSelector_AnyClass = nameof(ClassSelector_AnyClass);
    public const string ClassSelector_Hint = nameof(ClassSelector_Hint);
    // Fallback captions for the two fields whose caption varies by type, shown if a type ever falls
    // outside the switch. Not "Data 1/2/3" any more — there is no numbered slot left to name.
    public const string DataLabel_VitalAmount = nameof(DataLabel_VitalAmount);
    public const string DataLabel_Power = nameof(DataLabel_Power);
    public const string DataLabel_LevelReq = nameof(DataLabel_LevelReq);

    // ── EditorConnection (service-layer errors shown to the user) ─────────────
    public const string EditorConnection_ClosedUnexpectedly = nameof(EditorConnection_ClosedUnexpectedly);
    public const string EditorConnection_UnexpectedResponse = nameof(EditorConnection_UnexpectedResponse);
    public const string EditorConnection_ClosedBeforeData = nameof(EditorConnection_ClosedBeforeData);
    public const string EditorConnection_ExpectedDataPacket = nameof(EditorConnection_ExpectedDataPacket);

    // ── TilePaletteControl ────────────────────────────────────────────────────
    public const string TilePalette_NoTileset = nameof(TilePalette_NoTileset);

    // ── HelpDialog ────────────────────────────────────────────────────────────
    public const string HelpDialog_Title = nameof(HelpDialog_Title);
    public const string HelpDialog_Header = nameof(HelpDialog_Header);
    public const string HelpDialog_ControlsHeader = nameof(HelpDialog_ControlsHeader);
    public const string HelpDialog_SelectionHeader = nameof(HelpDialog_SelectionHeader);
    public const string HelpDialog_LayersHeader = nameof(HelpDialog_LayersHeader);
    public const string HelpDialog_AttributeHeader = nameof(HelpDialog_AttributeHeader);
    public const string HelpDialog_WorldHeader = nameof(HelpDialog_WorldHeader);
    public const string HelpDialog_WorldIntro = nameof(HelpDialog_WorldIntro);
    public const string HelpDialog_World_WorldFolder = nameof(HelpDialog_World_WorldFolder);
    public const string HelpDialog_World_WorldFolderDesc = nameof(HelpDialog_World_WorldFolderDesc);
    public const string HelpDialog_World_MapSlots = nameof(HelpDialog_World_MapSlots);
    public const string HelpDialog_World_MapSlotsDesc = nameof(HelpDialog_World_MapSlotsDesc);
    public const string HelpDialog_World_MapSize = nameof(HelpDialog_World_MapSize);
    public const string HelpDialog_World_MapSizeDesc = nameof(HelpDialog_World_MapSizeDesc);
    public const string HelpDialog_World_Links = nameof(HelpDialog_World_Links);
    public const string HelpDialog_World_LinksDesc = nameof(HelpDialog_World_LinksDesc);
    public const string HelpDialog_World_Groups = nameof(HelpDialog_World_Groups);
    public const string HelpDialog_World_GroupsDesc = nameof(HelpDialog_World_GroupsDesc);
    public const string HelpDialog_World_Properties = nameof(HelpDialog_World_Properties);
    public const string HelpDialog_World_PropertiesDesc = nameof(HelpDialog_World_PropertiesDesc);
    public const string HelpDialog_World_Planes = nameof(HelpDialog_World_Planes);

    public const string HelpDialog_ViewsHeader = nameof(HelpDialog_ViewsHeader);
    public const string HelpDialog_ViewsIntro = nameof(HelpDialog_ViewsIntro);
    public const string HelpDialog_Views_WorldPreview = nameof(HelpDialog_Views_WorldPreview);
    public const string HelpDialog_Views_WorldPreviewDesc = nameof(HelpDialog_Views_WorldPreviewDesc);
    public const string HelpDialog_Views_LayerVisibility = nameof(HelpDialog_Views_LayerVisibility);
    public const string HelpDialog_Views_LayerVisibilityDesc = nameof(HelpDialog_Views_LayerVisibilityDesc);
    public const string HelpDialog_Views_Console = nameof(HelpDialog_Views_Console);
    public const string HelpDialog_Views_ConsoleDesc = nameof(HelpDialog_Views_ConsoleDesc);

    // ── General help ──────────────────────────────────────────────────────────
    // The editor-wide reference: what is not about a map. Its own prefix rather than HelpDialog_*,
    // because the site's page generator asserts every string of a prefix reaches the page.
    public const string GeneralHelp_Title = nameof(GeneralHelp_Title);
    public const string GeneralHelp_Header = nameof(GeneralHelp_Header);
    public const string GeneralHelp_AssetsHeader = nameof(GeneralHelp_AssetsHeader);
    public const string GeneralHelp_AssetsIntro = nameof(GeneralHelp_AssetsIntro);
    public const string GeneralHelp_Assets_Sheets = nameof(GeneralHelp_Assets_Sheets);
    public const string GeneralHelp_Assets_SheetsDesc = nameof(GeneralHelp_Assets_SheetsDesc);
    public const string GeneralHelp_Assets_Manager = nameof(GeneralHelp_Assets_Manager);
    public const string GeneralHelp_Assets_ManagerDesc = nameof(GeneralHelp_Assets_ManagerDesc);
    public const string GeneralHelp_Assets_Reload = nameof(GeneralHelp_Assets_Reload);
    public const string GeneralHelp_Assets_ReloadDesc = nameof(GeneralHelp_Assets_ReloadDesc);
    public const string HelpDialog_World_PlanesDesc = nameof(HelpDialog_World_PlanesDesc);
    public const string HelpDialog_LayerGround = nameof(HelpDialog_LayerGround);
    public const string HelpDialog_LayerGroundDesc = nameof(HelpDialog_LayerGroundDesc);
    public const string HelpDialog_LayerAnimFlag = nameof(HelpDialog_LayerAnimFlag);
    public const string HelpDialog_LayerAnimFlagDesc = nameof(HelpDialog_LayerAnimFlagDesc);
    public const string HelpDialog_LayerTileset = nameof(HelpDialog_LayerTileset);
    public const string HelpDialog_LayerTilesetDesc = nameof(HelpDialog_LayerTilesetDesc);
    public const string HelpDialog_LayerFringe = nameof(HelpDialog_LayerFringe);
    public const string HelpDialog_LayerFringeDesc = nameof(HelpDialog_LayerFringeDesc);
    public const string HelpDialog_LayerCanopy = nameof(HelpDialog_LayerCanopy);
    public const string HelpDialog_LayerCanopyDesc = nameof(HelpDialog_LayerCanopyDesc);
    public const string HelpDialog_LayersIntro = nameof(HelpDialog_LayersIntro);
    public const string HelpDialog_SelectionIntro = nameof(HelpDialog_SelectionIntro);
    public const string HelpDialog_AttributesIntro = nameof(HelpDialog_AttributesIntro);
    // Controls section
    public const string HelpDialog_Ctrl_LeftClick = nameof(HelpDialog_Ctrl_LeftClick);
    public const string HelpDialog_Ctrl_LeftClickDesc = nameof(HelpDialog_Ctrl_LeftClickDesc);
    public const string HelpDialog_Ctrl_AltClick = nameof(HelpDialog_Ctrl_AltClick);
    public const string HelpDialog_Ctrl_AltClickDesc = nameof(HelpDialog_Ctrl_AltClickDesc);
    public const string HelpDialog_Ctrl_CtrlDrag = nameof(HelpDialog_Ctrl_CtrlDrag);
    public const string HelpDialog_Ctrl_CtrlDragDesc = nameof(HelpDialog_Ctrl_CtrlDragDesc);
    public const string MainWindow_HelpLogging = nameof(MainWindow_HelpLogging);
    public const string Logging_DialogTitle = nameof(Logging_DialogTitle);
    public const string Logging_DialogIntro = nameof(Logging_DialogIntro);
    public const string Logging_LevelLabel = nameof(Logging_LevelLabel);
    public const string Logging_RetentionLabel = nameof(Logging_RetentionLabel);
    public const string Logging_FolderLabel = nameof(Logging_FolderLabel);
    public const string Logging_OpenFolderButton = nameof(Logging_OpenFolderButton);
    public const string Logging_LevelError = nameof(Logging_LevelError);
    public const string Logging_LevelErrorDetail = nameof(Logging_LevelErrorDetail);
    public const string Logging_LevelWarning = nameof(Logging_LevelWarning);
    public const string Logging_LevelWarningDetail = nameof(Logging_LevelWarningDetail);
    public const string Logging_LevelInformation = nameof(Logging_LevelInformation);
    public const string Logging_LevelInformationDetail = nameof(Logging_LevelInformationDetail);
    public const string Logging_LevelDebug = nameof(Logging_LevelDebug);
    public const string Logging_LevelDebugDetail = nameof(Logging_LevelDebugDetail);
    public const string Logging_LevelVerbose = nameof(Logging_LevelVerbose);
    public const string Logging_LevelVerboseDetail = nameof(Logging_LevelVerboseDetail);
    public const string Logging_Retain3 = nameof(Logging_Retain3);
    public const string Logging_Retain7 = nameof(Logging_Retain7);
    public const string Logging_Retain14 = nameof(Logging_Retain14);
    public const string Logging_Retain30 = nameof(Logging_Retain30);
    public const string Logging_RetainForever = nameof(Logging_RetainForever);

    public const string HelpDialog_Ctrl_CtrlAltShiftClick = nameof(HelpDialog_Ctrl_CtrlAltShiftClick);
    public const string HelpDialog_Ctrl_CtrlAltShiftClickDesc = nameof(HelpDialog_Ctrl_CtrlAltShiftClickDesc);
    public const string HelpDialog_Ctrl_RightClick = nameof(HelpDialog_Ctrl_RightClick);
    public const string HelpDialog_Ctrl_RightClickDesc = nameof(HelpDialog_Ctrl_RightClickDesc);
    public const string HelpDialog_Ctrl_UndoRedo = nameof(HelpDialog_Ctrl_UndoRedo);
    public const string HelpDialog_Ctrl_UndoRedoDesc = nameof(HelpDialog_Ctrl_UndoRedoDesc);
    public const string HelpDialog_Ctrl_BackForward = nameof(HelpDialog_Ctrl_BackForward);
    public const string HelpDialog_Ctrl_BackForwardDesc = nameof(HelpDialog_Ctrl_BackForwardDesc);
    public const string HelpDialog_Ctrl_MouseBackForward = nameof(HelpDialog_Ctrl_MouseBackForward);
    public const string HelpDialog_Ctrl_MouseBackForwardDesc = nameof(HelpDialog_Ctrl_MouseBackForwardDesc);
    public const string HelpDialog_Ctrl_ShiftHold = nameof(HelpDialog_Ctrl_ShiftHold);
    public const string HelpDialog_Ctrl_ShiftHoldDesc = nameof(HelpDialog_Ctrl_ShiftHoldDesc);
    public const string HelpDialog_Ctrl_ScrollWheel = nameof(HelpDialog_Ctrl_ScrollWheel);
    public const string HelpDialog_Ctrl_ScrollWheelDesc = nameof(HelpDialog_Ctrl_ScrollWheelDesc);
    public const string HelpDialog_Ctrl_CtrlScrollWheel = nameof(HelpDialog_Ctrl_CtrlScrollWheel);
    public const string HelpDialog_Ctrl_CtrlScrollWheelDesc = nameof(HelpDialog_Ctrl_CtrlScrollWheelDesc);
    public const string HelpDialog_Ctrl_AltScrollWheel = nameof(HelpDialog_Ctrl_AltScrollWheel);
    public const string HelpDialog_Ctrl_AltScrollWheelDesc = nameof(HelpDialog_Ctrl_AltScrollWheelDesc);
    public const string HelpDialog_Ctrl_CtrlAltScrollWheel = nameof(HelpDialog_Ctrl_CtrlAltScrollWheel);
    public const string HelpDialog_Ctrl_CtrlAltScrollWheelDesc = nameof(HelpDialog_Ctrl_CtrlAltScrollWheelDesc);
    public const string HelpDialog_Ctrl_ZoomButtons = nameof(HelpDialog_Ctrl_ZoomButtons);
    public const string HelpDialog_Ctrl_ZoomButtonsDesc = nameof(HelpDialog_Ctrl_ZoomButtonsDesc);
    public const string HelpDialog_Ctrl_AnimPreview = nameof(HelpDialog_Ctrl_AnimPreview);
    public const string HelpDialog_Ctrl_AnimPreviewDesc = nameof(HelpDialog_Ctrl_AnimPreviewDesc);
    public const string HelpDialog_Ctrl_SaveMap = nameof(HelpDialog_Ctrl_SaveMap);
    public const string HelpDialog_Ctrl_SaveMapDesc = nameof(HelpDialog_Ctrl_SaveMapDesc);
    public const string HelpDialog_Ctrl_DiscardMap = nameof(HelpDialog_Ctrl_DiscardMap);
    public const string HelpDialog_Ctrl_DiscardMapDesc = nameof(HelpDialog_Ctrl_DiscardMapDesc);
    public const string HelpDialog_Ctrl_PaletteClick = nameof(HelpDialog_Ctrl_PaletteClick);
    public const string HelpDialog_Ctrl_PaletteClickDesc = nameof(HelpDialog_Ctrl_PaletteClickDesc);
    // Selection section
    public const string HelpDialog_Sel_ActionPlace = nameof(HelpDialog_Sel_ActionPlace);
    public const string HelpDialog_Sel_ActionPlaceDesc = nameof(HelpDialog_Sel_ActionPlaceDesc);
    public const string HelpDialog_Sel_ActionSelection = nameof(HelpDialog_Sel_ActionSelection);
    public const string HelpDialog_Sel_ActionSelectionDesc = nameof(HelpDialog_Sel_ActionSelectionDesc);
    public const string HelpDialog_Sel_CtrlCSelection = nameof(HelpDialog_Sel_CtrlCSelection);
    public const string HelpDialog_Sel_CtrlCSelectionDesc = nameof(HelpDialog_Sel_CtrlCSelectionDesc);
    public const string HelpDialog_Sel_ActionDelete = nameof(HelpDialog_Sel_ActionDelete);
    public const string HelpDialog_Sel_ActionDeleteDesc = nameof(HelpDialog_Sel_ActionDeleteDesc);
    public const string HelpDialog_Sel_CtrlXSelection = nameof(HelpDialog_Sel_CtrlXSelection);
    public const string HelpDialog_Sel_CtrlXSelectionDesc = nameof(HelpDialog_Sel_CtrlXSelectionDesc);
    public const string HelpDialog_Sel_ModeKeys = nameof(HelpDialog_Sel_ModeKeys);
    public const string HelpDialog_Sel_ModeKeysDesc = nameof(HelpDialog_Sel_ModeKeysDesc);
    public const string HelpDialog_Sel_ClickPlace = nameof(HelpDialog_Sel_ClickPlace);
    public const string HelpDialog_Sel_ClickPlaceDesc = nameof(HelpDialog_Sel_ClickPlaceDesc);
    public const string HelpDialog_Sel_CtrlShiftClick = nameof(HelpDialog_Sel_CtrlShiftClick);
    public const string HelpDialog_Sel_CtrlShiftClickDesc = nameof(HelpDialog_Sel_CtrlShiftClickDesc);
    public const string HelpDialog_Sel_Esc = nameof(HelpDialog_Sel_Esc);
    public const string HelpDialog_Sel_EscDesc = nameof(HelpDialog_Sel_EscDesc);
    // Attribute reference
    public const string HelpDialog_Attr_BlockedDesc = nameof(HelpDialog_Attr_BlockedDesc);
    public const string HelpDialog_Attr_WarpDesc = nameof(HelpDialog_Attr_WarpDesc);
    public const string HelpDialog_Attr_WarpData = nameof(HelpDialog_Attr_WarpData);
    public const string HelpDialog_Attr_ItemDesc = nameof(HelpDialog_Attr_ItemDesc);
    public const string HelpDialog_Attr_ItemData = nameof(HelpDialog_Attr_ItemData);
    public const string HelpDialog_Attr_NpcAvoidDesc = nameof(HelpDialog_Attr_NpcAvoidDesc);
    public const string HelpDialog_Attr_Key = nameof(HelpDialog_Attr_Key);
    public const string HelpDialog_Attr_KeyDesc = nameof(HelpDialog_Attr_KeyDesc);
    public const string HelpDialog_Attr_KeyDoorDesc = nameof(HelpDialog_Attr_KeyDoorDesc);
    public const string HelpDialog_Attr_KeyData = nameof(HelpDialog_Attr_KeyData);
    public const string HelpDialog_Attr_KeyOpen = nameof(HelpDialog_Attr_KeyOpen);
    public const string HelpDialog_Attr_KeyOpenDesc = nameof(HelpDialog_Attr_KeyOpenDesc);
    public const string HelpDialog_Attr_KeyOpenData = nameof(HelpDialog_Attr_KeyOpenData);
    public const string HelpDialog_Attr_NpcSpawnDesc = nameof(HelpDialog_Attr_NpcSpawnDesc);
    public const string HelpDialog_Attr_LayerRamp = nameof(HelpDialog_Attr_LayerRamp);
    public const string HelpDialog_Attr_LayerRampDesc = nameof(HelpDialog_Attr_LayerRampDesc);
    public const string HelpDialog_Attr_LayerRampData = nameof(HelpDialog_Attr_LayerRampData);

    // ── ConnectDialog ─────────────────────────────────────────────────────────
    public const string ConnectDialog_Title = nameof(ConnectDialog_Title);
    public const string ConnectDialog_UsernameLabel = nameof(ConnectDialog_UsernameLabel);
    public const string ConnectDialog_PasswordLabel = nameof(ConnectDialog_PasswordLabel);
    public const string ConnectDialog_HostLabel = nameof(ConnectDialog_HostLabel);
    public const string ConnectDialog_PortLabel = nameof(ConnectDialog_PortLabel);

    // ── DisconnectDialog ──────────────────────────────────────────────────────
    public const string DisconnectDialog_Title = nameof(DisconnectDialog_Title);
    public const string DisconnectDialog_Header = nameof(DisconnectDialog_Header);
    public const string DisconnectDialog_AbandonButton = nameof(DisconnectDialog_AbandonButton);
    public const string DisconnectDialog_ReconnectButton = nameof(DisconnectDialog_ReconnectButton);

    // ── ConfirmDialog ─────────────────────────────────────────────────────────
    public const string ConfirmDialog_OkButton = nameof(ConfirmDialog_OkButton);
    public const string ConfirmDialog_Title = nameof(ConfirmDialog_Title);         // "Confirm"
    public const string ConfirmDialog_AlertTitle = nameof(ConfirmDialog_AlertTitle); // "Notice"

    // ── PushChangesDialog ─────────────────────────────────────────────────────
    public const string PushChangesDialog_Title = nameof(PushChangesDialog_Title);
    public const string PushChangesDialog_UnsavedOnClose = nameof(PushChangesDialog_UnsavedOnClose);
    public const string PushChangesDialog_UnsavedPush = nameof(PushChangesDialog_UnsavedPush);
    public const string PushChangesDialog_UnsavedConnect = nameof(PushChangesDialog_UnsavedConnect);
    public const string PushChangesDialog_UnsavedOnline = nameof(PushChangesDialog_UnsavedOnline);
    public const string PushChangesDialog_SaveAndClose = nameof(PushChangesDialog_SaveAndClose);
    public const string PushChangesDialog_PushAndContinue = nameof(PushChangesDialog_PushAndContinue);
    public const string PushChangesDialog_SaveAndConnect = nameof(PushChangesDialog_SaveAndConnect);
    public const string PushChangesDialog_PushAndDisconnect = nameof(PushChangesDialog_PushAndDisconnect);
    public const string PushChangesDialog_DiscardAndClose = nameof(PushChangesDialog_DiscardAndClose);
    public const string PushChangesDialog_DiscardAndContinue = nameof(PushChangesDialog_DiscardAndContinue);
    public const string PushChangesDialog_DiscardAndConnect = nameof(PushChangesDialog_DiscardAndConnect);
    public const string PushChangesDialog_DiscardAndDisconnect = nameof(PushChangesDialog_DiscardAndDisconnect);
    public const string PushChangesDialog_Saving = nameof(PushChangesDialog_Saving);
    public const string PushChangesDialog_Pushing = nameof(PushChangesDialog_Pushing);
    public const string PushChangesDialog_Error = nameof(PushChangesDialog_Error);

    // ── Data / Refresh ────────────────────────────────────────────────────────
    public const string MainWindow_DataMenu = nameof(MainWindow_DataMenu);
    public const string MainWindow_DataRefresh = nameof(MainWindow_DataRefresh);
    public const string MainWindow_DataReloadAssets = nameof(MainWindow_DataReloadAssets);
    public const string World_NotFound = nameof(World_NotFound);
    public const string World_Open = nameof(World_Open);
    public const string World_Close = nameof(World_Close);
    public const string World_Recent = nameof(World_Recent);
    public const string World_Menu = nameof(World_Menu);
    public const string World_EmptyTitle = nameof(World_EmptyTitle);
    public const string World_EmptyHint = nameof(World_EmptyHint);
    public const string World_ReopenLast = nameof(World_ReopenLast);
    public const string World_Settings = nameof(World_Settings);
    public const string World_Check = nameof(World_Check);
    public const string World_Untitled = nameof(World_Untitled);
    public const string World_NotAWorld = nameof(World_NotAWorld);   // "{Path}"
    public const string World_New = nameof(World_New);
    public const string NewWorld_Title = nameof(NewWorld_Title);
    public const string NewWorld_Header = nameof(NewWorld_Header);
    public const string NewWorld_Explanation = nameof(NewWorld_Explanation);
    public const string NewWorld_NameLabel = nameof(NewWorld_NameLabel);
    public const string NewWorld_ChooseFolder = nameof(NewWorld_ChooseFolder);
    public const string NewWorld_AlreadyThere = nameof(NewWorld_AlreadyThere);   // "{Path}"
    public const string NewWorld_InvalidName = nameof(NewWorld_InvalidName);     // "{Name}"
    public const string NewWorld_Failed = nameof(NewWorld_Failed);   // "{Reason}"
    public const string World_UntitledAt = nameof(World_UntitledAt);   // "{Folder}"
    public const string WorldCheck_Title = nameof(WorldCheck_Title);
    public const string WorldCheck_Intro = nameof(WorldCheck_Intro);
    public const string WorldCheck_Summary = nameof(WorldCheck_Summary);
    public const string WorldCheck_Clean = nameof(WorldCheck_Clean);
    public const string WorldCheck_CleanNote = nameof(WorldCheck_CleanNote);
    public const string WorldCheck_Go = nameof(WorldCheck_Go);
    public const string WorldCheck_WhereRecord = nameof(WorldCheck_WhereRecord);
    public const string WorldCheck_KindMap = nameof(WorldCheck_KindMap);
    public const string WorldCheck_KindItem = nameof(WorldCheck_KindItem);
    public const string WorldCheck_KindNpc = nameof(WorldCheck_KindNpc);
    public const string WorldCheck_KindShop = nameof(WorldCheck_KindShop);
    public const string WorldCheck_KindSpell = nameof(WorldCheck_KindSpell);
    public const string WorldCheck_KindQuest = nameof(WorldCheck_KindQuest);
    public const string WorldCheck_KindConversation = nameof(WorldCheck_KindConversation);
    public const string WorldCheck_KindClass = nameof(WorldCheck_KindClass);
    public const string WorldCheck_WarpMapMissing = nameof(WorldCheck_WarpMapMissing);
    public const string WorldCheck_BootMapMissing = nameof(WorldCheck_BootMapMissing);
    public const string WorldCheck_NpcMissing = nameof(WorldCheck_NpcMissing);
    public const string WorldCheck_ItemMissing = nameof(WorldCheck_ItemMissing);
    public const string WorldCheck_SpellMissing = nameof(WorldCheck_SpellMissing);
    public const string WorldCheck_QuestMissing = nameof(WorldCheck_QuestMissing);
    public const string WorldCheck_ClassMissing = nameof(WorldCheck_ClassMissing);
    public const string WorldCheck_ConversationNodeMissing = nameof(WorldCheck_ConversationNodeMissing);
    public const string WorldCheck_ShopHasNoKeeper = nameof(WorldCheck_ShopHasNoKeeper);
    public const string WorldCheck_ConversationOpensNoShop = nameof(WorldCheck_ConversationOpensNoShop);
    public const string WorldCheck_ConversationOpensNoQuests = nameof(WorldCheck_ConversationOpensNoQuests);
    public const string WorldCheck_QuestPrereqCycle = nameof(WorldCheck_QuestPrereqCycle);
    public const string WorldCheck_WhereTile = nameof(WorldCheck_WhereTile);
    public const string WorldCheck_LinkSizeMismatch = nameof(WorldCheck_LinkSizeMismatch);
    public const string WorldCheck_LinkNotReciprocal = nameof(WorldCheck_LinkNotReciprocal);
    public const string WorldCheck_LinkOutOfRange = nameof(WorldCheck_LinkOutOfRange);
    public const string WorldCheck_WarpTileOutside = nameof(WorldCheck_WarpTileOutside);
    public const string WorldCheck_BootTileOutside = nameof(WorldCheck_BootTileOutside);
    public const string WorldCheck_MapGroupMissing = nameof(WorldCheck_MapGroupMissing);
    public const string WorldCheck_SpawnPinOutside = nameof(WorldCheck_SpawnPinOutside);
    public const string WorldCheck_LightOutside = nameof(WorldCheck_LightOutside);
    public const string World_Download = nameof(World_Download);
    public const string World_Upload = nameof(World_Upload);
    public const string WorldSettings_DialogTitle = nameof(WorldSettings_DialogTitle);
    public const string WorldSettings_Intro = nameof(WorldSettings_Intro);
    public const string WorldSettings_OfflineOnlyNotice = nameof(WorldSettings_OfflineOnlyNotice);
    // The world's name and the size new maps start at, both stored in world.json beside the ceilings.
    public const string WorldSettings_NameLabel = nameof(WorldSettings_NameLabel);
    public const string WorldSettings_NameHint = nameof(WorldSettings_NameHint);
    public const string WorldSettings_DefaultMapSizeLabel = nameof(WorldSettings_DefaultMapSizeLabel);
    public const string WorldSettings_DefaultMapSizeHint = nameof(WorldSettings_DefaultMapSizeHint);
    public const string WorldSettings_MapSizeSoftCapWarning = nameof(WorldSettings_MapSizeSoftCapWarning);   // "{Cap}"
    public const string WorldTransfer_DownloadTitle = nameof(WorldTransfer_DownloadTitle);
    public const string WorldTransfer_UploadTitle = nameof(WorldTransfer_UploadTitle);
    public const string WorldTransfer_NeedsConnection = nameof(WorldTransfer_NeedsConnection);
    public const string WorldTransfer_PickDownloadFolder = nameof(WorldTransfer_PickDownloadFolder);
    public const string WorldTransfer_PickUploadFolder = nameof(WorldTransfer_PickUploadFolder);
    public const string WorldTransfer_TargetNotEmpty = nameof(WorldTransfer_TargetNotEmpty);
    public const string WorldTransfer_Reading = nameof(WorldTransfer_Reading);
    public const string WorldTransfer_ReadingMaps = nameof(WorldTransfer_ReadingMaps);
    public const string WorldTransfer_Writing = nameof(WorldTransfer_Writing);
    public const string WorldTransfer_WritingCount = nameof(WorldTransfer_WritingCount);
    public const string WorldTransfer_DownloadDone = nameof(WorldTransfer_DownloadDone);
    public const string WorldTransfer_Failed = nameof(WorldTransfer_Failed);
    public const string WorldTransfer_Comparing = nameof(WorldTransfer_Comparing);
    public const string WorldTransfer_NoChanges = nameof(WorldTransfer_NoChanges);
    public const string WorldTransfer_Summary = nameof(WorldTransfer_Summary);
    public const string WorldTransfer_BackupAdvice = nameof(WorldTransfer_BackupAdvice);
    public const string WorldTransfer_RemovalsWarning = nameof(WorldTransfer_RemovalsWarning);
    public const string WorldTransfer_IncludeRemovals = nameof(WorldTransfer_IncludeRemovals);
    public const string WorldTransfer_Apply = nameof(WorldTransfer_Apply);
    public const string WorldTransfer_Applying = nameof(WorldTransfer_Applying);
    public const string WorldTransfer_Applied = nameof(WorldTransfer_Applied);
    public const string WorldTransfer_KindAdded = nameof(WorldTransfer_KindAdded);
    public const string WorldTransfer_KindChanged = nameof(WorldTransfer_KindChanged);
    public const string WorldTransfer_KindRemoved = nameof(WorldTransfer_KindRemoved);
    public const string WorldTransfer_OverCeiling = nameof(WorldTransfer_OverCeiling);
    public const string WorldTransfer_Unnamed = nameof(WorldTransfer_Unnamed);
    public const string Refresh_FromDisk = nameof(Refresh_FromDisk);
    public const string Refresh_SectionMoved = nameof(Refresh_SectionMoved);
    public const string Refresh_Changed = nameof(Refresh_Changed);
    public const string Refresh_NothingMoved = nameof(Refresh_NothingMoved);
    public const string Refresh_SameCount = nameof(Refresh_SameCount);
    public const string Refresh_Skipped = nameof(Refresh_Skipped);

    // ── Status / Connection ───────────────────────────────────────────────────
    public const string Status_Offline = nameof(Status_Offline);
    public const string Status_Online = nameof(Status_Online);
    public const string Status_LoadingMaps = nameof(Status_LoadingMaps);
    public const string Status_LoadingMapsProgress = nameof(Status_LoadingMapsProgress);
    public const string Status_LoadingSection = nameof(Status_LoadingSection);
    public const string Status_FilterCount = nameof(Status_FilterCount);
}
