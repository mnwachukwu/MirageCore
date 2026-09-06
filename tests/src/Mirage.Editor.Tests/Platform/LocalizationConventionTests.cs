using Avalonia.Controls;
using Mirage.Editor.Localization;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Stops live language switching from rotting as views are added. The editor's section views
/// assign their captions in code rather than binding them, and each view is constructed once and
/// reused for the life of the window — so a view with no re-apply hook silently keeps the old
/// language until the editor restarts.
/// </summary>
[TestFixture]
public class LocalizationConventionTests
{
    /// <summary>The rule is "derive from <see cref="LocalizedUserControl"/>", not "remember to
    /// subscribe": the base class owns the subscribe/unsubscribe and the re-apply-on-attach, so a
    /// new view only decides WHAT to re-apply, never WHEN. Dialogs are deliberately out of scope —
    /// they are Windows, constructed fresh per open, so they pick up the language for free.</summary>
    [Test]
    public void EveryEditorView_DerivesFromLocalizedUserControl()
    {
        var offenders = typeof(EditorStrings).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace == "Mirage.Editor.Views")
            .Where(t => typeof(UserControl).IsAssignableFrom(t))
            .Where(t => !typeof(LocalizedUserControl).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "These views will not refresh on a language change. Derive each from "
            + "LocalizedUserControl and move its caption assignments into an ApplyStrings() "
            + "override: " + string.Join(", ", offenders));
    }

    /// <summary>The check the reflection one cannot make. Reflection sees a type's base class, but
    /// not whether it USES localized strings — and that is the condition that actually needs a
    /// refresh hook. A plain <c>Control</c> that resolves a caption inside <c>Render</c> is the
    /// case in point: it is not a <c>UserControl</c>, so the rule above skips it, yet Avalonia is
    /// retained-mode and only calls <c>Render</c> on invalidation, so it silently keeps the old
    /// language. (<c>TilePaletteControl</c> is such a control.) Reading the sources is the only
    /// way to key off the real trigger, so this scans for the string calls directly.</summary>
    [Test]
    public void EverySourceUsingEditorStrings_HasARefreshHook()
    {
        string root = EditorSourceRoot();
        var offenders = new List<string>();
        foreach (string dir in new[] { "Views", "Controls" })
        {
            string path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;
            foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(file);
                bool usesStrings = src.Contains("EditorStrings.Get") || src.Contains("EditorStrings.Format");
                if (!usesStrings) continue;
                // A Window is a dialog: constructed fresh every time it is opened, so it resolves
                // its captions in the current language for free and needs no re-apply hook. Only
                // the long-lived controls that outlive a language switch do.
                if (src.Contains(": Window")) continue;
                // Either inherit the hook, or wire one explicitly.
                bool hasHook = src.Contains(": LocalizedUserControl") || src.Contains("LanguageChanged");
                if (!hasHook) offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.That(offenders, Is.Empty,
            "These files render localized text but never react to a language change. Derive from "
            + "LocalizedUserControl, or subscribe to EditorStrings.LanguageChanged and refresh "
            + "(InvalidateVisual for a render-time caption): " + string.Join(", ", offenders));
    }

    /// <summary>The same rule for view-models, which is where the nav sections and the record-list
    /// rows get their text — the half the Views/Controls scan above does not see. Aggregated per
    /// CLASS, not per file: <c>MapEditorViewModel</c> is split across a dozen partials, and only
    /// one of them carries the subscription.</summary>
    [Test]
    public void EveryViewModelUsingEditorStrings_HasARefreshHook()
    {
        // A view-model may be refreshed by its OWNER instead of subscribing itself. Those cases are
        // listed explicitly rather than pattern-matched, so adding one stays a conscious decision:
        //   *RowViewModel    - the list re-raises FilteredItems, which re-reads every row's DisplayName
        //   *DialogViewModel - dialogs are built fresh per open
        //   SectionViewModel - MainWindowViewModel.OnLanguageChanged re-raises each section's label
        //   *Option          - combo items built by a *DialogViewModel (auto-save, logging), so they
        //                      inherit the fresh-per-open rule above; the scan sees them as their own
        //                      classes. Named one by one rather than matched on the suffix, so a new
        //                      one stays a decision
        //   AutoSaveMessages - status lines, which are deliberately NOT re-localized: each is a record
        //                      of something that already happened, in the language it happened in
        static bool RefreshedByOwner(string cls) =>
            cls.EndsWith("RowViewModel", StringComparison.Ordinal)
            || cls.EndsWith("DialogViewModel", StringComparison.Ordinal)
            || cls is "SectionViewModel" or "AutoSaveIntervalOption" or "AutoSaveReachOption"
                   or "LogLevelOption" or "LogRetentionOption" or "AutoSaveMessages"
                   // Rebuilt from the recent-worlds setting every time MainWindowViewModel raises the
                   // property, which OnLanguageChanged does.
                   or "RecentWorldViewModel";

        var uses = new HashSet<string>(StringComparer.Ordinal);
        var hooked = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(EditorSourceRoot(), "ViewModels"), "*.cs"))
        {
            string src = File.ReadAllText(file);
            bool usesStrings = src.Contains("EditorStrings.Get") || src.Contains("EditorStrings.Format");
            // Inheriting the base's subscription counts as having the hook.
            bool hasHook = src.Contains("LanguageChanged") || src.Contains(": EditorViewModelBase<");
            // Anchored to a real declaration: the line must START with modifiers then `class`, so
            // prose like "// a class ID" or "// the class deals ..." is not mistaken for a type.
            foreach (Match m in Regex.Matches(src,
                         @"(?m)^\s*(?:(?:public|internal|private|protected|abstract|sealed|static|partial)\s+)*class\s+(\w+)"))
            {
                if (usesStrings) uses.Add(m.Groups[1].Value);
                if (hasHook) hooked.Add(m.Groups[1].Value);
            }
        }

        var offenders = uses.Where(c => !hooked.Contains(c) && !RefreshedByOwner(c))
                            .OrderBy(c => c, StringComparer.Ordinal)
                            .ToList();

        Assert.That(offenders, Is.Empty,
            "These view-models resolve localized text but never react to a language change. Subscribe "
            + "to EditorStrings.LanguageChanged and re-raise the affected properties (or document the "
            + "owner that refreshes them): " + string.Join(", ", offenders));
    }

    private static string EditorSourceRoot()
    {
        string root = typeof(LocalizationConventionTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Editor source root not found: {root}");
        return root;
    }

    /// <summary>Guards the broadcast itself: the base class is only useful if <c>Load</c> actually
    /// announces the swap. Without this, every view could be wired correctly and still never
    /// re-apply.</summary>
    [Test]
    public void Load_RaisesLanguageChanged()
    {
        int fired = 0;
        void Handler() => fired++;

        EditorStrings.LanguageChanged += Handler;
        try
        {
            EditorStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"), "en");
        }
        finally
        {
            EditorStrings.LanguageChanged -= Handler;
        }

        Assert.That(fired, Is.EqualTo(1), "EditorStrings.Load must raise LanguageChanged exactly once.");
    }
}
