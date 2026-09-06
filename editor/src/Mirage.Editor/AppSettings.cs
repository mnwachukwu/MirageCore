using System.Text.Json;

namespace Mirage.Editor;

public sealed class AppSettings
{
    public string DefaultServerHost { get; set; } = "localhost";
    public int DefaultServerPort { get; set; } = 4000;
    public string Language { get; set; } = "en";

    /// <summary>The world folder last opened, so it can be offered again and reopened on request. A world
    /// is a directory holding maps/, npcs/, items/ and the rest — the editor never keeps one of its own.</summary>
    public string? LastWorldPath { get; set; }

    /// <summary>Whether to reopen <see cref="LastWorldPath"/> at startup. Off by default: the editor opens
    /// on nothing and asks, so launching it never silently attaches to a world you had finished with.</summary>
    public bool ReopenLastWorld { get; set; }

    /// <summary>Where the folder picker was last browsing — the FOLDER a world was chosen from, not the
    /// world itself, so the next pick opens among that world's siblings rather than inside its `maps/`
    /// and `items/`. Worlds live wherever an operator keeps them, which is rarely next to the
    /// application.</summary>
    public string? LastWorldBrowsePath { get; set; }

    /// <summary>Worlds opened before, most recent first, for the File menu.</summary>
    public List<string> RecentWorlds { get; set; } = [];

    /// <summary>Optional path to the editable graphics dir (tilesets/sprites/items). Null/empty = the
    /// per-user data dir. A relative path is resolved against the install dir, never the CWD.</summary>
    public string? AssetsDir { get; set; }

    // Window state
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    /// <summary>Whether the section rail is showing icons only. Persisted with the rest of the window
    /// layout, so the editor reopens in the shape it was left in.</summary>
    public bool RailCollapsed { get; set; }

    // Map editor panel widths/heights
    public double MapEditorLeftWidth { get; set; } = 256;
    public double MapEditorRightWidth { get; set; } = 300;
    public double MapEditorRightBottomHeight { get; set; } = 220;
    // Whether the map editor's animated-tile preview starts on. Default on so mis-flagged tiles are visible.
    public bool MapEditorAnimPreview { get; set; } = true;

    // Other editor left-panel widths
    public double ItemEditorLeftWidth { get; set; } = 200;
    public double ItemEditorRightWidth { get; set; } = 220;
    public double NpcEditorLeftWidth { get; set; } = 200;
    public double NpcEditorRightWidth { get; set; } = 220;
    public double SpellEditorLeftWidth { get; set; } = 200;
    public double SpellEditorRightWidth { get; set; } = 220;
    public double ShopEditorLeftWidth { get; set; } = 200;
    public double ClassEditorLeftWidth { get; set; } = 200;
    public double ClassEditorRightWidth { get; set; } = 220;
    public double QuestEditorLeftWidth { get; set; } = 200;
    public double QuestEditorRightWidth { get; set; } = 220;
    public double ConversationEditorLeftWidth { get; set; } = 200;
    /// <summary>Draw the dialogue nodes as the branching graph rather than as a stack of cards.</summary>
    public bool ConversationEditorGraphView { get; set; } = true;
    public double MapGroupEditorLeftWidth { get; set; } = 200;
    public double MapGroupEditorRightWidth { get; set; } = 220;

    // World Preview window. It is not a child of the main window, so it carries its own geometry rather
    // than being swept up by MainWindow.SaveWindowState.
    /// <summary>Whether the World Preview window is showing. Saved the moment it is toggled, so the
    /// window comes back on next launch even if the session ends badly.</summary>
    public bool WorldPreviewOpen { get; set; }
    public double? WorldPreviewX { get; set; }
    public double? WorldPreviewY { get; set; }
    public double? WorldPreviewWidth { get; set; }
    public double? WorldPreviewHeight { get; set; }

    // Console window, on the same terms.
    /// <summary>Whether the Console window is showing. It reads a log that is kept whether the window is
    /// open or not, so reopening it costs nothing and loses nothing.</summary>
    public bool ConsoleOpen { get; set; }
    public double? ConsoleX { get; set; }
    public double? ConsoleY { get; set; }
    public double? ConsoleWidth { get; set; }
    public double? ConsoleHeight { get; set; }

    // Layer Visibility window, on the same terms.
    /// <summary>Whether the Layer Visibility window is showing. The WINDOW is remembered; which layers
    /// were hidden is deliberately not. A layer left put away from a previous session is exactly the trap
    /// this window exists to prevent, and the author has no reason to suspect it.</summary>
    public bool LayerVisibilityOpen { get; set; }
    public double? LayerVisibilityX { get; set; }
    public double? LayerVisibilityY { get; set; }
    public double? LayerVisibilityWidth { get; set; }
    public double? LayerVisibilityHeight { get; set; }
    /// <summary>Canvas scale; 1.0 is one screen pixel per map pixel. A quarter scale on a world nobody has
    /// opened before: readable tiles, and enough maps in view to see the shape of a region.
    ///
    /// <para>Only the starting point. Every zoom is written straight back here, so an existing install keeps
    /// whatever it was last left at and never sees this value again.</para></summary>
    public double WorldPreviewZoom { get; set; } = 0.25;

    /// <summary>Per-editor auto-save, keyed by the section id ("Maps", "Items", …). A section missing
    /// from the map is off with the defaults; Accounts never appears, because those records are the
    /// server's and are saved one deliberate press at a time.</summary>
    public Dictionary<string, AutoSaveSetting> AutoSave { get; set; } = [];

    /// <summary>File-logging capture level and how long the files are kept. Configured from Help > Logging.</summary>
    public LoggingSetting Logging { get; set; } = new();

    private static AppSettings? _instance;
    public static AppSettings Current => _instance ??= Load();

    // Editor settings persist in the per-user config dir. appsettings.json is not bundled; until the
    // user changes a setting the app falls back to the in-code defaults above.
    private static string SettingsFile => Path.Combine(EditorPaths.Config, "appsettings.json");

    private static AppSettings Load()
    {
        string path = SettingsFile;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            string path = SettingsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
