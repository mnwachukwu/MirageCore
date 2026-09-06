using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Opening and closing a world offline.
///
/// <para>A world is a directory holding maps/, npcs/, items/ and the rest — the numbered set the records
/// address each other through. The editor keeps none of its own: it opens one wherever it lives, and several
/// can sit side by side. Only settings, logs and the editable graphics belong to the editor.</para>
///
/// <para>Nothing is open until something is opened, so the window starts empty and says so.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Set by the View: shows a folder picker starting at <paramref name="startAt"/>, or null if
    /// the person cancelled.</summary>
    public Func<string, Task<string?>>? PickWorldFolderAsync { get; set; }

    /// <summary>Whether a world is open. Everything that lists or edits records hangs off this.
    ///
    /// <para>Connecting IS opening a world — the server's — so a live session counts. A session has one
    /// world or none: an open folder, or a connection, never both at once as far as this is concerned.
    /// Where something means the FOLDER specifically, it asks <see cref="EditorPaths.HasWorld"/>.</para></summary>
    public bool HasWorld => EditorPaths.HasWorld || IsOnline;

    /// <summary>Whether the window has nothing to show.</summary>
    public bool ShowEmptyWorld => !HasWorld;

    /// <summary>The open world's path, for the title bar and the empty-state prompt.</summary>
    public string WorldPath => EditorPaths.Data;

    /// <summary>What to call the open world in the window title: its own name, or "Untitled World" where
    /// it has none, or nothing at all when none is open. Telling a live world from a test copy of it is the
    /// whole reason a world carries a name, and the title bar is where that has to be legible.
    ///
    /// <para>Connected, the name is the SERVER's — the folder that may also be open is not what is being
    /// edited.</para></summary>
    public string WorldLabel
    {
        get
        {
            if (!HasWorld) return "";
            string name = IsOnline ? _data.OnlineWorldName : _data.Manifest.Name;
            return string.IsNullOrWhiteSpace(name) ? EditorStrings.Get(EditorStrings.World_Untitled) : name.Trim();
        }
    }

    /// <summary>Worlds opened before, most recent first.</summary>
    public IReadOnlyList<RecentWorldViewModel> RecentWorlds =>
        [.. AppSettings.Current.RecentWorlds.Select(p => new RecentWorldViewModel(p, OpenRecentWorldAsync))];

    /// <summary>Where a folder picker opens. Every one of them asks the same question — which of your
    /// worlds — so they share an answer and each pick teaches the next.
    ///
    /// <para>Falling back: the folder browsed last, then the one holding the open world, then the shipped
    /// seed, which is the one thing a first run is guaranteed to have. A remembered folder that has since
    /// gone is skipped rather than handed to a picker that would ignore it anyway.</para></summary>
    private static string WorldPickerStart()
    {
        string? remembered = AppSettings.Current.LastWorldBrowsePath;
        if (!string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered)) return remembered;

        if (EditorPaths.HasWorld && ParentOf(EditorPaths.Data) is { } parent) return parent;
        if (Directory.Exists(EditorPaths.BundledWorld)) return EditorPaths.BundledWorld;
        return AppContext.BaseDirectory;
    }

    /// <summary>Remembers where a pick came from, so the next picker opens there. The world's PARENT: a
    /// picker aimed inside a world shows its collections, and the next world over is the likelier target.</summary>
    private static void RememberBrowsedFrom(string pickedWorld)
    {
        if (ParentOf(pickedWorld) is not { } parent) return;
        var s = AppSettings.Current;
        if (PathComparison.SameLocation(s.LastWorldBrowsePath, parent)) return;
        s.LastWorldBrowsePath = parent;
        s.Save();
    }

    /// <summary>Set by the View: asks what a new world is called, or null if the person backed out.</summary>
    public Func<NewWorldDialogViewModel, Task<string?>>? AskNewWorldNameAsync { get; set; }

    /// <summary>Makes a world: a name, then somewhere to keep it. <b>The name is the folder's name</b> — a
    /// world called Demo Landia is a folder called Demo Landia, made inside the one that was picked — so a
    /// world is never dropped loose among whatever else was in there.
    ///
    /// <para>What lands in it is the `world.json` that makes the folder a world and an empty directory per
    /// record family. No record FILE is written: a slot with no file is a blank record, not a missing
    /// one.</para></summary>
    [RelayCommand]
    private async Task NewWorldAsync()
    {
        if (AskNewWorldNameAsync is null || PickWorldFolderAsync is null) return;

        string? name = await AskNewWorldNameAsync(new NewWorldDialogViewModel());
        if (name is null) return;

        // Unnamed is a real answer, and the folder is called what the window would call the world.
        string folderName = name.Length > 0 ? name : EditorStrings.Get(EditorStrings.World_Untitled);
        if (!PortableFileName.IsValid(folderName))
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.NewWorld_InvalidName, ("Name", folderName)));
            return;
        }

        string? parent = await PickWorldFolderAsync(WorldPickerStart());
        if (parent is null) return;

        string folder = Path.Combine(parent, folderName);

        // A name collision is a failure rather than a question: the answer to "there is already one of
        // those here" is a different name, which is not something a yes/no can supply.
        if (Directory.Exists(folder))
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.NewWorld_AlreadyThere, ("Path", folder)));
            return;
        }

        if (!await ConfirmDiscardIfDirtyAsync()) return;

        try
        {
            await EditorDataService.CreateWorldAsync(folder, new WorldManifest { Name = name });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.NewWorld_Failed, ("Reason", e.Message)));
            return;
        }

        EditorLog.Info("Created world {Path}.", folder);
        await OpenWorldAsync(folder, remember: true);
    }

    private static string? ParentOf(string path)
    {
        try
        {
            // Always a path that resolves on THIS machine — every caller has passed Directory.Exists — so the
            // platform's own parsing is the right one, and a trailing separator is all it needs handling.
            return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task OpenWorldAsync()
    {
        if (PickWorldFolderAsync is null) return;
        string? picked = await PickWorldFolderAsync(WorldPickerStart());
        if (picked is not null) await OpenWorldAsync(picked, remember: true);
    }

    /// <summary>Points the editor at a world and reads it. Anything unsaved is offered first, since opening
    /// another world replaces everything in memory.</summary>
    public async Task OpenWorldAsync(string path, bool remember)
    {
        if (!Directory.Exists(path))
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.World_NotFound, ("Path", (object?)path)));
            Forget(path);
            return;
        }
        // A world is a folder with a world.json in it, and nothing else opens. Creating one is its own
        // command, so there is no "open somewhere empty and it becomes a world" — which is what would let
        // a mistaken pick end up with maps/ and items/ written into it.
        if (!IsWorldFolder(path))
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.World_NotAWorld, ("Path", path)));
            return;
        }

        if (!await ConfirmDiscardIfDirtyAsync()) return;

        EditorPaths.OpenWorld(path);
        EditorLog.Info("Opening world {Path}.", path);

        IsLoading = true;
        LoadingStatus = EditorStrings.Get(EditorStrings.MainWindow_LoadingData);
        try
        {
            await _data.LoadOfflineAsync();
            RefreshEditors(online: false);
            MarkSourceSeen();
            SelectedSection = _sectionMap["Maps"];
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }

        if (remember) Remember(path);
        NotifyWorldChanged();
    }

    [RelayCommand]
    private async Task CloseWorldAsync()
    {
        if (!EditorPaths.HasWorld) return;
        if (!await ConfirmDiscardIfDirtyAsync()) return;
        EditorLog.Info("Closing world {Path}.", EditorPaths.Data);
        EditorPaths.OpenWorld("");
        // The records go with it: the lists refill from these arrays, so they have to be empty too.
        _data.ClearOffline();
        RefreshEditors(online: false);
        SelectedSection = null;
        CurrentEditor = null;
        NotifyWorldChanged();
    }

    /// <summary>Opens one from the recent list.</summary>
    private Task OpenRecentWorldAsync(string path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : OpenWorldAsync(path, remember: true);

    /// <summary>Set by the View: shows the world's record ceilings and answers with the new ones.</summary>
    public Func<WorldSettingsDialogViewModel, Task>? ShowWorldSettingsDialogAsync { get; set; }

    /// <summary>The world's name, its default map size and its record ceilings. A confirmed change is
    /// written to the folder and the world reread, since every list is sized from it.</summary>
    [RelayCommand]
    private async Task WorldSettingsAsync()
    {
        if (ShowWorldSettingsDialogAsync is null || !EditorPaths.HasWorld) return;
        var dlg = new WorldSettingsDialogViewModel(_data.Manifest, IsOnline);
        WorldManifest? chosen = null;
        dlg.Confirmed += manifest => chosen = manifest;
        await ShowWorldSettingsDialogAsync(dlg);
        if (chosen is null) return;

        await EditorDataService.SaveManifestAsync(EditorPaths.Data, chosen);
        EditorLog.Info("World settings written to {Path}.", EditorPaths.Data);
        await OpenWorldAsync(EditorPaths.Data, remember: false);
    }

    /// <summary>Set by the View: shows the world check's findings.</summary>
    public Func<WorldCheckDialogViewModel, Task>? ShowWorldCheckDialogAsync { get; set; }

    /// <summary>Reads every map against every other and reports what does not agree — a link joining two
    /// sizes, a warp naming a tile its destination has not got, a group index no group backs. Records only,
    /// so it needs no server and changes nothing.</summary>
    [RelayCommand]
    private async Task CheckWorldAsync()
    {
        if (ShowWorldCheckDialogAsync is null || !HasWorld) return;

        var groups = MapGroupEditor.MapGroups.Select(g => g.Index).ToHashSet();
        var world = new WorldContent
        {
            Maps = Slots(MapEditor.Maps, r => r.Index, r => r.Record),
            Items = Slots(ItemEditor.Items, r => r.Index, r => r.ToRecord()),
            Npcs = Slots(NpcEditor.Items, r => r.Index, r => r.ToRecord()),
            Shops = Slots(ShopEditor.Items, r => r.Index, r => r.ToRecord()),
            Spells = Slots(SpellEditor.Items, r => r.Index, r => r.ToRecord()),
            Quests = Slots(QuestEditor.Items, r => r.Index, r => r.ToRecord()),
            Conversations = Slots(ConversationEditor.Items, r => r.Index, r => r.ToRecord()),
            Classes = Slots(ClassEditor.Items, r => r.Index, r => r.ToRecord()),
            GroupExists = groups.Contains,
        };

        var dlg = new WorldCheckDialogViewModel(WorldCheck.Run(world), NameOf);
        dlg.Navigate += GoTo;
        await ShowWorldCheckDialogAsync(dlg);
    }

    /// <summary>An editor's rows as the 1-based array every record family is held in, index 0 unused.</summary>
    private static TRecord?[] Slots<TRow, TRecord>(IReadOnlyList<TRow> rows, Func<TRow, int> numOf,
                                                   Func<TRow, TRecord> recordOf) where TRecord : class
    {
        int max = 0;
        foreach (var row in rows) max = Math.Max(max, numOf(row));
        var all = new TRecord?[max + 1];
        foreach (var row in rows)
        {
            int n = numOf(row);
            if (n > 0) all[n] = recordOf(row);
        }

        return all;
    }

    /// <summary>Follows a finding to the record it is on. A tile-scoped one goes to its map and no further:
    /// selecting a tile belongs to the canvas control rather than here, and the row names the coordinates.</summary>
    private void GoTo(WorldRecordKind kind, int num)
    {
        switch (kind)
        {
            case WorldRecordKind.Map: OpenMap(num); break;
            case WorldRecordKind.Item: Open("Items", ItemEditor, num); break;
            case WorldRecordKind.Npc: Open("NPCs", NpcEditor, num); break;
            case WorldRecordKind.Shop: Open("Shops", ShopEditor, num); break;
            case WorldRecordKind.Spell: Open("Spells", SpellEditor, num); break;
            case WorldRecordKind.Quest: Open("Quests", QuestEditor, num); break;
            case WorldRecordKind.Conversation: Open("Conversations", ConversationEditor, num); break;
            case WorldRecordKind.Class: Open("Classes", ClassEditor, num); break;
        }
    }

    /// <summary>What to call the record a finding is on, for the row that names it.</summary>
    private string NameOf(WorldRecordKind kind, int num) => kind switch
    {
        WorldRecordKind.Map => MapEditor.Maps.FirstOrDefault(m => m.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Item => ItemEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Npc => NpcEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Shop => ShopEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Spell => SpellEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Quest => QuestEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Conversation => ConversationEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        _ => ClassEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
    };

    private void NotifyWorldChanged()
    {
        OnPropertyChanged(nameof(HasWorld));
        OnPropertyChanged(nameof(ShowEmptyWorld));
        OnPropertyChanged(nameof(WorldPath));
        OnPropertyChanged(nameof(WorldLabel));
        OnPropertyChanged(nameof(RecentWorlds));
    }

    private static void Remember(string path)
    {
        RememberBrowsedFrom(path);
        var s = AppSettings.Current;
        s.RecentWorlds.RemoveAll(p => PathComparison.SameLocation(p, path));
        s.RecentWorlds.Insert(0, path);
        if (s.RecentWorlds.Count > 8) s.RecentWorlds.RemoveRange(8, s.RecentWorlds.Count - 8);
        s.LastWorldPath = path;
        s.Save();
    }

    /// <summary>Removes a missing path from the recent list.</summary>
    private void Forget(string path)
    {
        var s = AppSettings.Current;
        if (s.RecentWorlds.RemoveAll(p => PathComparison.SameLocation(p, path)) == 0) return;
        if (PathComparison.SameLocation(s.LastWorldPath, path)) s.LastWorldPath = null;
        s.Save();
        OnPropertyChanged(nameof(RecentWorlds));
    }

    /// <summary>Offers to keep unsaved work before something replaces it. False means the person backed out
    /// and whatever was about to happen must not.</summary>
    private async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        var dirty = GetAllDirty().ToList();
        if (dirty.Count == 0 || ShowPushChangesDialogAsync is null) return true;
        var vm = new PushChangesDialogViewModel(dirty, _conn, _data, PushChangesReason.SwitchingWorld);
        bool go = false;
        vm.ProceedConfirmed += () => go = true;
        await ShowPushChangesDialogAsync(vm);
        return go;
    }
}
