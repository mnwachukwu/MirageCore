using NUnit.Framework;
using System.Reflection;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// Naming rules that answer the same on every platform.
///
/// <para>🔴 The cases below are not hypothetical. <c>a:b</c> and <c>why?</c> are legal directory names on
/// Linux and macOS and illegal on Windows, so a check built on <see cref="Path.GetInvalidFileNameChars"/>
/// passes them on two of the three platforms the game ships to — and a world folder, an exported PNG or a
/// config file named that way cannot be cloned, unzipped or checked out on Windows at all.</para>
///
/// <para>A Windows developer cannot reach these by running the suite locally: there the platform set and the
/// portable set are identical and every assertion passes either way. They fail only on the Linux and macOS
/// CI legs, which is exactly why the convention test at the bottom reads the source instead.</para>
/// </summary>
[TestFixture]
public class PortableFileNameTests
{
    /// <summary>Illegal on Windows, legal on POSIX — the whole point of the type.</summary>
    [TestCase("what/now")]
    [TestCase("a:b")]
    [TestCase("why?")]
    [TestCase("star*")]
    [TestCase("pipe|line")]
    [TestCase("quote\"d")]
    [TestCase("back\\slash")]
    [TestCase("less<than")]
    [TestCase("more>than")]
    public void ANameWindowsRefuses_IsRejectedEverywhere(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    [TestCase("Demo Landia")]
    [TestCase("Untitled World")]
    [TestCase("Mundo sin titulo")]
    [TestCase("Monde sans titre")]
    [TestCase("map-0007-Town Square.png")]
    [TestCase("under_scored")]
    [TestCase(".hidden")]
    public void AnOrdinaryName_IsAccepted(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.True);
    }

    /// <summary>Every locale's untitled-world folder name has to be creatable on every platform, since that
    /// is the folder an unnamed world gets.</summary>
    [Test]
    public void EveryUntitledWorldName_IsPortable()
    {
        foreach (string name in new[] { "Untitled World", "Mundo sin titulo", "Monde sans titre", "Mundo sem titulo" })
        {
            Assert.That(PortableFileName.IsValid(name), Is.True, name);
        }
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase("..")]
    public void ANameThatIsNotAName_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    /// <summary>Windows drops a trailing dot or space without saying so, so the folder that appears is not
    /// the one that was asked for — and a later lookup by the requested name misses it.</summary>
    [TestCase("trailing ")]
    [TestCase("trailing.")]
    public void ANameWindowsWouldSilentlyRewrite_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    /// <summary>DOS device names are refused by Windows in any casing and at any extension. POSIX takes them,
    /// so a world called <c>aux</c> authored on Linux is unopenable on Windows.</summary>
    [TestCase("NUL")]
    [TestCase("nul")]
    [TestCase("CON")]
    [TestCase("aux")]
    [TestCase("COM1")]
    [TestCase("lpt9")]
    [TestCase("nul.png")]
    public void AReservedDeviceName_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    [TestCase("CONtinue")]
    [TestCase("NULL")]
    [TestCase("COM10")]
    public void ANameThatMerelyStartsLikeADevice_IsAccepted(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.True);
    }

    // ── Sanitize ────────────────────────────────────────────────────────────────

    [TestCase("a:b", "a_b")]
    [TestCase("why?", "why_")]
    [TestCase("what/now", "what_now")]
    [TestCase("Town Square", "Town Square")]
    [TestCase("trailing.", "trailing")]
    [TestCase("trailing ", "trailing")]
    public void Sanitize_RewritesWhatItMust_AndLeavesTheRest(string input, string expected)
    {
        Assert.That(PortableFileName.Sanitize(input), Is.EqualTo(expected));
    }

    /// <summary>Whatever goes in, what comes out is usable — including the inputs that sanitize to nothing.</summary>
    [TestCase("")]
    [TestCase("???")]
    [TestCase("...")]
    [TestCase("   ")]
    [TestCase("NUL")]
    [TestCase("a:b")]
    [TestCase("Town Square")]
    public void Sanitize_AlwaysProducesAValidName(string input)
    {
        Assert.That(PortableFileName.IsValid(PortableFileName.Sanitize(input)), Is.True);
    }

    /// <summary>The hardcoded set has to remain at least as strict as whatever platform is running the
    /// suite — the one place asking the OS is the right move, because here it is the lower bound being
    /// checked rather than the rule being enforced. On Windows this pins the full 41; on Linux and macOS it
    /// confirms the two.</summary>
    [Test]
    public void TheHardcodedSet_CoversThisPlatformsOwn()
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            // Control characters are covered by the c < ' ' arm rather than by the array.
            if (c < ' ') continue;
            Assert.That(PortableFileName.InvalidChars, Does.Contain(c),
                $"This platform rejects U+{(int)c:X4} and PortableFileName does not.");
        }
    }

    // ── The convention ──────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 Production code never asks the OS what a legal filename is, or which character separates a path.
    ///
    /// <para>A source scan because it CANNOT be a behavioural test. On Windows every one of these members
    /// returns what the portable replacement declares — the 41-character set, and a backslash plus a forward
    /// slash — so code using them behaves identically here and no assertion can tell the two apart. They
    /// diverge only on Linux and macOS, where the invalid set is two characters and BOTH separator members
    /// are <c>/</c>. That is the whole shape of this bug class: invisible on the machine that writes it, red
    /// on CI for everyone else. It has shipped twice.</para>
    ///
    /// <para>Both replacements live in Shared and are the only files allowed to name these:
    /// <see cref="PortableFileName"/> for what a name may contain, <see cref="PortablePath"/> for reading a
    /// path that travels. <c>Path.Combine</c>, <c>GetDirectoryName</c> and
    /// <c>TrimEndingDirectorySeparator</c> are NOT on this list and stay correct for a local path — the
    /// members below are the ones whose VALUE changes per platform.</para>
    /// </summary>
    [Test]
    public void NoProductionCode_AsksThePlatformWhatALegalNameOrSeparatorIs()
    {
        string[] platformDependent =
        [
            "GetInvalidFileNameChars",
            "GetInvalidPathChars",
            "DirectorySeparatorChar",     // also catches AltDirectorySeparatorChar
            "VolumeSeparatorChar",
            "PathSeparator",
        ];

        string root = typeof(PortableFileNameTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");

        var offenders = new List<string>();
        foreach (string area in new[] { "client", "server", "editor", "shared" })
        {
            string dir = Path.Combine(root, area);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                // The two files allowed to name them are the two that replace them.
                if (Path.GetFileName(file) is "PortableFileName.cs" or "PortablePath.cs") continue;

                string text = File.ReadAllText(file);
                foreach (string member in platformDependent)
                {
                    if (!text.Contains(member)) continue;
                    // TrimEndingDirectorySeparator is a method, not the char, and parses both on Windows.
                    if (member == "DirectorySeparatorChar"
                        && !text.Replace("TrimEndingDirectorySeparator", "").Contains(member)) continue;
                    offenders.Add($"{rel}  ({member})");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "These ask the CURRENT OS a question whose answer has to hold on every platform the game ships "
            + "to. Windows rejects 41 filename characters and POSIX rejects 2; Windows separates paths with "
            + "two different characters and POSIX with one. Code keyed on them enforces whatever the "
            + "authoring machine happened to do. Use PortableFileName (names) or PortablePath (reading a "
            + "path that travels); for a path that never leaves this machine, Path.Combine / "
            + "GetDirectoryName / TrimEndingDirectorySeparator are correct:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
