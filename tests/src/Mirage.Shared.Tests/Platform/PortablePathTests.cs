using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// Reading a path that travels between machines.
///
/// <para>🔴 This guards a bug that has already shipped once: a stored path written on Windows lost its folder
/// name when read on Linux, because <c>Path.DirectorySeparatorChar</c> and <c>AltDirectorySeparatorChar</c>
/// are BOTH <c>/</c> there and neither can see a backslash.</para>
///
/// <para>🔴 NOTHING in this fixture can fail on Windows. There <c>/</c> is the alt separator, so a
/// platform-keyed implementation produces the same two characters and satisfies every assertion here — the
/// values only diverge on POSIX. That is true of the behavioural cases AND of
/// <see cref="TheSeparators_AreBothOfThem"/>, so treat these as the Linux and macOS legs' assertions.</para>
///
/// <para>The check that bites on Windows is
/// <c>PortableFileNameTests.NoProductionCode_AsksThePlatformWhatALegalNameOrSeparatorIs</c>, which reads the source
/// instead of running it. A platform difference cannot be observed from the platform that lacks it.</para>
/// </summary>
[TestFixture]
public class PortablePathTests
{
    [Test]
    public void TheSeparators_AreBothOfThem()
    {
        Assert.That(PortablePath.Separators, Is.EquivalentTo(new[] { '\\', '/' }),
            "Both, on every platform. Path.DirectorySeparatorChar and AltDirectorySeparatorChar are the same "
            + "character on Linux and macOS, so deriving this from the platform loses the backslash there.");
    }

    [TestCase(@"D:\worlds\Brightwater", Description = "written on Windows")]
    [TestCase("/srv/worlds/Brightwater", Description = "written on Linux or macOS")]
    [TestCase(@"D:\worlds\Brightwater\", Description = "Windows, trailing separator")]
    [TestCase("/srv/worlds/Brightwater/", Description = "POSIX, trailing separator")]
    public void Leaf_IsTheLastSegment_WhicheverSlashWroteIt(string path)
    {
        Assert.That(PortablePath.Leaf(path), Is.EqualTo("Brightwater"));
    }

    [Test]
    public void APathWithNoSeparator_IsItsOwnLeaf()
    {
        Assert.That(PortablePath.Leaf("Brightwater"), Is.EqualTo("Brightwater"));
    }

    /// <summary>A backslash is a legal character in a POSIX filename, so this is genuinely ambiguous — the
    /// reading that serves a travelling settings file is chosen, and pinned so it is not changed by accident.</summary>
    [Test]
    public void ABackslashIsAlwaysASeparator_EvenWhereItCouldBeAName()
    {
        Assert.That(PortablePath.Leaf(@"/srv/worlds/odd\name"), Is.EqualTo("name"));
    }
}
