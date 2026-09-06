using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Moving a whole world between the connected server and a folder.
///
/// <para>Downloading writes the server's records into a folder of your choosing, which is then an ordinary
/// world this editor can open, keep, and edit with nothing connected.</para>
///
/// <para>Uploading reads a folder, compares it against the server, and shows what it would do before it
/// does any of it. Nothing is sent until that is agreed to.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Set by the View: shows the upload's diff and waits for a decision.</summary>
    public Func<WorldTransferDialogViewModel, Task>? ShowWorldTransferDialogAsync { get; set; }

    /// <summary>Set by the View: shows a question and answers it.</summary>
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Both directions need a server on the other end.</summary>
    public bool CanTransferWorld => IsOnline;

    [RelayCommand]
    private async Task DownloadWorldAsync()
    {
        if (!await RequireConnectionAsync() || PickWorldFolderAsync is null) return;

        string? target = await PickWorldFolderAsync(WorldPickerStart());
        if (target is null) return;
        RememberBrowsedFrom(target);

        // Anything already in the folder is replaced record for record, which is worth saying out loud
        // before it happens rather than after.
        if (IsWorldFolder(target) && ConfirmAsync is not null &&
            !await ConfirmAsync(EditorStrings.Format(EditorStrings.WorldTransfer_TargetNotEmpty, ("Path", target))))
            return;

        IsLoading = true;
        try
        {
            LoadingStatus = EditorStrings.Get(EditorStrings.WorldTransfer_Reading);
            var progress = new Progress<WorldTransferProgress>(p =>
                LoadingStatus = p.Total == 0 ? EditorStrings.Get(EditorStrings.WorldTransfer_Reading)
                    : EditorStrings.Format(EditorStrings.WorldTransfer_ReadingMaps,
                        ("Done", p.Done), ("Total", p.Total)));

            var world = await WorldTransfer.FetchAsync(_conn, _data.Limits, progress);

            LoadingStatus = EditorStrings.Format(EditorStrings.WorldTransfer_Writing, ("Path", target));
            var writeProgress = new Progress<WorldTransferProgress>(p =>
                LoadingStatus = EditorStrings.Format(EditorStrings.WorldTransfer_WritingCount,
                    ("Done", p.Done), ("Total", p.Total)));

            // On a worker. This writes a file per authored record, and every one of those awaits resumes on
            // the thread it was started from — so run on the UI thread it queues thousands of continuations
            // ahead of the render, and the status it reports never gets painted.
            int written = await Task.Run(() => WorldTransfer.WriteFolderAsync(target, world, writeProgress));

            EditorLog.Info("Downloaded {Count} records from {Server} into {Path}.", written, _conn.Endpoint, target);
            Remember(target);
            NotifyWorldChanged();
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.WorldTransfer_DownloadDone,
                    ("Records", written), ("Path", target)));
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(ex, "download");
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    [RelayCommand]
    private async Task UploadWorldAsync()
    {
        if (!await RequireConnectionAsync() || PickWorldFolderAsync is null ||
            ShowWorldTransferDialogAsync is null) return;

        string? source = await PickWorldFolderAsync(WorldPickerStart());
        if (source is null) return;
        RememberBrowsedFrom(source);

        WorldTransferDialogViewModel dialog;
        IReadOnlyList<WorldChange> approved;
        WorldSnapshot folder;
        WorldTransfer.PacketContext ctx;

        IsLoading = true;
        try
        {
            LoadingStatus = EditorStrings.Get(EditorStrings.WorldTransfer_Reading);
            var progress = new Progress<WorldTransferProgress>(p =>
                LoadingStatus = p.Total == 0 ? EditorStrings.Get(EditorStrings.WorldTransfer_Reading)
                    : EditorStrings.Format(EditorStrings.WorldTransfer_ReadingMaps,
                        ("Done", p.Done), ("Total", p.Total)));

            // On a worker for the same reason the download's write is: a file per record.
            folder = await Task.Run(() => WorldTransfer.ReadFolderAsync(source));
            var server = await WorldTransfer.FetchAsync(_conn, _data.Limits, progress);

            LoadingStatus = EditorStrings.Get(EditorStrings.WorldTransfer_Comparing);
            var diff = WorldTransfer.Compare(folder, server);
            ctx = new WorldTransfer.PacketContext(server);

            if (diff.IsEmpty)
            {
                if (ShowAlertAsync is not null)
                    await ShowAlertAsync(EditorStrings.Format(EditorStrings.WorldTransfer_NoChanges, ("Path", source)));
                return;
            }

            dialog = new WorldTransferDialogViewModel(source, _conn.Endpoint, diff);
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(ex, "upload");
            return;
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }

        bool go = false;
        dialog.Confirmed += () => go = true;
        await ShowWorldTransferDialogAsync(dialog);
        if (!go) return;
        approved = dialog.Approved;

        IsLoading = true;
        try
        {
            var progress = new Progress<WorldTransferProgress>(p =>
                LoadingStatus = EditorStrings.Format(EditorStrings.WorldTransfer_Applying,
                    ("Done", p.Done), ("Total", p.Total)));
            // On a worker, like the download's write: one save per approved change, and the sends are
            // sequential either way, so this adds no concurrency — only somewhere else to await from.
            await Task.Run(() => WorldTransfer.ApplyAsync(_conn, folder, approved, ctx, progress));

            EditorLog.Info("Uploaded {Count} records from {Path} to {Server}.",
                approved.Count, source, _conn.Endpoint);
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.WorldTransfer_Applied,
                    ("Count", approved.Count), ("Server", _conn.Endpoint)));
        }
        catch (Exception ex)
        {
            await ReportFailureAsync(ex, "upload");
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    private async Task<bool> RequireConnectionAsync()
    {
        if (IsOnline) return true;
        if (ShowAlertAsync is not null)
            await ShowAlertAsync(EditorStrings.Get(EditorStrings.WorldTransfer_NeedsConnection));
        return false;
    }

    private async Task ReportFailureAsync(Exception ex, string what)
    {
        EditorLog.Error(ex, "World {What} failed.", what);
        if (ShowAlertAsync is not null)
            await ShowAlertAsync(EditorStrings.Format(EditorStrings.WorldTransfer_Failed, ("Reason", ex.Message)));
    }

    /// <summary>Whether a folder is a world: it has a <c>world.json</c>. Nothing else counts, and the
    /// record directories deliberately do not — "maps" and "items" are ordinary words, and one of them
    /// matching should never let a folder of something else be opened and written into.
    ///
    /// <para>A world with no records yet is still a world; the manifest is the claim, not the contents.</para></summary>
    public static bool IsWorldFolder(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, Mirage.Shared.Records.WorldManifest.FileName));
}
