using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using System.Text;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Rereading the offline store on disk and reporting what arrived, so a change made to the folder outside
/// the editor does not need a restart to be seen.
///
/// <para>OFFLINE ONLY, and the command is disabled online. A live session is already told about every
/// record the moment it changes — the server pushes each save to every other editor — so there is nothing
/// for a manual reread to find, and a button that always reports "nothing moved" reads as broken.</para>
///
/// <para>A section holding unsaved edits is left alone. Reloading it would drop that work with no way back,
/// and the report names it so a section that did not move is never mistaken for one that had nothing to
/// take.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The offline store folder behind each section. A section absent from here keeps no folder.</summary>
    private static readonly Dictionary<string, string> SectionFolder = new()
    {
        ["Maps"] = "maps", ["MapGroups"] = "map_groups", ["Items"] = "items", ["NPCs"] = "npcs",
        ["Shops"] = "shops", ["Spells"] = "spells", ["Classes"] = "classes", ["Quests"] = "quests",
        ["Conversations"] = "conversations", ["Accounts"] = "accounts",
    };

    /// <summary>What each section's folder looked like at the last load, so the next refresh can say whether
    /// anything actually arrived.</summary>
    private readonly Dictionary<string, string> _lastSeen = [];

    private (string Key, Action Reload, Func<bool> Dirty, Func<int> Count)[] Refreshable =>
    [
        ("Maps",          MapEditor.LoadOffline,          () => MapEditor.HasAnyDirtyMap,       () => MapEditor.Maps.Count),
        ("MapGroups",     MapGroupEditor.LoadOffline,     () => MapGroupEditor.HasAnyDirty,     () => MapGroupEditor.MapGroups.Count),
        ("Items",         ItemEditor.LoadOffline,         () => ItemEditor.HasAnyDirty,         () => ItemEditor.Items.Count),
        ("NPCs",          NpcEditor.LoadOffline,          () => NpcEditor.HasAnyDirty,          () => NpcEditor.Npcs.Count),
        ("Shops",         ShopEditor.LoadOffline,         () => ShopEditor.HasAnyDirty,         () => ShopEditor.Shops.Count),
        ("Spells",        SpellEditor.LoadOffline,        () => SpellEditor.HasAnyDirty,        () => SpellEditor.Spells.Count),
        ("Classes",       ClassEditor.LoadOffline,        () => ClassEditor.HasAnyDirty,        () => ClassEditor.Classes.Count),
        ("Quests",        QuestEditor.LoadOffline,        () => QuestEditor.HasAnyDirty,        () => QuestEditor.Quests.Count),
        ("Conversations", ConversationEditor.LoadOffline, () => ConversationEditor.HasAnyDirty, () => ConversationEditor.Conversations.Count),
        ("Accounts",      AccountEditor.LoadOffline,      () => false,                          () => AccountEditor.Accounts.Count),
    ];

    public string RefreshMenuItemLabel => EditorStrings.Get(EditorStrings.MainWindow_DataRefresh);

    /// <summary>
    /// A cheap stand-in for the content of a section's folder: every file's name, length and write time. It
    /// moves whenever a record is added, removed OR rewritten, which a record count does not — a folder whose
    /// files were all replaced in place still holds the same number of them.
    /// </summary>
    private static string SourceStamp(string section)
    {
        if (!SectionFolder.TryGetValue(section, out string? folder)) return "";
        string dir = Path.Combine(EditorPaths.Data, folder);
        if (!Directory.Exists(dir)) return "";
        try
        {
            var sb = new StringBuilder();
            foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append('|');
            }
            return sb.ToString();
        }
        catch
        {
            return "";   // a folder that cannot be read reports as unknown rather than as unchanged
        }
    }

    /// <summary>Records what every section's folder looks like now, so the next refresh has a baseline.
    /// Called once the startup load has finished.</summary>
    public void MarkSourceSeen()
    {
        foreach (var (key, _, _, _) in Refreshable) _lastSeen[key] = SourceStamp(key);
    }

    /// <summary>Offline only. Online the server pushes every change as it happens, so there is nothing here
    /// to ask for.</summary>
    private bool CanRefreshFromDisk() => !IsOnline;

    [RelayCommand(CanExecute = nameof(CanRefreshFromDisk))]
    private async Task RefreshFromDiskAsync()
    {
        EditorLog.Info("Refresh from disk requested.");

        // The whole world is replaced under the open editors, so the window is covered while it happens.
        // Editing a row that is about to be thrown away reads as the editor discarding the work.
        IsLoading = true;
        LoadingStatus = EditorStrings.Get(EditorStrings.MainWindow_LoadingData);
        string? report;
        try
        {
            report = await RereadAsync();
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }

        // Reported once the cover is down, so the result is read against the world it describes.
        if (report is not null && ShowAlertAsync is not null) await ShowAlertAsync(report);
    }

    /// <summary>Rereads the world and returns what moved, for reporting once the cover is down.</summary>
    private async Task<string?> RereadAsync()
    {
        // THE reread, and the whole point of the command. Each editor's LoadOffline() refills its rows from
        // a cache filled once at startup. Calling those alone rebuilds the view from the same records and
        // reports, correctly and uselessly, that nothing changed.
        await _data.LoadOfflineAsync();

        var changed = new List<string>();
        var same = new List<string>();
        var skipped = new List<string>();

        foreach (var (key, reload, dirty, count) in Refreshable)
        {
            string label = EditorStrings.Get(SectionLabelKey(key));
            if (dirty()) { skipped.Add(label); continue; }

            string before = _lastSeen.GetValueOrDefault(key, "");
            string now = SourceStamp(key);
            int countBefore = count();

            reload();

            _lastSeen[key] = now;
            int countAfter = count();

            if (now == before) { same.Add(label); continue; }
            changed.Add(countAfter == countBefore
                ? label
                : EditorStrings.Format(EditorStrings.Refresh_SectionMoved,
                    ("Section", label), ("Before", countBefore), ("After", countAfter)));
        }

        var sb = new StringBuilder();
        sb.AppendLine(EditorStrings.Get(EditorStrings.Refresh_FromDisk));
        sb.AppendLine();
        sb.AppendLine(changed.Count > 0
            ? EditorStrings.Format(EditorStrings.Refresh_Changed, ("Sections", string.Join(", ", changed)))
            : EditorStrings.Get(EditorStrings.Refresh_NothingMoved));
        if (same.Count > 0)
            sb.AppendLine(EditorStrings.Format(EditorStrings.Refresh_SameCount, ("Sections", string.Join(", ", same))));
        if (skipped.Count > 0)
            sb.AppendLine(EditorStrings.Format(EditorStrings.Refresh_Skipped, ("Sections", string.Join(", ", skipped))));

        EditorLog.Info("Refresh done: {Changed} changed, {Same} unchanged, {Skipped} skipped.",
                       changed.Count, same.Count, skipped.Count);

        return sb.ToString().TrimEnd();
    }
}
