using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;

namespace Mirage.Editor.Views;

/// <summary>
/// Code-behind for the map editor. Handles what the view-model cannot reach: localized chrome,
/// global hotkeys, the animation-preview timer, the hover readout, and persisted panel sizes.
/// <para>The inline dialogs (warp, NPC pin, item spawn, light, key tile, tile animation) are
/// visibility-toggled Borders rather than modal windows, so keyboard handling here has to route
/// Esc through them explicitly — see <see cref="OnGlobalKeyDown"/>.</para>
/// </summary>
public partial class MapEditorView : LocalizedUserControl
{
    // ── Access-key mode is off while the map editor is showing ────────────────
    // A bare Alt press puts the window into access-key mode, and that mode swallows pointer input:
    // the tile cursor stops following the mouse and re-entering the grid gives no preview, while
    // clicks still land. This grid uses Alt only as a POINTER modifier — Alt+Click retains values,
    // Alt+Wheel steps the tileset, Ctrl+Alt+Wheel the layer — and those read KeyModifiers off the
    // pointer event, not the key event. So the key itself is swallowed and all three still work.
    //
    // Tunnelled on the TopLevel so it is seen before the access-key handler, and hooked to this
    // view's lifetime so every other section keeps its normal Alt behavior.
    private static void SwallowAltKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt) e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.AddHandler(KeyDownEvent, SwallowAltKey, RoutingStrategies.Tunnel);
            top.AddHandler(KeyUpEvent, SwallowAltKey, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.RemoveHandler(KeyDownEvent, SwallowAltKey);
            top.RemoveHandler(KeyUpEvent, SwallowAltKey);
        }
        base.OnDetachedFromVisualTree(e);
    }

    // ── Hover panel state ─────────────────────────────────────────────────────
    private bool _shiftDown;
    private bool _ctrlDown;
    private bool _hoveredValid;

    // ── Animation preview timer ───────────────────────────────────────────────
    private DispatcherTimer? _animTimer;

    // The view-model and grid this view is currently answering for. Held so the named handlers can be
    // removed on detach and so they never touch a grid belonging to a view that has been replaced.
    private MapEditorViewModel? _wiredVm;
    private TileGridControl? _wiredGrid;

    // Snapshotted properties-panel scroll offset captured the moment before the
    // SelectedMap changes, so that BringIntoView calls triggered by the cascade
    // of binding updates don't yank the panel around.
    private Vector? _savedPropertiesOffset;

    public MapEditorView()
    {
        InitializeComponent();
        ApplyStrings();
        Loaded += OnLoaded;
    }

    /// <summary>Push the current language's strings into every caption, tooltip, and placeholder in
    /// this view. Re-run on a language change; these are set in code rather than bound.</summary>
    protected override void ApplyStrings()
    {
        // Left panel
        _mapsFilterBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);

        // Center toolbar
        ToolTip.SetTip(_animPreviewToggle, EditorStrings.Get(EditorStrings.MapEditor_AnimPreviewTooltip));
        ToolTip.SetTip(_doorPreviewToggle, EditorStrings.Get(EditorStrings.MapEditor_DoorPreviewTooltip));
        ToolTip.SetTip(_nightPreviewToggle, EditorStrings.Get(EditorStrings.MapEditor_NightPreviewTooltip));
        _btnZoomOut.Content = EditorStrings.Get(EditorStrings.MapEditor_ZoomOutButton);
        ToolTip.SetTip(_btnZoomOut, EditorStrings.Get(EditorStrings.MapEditor_ZoomOutTooltip));
        _btnZoomIn.Content = EditorStrings.Get(EditorStrings.MapEditor_ZoomInButton);
        ToolTip.SetTip(_btnZoomIn, EditorStrings.Get(EditorStrings.MapEditor_ZoomInTooltip));
        _btnZoomReset.Content = EditorStrings.Get(EditorStrings.MapEditor_ResetZoomButton);
        ToolTip.SetTip(_btnZoomReset, EditorStrings.Get(EditorStrings.MapEditor_ResetZoomTooltip));

        // Tools panel — Mode / Action / Layer / Attribute / Brush
        _modeHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_ModeHeader);
        _modeTile.Content = EditorStrings.Get(EditorStrings.MapEditor_ModeTile);
        _modeAttribute.Content = EditorStrings.Get(EditorStrings.MapEditor_ModeAttribute);
        _modeLight.Content = EditorStrings.Get(EditorStrings.MapEditor_ModeLight);
        _actionHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_ActionHeader);
        _actionPlace.Content = EditorStrings.Get(EditorStrings.MapEditor_ActionPlace);
        _actionSelect.Content = EditorStrings.Get(EditorStrings.MapEditor_ActionSelect);
        _actionDelete.Content = EditorStrings.Get(EditorStrings.MapEditor_ActionDelete);
        _layerHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_LayerHeader);
        _animLayerCheck.Content = EditorStrings.Get(EditorStrings.MapEditor_AnimLayer);
        ToolTip.SetTip(_animLayerCheck, EditorStrings.Get(EditorStrings.MapEditor_AnimLayerTooltip));
        _btnFill.Content = EditorStrings.Get(EditorStrings.MapEditor_FillButton);
        ToolTip.SetTip(_btnFill, EditorStrings.Get(EditorStrings.MapEditor_FillTooltip));
        _btnClearLayer.Content = EditorStrings.Get(EditorStrings.MapEditor_ClearLayerButton);
        ToolTip.SetTip(_btnClearLayer, EditorStrings.Get(EditorStrings.MapEditor_ClearLayerTooltip));
        _attrLayerHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_LayerHeader);
        _attributeHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_AttributeHeader);
        _btnClearAttr.Content = EditorStrings.Get(EditorStrings.MapEditor_ClearAttrButton);
        ToolTip.SetTip(_btnClearAttr, EditorStrings.Get(EditorStrings.MapEditor_ClearAttrTooltip));
        _altClickHint.Text = EditorStrings.Get(EditorStrings.MapEditor_AltClickHint);
        _lightHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_LightHeader);
        _lightModeHint.Text = EditorStrings.Get(EditorStrings.MapEditor_LightModeHint);
        _btnClearLights.Content = EditorStrings.Get(EditorStrings.MapEditor_ClearLightsButton);
        ToolTip.SetTip(_btnClearLights, EditorStrings.Get(EditorStrings.MapEditor_ClearLightsTooltip));
        _brushSizeHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_BrushSizeHeader);
        _brushWLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BrushW);
        _brushHLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BrushH);
        _tilesetHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_TilesetHeader);
        _searchTileset.PlaceholderText = EditorStrings.Get(EditorStrings.MapEditor_TilesetSearchPlaceholder);
        _paletteHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_PaletteHeader);

        // Properties panel
        _propertiesHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_PropertiesHeader);
        _selectMapPrompt.Text = EditorStrings.Get(EditorStrings.MapEditor_SelectMapPrompt);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _displayNameLabel.Text = EditorStrings.Get(EditorStrings.Common_DisplayNameLabel);
        _moralLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_MoralLabel);
        _mapLinksGroup.Header = EditorStrings.Get(EditorStrings.MapEditor_MapLinksHeader);
        _respawnGroup.Header = EditorStrings.Get(EditorStrings.MapEditor_RespawnHeader);
        _greetingGroup.Header = EditorStrings.Get(EditorStrings.MapEditor_GreetingHeader);
        _upLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_UpLabel);
        _downLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_DownLabel);
        _leftLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_LeftLabel);
        _rightLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_RightLabel);
        _musicLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_MusicLabel);
        _bootMapLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootMapLabel);
        _bootXLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootXLabel);
        _bootYLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootYLabel);
        _greetingSpeakerLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_GreetingSpeakerLabel);
        _joinSayLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_JoinSayLabel);
        _leaveSayLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_LeaveSayLabel);
        _mapGroupLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_MapGroupLabel);
        _indoorsLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_IndoorsLabel);
        _alwaysLitLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_AlwaysLitLabel);
        _alwaysDarkLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_AlwaysDarkLabel);
        _inheritHint.Text = EditorStrings.Get(EditorStrings.MapEditor_InheritHint);
        _sizeLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_SizeLabel);
        _resizeBtn.Content = EditorStrings.Get(EditorStrings.MapEditor_ResizeButton);
        _npcSlotsLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_NpcSlotsLabel);
        _noNpcsHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addNpcRowBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _usedTilesheetsLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_UsedTilesheets);

        // Search-box placeholders
        string mapPh = EditorStrings.Get(EditorStrings.MapEditor_SearchMapsPlaceholder);
        string itemPh = EditorStrings.Get(EditorStrings.MapEditor_SearchItemsPlaceholder);
        _searchUpMap.PlaceholderText = mapPh;
        _searchDown.PlaceholderText = mapPh;
        _searchLeft.PlaceholderText = mapPh;
        _searchRight.PlaceholderText = mapPh;
        _searchBootMap.PlaceholderText = mapPh;
        _searchMapGroup.PlaceholderText = EditorStrings.Get(EditorStrings.MapEditor_SearchMapGroupsPlaceholder);

        // Every picker's clear button says the same thing on hover.
        string clear = EditorStrings.Get(EditorStrings.MapEditor_ClearTooltip);
        foreach (var btn in new[] { _clearUp, _clearDown, _clearLeft, _clearRight, _clearBootMap, _clearMapGroup })
            ToolTip.SetTip(btn, clear);

        // Footer action buttons
        _btnUndo.Content = EditorStrings.Get(EditorStrings.MapEditor_UndoButton);
        ToolTip.SetTip(_btnUndo, EditorStrings.Get(EditorStrings.MapEditor_UndoTooltip));
        _btnRedo.Content = EditorStrings.Get(EditorStrings.MapEditor_RedoButton);
        ToolTip.SetTip(_btnRedo, EditorStrings.Get(EditorStrings.MapEditor_RedoTooltip));
        _btnBack.Content = EditorStrings.Get(EditorStrings.MapEditor_BackButton);
        _btnForward.Content = EditorStrings.Get(EditorStrings.MapEditor_ForwardButton);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _btnDiscardMap.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _btnDiscardAll.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _btnSaveMap.Content = EditorStrings.Get(EditorStrings.MapEditor_SaveMapButton);
        _btnSaveAll.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);

        // Hover preview
        _tilePreviewHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_TilePreviewHeader);
        // Layer names are vocabulary, not translated prose — same word as the layer picker.
        _groundColumnHeader.Text = EditorVocabulary.NameOf(LayerType.Ground);
        _fringeColumnHeader.Text = EditorVocabulary.NameOf(LayerType.Fringe);
        _canopyColumnHeader.Text = EditorVocabulary.NameOf(LayerType.Canopy);

        // Warp dialog
        _warpTitle.Text = EditorStrings.Format(EditorStrings.WarpDialog_Title,
            ("Name", EditorVocabulary.NameOf(AttributeTool.Warp)));
        _warpMapLabel.Text = EditorStrings.Get(EditorStrings.WarpDialog_MapLabel);
        _warpXLabel.Text = EditorStrings.Get(EditorStrings.WarpDialog_XLabel);
        _warpYLabel.Text = EditorStrings.Get(EditorStrings.WarpDialog_YLabel);
        _warpLayerLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_LayerHeader);   // reuse the "Layer" string
        _searchWarpMap.PlaceholderText = mapPh;
        _warpFillRunCheck.Content = EditorStrings.Get(EditorStrings.Common_FillConnectedRun);
        ToolTip.SetTip(_warpFillRunCheck, EditorStrings.Get(EditorStrings.Common_FillConnectedRunTooltip));
        _warpRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _warpCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _warpConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // NPC-spawn pin dialog
        _npcSpawnTitle.Text = EditorStrings.Format(EditorStrings.NpcSpawnDialog_Title,
            ("Name", EditorVocabulary.NameOf(AttributeTool.NpcSpawn)));
        _npcSpawnSlotLabel.Text = EditorStrings.Get(EditorStrings.NpcSpawnDialog_SlotLabel);
        _npcSpawnCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _npcSpawnConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // Item Spawn dialog
        // The whole title is the attribute's name, so there is nothing left to translate around it.
        _itemTitle.Text = EditorVocabulary.NameOf(AttributeTool.Item);
        _itemItemLabel.Text = EditorStrings.Get(EditorStrings.ItemSpawnDialog_ItemLabel);
        _itemQuantityLabel.Text = EditorStrings.Get(EditorStrings.ItemSpawnDialog_ValueLabel);
        _itemRespawnLabel.Text = EditorStrings.Get(EditorStrings.ItemSpawnDialog_RespawnLabel);
        string respawnTooltip = EditorStrings.Get(EditorStrings.ItemSpawnDialog_RespawnTooltip);
        ToolTip.SetTip(_itemRespawnLabel, respawnTooltip);
        ToolTip.SetTip(_itemRespawnInput, respawnTooltip);
        _searchItemSpawn.PlaceholderText = itemPh;
        _itemFillRunCheck.Content = EditorStrings.Get(EditorStrings.Common_FillConnectedRun);
        ToolTip.SetTip(_itemFillRunCheck, EditorStrings.Get(EditorStrings.Common_FillConnectedRunTooltip));
        _itemRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _itemCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _itemConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // Light source dialog
        _lightTitle.Text = EditorStrings.Get(EditorStrings.LightDialog_Title);
        _lightColorLabel.Text = EditorStrings.Get(EditorStrings.LightDialog_ColorLabel);
        _lightRadiusLabel.Text = EditorStrings.Get(EditorStrings.LightDialog_RadiusLabel);
        _lightIntensityLabel.Text = EditorStrings.Get(EditorStrings.LightDialog_IntensityLabel);
        _lightFlickerLabel.Text = EditorStrings.Get(EditorStrings.LightDialog_FlickerLabel);
        _lightRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _lightCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _lightConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // Key Tile dialog
        _hiddenLayerTitle.Text = EditorStrings.Get(EditorStrings.MapEditor_HiddenLayerTitle);
        _hiddenLayerCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _hiddenLayerConfirm.Content = EditorStrings.Get(EditorStrings.MapEditor_HiddenLayerShow);
        _blockedTitle.Text = EditorStrings.Get(EditorStrings.BlockedTileDialog_Title);
        _blockedDesc.Text = EditorStrings.Get(EditorStrings.BlockedTileDialog_Description);
        _blockedLightCheck.Content = EditorStrings.Get(EditorStrings.BlockedTileDialog_BlocksLight);
        ToolTip.SetTip(_blockedLightCheck, EditorStrings.Get(EditorStrings.BlockedTileDialog_BlocksLightTooltip));
        _blockedSightCheck.Content = EditorStrings.Get(EditorStrings.BlockedTileDialog_BlocksSight);
        ToolTip.SetTip(_blockedSightCheck, EditorStrings.Get(EditorStrings.BlockedTileDialog_BlocksSightTooltip));
        _blockedFillRunCheck.Content = EditorStrings.Get(EditorStrings.Common_FillConnectedRun);
        ToolTip.SetTip(_blockedFillRunCheck, EditorStrings.Get(EditorStrings.Common_FillConnectedRunTooltip));
        _blockedRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _blockedCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _blockedConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        _keyTitle.Text = EditorStrings.Get(EditorStrings.KeyTileDialog_Title);
        _keyDesc.Text = EditorStrings.Get(EditorStrings.KeyTileDialog_Description);
        _keyItemLabel.Text = EditorStrings.Get(EditorStrings.KeyTileDialog_KeyItemLabel);
        ToolTip.SetTip(_keyItemLabel, EditorStrings.Get(EditorStrings.KeyTileDialog_KeyItemTooltip));
        _searchKeyItem.PlaceholderText = itemPh;
        _keyTakeCheck.Content = EditorStrings.Get(EditorStrings.KeyTileDialog_TakeKeyCheckbox);
        ToolTip.SetTip(_keyTakeCheck, EditorStrings.Get(EditorStrings.KeyTileDialog_TakeKeyTooltip));
        _keyFillRunCheck.Content = EditorStrings.Get(EditorStrings.Common_FillConnectedRun);
        ToolTip.SetTip(_keyFillRunCheck, EditorStrings.Get(EditorStrings.Common_FillConnectedRunTooltip));
        _keyRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _keyCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _keyConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // KeyOpen Trigger dialog
        _keyOpenTitle.Text = EditorStrings.Format(EditorStrings.KeyOpenDialog_Title,
            ("Name", EditorVocabulary.NameOf(AttributeTool.KeyOpen)));
        _keyOpenDesc.Text = EditorStrings.Get(EditorStrings.KeyOpenDialog_Description);
        _keyOpenXLabel.Text = EditorStrings.Get(EditorStrings.KeyOpenDialog_XLabel);
        string keyOpenXTooltip = EditorStrings.Get(EditorStrings.KeyOpenDialog_XTooltip);
        ToolTip.SetTip(_keyOpenXLabel, keyOpenXTooltip);
        ToolTip.SetTip(_keyOpenXInput, keyOpenXTooltip);
        _keyOpenYLabel.Text = EditorStrings.Get(EditorStrings.KeyOpenDialog_YLabel);
        string keyOpenYTooltip = EditorStrings.Get(EditorStrings.KeyOpenDialog_YTooltip);
        ToolTip.SetTip(_keyOpenYLabel, keyOpenYTooltip);
        ToolTip.SetTip(_keyOpenYInput, keyOpenYTooltip);
        _keyOpenLayerLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_LayerHeader);   // reuse the "Layer" string
        _keyOpenFillRunCheck.Content = EditorStrings.Get(EditorStrings.Common_FillConnectedRun);
        ToolTip.SetTip(_keyOpenFillRunCheck, EditorStrings.Get(EditorStrings.Common_FillConnectedRunTooltip));
        _keyOpenRetainCheck.Content = EditorStrings.Get(EditorStrings.Common_RetainOnAltClick);
        _keyOpenCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _keyOpenConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);

        // Tile-animation dialog
        _animTitle.Text = EditorStrings.Get(EditorStrings.MapEditor_AnimDialogTitle);
        _animLayerColHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_LayerHeader);
        _animAnimColHeader.Text = EditorStrings.Get(EditorStrings.MapEditor_AnimLayer);
        _animGroundStyleLabel.Text = EditorVocabulary.NameOf(LayerType.Ground);
        _animFringeStyleLabel.Text = EditorVocabulary.NameOf(LayerType.Fringe);
        _animCancel.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _animConfirm.Content = EditorStrings.Get(EditorStrings.Common_Confirm);
    }

    /// <summary>Subscribe the window-level key handlers once the view joins a window, so map hotkeys
    /// work regardless of which child control has focus.</summary>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (e.Root is TopLevel root)
        {
            root.AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
            root.AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
        }
    }

    /// <summary>Detach the window-level key handlers and stop the preview timer. Must mirror
    /// <see cref="OnAttachedToLogicalTree"/> exactly, or the handlers outlive the view.</summary>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        if (e.Root is TopLevel root)
        {
            root.RemoveHandler(KeyDownEvent, OnGlobalKeyDown);
            root.RemoveHandler(KeyUpEvent, OnGlobalKeyUp);
        }
        if (_wiredVm is { } vm)
        {
            vm.PropertyChanging -= OnViewModelPropertyChanging;
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            _wiredVm = null;
        }
        _wiredGrid = null;
        _animTimer?.Stop();
        _animTimer = null;

        SavePanelState();
        AppSettings.Current.Save();
    }

    /// <summary>Persist the splitter/panel sizes. Called by the window on close rather than from a
    /// Unloaded handler, so a layout tweak made right before quitting is still captured.</summary>
    internal void SavePanelState()
    {
        var settings = AppSettings.Current;
        if (LeftPanel.Bounds.Width > 0)
            settings.MapEditorLeftWidth = LeftPanel.Bounds.Width;
        if (RightPanel.Bounds.Width > 0)
            settings.MapEditorRightWidth = RightPanel.Bounds.Width;
        var bottomH = RightPanel.RowDefinitions[2].ActualHeight;
        if (bottomH > 0)
            settings.MapEditorRightBottomHeight = bottomH;
    }

    /// <summary>Window-level key handling: modifier tracking for the hover readout, the Esc cascade
    /// through open inline dialogs, and the map-editing hotkeys. Ordering inside matters and is
    /// commented at each step.</summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm) return;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // Track Ctrl/Shift state for the HoverPanel gate.  Routed above the hotkey
        // dispatch so that the panel reacts even when a hotkey is also handled.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _ctrlDown = true;
            RefreshHoverPanel();
        }
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _shiftDown = true;
            RefreshHoverPanel();
        }

        // Esc: cascade through whichever overlay is currently visible.  Dialogs
        // are inline IsVisible Borders, not modal windows, so they don't eat keys.
        // Kept above the text-field guard so Esc still cancels an open dialog while a
        // field inside it (e.g. the warp X/Y boxes) has focus.
        if (e.Key == Key.Escape)
        {
            if (vm.ShowWarpDialog)
            {
                vm.CancelWarpCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.ShowNpcSpawnDialog)
            {
                vm.CancelNpcSpawnCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.ShowItemDialog)
            {
                vm.CancelItemCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.ShowKeyDialog)
            {
                vm.CancelKeyCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.ShowKeyOpenDialog)
            {
                vm.CancelKeyOpenCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.ShowAnimDialog)
            {
                vm.CancelAnimCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.IsPlacingNpc)
            {
                vm.CancelPlaceNpcCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (vm.IsSelectAction && vm.SelectionRect is not null)
            {
                vm.SelectionRect = null;
                e.Handled = true;
                return;
            }
            if (vm.IsPlaceAction && vm.ClipboardKind != ClipboardKind.None)
            {
                vm.ClearClipboard();
                e.Handled = true;
                return;
            }
        }

        // Let a focused text field own clipboard/undo shortcuts (Ctrl+C/X/V/Z/Y).  This is a
        // window-level Tunnel handler, so without this guard it would consume those keys before the
        // TextBox — including the inner box of a NumericUpDown / AutoCompleteBox — ever sees them.
        // Under tunnel routing e.Source is the focused element.
        if (e.Source is TextBox) return;

        if (ctrl && !shift && e.Key == Key.Z && vm.UndoCommand.CanExecute(null))
        {
            vm.UndoCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.Y || (shift && e.Key == Key.Z)) && vm.RedoCommand.CanExecute(null))
        {
            vm.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl+1/2/3 switch MODE (Tile / Attribute / Light); Ctrl+Shift+1/2/3 switch ACTION (Place / Select /
        // Delete). Each reads left-to-right. Placed before the clipboard keys so the digits are unambiguous.
        if (ctrl && e.Key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2 or Key.D3 or Key.NumPad3)
        {
            int n = e.Key is Key.D1 or Key.NumPad1 ? 1 : e.Key is Key.D2 or Key.NumPad2 ? 2 : 3;
            if (shift)
                vm.SelectedAction = n switch { 1 => EditorAction.Place, 2 => EditorAction.Select, _ => EditorAction.Delete };
            else
                vm.SelectedMode = n switch { 1 => EditorMode.Tile, 2 => EditorMode.Attribute, _ => EditorMode.Light };
            e.Handled = true;
            return;
        }

        // Ctrl+C copies the current selection; Ctrl+X cuts it (Select action). Action switching lives on
        // Ctrl+Shift+1/2/3 above; there is no Ctrl+V paste.
        if (ctrl && e.Key == Key.C)
        {
            if (vm.SelectionRect is not null) vm.CopySelection();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.X)
        {
            if (vm.IsSelectAction && vm.SelectionRect is not null) vm.CutSelection();
            e.Handled = true;
            return;
        }
    }

    /// <summary>Clears the tracked Ctrl/Shift state so the hover readout stops showing the
    /// modifier-gated detail once the key is released.</summary>
    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _ctrlDown = false;
            RefreshHoverPanel();
        }
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _shiftDown = false;
            RefreshHoverPanel();
        }
    }

    // Show the hover panel iff Shift is held, Ctrl is NOT, AND a valid tile is hovered.
    // Ctrl gating gives Ctrl-modified gestures (pan, paste-with-retain) an unobstructed view.
    /// <summary>Re-evaluate the hover readout after a modifier or hovered-tile change.</summary>
    private void RefreshHoverPanel()
    {
        HoverPanel.IsVisible = _shiftDown && !_ctrlDown && _hoveredValid;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(settings.MapEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(settings.MapEditorRightWidth);
        RightPanel.RowDefinitions[2].Height = new GridLength(settings.MapEditorRightBottomHeight);

        if (DataContext is not MapEditorViewModel vm) return;

        var grid = this.FindControl<TileGridControl>("TileGrid");
        if (grid is null) return;

        grid.TileClicked += c => vm.TileClickedCommand.Execute(c);
        grid.AnimEditRequested += c => vm.AnimEditCommand.Execute((c.X, c.Y));
        grid.NeighborMapClicked += cell => vm.NeighborMapClickedCommand.Execute(cell);
        grid.WarpDestinationClicked += warp => vm.WarpDestinationClickedCommand.Execute(warp);
        grid.NavigateBackRequested += () => { if (vm.NavigateBackCommand.CanExecute(null)) vm.NavigateBackCommand.Execute(null); };
        grid.NavigateForwardRequested += () => { if (vm.NavigateForwardCommand.CanExecute(null)) vm.NavigateForwardCommand.Execute(null); };
        grid.PanRequested += delta => MapScrollViewer.Offset -= delta;
        grid.TileRightClicked += coords => vm.TileRightClickedCommand.Execute(coords);
        grid.TileDeleteRequested += coords => vm.DeleteAtCommand.Execute(coords);
        grid.SelectionChanged += s => vm.SelectionPhase(s.X1, s.Y1, s.X2, s.Y2, s.Phase);
        grid.DragBegan += () => vm.BeginBatch();
        grid.DragEnded += () => vm.CommitBatch();
        grid.ZoomRequested += newZoom => vm.MapZoom = newZoom;
        grid.NpcSizeLookup = vm.NpcSize;   // size-aware spawn-pin footprint overlay

        // MODE 2 transient placement: the grid draws a live footprint brush and
        // routes place/cancel gestures here; validity colors the brush green/red.
        grid.NpcPlacementValidAt = vm.CanPlacePlacingNpcAt;
        grid.NpcPlacementClicked += c => vm.PlaceNpcAtHover(c.X, c.Y);
        grid.NpcPlacementCancelRequested += () => vm.CancelPlaceNpcCommand.Execute(null);

        grid.HoverChanged += (x, y) =>
        {
            vm.HoveredX = x;
            vm.HoveredY = y;
            _hoveredValid = x >= 0 && vm.SelectedMap is not null;
            RefreshHoverPanel();
        };

        grid.PointerMoved += (_, pe) => UpdateHoverPosition(pe.GetPosition(HoverCanvas));

        // Hide the panel when the mouse leaves the grid entirely. The position decides, not the event:
        // access-key mode raises a synthetic exit with the pointer still over the grid.
        grid.PointerExited += (_, pe) =>
        {
            if (new Rect(grid.Bounds.Size).Contains(pe.GetPosition(grid))) return;
            _hoveredValid = false;
            RefreshHoverPanel();
        };

        vm.InvalidateTileGrid = (x, y) => grid.InvalidateTileAt(x, y);
        vm.InvalidateAllTiles = () => grid.InvalidateMapRender();

        // A cached-render failure degrades the map to blank until the next edit rebuilds it. Say so, with the
        // reason, so a blank grid reads as a reported fault instead of a hang.
        grid.TileCacheRenderFailed += ex =>
        {
            EditorLog.Error(ex, "The cached map render failed; the grid is blank until the next edit rebuilds it.");
            vm.StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_TileRenderFailed, ("Error", ex.Message));
        };

        // Every box that captions a record reference: an empty selection must not keep text typed against
        // whichever record was loaded before.
        vm.MapPropertiesRefreshed = () =>
        {
            _searchUpMap.ResyncTextToSelection();
            _searchDown.ResyncTextToSelection();
            _searchLeft.ResyncTextToSelection();
            _searchRight.ResyncTextToSelection();
            _searchBootMap.ResyncTextToSelection();
            _searchMapGroup.ResyncTextToSelection();
        };

        // The section host builds a fresh view on every switch while the view-model lives on. Named
        // handlers so OnDetachedFromLogicalTree can take them off again; anonymous ones would accumulate
        // on the view-model, one per switch.
        _wiredVm = vm;
        _wiredGrid = grid;
        vm.PropertyChanging += OnViewModelPropertyChanging;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Each preview is pushed to the grid outright, not left to the property notification. The
        // view-model carries the persisted values already, so an assignment equal to what is there raises
        // nothing, and the toggle would read ON over a grid doing nothing.
        vm.IsAnimPreview = settings.MapEditorAnimPreview;
        ApplyAnimPreview(vm.IsAnimPreview, grid);
        grid.SetDoorPreview(vm.IsDoorPreview);
        grid.SetNightPreview(vm.IsNightPreview);
    }

    private void OnViewModelPropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs pe)
    {
        if (pe.PropertyName == nameof(MapEditorViewModel.SelectedMap))
            _savedPropertiesOffset = PropertiesScrollViewer.Offset;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs pe)
    {
        if (_wiredVm is not { } vm || _wiredGrid is not { } grid) return;

        if (pe.PropertyName == nameof(MapEditorViewModel.IsAnimPreview))
        {
            ApplyAnimPreview(vm.IsAnimPreview, grid);
            AppSettings.Current.MapEditorAnimPreview = vm.IsAnimPreview;   // persisted on unload Save()
        }
        if (pe.PropertyName == nameof(MapEditorViewModel.IsDoorPreview))
            grid.SetDoorPreview(vm.IsDoorPreview);
        if (pe.PropertyName == nameof(MapEditorViewModel.IsNightPreview))
            grid.SetNightPreview(vm.IsNightPreview);
        if (pe.PropertyName == nameof(MapEditorViewModel.SelectedMap) && vm.SelectedMap is not null)
        {
            ScrollToActiveMap(vm.MapZoom);
            if (_savedPropertiesOffset is { } saved)
            {
                // Restore after layout/binding settles so BringIntoView calls
                // dispatched during property propagation are overridden.
                Dispatcher.UIThread.Post(
                    () => PropertiesScrollViewer.Offset = saved,
                    DispatcherPriority.Loaded);
                _savedPropertiesOffset = null;
            }
        }
    }

    // ── Animation preview ─────────────────────────────────────────────────────

    /// <summary>Start or stop the tile-animation preview timer and reset the grid to frame 0 when it
    /// stops, so toggling the preview off leaves a predictable still image.</summary>
    private void ApplyAnimPreview(bool on, TileGridControl grid)
    {
        _animTimer?.Stop();
        _animTimer = null;
        grid.SetAnimPreview(on);
        if (!on) return;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.MapAnimIntervalMs) };
        _animTimer.Tick += (_, _) => grid.TickAnimFrame();
        _animTimer.Start();
    }

    // ── Scroll to active map ──────────────────────────────────────────────────

    /// <summary>Center the canvas on the selected map at the given zoom — used after a map switch or a
    /// zoom change so the active map stays in view.</summary>
    private void ScrollToActiveMap(double zoom)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // The center cell starts one map in on each axis, so the offset is the open map's own size.
            var grid = this.FindControl<TileGridControl>("TileGrid");
            if (grid is null) return;
            MapScrollViewer.Offset = new Vector(
                grid.OffsetCol * TileGridControl.TileW * zoom,
                grid.OffsetRow * TileGridControl.TileH * zoom);
        }, DispatcherPriority.Loaded);
    }

    // ── Hover panel position (cursor-following) ───────────────────────────────

    /// <summary>Move the hover readout to follow the pointer, keeping it inside the view's bounds.</summary>
    private void UpdateHoverPosition(Point pos)
    {
        const double offsetX = 14;
        const double offsetY = 14;
        const double panelW = 230;
        const double panelH = 250;

        double left = pos.X + offsetX;
        double top = pos.Y + offsetY;

        double canvasW = HoverCanvas.Bounds.Width;
        if (canvasW > 0 && left + panelW > canvasW) left = pos.X - offsetX - panelW;

        double canvasH = HoverCanvas.Bounds.Height;
        if (canvasH > 0 && top + panelH > canvasH) top = pos.Y - offsetY - panelH;

        Canvas.SetLeft(HoverPanel, Math.Max(0, left));
        Canvas.SetTop(HoverPanel, Math.Max(0, top));
    }
}
