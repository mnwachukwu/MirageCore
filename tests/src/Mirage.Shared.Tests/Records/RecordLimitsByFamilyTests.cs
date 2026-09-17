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

    /// <summary>Zero reads as "no room", which a caller stops on. A family this server does not know
    /// answering with some large number would have it allocate for content that cannot exist.</summary>
    [Test]
    public void AnUnknownFamilyHasNoRoom()
    {
        Assert.That(Configured.For("Creatures"), Is.Zero);
    }

    // ── A family a MODULE declared ───────────────────────────────────────

    // The id overload can only look one up in Core's table, where a game's family is not, so it answers
    // "no room" for content that exists. Holding the family leaves the question answerable.
    [Test]
    public void AModulesFamily_AnswersWithItsOwnDefault_ByFamilyButNotById()
    {
        var species = new RecordFamily { Id = "Species", Directory = "species", DefaultLimit = 386 };

        Assert.Multiple(() =>
        {
            Assert.That(Configured.For(species), Is.EqualTo(386));
            Assert.That(Configured.For(species.Id), Is.Zero, "by id, a family Core never heard of has no room");
        });
    }

    /// <summary>A fixed ceiling is baked into something that is not a setting — a save format, a wire
    /// shape — so raising it is a data migration. Core ships no family that sets it, which is exactly why
    /// this exercises a synthetic one: the flag is read on every lookup and nothing else would notice it
    /// being dropped.</summary>
    [Test]
    public void AFixedCeiling_IgnoresWhatTheOperatorConfigured()
    {
        var fixedFamily = new RecordFamily
        {
            Id = CoreRecordFamilies.Items,   // configurable to 13 above
            DefaultLimit = 64,
            LimitIsFixed = true,
        };

        Assert.That(Configured.For(fixedFamily), Is.EqualTo(64), "not 13 — a fixed ceiling is not a setting");
    }

    [Test]
    public void AConfigurableFamily_DoesNotIgnoreTheOperator()
    {
        var configurable = new RecordFamily { Id = CoreRecordFamilies.Items, DefaultLimit = 64 };

        Assert.That(Configured.For(configurable), Is.EqualTo(13), "the same family without the flag reads the setting");
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
