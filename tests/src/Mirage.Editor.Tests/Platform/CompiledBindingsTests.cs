using NUnit.Framework;
using System.Reflection;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Compiled bindings stay on, and nothing opts out of them.
///
/// <para>With them on, a binding to a property that does not exist is a build error naming the file and
/// line. With them off, the same binding compiles, constructs, renders an empty control, throws nothing and
/// logs nothing — so the only symptom is a field that silently shows nothing, and only if somebody opens
/// that screen and looks.</para>
///
/// <para>The compiler checks every path, so nothing here re-checks one. What it cannot see is the setting
/// being turned back off, or one file exempting itself — which are the three ways the protection is lost,
/// and are what these pin.</para>
/// </summary>
[TestFixture]
public class CompiledBindingsTests
{
    private static string SourceRoot()
    {
        string dir = typeof(CompiledBindingsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(dir), Is.True, $"Editor source root not found: {dir}");
        return dir;
    }

    private static string[] ViewFiles() =>
        [.. Directory.GetFiles(SourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p, StringComparer.Ordinal)];

    [Test]
    public void TheProject_CompilesItsBindings()
    {
        string csproj = Path.Combine(SourceRoot(), "Mirage.Editor.csproj");
        Assert.That(File.Exists(csproj), Is.True, csproj);

        string xml = File.ReadAllText(csproj).Replace(" ", "");

        Assert.That(xml, Does.Contain("<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>"),
            "Turning this off makes every wrong binding path silent again.");
    }

    /// <summary>A file, or one element in it, can exempt itself. Then its bindings resolve by reflection
    /// while everything around them is checked, which is the hardest version to notice.</summary>
    [Test]
    public void NoView_OptsOutOfCompiledBindings()
    {
        var offenders = ViewFiles()
            .Where(f => File.ReadAllText(f).Replace(" ", "").Contains("x:CompileBindings=\"False\"",
                                                                     StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(offenders, Is.Empty, "These exempt themselves from binding checks: " + string.Join(", ", offenders));
    }

    /// <summary>The per-binding escape hatch. One of these resolves by reflection however the project is
    /// configured, so it fails the same silent way.</summary>
    [Test]
    public void NoBinding_FallsBackToReflection()
    {
        var offenders = ViewFiles()
            .Where(f => File.ReadAllText(f).Contains("{ReflectionBinding", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(offenders, Is.Empty, "These use a reflection binding: " + string.Join(", ", offenders));
    }
}
