using Avalonia.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor.Views;

/// <summary>What an upload would do to the server, shown before anything is sent. The Upload button
/// states its own count, which follows the removals switch.</summary>
public partial class WorldTransferDialog : Window
{
    public WorldTransferDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.WorldTransfer_UploadTitle);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        ApplyCaption();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not WorldTransferDialogViewModel vm) return;
        _route.Text = $"{vm.FolderPath}  →  {vm.ServerName}";
        ApplyCaption();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.ApplyCount)) ApplyCaption();
        };
    }

    private void ApplyCaption()
    {
        int count = (DataContext as WorldTransferDialogViewModel)?.ApplyCount ?? 0;
        _applyBtn.Content = $"{EditorStrings.Get(EditorStrings.WorldTransfer_Apply)} ({count})";
        _applyBtn.IsEnabled = count > 0;
    }
}
