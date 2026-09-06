using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One entry of the recent-worlds menu.
///
/// <para>It carries its own command, so the menu item binds to itself and never reaches back up the visual
/// tree for the window's view-model — the menu's Style names this type and every path in it is checked
/// against it at build time.</para>
///
/// <para>A world's name and its path are its own; only the word for an unnamed one is localized.</para>
/// </summary>
public sealed class RecentWorldViewModel(string path, Func<string, Task> open)
{
    /// <summary>The whole path, shown on hover.</summary>
    public string Path { get; } = path;

    /// <summary>What the menu shows: the world's own name where it has one. A name is what an operator
    /// picked to tell this world from a copy of it, so it identifies the entry better than any part of a
    /// path can.
    ///
    /// <para>An unnamed world is shown as "Untitled World" WITH its folder, because several of them read
    /// alike otherwise and a menu of identical rows is no menu. The whole path stays on hover either way,
    /// for two worlds sharing a name and for a folder moved out from under the list.</para></summary>
    public string Display { get; } = NameOf(path) is { Length: > 0 } name
        ? name
        : EditorStrings.Format(EditorStrings.World_UntitledAt, ("Folder", Leaf(path)));

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(path));

    // Read straight off disk rather than cached beside the path: a name the operator has since changed, or a
    // folder that has since gone, would both be remembered wrong. Eight small files, read when the menu opens.
    private static string NameOf(string worldPath)
    {
        try
        {
            string file = System.IO.Path.Combine(worldPath, WorldManifest.FileName);
            if (!File.Exists(file)) return "";
            return JsonSerializer.Deserialize<WorldManifest>(File.ReadAllText(file))?.Name.Trim() ?? "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return "";
        }
    }

    /// <summary>The folder a path ends in, which is what tells two unnamed worlds apart. Reads both
    /// separators on every platform: this list is a settings file that travels.</summary>
    private static string Leaf(string path) => PortablePath.Leaf(path);
}
