using NUnit.Framework;
using System.Reflection;
using System.Xml.Linq;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Ctrl+S saves the open record and Ctrl+Shift+S saves every dirty one, on whichever section is
/// showing. Both are wired as a <c>HotKey</c> on the pane's own Save button rather than as bindings
/// on the window, so each shortcut inherits three things it would otherwise have to re-derive: the
/// button's dirty gate (Avalonia's HotKeyManager checks <c>IsEffectivelyEnabled</c> and the command's
/// CanExecute before firing), the command itself, and the scope — the binding is registered on the
/// top level when the view attaches and removed when it detaches, and only the active section's view
/// is ever attached.
///
/// <para>The cost of that is a per-button attribute, and a missing one is silent: the pane looks
/// finished, saves fine by mouse, and simply ignores the key. Nothing else in the suite can see it,
/// because the wiring exists only in markup.</para>
/// </summary>
[TestFixture]
public class SaveShortcutTests
{
    private const string SaveOpenRecord = "Ctrl+S";
    private const string SaveEveryRecord = "Ctrl+Shift+S";

    // Maps saves the open record through its own command; every other pane shares SaveCommand.
    private static readonly string[] SaveOpenRecordCommands = ["{Binding SaveCommand}", "{Binding SaveMapCommand}"];
    private const string SaveEveryRecordCommand = "{Binding SaveAllCommand}";

    // Every section pane, by the naming convention App.axaml's DataTemplates already rely on.
    private static string[] EditorViews() =>
        Directory.GetFiles(Path.Combine(EditorSourceRoot(), "Views"), "*EditorView.axaml")
                 .OrderBy(p => p, StringComparer.Ordinal)
                 .ToArray();

    private static string EditorSourceRoot()
    {
        string root = typeof(SaveShortcutTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Editor source root not found: {root}");
        return root;
    }

    private static List<XElement> Controls(string file) => XDocument.Load(file).Descendants().ToList();

    private static List<XElement> Bound(string file, string gesture) =>
        Controls(file).Where(e => (string?)e.Attribute("HotKey") == gesture).ToList();

    [Test]
    public void EveryPane_BindsTheSaveShortcut()
    {
        var views = EditorViews();
        Assert.That(views, Is.Not.Empty, "Found no *EditorView.axaml to scan.");

        var offenders = views.Where(f => Bound(f, SaveOpenRecord).Count == 0)
                             .Select(Path.GetFileName)
                             .ToList();

        Assert.That(offenders, Is.Empty,
            $"These panes ignore {SaveOpenRecord}. Add HotKey=\"{SaveOpenRecord}\" to the pane's Save "
            + "button: " + string.Join(", ", offenders));
    }

    /// <summary>Keyed off the button rather than off the pane: Accounts saves one account at a time and
    /// has no Save All at all, so the rule has to be "a Save All button carries the gesture", not
    /// "every pane binds it".</summary>
    [Test]
    public void EverySaveAllButton_BindsTheSaveAllShortcut()
    {
        var withSaveAll = EditorViews()
            .Where(f => Controls(f).Any(e => (string?)e.Attribute("Command") == SaveEveryRecordCommand))
            .ToList();
        Assert.That(withSaveAll, Has.Count.GreaterThan(1),
            "Found almost no Save All buttons to scan - the rule below would pass vacuously.");

        var offenders = withSaveAll.Where(f => Bound(f, SaveEveryRecord).Count == 0)
                                   .Select(Path.GetFileName)
                                   .ToList();

        Assert.That(offenders, Is.Empty,
            $"These panes have a Save All button that ignores {SaveEveryRecord}: "
            + string.Join(", ", offenders));
    }

    /// <summary>Two controls claiming one gesture both register a binding on the top level and the
    /// first one added wins, which makes the loser's failure look like a broken command.</summary>
    [Test]
    public void NoPane_ClaimsAGestureTwice()
    {
        var problems = new List<string>();
        foreach (string file in EditorViews())
        {
            foreach (string gesture in new[] { SaveOpenRecord, SaveEveryRecord })
            {
                int count = Bound(file, gesture).Count;
                if (count > 1) problems.Add($"{Path.GetFileName(file)}: {gesture} claimed {count} times");
            }
        }

        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The two buttons sit side by side in every footer, one line apart in the markup, and
    /// swapping their gestures still builds and still saves — just not what was asked for.</summary>
    [Test]
    public void EachGesture_SitsOnTheButtonItNames()
    {
        var problems = new List<string>();

        foreach (string file in EditorViews())
        {
            string name = Path.GetFileName(file);
            foreach (var control in Bound(file, SaveOpenRecord))
            {
                string? command = (string?)control.Attribute("Command");
                if (!SaveOpenRecordCommands.Contains(command))
                    problems.Add($"{name}: {SaveOpenRecord} is bound to {command ?? "no command"}");
            }
            foreach (var control in Bound(file, SaveEveryRecord))
            {
                string? command = (string?)control.Attribute("Command");
                if (command != SaveEveryRecordCommand)
                    problems.Add($"{name}: {SaveEveryRecord} is bound to {command ?? "no command"}");
            }
            // Without a gate a shortcut fires on a clean record: HotKeyManager asks the button
            // whether it is enabled, and an ungated button always says yes.
            foreach (var control in Bound(file, SaveOpenRecord).Concat(Bound(file, SaveEveryRecord)))
            {
                if (control.Attribute("IsEnabled") is null)
                    problems.Add($"{name}: the {(string?)control.Attribute("HotKey")} button has no IsEnabled gate");
            }
        }

        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
    }
}
