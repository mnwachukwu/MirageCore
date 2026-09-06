using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Auto-save: one ticker for the whole app, and a last-saved stamp per editor.
///
/// <para>One ticker rather than a timer per editor, and the stamps are what make that work — an editor
/// you navigated away from still reaches its interval and still gets written. A per-editor timer that
/// only ran while its section was showing would never fire for background work at all, and would restart
/// from zero every time you switched back, which on a five-minute interval means most sessions never
/// auto-save anything.</para>
///
/// <para>OFFLINE ONLY. Online, a save is a packet into a running game that the server can refuse on
/// access grounds, and a refusal on a timer would repeat silently every interval. The configuration
/// window says so and disables itself while connected.</para>
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>Section ids that can auto-save. Accounts is absent on purpose: those records are the
    /// server's, they have no dirty tracking to drive a schedule, and every save is a live write.</summary>
    public static readonly string[] AutoSaveSections =
        ["Maps", "MapGroups", "Items", "NPCs", "Shops", "Spells", "Classes", "Quests", "Conversations"];

    // When each section is next due. Seeded on the first tick, so a slow startup does not count against
    // the first interval.
    private readonly AutoSaveSchedule _autoSaveSchedule = new();

    /// <summary>The editor behind a section id, or null for one that cannot auto-save.</summary>
    private IAutoSaveTarget? AutoSaveTargetFor(string section) => section switch
    {
        "Maps" => MapEditor,
        "MapGroups" => MapGroupEditor,
        "Items" => ItemEditor,
        "NPCs" => NpcEditor,
        "Shops" => ShopEditor,
        "Spells" => SpellEditor,
        "Classes" => ClassEditor,
        "Quests" => QuestEditor,
        "Conversations" => ConversationEditor,
        _ => null,
    };

    /// <summary>The Auto-Save menu item's caption: what it does when it is available, and why it is not
    /// when connected. Bound rather than assigned in ApplyStrings, because it has to follow the connection
    /// as well as the language.</summary>
    public string AutoSaveMenuItemLabel => EditorStrings.Get(IsOnline
        ? EditorStrings.AutoSave_DisabledOnline
        : EditorStrings.AutoSave_ConfigureItem);

    /// <summary>Re-read the menu caption. Called on a language change and whenever the connection moves.</summary>
    public void NotifyAutoSaveMenuChanged() => OnPropertyChanged(nameof(AutoSaveMenuItemLabel));

    /// <summary>This editor's configuration, defaulted when the settings file has never seen it.</summary>
    public static AutoSaveSetting SettingFor(string section) =>
        AppSettings.Current.AutoSave.TryGetValue(section, out var s) ? s : new AutoSaveSetting();

    /// <summary>One tick of the app-wide ticker. Takes the clock rather than reading it, so the schedule
    /// can be tested without waiting for it.</summary>
    public async Task AutoSaveTickAsync(DateTime now)
    {
        // Never while connected: see the class remarks.
        if (IsOnline) return;

        foreach (string section in AutoSaveSections)
        {
            if (!_autoSaveSchedule.IsDue(section, SettingFor(section), now)) continue;

            var editor = AutoSaveTargetFor(section);
            if (editor is null) continue;

            // The record's name has to be read BEFORE the save: saving clears the dirty flags and the
            // map editor's selection can move, and the line is about what was just written.
            string openName = editor.OpenRecordName;
            try
            {
                int saved = await editor.AutoSaveAsync(SettingFor(section).Reach);
                if (saved == 0) continue;   // nothing was dirty; say nothing rather than announce a no-op
                editor.StatusMessage = AutoSaveMessages.For(saved, openName, now);
            }
            catch (Exception ex)
            {
                editor.StatusMessage = EditorStrings.Format(EditorStrings.AutoSave_Failed,
                    ("Error", ex.Message));
            }
        }
    }

    /// <summary>Forget every stamp — used when the settings change, so a freshly enabled editor waits a
    /// full interval rather than firing on the next tick.</summary>
    public void ResetAutoSaveSchedule() => _autoSaveSchedule.Reset();
}
