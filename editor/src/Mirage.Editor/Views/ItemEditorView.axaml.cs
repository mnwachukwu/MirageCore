using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits item definitions, including the type-dependent data slots and the restriction flags.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class ItemEditorView : LocalizedUserControl
{
    public ItemEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _refsHeader.Text = EditorStrings.Get(EditorStrings.References_Header);
        _noRefs.Text = EditorStrings.Get(EditorStrings.References_None);
        _typeFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.ItemEditor_AllTypesFilter);
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_FilterByName);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.ItemEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.ItemEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _itemSheetLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_ItemSheetLabel);
        _picLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_PicLabel);
        _typeLabel.Text = EditorStrings.Get(EditorStrings.Common_TypeLabel);
        _restrictionsLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_RestrictionsLabel);
        _nonTradeableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonTradeable);
        _nonListableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonListable);
        _nonMailableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonMailable);
        _destroyOnDropCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_DestroyOnDrop);
        _nonJunkableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonJunkable);
        _priceLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_PriceLabel);
        _notesExpander.Header = EditorStrings.Get(EditorStrings.Common_Notes);

        // Captions for the fields that mean one thing wherever they apply; Power and VitalAmount bind
        // their captions instead, since those two vary by item type.
        _durabilityLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_Durability);
        _levelReqLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_LevelReq);
        _allowedClassesLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_AllowedClasses);
        _spellNumLabel.Text = EditorStrings.Get(EditorStrings.DataLabel_SpellNumber);

        _spellPicker.PlaceholderText = EditorStrings.Get(EditorStrings.ItemEditor_SpellSearchPlaceholder);

        _notesEquipmentHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentHeader);
        _notesEquipmentDurability.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentDurability);
        _notesEquipmentPower.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentPower);
        _notesEquipmentWeapon.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentWeapon);
        _notesEquipmentArmor.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentArmor);
        _notesEquipmentHelmet.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentHelmet);
        _notesEquipmentShield.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentShield);
        _notesEquipmentClassReq.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentClassReq);
        _notesEquipmentShieldSide.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentShieldSide);

        _notesPotionsHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_PotionsHeader);
        _notesPotionsAmount.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_PotionsAmount);

        _notesSpellScrollHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_SpellScrollHeader);
        _notesSpellScrollSpell.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_SpellScrollSpell);

        _notesKeyHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_KeyHeader);
        _notesKeyId.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_KeyId);

        _notesCurrencyHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_CurrencyHeader);
        _notesCurrencyDesc.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_CurrencyDesc);

        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.ItemEditor_SaveItemButton);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.ItemEditorLeftWidth);
        PanelGrid.ColumnDefinitions[4].Width = new GridLength(AppSettings.Current.ItemEditorRightWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.ItemEditorLeftWidth = LeftPanel.Bounds.Width;
        // The COLUMN, not the panel: the panel is inset by its margin, so persisting its own width and
        // restoring it as the column width would narrow the column a little more every session.
        if (RightPanel.IsVisible && PanelGrid.ColumnDefinitions[4].ActualWidth > 0)
            AppSettings.Current.ItemEditorRightWidth = PanelGrid.ColumnDefinitions[4].ActualWidth;
    }
}
