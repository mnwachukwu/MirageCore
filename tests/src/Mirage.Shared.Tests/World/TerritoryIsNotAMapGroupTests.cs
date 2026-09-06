using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;
namespace Mirage.Shared.Tests.World;

/// <summary>
/// A map group and a territory are two things, and the whole of the link between them is that a territory
/// IS the maps of its group.
///
/// <para>The group is authored: it travels inside a world folder, the editor writes it, and a world handed
/// to somebody else arrives with it. What a running server made of that group — an owner, income, a
/// war-night queue — belongs to one installation and lives in the data folder instead. Put any of it back
/// on the group and an authoring save starts overwriting who holds a territory, and a copied world starts
/// carrying somebody else's war.</para>
///
/// <para>These are convention tests because nothing else would notice: adding a property compiles, and the
/// damage only shows up on a world somebody sent to a friend.</para>
/// </summary>
[TestFixture]
public class TerritoryIsNotAMapGroupTests
{
    // Everything about who holds a territory. The names are the check: a re-coupling would reintroduce them
    // under the same names, because they are what the domain calls these things.
    private static readonly string[] TerritoryState =
    [
        "ControllingGuild", "PendingTerritoryIncome", "IncomeThisWeek", "PreviousWeekIncome",
        "LastWeekRollDate", "WeeksHeld", "Challengers", "DefenderAbandoned",
    ];

    private static string[] PropertyNames<T>() =>
        [.. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)];

    [Test]
    public void AMapGroupSaysNothingAboutWhoHoldsIt()
    {
        var onTheGroup = PropertyNames<MapGroupRecord>().Intersect(TerritoryState).ToArray();
        Assert.That(onTheGroup, Is.Empty,
            "an authored map group carries territory state, so an editor save can overwrite a live war");
    }

    [Test]
    public void ATerritoryCarriesEveryPieceOfThatState()
    {
        var missing = TerritoryState.Except(PropertyNames<TerritoryRecord>()).ToArray();
        Assert.That(missing, Is.Empty, "territory state with nowhere to live");
    }

    [Test]
    public void AMapGroupStillSaysWhichMapsAreContestable()
    {
        Assert.That(PropertyNames<MapGroupRecord>(), Does.Contain(nameof(MapGroupRecord.Territory)),
            "the group is what declares a territory; only its state moved");
    }

    [Test]
    public void ATerritoryKnowsTheGroupItIs()
    {
        var terr = new TerritoryRecord { MapGroup = 7 };
        Assert.That(terr.MapGroup, Is.EqualTo(7));
    }

    [Test]
    public void TerritoriesAreNotPartOfAWorldFolder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorldLayout.WorldFolders, Does.Contain("map_groups"),
                "a world is where the authored groups live");
            Assert.That(WorldLayout.WorldFolders, Does.Not.Contain("territories"),
                "a world handed to somebody else must not carry this server's territory ownership");
        });
    }

    [Test]
    public void ATerritorySnapshotDoesNotShareItsChallengerList()
    {
        var terr = new TerritoryRecord { MapGroup = 3 };
        terr.Challengers.Add(4);

        var snapshot = terr.Clone();
        terr.Challengers.Add(9);

        Assert.That(snapshot.Challengers, Is.EqualTo(new[] { 4 }),
            "a background save writes the list it was handed, not whatever it grew into meanwhile");
    }
}
