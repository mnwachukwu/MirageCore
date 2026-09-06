using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits spell definitions and their live MP / reagent cost preview.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class SpellEditorView : LocalizedUserControl
{
    public SpellEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _refsHeader.Text = EditorStrings.Get(EditorStrings.References_Header);
        _noRefs.Text = EditorStrings.Get(EditorStrings.References_None);
        _typeFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.SpellEditor_AllSpellTypesFilter);
        _classReqFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.SpellEditor_AllClassesFilter);
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_FilterByName);
        _giveItemPicker.PlaceholderText = EditorStrings.Get(EditorStrings.SpellEditor_GiveItemSearchPlaceholder);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.SpellEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.SpellEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _typeLabel.Text = EditorStrings.Get(EditorStrings.Common_TypeLabel);
        _allowedClassesLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_AllowedClasses);
        _levelReqLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_LevelReq);
        _maxMpCostLabel.Text = EditorStrings.Get(EditorStrings.SpellEditor_MaxMpCostLabel);
        _reagentCostLabel.Text = EditorStrings.Get(EditorStrings.SpellEditor_ReagentCostLabel);
        _reagentChanceLabel.Text = EditorStrings.Get(EditorStrings.SpellEditor_ReagentChanceLabel);
        _mpCostNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_MpCostNote);
        _notesExpander.Header = EditorStrings.Get(EditorStrings.Common_Notes);
        _giveItemLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_ItemNumber);
        _itemQuantityLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_Quantity);
        _intReqLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_IntReq);
        _fmtMagnitudeIntro.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeIntro);
        _fmtMagnitudeBullet1.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeBullet1);
        _fmtMagnitudeBullet2.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeBullet2);
        _fmtMagnitudeBullet3.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeBullet3);
        _fmtClassIntNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_ClassIntNote);
        _fmtPlayerIntNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_PlayerIntNote);
        _fmtMagnitudeHeader.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeHeader);
        _fmtMagnitudeRaw.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeRaw);
        _fmtMagnitudeContribution.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeContribution);
        _fmtMagnitudeActualHit.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeActualHit);
        _fmtMagnitudeMitNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MagnitudeMitNote);
        _fmtMpCostHeader.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MpCostHeader);
        _fmtMpCostFormula.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MpCostFormula);
        _fmtMpCostNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MpCostNote);
        _fmtGiveItemHeader.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_GiveItemHeader);
        _fmtGiveItemBullet1.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_GiveItemBullet1);
        _fmtGiveItemBullet2.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_GiveItemBullet2);
        _fmtGiveItemBullet3.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_GiveItemBullet3);
        _fmtMaxMpHeader.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MaxMpHeader);
        _fmtMaxMpFormula.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MaxMpFormula);
        _fmtMaxMpNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_MaxMpNote);
        _fmtRangeHeader.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_RangeHeader);
        _fmtRangeFormula.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_RangeFormula);
        _fmtRangeNote.Text = EditorStrings.Get(EditorStrings.SpellEditor_Formula_RangeNote);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.SpellEditor_SaveSpellButton);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.SpellEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.SpellEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.SpellEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.SpellEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
