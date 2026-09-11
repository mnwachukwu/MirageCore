using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// Asking for a family's ceiling by name has to give the same answer as reading the typed property,
/// because both are live: the server sizes its arrays from the properties and a caller holding a
/// family row asks by name.
/// </summary>
[TestFixture]
public class RecordLimitsByFamilyTests
{
    private static readonly RecordLimits Configured = new()
    {
        Maps = 11,
        MapGroups = 12,
        Items = 13,
        Npcs = 14,
        Shops = 15,
        Spells = 16,
        Quests = 17,
        Conversations = 18,
    };

    [Test]
    public void EachConfigurableFamilyReadsItsOwnProperty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Configured.For(CoreRecordFamilies.Maps), Is.EqualTo(11));
            Assert.That(Configured.For(CoreRecordFamilies.MapGroups), Is.EqualTo(12));
            Assert.That(Configured.For(CoreRecordFamilies.Items), Is.EqualTo(13));
            Assert.That(Configured.For(CoreRecordFamilies.Npcs), Is.EqualTo(14));
            Assert.That(Configured.For(CoreRecordFamilies.Shops), Is.EqualTo(15));
            Assert.That(Configured.For(CoreRecordFamilies.Spells), Is.EqualTo(16));
            Assert.That(Configured.For(CoreRecordFamilies.Quests), Is.EqualTo(17));
            Assert.That(Configured.For(CoreRecordFamilies.Conversations), Is.EqualTo(18));
        });
    }

    /// <summary>Every configurable family has to be wired, or one of them silently answers with its
    /// default however the operator configured it.</summary>
    [Test]
    public void NoConfigurableFamilyFallsThroughToItsDefault()
    {
        var stuck = CoreRecordFamilies.World
            .Where(f => !f.LimitIsFixed)
            .Where(f => Configured.For(f.Id) == f.DefaultLimit)
            .Select(f => f.Id)
            .ToList();

        Assert.That(stuck, Is.Empty,
            "these read their default rather than the configured value: " + string.Join(", ", stuck));
    }

    /// <summary>A class number rides in every saved character, so no configuration moves it.</summary>
    [Test]
    public void AFixedCeilingIgnoresConfiguration()
    {
        Assert.That(Configured.For(CoreRecordFamilies.Classes), Is.EqualTo(Constants.MaxClasses));
    }

    /// <summary>Zero reads as "no room", which a caller stops on. A family this server does not know
    /// answering with some large number would have it allocate for content that cannot exist.</summary>
    [Test]
    public void AnUnknownFamilyHasNoRoom()
    {
        Assert.That(Configured.For("Creatures"), Is.Zero);
    }

    [Test]
    public void TheStockLimitsMatchEachFamilysDeclaredDefault()
    {
        Assert.Multiple(() =>
        {
            foreach (var family in CoreRecordFamilies.World)
            {
                Assert.That(RecordLimits.Default.For(family.Id), Is.EqualTo(family.DefaultLimit), family.Id);
            }
        });
    }
}
