using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits map groups, which supply inherited defaults to their member maps.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class MapGroupEditorView : LocalizedUserControl
{
    public MapGroupEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _displayNameLabel.Text = EditorStrings.Get(EditorStrings.Common_DisplayNameLabel);
        _territoryLabel.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_TerritoryLabel);
        _fallbackHeader.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_FallbackHeader);
        // Reuse the map editor's labels for the shared fallback fields (identical wording).
        _moralLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_MoralLabel);
        _musicLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_MusicLabel);
        _greetingSpeakerLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_GreetingSpeakerLabel);
        _joinSayLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_JoinSayLabel);
        _leaveSayLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_LeaveSayLabel);
        _respawnGroup.Header = EditorStrings.Get(EditorStrings.MapEditor_RespawnHeader);
        _greetingGroup.Header = EditorStrings.Get(EditorStrings.MapEditor_GreetingHeader);
        _bootMapLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootMapLabel);
        _bootXLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootXLabel);
        _bootYLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_BootYLabel);
        _indoorsLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_IndoorsLabel);
        _alwaysLitLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_AlwaysLitLabel);
        _alwaysDarkLabel.Text = EditorStrings.Get(EditorStrings.MapEditor_AlwaysDarkLabel);
        _triStateHint.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_TriStateHint);
        _searchBootMap.PlaceholderText = EditorStrings.Get(EditorStrings.MapEditor_SearchMapsPlaceholder);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.MapGroupEditor_SaveButton);
        _saveAllBtn.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
        _mapsHeader.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_MapsHeader);
        _noMaps.Text = EditorStrings.Get(EditorStrings.MapGroupEditor_NoMaps);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.MapGroupEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.MapGroupEditorRightWidth);
    }

    /// <summary>Save the splitter widths. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.MapGroupEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.MapGroupEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
