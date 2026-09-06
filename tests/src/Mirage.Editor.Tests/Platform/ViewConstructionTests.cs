using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using System.Reflection;

[assembly: AvaloniaTestApplication(typeof(Mirage.Editor.Tests.Platform.ViewTestApp))]

namespace Mirage.Editor.Tests.Platform;

/// <summary>The editor's own <see cref="App"/> on a headless platform: the real styles, resources and
/// theme, so a view that resolves a brush or a control theme is checked against the ones it ships
/// with.</summary>
public sealed class ViewTestApp : App
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ViewTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Every window and view is built, on a headless platform, with nothing on screen.
///
/// <para>This is the only check that runs the XAML. <c>AvaloniaUseCompiledBindingsByDefault</c> is off, so
/// a view is resolved by reflection when it is constructed and a broken one compiles perfectly: an
/// undeclared namespace prefix, a missing <c>x:Name</c>, a control theme that is not there. None of it
/// shows up until the window is opened, and for <see cref="MainWindow"/> that means the editor does not
/// start.</para>
///
/// <para>Views are found by reflection rather than listed, so a new one is covered the moment it
/// exists.</para>
/// </summary>
[TestFixture]
public class ViewConstructionTests
{
    private static IEnumerable<Type> Views() =>
        typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>One pass over everything. Each view is built once and only once: FluentAvalonia registers
    /// its window type with the process on the first one, and a second build of the same window collides
    /// with that registration rather than saying anything about the XAML.</summary>
    [AvaloniaTest]
    public void EveryView_Constructs()
    {
        var built = new List<Type>();
        var failures = new List<string>();

        foreach (var type in Views())
        {
            try
            {
                Activator.CreateInstance(type);
                built.Add(type);
            }
            catch (TargetInvocationException ex)
            {
                failures.Add($"{type.Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty,
            "these views throw when built, which is what happens the moment one is opened:\n  "
            + string.Join("\n  ", failures));

        // The window the editor opens with. Its failure is not a broken panel somewhere, it is an editor
        // that does not start, so the sweep is checked for having actually reached it.
        Assert.That(built, Does.Contain(typeof(MainWindow)),
            "the sweep no longer covers MainWindow, so nothing here would notice the editor failing to open");
    }
}
