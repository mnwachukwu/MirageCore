using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits NPC templates — stats, behavior, drops, size, and the live stat readout.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class NpcEditorView : LocalizedUserControl
{
    public NpcEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _refsHeader.Text = EditorStrings.Get(EditorStrings.References_Header);
        _noRefs.Text = EditorStrings.Get(EditorStrings.References_None);
        _behaviorFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.NpcEditor_AllBehaviorsFilter);
        _nameFilterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_FilterByName);

        _selectPromptText.Text = EditorStrings.Get(EditorStrings.NpcEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.NpcEditor_SectionTitle);

        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _saysLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SaysLabel);
        _spriteSheetLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpriteSheetLabel);
        _spriteLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpriteLabel);
        _sizeLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SizeLabel);
        _spawnSecsLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_SpawnSecsLabel);
        _behaviorLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_BehaviorLabel);
        _lightingHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightingHeader);
        _emitsLightCheck.Content = EditorStrings.Get(EditorStrings.NpcEditor_EmitsLightLabel);
        _lightColorLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightColorLabel);
        _lightRadiusLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightRadiusLabel);
        _lightIntensityLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightIntensityLabel);
        _lightFlickerLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_LightFlickerLabel);
        _groupLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_GroupLabel);
        _rangeLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_RangeLabel);
        // One label for the whole table now, plus the add-row button. The per-field labels the single
        // drop had (chance / item / quantity) are column positions in the table instead.
        _dropTableLabel.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropTableLabel);
        _dropItemHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropQuantityHeader);
        _dropChanceHeader.Text = EditorStrings.Get(EditorStrings.NpcEditor_DropChanceHeader);
        _addDropButton.Content = EditorStrings.Get(EditorStrings.NpcEditor_AddDrop);

        // The drop-item picker is per-ROW now, so its placeholder is bound through
        // NpcDropRowViewModel.ItemPlaceholder rather than set once on a single control here.

        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveNpcBtn.Content = EditorStrings.Get(EditorStrings.NpcEditor_SaveNpcButton);
        _saveAllBtn.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
    }

    /// <summary>Persist the panel layout as the view leaves the tree, so switching sections
    /// keeps the splitter position.</summary>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        SavePanelState();
        AppSettings.Current.Save();
    }

    /// <summary>Restore the saved splitter width once the visual tree exists.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.NpcEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.NpcEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.NpcEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.NpcEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
