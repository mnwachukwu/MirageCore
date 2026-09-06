using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// What a world folder is made of, and what is deliberately not part of one.
///
/// <para>The split is one question: does it change while the server runs? Everything the server writes by
/// itself is on the other side of it, which is what lets a world be handed to another machine without
/// carrying anybody's password hashes.</para>
/// </summary>
[TestFixture]
public class WorldLayoutTests
{
    [Test]
    public void TheWorldFolders_AreOnlyWhatAnAuthorWrites()
    {
        Assert.That(WorldLayout.WorldFolders,
                    Has.No.Member("accounts").And.No.Member("guilds").And.No.Member("market")
                       .And.No.Member("trades").And.No.Member("seasons").And.No.Member("map_items"));
    }

    /// <summary>The MOTD greets whoever is hosting, not whoever authored the world, so it is not something
    /// a world folder carries.</summary>
    [Test]
    public void TheMotd_IsNotAWorldFile()
    {
        Assert.That(WorldLayout.WorldFiles, Has.No.Member("motd.json"));
    }
}
