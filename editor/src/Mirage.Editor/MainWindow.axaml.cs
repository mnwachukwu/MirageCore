using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Editor.Views;
using Mirage.Shared;

namespace Mirage.Editor;

/// <summary>
/// Code-behind for the editor shell window. Owns the three things a view-model cannot: the
/// localized menu chrome, persisted window geometry, and dialog ownership.
/// <para><see cref="OnDataContextChanged"/> supplies the view-model's <c>Show…Async</c> delegates,
/// which is how <see cref="MainWindowViewModel"/> opens dialogs without referencing a View type.</para>
///
/// <para><see cref="FAAppWindow"/> rather than <see cref="Window"/>, matching the server window: on
/// Windows it draws its own title bar, so the frame carries the app's palette instead of the system's
/// gray. It does that ONLY under <c>OperatingSystem.IsWindows()</c> — elsewhere the window keeps native
/// decorations and every bit of native window behavior with them. The editor was left on a plain
/// Window when the shell was converted, which is why the two apps disagreed about their own chrome.</para>
/// </summary>
public partial class MainWindow : FAAppWindow
{
    // Set once the unsaved-changes prompt has been answered, so the second Close() call that
    // actually shuts the window down doesn't re-run the guard and prompt again.
    private bool _skipCloseGuard;

    public MainWindow()
    {
        InitializeComponent();

        ApplyStrings();
        RefreshLanguageMenu();

        var settings = AppSettings.Current;
        if (settings.WindowWidth.HasValue) Width = settings.WindowWidth.Value;
        if (settings.WindowHeight.HasValue) Height = settings.WindowHeight.Value;
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;

        Opened += OnWindowOpened;
        StartAutoSaveTicker();
        Closing += OnWindowClosing;
    }

    /// <summary>The window title, with the open world's name where one is open. Re-run on a language
    /// change and whenever a world opens or closes.</summary>
    private void ApplyTitle()
    {
        string label = (DataContext as ViewModels.MainWindowViewModel)?.WorldLabel ?? "";
        string stem = $"{Constants.GameName} — {EditorStrings.Get(EditorStrings.MainWindow_Title)}";
        Title = label.Length > 0 ? $"{stem}: {label}" : stem;
    }

    /// <summary>Push the current language's strings into the window chrome. Re-run whenever the
    /// language changes, since these captions are set in code rather than bound.</summary>
    private void ApplyStrings()
    {
        ApplyTitle();
        _helpMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpMenu);
        _helpGeneralItem.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpGeneral);
        _helpMapEditorItem.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpMapEditor);
        _helpLoggingItem.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpLogging);
        _helpAboutItem.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpAbout);
        _dataMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_DataMenu);
        _dataReloadAssetsItem.Header = EditorStrings.Get(EditorStrings.MainWindow_DataReloadAssets);
        _worldMenu.Header = EditorStrings.Get(EditorStrings.World_Menu);
        _worldNewItem.Header = EditorStrings.Get(EditorStrings.World_New);
        _worldOpenItem.Header = EditorStrings.Get(EditorStrings.World_Open);
        _worldCloseItem.Header = EditorStrings.Get(EditorStrings.World_Close);
        _worldRecentItem.Header = EditorStrings.Get(EditorStrings.World_Recent);
        _worldCheckItem.Header = EditorStrings.Get(EditorStrings.World_Check);
        _worldSettingsItem.Header = EditorStrings.Get(EditorStrings.World_Settings);
        _worldDownloadItem.Header = EditorStrings.Get(EditorStrings.World_Download);
        _worldUploadItem.Header = EditorStrings.Get(EditorStrings.World_Upload);
        _emptyWorldTitle.Text = EditorStrings.Get(EditorStrings.World_EmptyTitle);
        _emptyWorldHint.Text = EditorStrings.Get(EditorStrings.World_EmptyHint);
        _emptyWorldNew.Content = EditorStrings.Get(EditorStrings.World_New);
        _emptyWorldOpen.Content = EditorStrings.Get(EditorStrings.World_Open);
        _languageMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_LanguageMenu);
        _assetsMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_AssetsMenu);
        _assetsManageItem.Header = EditorStrings.Get(EditorStrings.AssetManager_MenuItem);
        _viewMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_ViewMenu);
        _viewWorldPreviewItem.Header = EditorStrings.Get(EditorStrings.WorldPreview_MenuItem);
        _viewLayerVisibilityItem.Header = EditorStrings.Get(EditorStrings.LayerVisibility_MenuItem);
        _viewConsoleItem.Header = EditorStrings.Get(EditorStrings.Console_MenuItem);
        _exportMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_ExportMenu);
        _exportMapItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportMapButton);
        _exportAreaItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportAreaButton);
        _exportWorldItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportWorldButton);
        ToolTip.SetTip(_exportMapItem, EditorStrings.Get(EditorStrings.MapEditor_ExportMapTooltip));
        ToolTip.SetTip(_exportAreaItem, EditorStrings.Get(EditorStrings.MapEditor_ExportAreaTooltip));
        ToolTip.SetTip(_exportWorldItem, EditorStrings.Get(EditorStrings.MapEditor_ExportWorldTooltip));
        _autoSaveMenu.Header = EditorStrings.Get(EditorStrings.AutoSave_Menu);
        _connectBtn.Content = EditorStrings.Get(EditorStrings.Common_Connect);
        _disconnectBtn.Content = EditorStrings.Get(EditorStrings.MainWindow_DisconnectButton);
    }

    /// <summary>How often the auto-save schedule is checked. Not the save interval — the shortest one on
    /// offer is five minutes, and this only has to be fine enough that a due save is not noticeably late.</summary>
    private static readonly TimeSpan AutoSaveTickInterval = TimeSpan.FromSeconds(30);
    private DispatcherTimer? _autoSaveTimer;

    /// <summary>Start the one app-wide auto-save ticker. One timer for every editor, with the per-editor
    /// schedules kept as last-saved stamps in the view-model — so an editor whose section is not showing
    /// still reaches its interval and still gets written.</summary>
    private void StartAutoSaveTicker()
    {
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveTickInterval };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) await vm.AutoSaveTickAsync(DateTime.Now);
        };
        _autoSaveTimer.Start();
    }

    private async void AutoSaveConfigure_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        // The menu item is already disabled while connected; this is the second line of defence, since a
        // dialog that cannot be acted on is worse than no dialog at all.
        if (vm.IsOnline) return;
        var dlgVm = new AutoSaveDialogViewModel(vm.IsOnline);
        var dlg = new AutoSaveDialog { DataContext = dlgVm };
        // A confirmed change restarts every schedule, so a freshly enabled editor waits a full interval
        // instead of firing on the next 30-second tick.
        dlgVm.Confirmed += vm.ResetAutoSaveSchedule;
        dlg.CloseWhen(h => dlgVm.Confirmed += h, h => dlgVm.Canceled += h);
        await dlg.ShowDialog(this);
    }

    /// <summary>Rebuild the language menu from the locale files found on disk, with the active one
    /// checked. Each item captures its own locale so the click handler picks the right one.</summary>
    private void RefreshLanguageMenu()
    {
        _languageMenu.ItemsSource = null;
        var items = new List<MenuItem>();
        string current = AppSettings.Current.Language;
        foreach (var (locale, displayName) in EditorStrings.GetAvailableLanguages(EditorStrings.LangDir))
        {
            string capturedLocale = locale;
            var item = new MenuItem
            {
                Header = displayName,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = locale == current,
            };
            item.Click += (_, _) => OnLanguageSelected(capturedLocale);
            items.Add(item);
        }
        _languageMenu.ItemsSource = items;
    }

    /// <summary>Persist the chosen language and re-localize the chrome in place — no restart.</summary>
    private void OnLanguageSelected(string locale)
    {
        if (locale == AppSettings.Current.Language) return;
        AppSettings.Current.Language = locale;
        AppSettings.Current.Save();
        EditorStrings.Load(EditorStrings.LangDir, locale);
        ApplyStrings();
        RefreshLanguageMenu();
    }

    // Position is restored on Opened rather than in the constructor: before the window is shown the
    // platform can still move it, so an earlier assignment would be overwritten.
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        var settings = AppSettings.Current;
        if (!settings.WindowMaximized && settings.WindowX.HasValue && settings.WindowY.HasValue)
            Position = new PixelPoint((int)settings.WindowX.Value, (int)settings.WindowY.Value);
        RestoreWorldPreview();
        RestoreLayerVisibility();
        RestoreConsole();
    }

    /// <summary>Close guard. With unsaved work or a live connection the close is canceled and
    /// resumed asynchronously (a Closing handler cannot await), otherwise it proceeds straight
    /// through after saving window state.</summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_skipCloseGuard)
        {
            SaveWindowState();
            return;
        }
        if (DataContext is not MainWindowViewModel vm)
        {
            SaveWindowState();
            return;
        }
        if (!vm.HasAnyDirty && !vm.IsOnline)
        {
            SaveWindowState();
            return;
        }
        e.Cancel = true;
        _ = HandleCloseAsync(vm);
    }

    /// <summary>Persist window geometry plus each editor view's panel layout. Geometry is only
    /// captured while in the Normal state, so a maximized session doesn't overwrite the restore size.</summary>
    private void SaveWindowState()
    {
        this.FindDescendantOfType<MapEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ItemEditorView>()?.SavePanelState();
        this.FindDescendantOfType<NpcEditorView>()?.SavePanelState();
        this.FindDescendantOfType<SpellEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ShopEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ClassEditorView>()?.SavePanelState();
        this.FindDescendantOfType<QuestEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ConversationEditorView>()?.SavePanelState();
        var settings = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        settings.Save();
    }

    /// <summary>Finish a close that <see cref="OnWindowClosing"/> canceled: prompt for unsaved work,
    /// disconnect, then close for real. Returns without closing if the author backs out.</summary>
    private async Task HandleCloseAsync(MainWindowViewModel vm)
    {
        if (!await vm.HandleDirtyForCloseAsync()) return;
        await vm.ForceDisconnectAsync();
        _skipCloseGuard = true;
        Close();
    }

    /// <summary>Wire the view-model's dialog and file-picker delegates. Keeping them here is what lets
    /// the view-model stay free of View references while still driving modal dialogs.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // The view-model arrives after the constructor, so the title is set again here and thereafter
            // whenever the open world changes.
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainWindowViewModel.WorldLabel)) ApplyTitle();
            };
            ApplyTitle();

            // A world is a directory; the picker opens on the shipped one so a first run has something
            // to say yes to.
            vm.PickWorldFolderAsync = async startAt =>
            {
                var start = await StorageProvider.TryGetFolderFromPathAsync(startAt);
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = EditorStrings.Get(EditorStrings.World_Open),
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                });
                return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            };

            vm.ShowWorldCheckDialogAsync = async dlgVm =>
            {
                var dlg = new WorldCheckDialog { DataContext = dlgVm };
                dlg.CloseWhen(h => dlgVm.Closed += h);
                await dlg.ShowDialog(this);
            };

            // The name, then the folder. Answers null when the person backs out of either.
            vm.AskNewWorldNameAsync = async dlgVm =>
            {
                string? name = null;
                var dlg = new NewWorldDialog { DataContext = dlgVm };
                dlgVm.Confirmed += n => name = n;
                dlg.CloseWhen<string>(h => dlgVm.Confirmed += h);
                dlg.CloseWhen(h => dlgVm.Canceled += h);
                await dlg.ShowDialog(this);
                return name;
            };

            vm.ShowWorldSettingsDialogAsync = async dlgVm =>
            {
                var dlg = new WorldSettingsDialog { DataContext = dlgVm };
                dlg.CloseWhen<Mirage.Shared.Records.WorldManifest>(h => dlgVm.Confirmed += h);
                dlg.CloseWhen(h => dlgVm.Canceled += h);
                await dlg.ShowDialog(this);
            };

            vm.ShowWorldTransferDialogAsync = async dlgVm =>
            {
                var dlg = new WorldTransferDialog { DataContext = dlgVm };
                dlg.CloseWhen(h => dlgVm.Confirmed += h, h => dlgVm.Canceled += h);
                await dlg.ShowDialog(this);
            };

            vm.ConfirmAsync = async msg => await new ConfirmDialog(msg).ShowDialog<bool>(this);

            vm.ShowConnectDialogAsync = async dlgVm =>
            {
                var dlg = new ConnectDialog { DataContext = dlgVm };
                dlg.CloseWhen(h => dlgVm.CloseRequested += h);
                await dlg.ShowDialog(this);
            };

            vm.ShowPushChangesDialogAsync = async dlgVm =>
            {
                var dlg = new PushChangesDialog { DataContext = dlgVm };
                dlg.CloseWhen(h => dlgVm.ProceedConfirmed += h, h => dlgVm.Canceled += h);
                await dlg.ShowDialog(this);
            };

            vm.MapEditor.ConfirmAsync = async msg =>
                await new ConfirmDialog(msg).ShowDialog<bool>(this);

            vm.ConversationEditor.ShowNodeDialogAsync = async (conversation, node) =>
                await new ConversationNodeDialog(conversation, node).ShowDialog(this);

            vm.ShowAlertAsync = async msg =>
                await new ConfirmDialog(msg, alertOnly: true).ShowDialog<bool>(this);

            vm.MapEditor.ShowAlertAsync = vm.ShowAlertAsync;

            vm.MapEditor.ShowMapResizeDialogAsync = async dlgVm =>
            {
                var dlg = new MapResizeDialog { DataContext = dlgVm };
                dlg.CloseWhen<Mirage.Shared.Records.MapSize>(h => dlgVm.Confirmed += h);
                dlg.CloseWhen(h => dlgVm.Canceled += h);
                await dlg.ShowDialog(this);
            };

            vm.MapEditor.SaveFilePngAsync = async suggestedName =>
            {
                var options = new FilePickerSaveOptions
                {
                    Title = EditorStrings.Get(EditorStrings.MapEditor_ExportPngDialogTitle),
                    SuggestedFileName = suggestedName,
                    DefaultExtension = "png",
                    FileTypeChoices = [new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }],
                };
                if (await StorageProvider.TryGetFolderFromPathAsync(EditorPaths.Data) is { } folder)
                    options.SuggestedStartLocation = folder;
                var file = await StorageProvider.SaveFilePickerAsync(options);
                return file?.TryGetLocalPath();
            };

            vm.ShowDisconnectDialogAsync = async dlgVm =>
            {
                var dlg = new DisconnectDialog { DataContext = dlgVm };
                // ConnectDialog must be owned by this dialog, not the main window,
                // because ShowDialog blocks the owner's message loop.
                dlgVm.ShowConnectDialogAsync = async connectVm =>
                {
                    var connectDlg = new ConnectDialog { DataContext = connectVm };
                    connectDlg.CloseWhen(h => connectVm.CloseRequested += h);
                    await connectDlg.ShowDialog(dlg);
                };
                dlg.CloseWhen(h => dlgVm.CloseRequested += h);
                // Closing the window IS a decision: carry on offline, which is what the caller does with any
                // exit that is not a reconnect. Nothing is preserved by refusing the close — the session is
                // already gone — and a modal with no way out turns an unexpected open into a frozen editor.
                await dlg.ShowDialog(this);
            };
        }
    }

    private async void HelpGeneral_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new GeneralHelpDialog();
        await dlg.ShowDialog(this);
    }

    private async void HelpMapControls_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new HelpDialog();
        await dlg.ShowDialog(this);
    }

    private async void HelpLogging_Click(object? sender, RoutedEventArgs e) => await ShowLoggingDialogAsync(this);

    /// <summary>Capture level and retention. A confirmed change is applied to the live sink at once and
    /// persisted, so the next thing logged is already at the new level.
    ///
    /// <para>One method for both ways in — the Help menu and the Console window's button — so the two can
    /// never offer different controls over the same setting. <paramref name="owner"/> is whichever window
    /// asked, because a dialog modal to the wrong one is a dialog you cannot reach.</para></summary>
    internal async Task ShowLoggingDialogAsync(Window owner)
    {
        var dlgVm = new LoggingDialogViewModel(AppSettings.Current.Logging);
        var dlg = new LoggingDialog { DataContext = dlgVm };
        dlgVm.RevealFolderAsync = async path =>
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        dlgVm.Confirmed += () =>
        {
            var setting = dlgVm.ToSetting();
            AppSettings.Current.Logging = setting;
            AppSettings.Current.Save();
            EditorLog.Reconfigure(setting);
            EditorLog.Info("Logging reconfigured: level {Level}, retention {Retention}.",
                setting.Level, setting.Retention);
        };
        dlg.CloseWhen(h => dlgVm.Confirmed += h, h => dlgVm.Canceled += h);
        EditorLog.Debug("Logging configuration opened.");
        await dlg.ShowDialog(owner);
    }

    private async void HelpAbout_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog();
        await dlg.ShowDialog(this);
    }

    /// <summary>The asset manager. Every change it makes is on disk, so it re-reads the editor's sheets as
    /// it goes rather than only on close — a rename the palette does not show is a rename that looks like
    /// it failed.</summary>
    private async void AssetsManage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dlgVm = new AssetManagerDialogViewModel(EditorPaths.Assets, EditorPaths.BundledAssets)
        {
            UsageProvider = vm.DescribeSheetUsage,
            PreviewProvider = vm.SheetsOf,
        };

        dlgVm.PickSheetFileAsync = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = EditorStrings.Get(EditorStrings.AssetManager_PickTitle),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(EditorStrings.Get(EditorStrings.AssetManager_PickFilter))
                    {
                        Patterns = [.. Mirage.Shared.SheetFile.Extensions.Select(x => $"*{x}")],
                    },
                ],
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };
        dlgVm.ConfirmAsync = async msg => await new ConfirmDialog(msg).ShowDialog<bool>(this);
        dlgVm.RevealFolderAsync = async path =>
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        dlgVm.AssetsChanged += vm.ReloadAssetsFromDisk;

        var dlg = new AssetManagerDialog { DataContext = dlgVm };
        dlg.CloseWhen(h => dlgVm.Closed += h);
        dlgVm.Refresh();
        EditorLog.Debug("Asset manager opened.");
        await dlg.ShowDialog(this);
    }

    // ── World Preview ─────────────────────────────────────────────────────────
    // The one window the editor shows modelessly, so its lifetime is held here rather than awaited.

    private WorldPreviewWindow? _worldPreview;

    private void ViewWorldPreview_Click(object? sender, RoutedEventArgs e)
    {
        bool wanted = _viewWorldPreviewItem.IsChecked;
        if (wanted) OpenWorldPreview(); else _worldPreview?.CloseDeferred();

        AppSettings.Current.WorldPreviewOpen = wanted;
        AppSettings.Current.Save();
    }

    private void OpenWorldPreview()
    {
        if (_worldPreview is not null)
        {
            _worldPreview.Activate();
            return;
        }
        if (DataContext is not MainWindowViewModel vm) return;

        // A closed window cannot be shown again, so each toggle builds a fresh one.
        var window = new WorldPreviewWindow { DataContext = new WorldPreviewViewModel(vm.MapEditor) };
        window.Closed += (_, _) =>
        {
            _worldPreview = null;
            _viewWorldPreviewItem.IsChecked = false;
            AppSettings.Current.WorldPreviewOpen = false;
            AppSettings.Current.Save();
        };
        _worldPreview = window;
        window.Show(this);
    }

    // Reopened after the main window exists so it has an owner to sit above. A world need not be open
    // yet: the preview shows its empty state and fills in when a map is selected.
    private void RestoreWorldPreview()
    {
        if (!AppSettings.Current.WorldPreviewOpen) return;
        _viewWorldPreviewItem.IsChecked = true;
        OpenWorldPreview();
    }

    // ── Layer Visibility ──────────────────────────────────────────────────────

    private LayerVisibilityWindow? _layerVisibility;

    private void ViewLayerVisibility_Click(object? sender, RoutedEventArgs e)
    {
        bool wanted = _viewLayerVisibilityItem.IsChecked;
        if (wanted) OpenLayerVisibility(); else _layerVisibility?.CloseDeferred();

        AppSettings.Current.LayerVisibilityOpen = wanted;
        AppSettings.Current.Save();
    }

    private void OpenLayerVisibility()
    {
        if (_layerVisibility is not null)
        {
            _layerVisibility.Activate();
            return;
        }
        if (DataContext is not MainWindowViewModel vm) return;

        // A closed window cannot be shown again, so each toggle builds a fresh one.
        var window = new LayerVisibilityWindow { DataContext = new LayerVisibilityViewModel(vm.MapEditor) };
        window.Closed += (_, _) =>
        {
            _layerVisibility = null;
            _viewLayerVisibilityItem.IsChecked = false;
            AppSettings.Current.LayerVisibilityOpen = false;
            AppSettings.Current.Save();
        };
        _layerVisibility = window;
        window.Show(this);
    }

    // The window comes back where it was; the layers do not. Only the window's open state is remembered,
    // so a session always starts with the whole map on screen.
    private void RestoreLayerVisibility()
    {
        if (!AppSettings.Current.LayerVisibilityOpen) return;
        _viewLayerVisibilityItem.IsChecked = true;
        OpenLayerVisibility();
    }

    private ConsoleWindow? _console;

    private void ViewConsole_Click(object? sender, RoutedEventArgs e)
    {
        bool wanted = _viewConsoleItem.IsChecked;
        if (wanted) OpenConsole(); else _console?.CloseDeferred();

        AppSettings.Current.ConsoleOpen = wanted;
        AppSettings.Current.Save();
    }

    private void OpenConsole()
    {
        if (_console is not null)
        {
            _console.Activate();
            return;
        }

        // A closed window cannot be shown again, so each toggle builds a fresh one. The log it shows
        // outlives it either way — the sink is on EditorLog, not on this.
        var window = new ConsoleWindow { DataContext = new ConsoleViewModel() };
        window.Closed += (_, _) =>
        {
            _console = null;
            _viewConsoleItem.IsChecked = false;
            AppSettings.Current.ConsoleOpen = false;
            AppSettings.Current.Save();
        };
        _console = window;
        window.Show(this);
    }

    private void RestoreConsole()
    {
        if (!AppSettings.Current.ConsoleOpen) return;
        _viewConsoleItem.IsChecked = true;
        OpenConsole();
    }
}
