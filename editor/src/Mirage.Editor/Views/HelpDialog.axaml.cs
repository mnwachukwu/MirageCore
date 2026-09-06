using Avalonia.Controls;
using Avalonia.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Shared;

namespace Mirage.Editor.Views;

/// <summary>Modal reference sheet for the map editor's mouse and keyboard controls.</summary>
public partial class HelpDialog : Window
{
    // The sheet has no buttons, so there is no IsCancel button to carry Esc the way the other
    // dialogs do. Closes on Esc here instead.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            this.CloseDeferred();
            return;
        }
        base.OnKeyDown(e);
    }

    public HelpDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.HelpDialog_Title);
        _header.Text = EditorStrings.Get(EditorStrings.HelpDialog_Header);
        _controlsHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_ControlsHeader);
        _selectionHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_SelectionHeader);
        _layersHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayersHeader);
        _attributeHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_AttributeHeader);
        _worldHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_WorldHeader);
        _worldIntro.Text = EditorStrings.Get(EditorStrings.HelpDialog_WorldIntro);
        _worldWorldFolder.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_WorldFolder);
        _worldWorldFolderDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_WorldFolderDesc);
        _worldMapSlots.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_MapSlots);
        _worldMapSlotsDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_MapSlotsDesc);
        _worldMapSize.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_MapSize);
        _worldMapSizeDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_MapSizeDesc);
        _worldLinks.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_Links);
        _worldLinksDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_LinksDesc);
        _worldGroups.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_Groups);
        _worldGroupsDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_GroupsDesc);
        _worldProperties.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_Properties);
        _worldPropertiesDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_PropertiesDesc);
        _worldPlanes.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_Planes);
        _worldPlanesDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_World_PlanesDesc);

        _viewsHeader.Text = EditorStrings.Get(EditorStrings.HelpDialog_ViewsHeader);
        _viewsIntro.Text = EditorStrings.Get(EditorStrings.HelpDialog_ViewsIntro);
        _viewsWorldPreview.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_WorldPreview);
        _viewsWorldPreviewDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_WorldPreviewDesc);
        _viewsLayerVisibility.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_LayerVisibility);
        _viewsLayerVisibilityDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_LayerVisibilityDesc);
        _viewsConsole.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_Console);
        _viewsConsoleDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Views_ConsoleDesc);

        _ctrlLeftClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_LeftClick);
        _ctrlLeftClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_LeftClickDesc);
        _ctrlAltClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AltClick);
        _ctrlAltClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AltClickDesc);
        _ctrlCtrlDrag.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlDrag);
        _ctrlCtrlDragDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlDragDesc);
        _ctrlCtrlAltShiftClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlAltShiftClick);
        _ctrlCtrlAltShiftClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlAltShiftClickDesc);
        _ctrlRightClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_RightClick);
        _ctrlRightClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_RightClickDesc);
        _ctrlUndoRedo.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_UndoRedo);
        _ctrlUndoRedoDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_UndoRedoDesc);
        _ctrlBackForward.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_BackForward);
        _ctrlBackForwardDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_BackForwardDesc);
        _ctrlMouseBackForward.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_MouseBackForward);
        _ctrlMouseBackForwardDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_MouseBackForwardDesc);
        _ctrlShiftHold.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ShiftHold);
        _ctrlShiftHoldDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ShiftHoldDesc);
        _ctrlScrollWheel.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ScrollWheel);
        _ctrlScrollWheelDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ScrollWheelDesc);
        _ctrlCtrlScrollWheel.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlScrollWheel);
        _ctrlCtrlScrollWheelDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlScrollWheelDesc);
        _ctrlAltScrollWheel.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AltScrollWheel);
        _ctrlAltScrollWheelDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AltScrollWheelDesc);
        _ctrlCtrlAltScrollWheel.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlAltScrollWheel);
        _ctrlCtrlAltScrollWheelDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_CtrlAltScrollWheelDesc);
        _ctrlZoomButtons.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ZoomButtons);
        _ctrlZoomButtonsDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_ZoomButtonsDesc);
        _ctrlAnimPreview.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AnimPreview);
        _ctrlAnimPreviewDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_AnimPreviewDesc);
        _ctrlSaveMap.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_SaveMap);
        _ctrlSaveMapDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_SaveMapDesc);
        _ctrlDiscardMap.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_DiscardMap);
        _ctrlDiscardMapDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_DiscardMapDesc);
        _ctrlPaletteClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_PaletteClick);
        _ctrlPaletteClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Ctrl_PaletteClickDesc);

        _selectionIntro.Text = EditorStrings.Get(EditorStrings.HelpDialog_SelectionIntro);
        _selActionPlace.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionPlace);
        _selActionPlaceDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionPlaceDesc);
        _selActionSelection.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionSelection);
        _selActionSelectionDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionSelectionDesc);
        _selCtrlCSelection.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlCSelection);
        _selCtrlCSelectionDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlCSelectionDesc);
        _selActionDelete.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionDelete);
        _selActionDeleteDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ActionDeleteDesc);
        _selCtrlXSelection.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlXSelection);
        _selCtrlXSelectionDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlXSelectionDesc);
        _selModeKeys.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ModeKeys);
        _selModeKeysDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ModeKeysDesc);
        _selClickPlace.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ClickPlace);
        _selClickPlaceDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_ClickPlaceDesc);
        _selCtrlShiftClick.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlShiftClick);
        _selCtrlShiftClickDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_CtrlShiftClickDesc);
        _selEsc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_Esc);
        _selEscDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Sel_EscDesc);

        _layersIntro.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayersIntro);
        // Layer and attribute NAMES come from EditorVocabulary — English in every language, matching
        // the pickers and the map files. The headings keep a key only where they add translatable
        // wording around the name ("{Name} 1-5", "{Name} (Locked Door)").
        _layerGround.Text = EditorStrings.Format(EditorStrings.HelpDialog_LayerGround,
            ("Name", EditorVocabulary.NameOf(LayerType.Ground)));
        _layerGroundDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerGroundDesc);
        _layerFringe.Text = EditorStrings.Format(EditorStrings.HelpDialog_LayerFringe,
            ("Name", EditorVocabulary.NameOf(LayerType.Fringe)));
        _layerFringeDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerFringeDesc);
        _layerCanopy.Text = EditorStrings.Format(EditorStrings.HelpDialog_LayerCanopy,
            ("Name", EditorVocabulary.NameOf(LayerType.Canopy)));
        _layerCanopyDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerCanopyDesc);
        _layerAnimFlag.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerAnimFlag);
        _layerAnimFlagDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerAnimFlagDesc);
        _layerTileset.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerTileset);
        _layerTilesetDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_LayerTilesetDesc);

        _attributesIntro.Text = EditorStrings.Get(EditorStrings.HelpDialog_AttributesIntro);
        _attrBlocked.Text = EditorVocabulary.NameOf(AttributeTool.Blocked);
        _attrBlockedDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_BlockedDesc);
        _attrWarp.Text = EditorVocabulary.NameOf(AttributeTool.Warp);
        _attrWarpDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_WarpDesc);
        _attrWarpData.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_WarpData);
        _attrItem.Text = EditorVocabulary.NameOf(AttributeTool.Item);
        _attrItemDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_ItemDesc);
        _attrItemData.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_ItemData);
        _attrNpcAvoid.Text = EditorVocabulary.NameOf(AttributeTool.NpcAvoid);
        _attrNpcAvoidDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_NpcAvoidDesc);
        _attrKey.Text = EditorStrings.Format(EditorStrings.HelpDialog_Attr_Key,
            ("Name", EditorVocabulary.NameOf(AttributeTool.Key)));
        _attrKeyDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_KeyDesc);
        _attrKeyDoorDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_KeyDoorDesc);
        _attrKeyData.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_KeyData);
        _attrKeyOpen.Text = EditorStrings.Format(EditorStrings.HelpDialog_Attr_KeyOpen,
            ("Name", EditorVocabulary.NameOf(AttributeTool.KeyOpen)));
        _attrKeyOpenDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_KeyOpenDesc);
        _attrKeyOpenData.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_KeyOpenData);
        _attrNpcSpawn.Text = EditorVocabulary.NameOf(AttributeTool.NpcSpawn);
        _attrNpcSpawnDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_NpcSpawnDesc);
        _attrLayerRamp.Text = EditorStrings.Format(EditorStrings.HelpDialog_Attr_LayerRamp,
            ("Name", EditorVocabulary.NameOf(AttributeTool.LayerRamp)));
        _attrLayerRampDesc.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_LayerRampDesc);
        _attrLayerRampData.Text = EditorStrings.Get(EditorStrings.HelpDialog_Attr_LayerRampData);
    }
}
