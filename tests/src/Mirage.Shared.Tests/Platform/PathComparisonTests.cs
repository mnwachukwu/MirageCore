using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// Whether two paths name the same place.
///
/// <para>🔴 The answer is genuinely different per platform, so these assertions are written against
/// <see cref="OperatingSystem"/> rather than against a fixed expectation. Windows and macOS match filenames
/// case-insensitively; Linux matches them case-sensitively. Hardcoding either answer would make this suite
/// pass on one platform and fail on the others, which is the same mistake the type exists to prevent.</para>
/// </summary>
[TestFixture]
public class PathComparisonTests
{
    // What the platform running this suite does with case.
    private static bool CaseSensitive => OperatingSystem.IsLinux();

    [Test]
    public void TheRule_FollowsThePlatform()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathComparison.Rule,
                Is.EqualTo(CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            Assert.That(PathComparison.Comparer,
                Is.EqualTo(CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase));
        });
    }

    [Test]
    public void AnIdenticalPath_IsTheSamePlaceEverywhere()
    {
        Assert.That(PathComparison.SameLocation("/srv/worlds/Demo Landia", "/srv/worlds/Demo Landia"), Is.True);
    }

    [Test]
    public void ADifferentPath_IsNeverTheSamePlace()
    {
        Assert.That(PathComparison.SameLocation("/srv/worlds/Demo Landia", "/srv/worlds/Reed Shallows"), Is.False);
    }

    /// <summary>🔴 The case that made this a type. On Linux these are two worlds and the recent list must
    /// keep both; on Windows and macOS they are one world listed twice.</summary>
    [Test]
    public void PathsDifferingOnlyInCase_FollowTheFilesystem()
    {
        bool same = PathComparison.SameLocation("/srv/worlds/World", "/srv/worlds/world");

        Assert.That(same, Is.EqualTo(!CaseSensitive),
            CaseSensitive
                ? "Linux has two folders here, and merging them loses one from the recent list."
                : "Windows and macOS have one folder here, and splitting it lists the same world twice.");
    }

    /// <summary>A folder picker and a stored setting routinely disagree about the trailing separator, and it
    /// carries no meaning either way.</summary>
    [Test]
    public void ATrailingSeparator_IsNotADifference()
    {
        string bare = Path.Combine("srv", "worlds", "Demo Landia");

        Assert.That(PathComparison.SameLocation(bare, bare + Path.DirectorySeparatorChar), Is.True);
    }

    /// <summary>A root is all separator, and trimming one off it would leave something that is not a path.
    /// It has to stay itself, and stay distinct from what sits inside it.</summary>
    [Test]
    public void ARootPath_SurvivesTheTrim()
    {
        string root = Path.GetPathRoot(Path.GetFullPath("."))!;

        Assert.Multiple(() =>
        {
            Assert.That(PathComparison.SameLocation(root, root), Is.True);
            Assert.That(PathComparison.SameLocation(root, Path.Combine(root, "worlds")), Is.False);
        });
    }

    /// <summary>Null is a real state here — no world has been opened yet — so it compares rather than throws.</summary>
    [Test]
    public void Null_ComparesWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PathComparison.SameLocation(null, null), Is.True);
            Assert.That(PathComparison.SameLocation(null, "/srv/worlds/Demo Landia"), Is.False);
            Assert.That(PathComparison.SameLocation("/srv/worlds/Demo Landia", null), Is.False);
        });
    }

    /// <summary>Textual only. Two spellings of one folder read as two, which is the documented limit — this
    /// pins it so a later change to resolve them is a deliberate one.</summary>
    [Test]
    public void ADotSegment_IsNotResolved()
    {
        Assert.That(PathComparison.SameLocation("/srv/worlds/Demo Landia", "/srv/worlds/./Demo Landia"), Is.False);
    }
}
