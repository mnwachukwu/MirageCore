using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits shops and inns — trade lists, repair, banking, and the keeper NPC.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class ShopEditorView : LocalizedUserControl
{
    public ShopEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.ShopEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.ShopEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _typeLabel.Text = EditorStrings.Get(EditorStrings.Common_TypeLabel);
        _storeRadio.Content = EditorStrings.Get(EditorStrings.ShopEditor_TypeStore);
        _innRadio.Content = EditorStrings.Get(EditorStrings.ShopEditor_TypeInn);
        _fixesItemsLabel.Text = EditorStrings.Get(EditorStrings.ShopEditor_FixesItemsLabel);
        _allowBankingLabel.Text = EditorStrings.Get(EditorStrings.ShopEditor_AllowBankingLabel);
        _keeperLabel.Text = EditorStrings.Get(EditorStrings.ShopEditor_KeeperLabel);
        _tradesHeader.Text = EditorStrings.Get(EditorStrings.ShopEditor_TradesHeader);
        _colGiveItem.Text = EditorStrings.Get(EditorStrings.ShopEditor_TradesColGiveItem);
        _colGiveQty.Text = EditorStrings.Get(EditorStrings.ShopEditor_TradesColGiveQty);
        _colGetItem.Text = EditorStrings.Get(EditorStrings.ShopEditor_TradesColGetItem);
        _colGetQty.Text = EditorStrings.Get(EditorStrings.ShopEditor_TradesColGetQty);
        _noBartersHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addBarterBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _salesHeader.Text = EditorStrings.Get(EditorStrings.ShopEditor_SalesHeader);
        _salesHint.Text = EditorStrings.Get(EditorStrings.ShopEditor_SalesHint);
        _colSalesOrder.Text = EditorStrings.Get(EditorStrings.ShopEditor_SalesColOrder);
        _colSalesItem.Text = EditorStrings.Get(EditorStrings.ShopEditor_SalesColItem);
        _colSalesPrice.Text = EditorStrings.Get(EditorStrings.ShopEditor_SalesColPrice);
        _noSalesHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addSaleBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.ShopEditor_SaveShopButton);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.ShopEditorLeftWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.ShopEditorLeftWidth = LeftPanel.Bounds.Width;
    }
}
