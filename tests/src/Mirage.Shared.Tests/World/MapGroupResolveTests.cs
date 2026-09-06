using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>The pure map/MapGroup property-fallback resolver. Int/enum fields use their default
/// (0 / None) as the "not set → inherit" sentinel; the environment bools are nullable (null = inherit,
/// explicit true/false overrides, null on both → false). The display-name chain inserts the group between the
/// map's DisplayName and its Name. All resolvers are safe when the group is null.</summary>
[TestFixture]
public class MapGroupResolveTests
{
    [Test]
    public void IntProps_NonDefaultWins_ElseGroup_ElseDefault()
    {
        var g = new MapGroupRecord { Music = 9 };
        Assert.That(MapGroupResolve.Music(new MapRecord { Music = 5 }, g), Is.EqualTo(5));   // map set (non-0) wins
        Assert.That(MapGroupResolve.Music(new MapRecord { Music = 0 }, g), Is.EqualTo(9));   // map 0 = unset → group
        Assert.That(MapGroupResolve.Music(new MapRecord { Music = 0 }, new MapGroupRecord()), Is.EqualTo(0)); // both unset → 0
        Assert.That(MapGroupResolve.Music(new MapRecord { Music = 0 }, null), Is.EqualTo(0));  // null group is safe
    }

    // Map-enter/leave greeting: each of the three strings resolves independently, the map's own
    // non-blank value winning over the group's, blank inheriting — so a map can override just one line.
    [Test]
    public void Greeting_ResolvesEachFieldMapOverGroup_BlankInherits()
    {
        var g = new MapGroupRecord { GreetingSpeaker = "Guild", JoinSay = "Welcome to the hall.", LeaveSay = "Farewell." };
        var map = new MapRecord { GreetingSpeaker = "Innkeeper", JoinSay = "Rest here, traveler." };  // no own LeaveSay
        var r = MapGroupResolve.Greeting(map, g);
        Assert.Multiple(() =>
        {
            Assert.That(r.Speaker, Is.EqualTo("Innkeeper"), "map's own speaker wins");
            Assert.That(r.JoinSay, Is.EqualTo("Rest here, traveler."), "map's own join line wins");
            Assert.That(r.LeaveSay, Is.EqualTo("Farewell."), "blank leave line inherits the group's");
        });
        Assert.That(MapGroupResolve.Greeting(new MapRecord(), null), Is.EqualTo(new MapGreeting("", "", "")),
            "no greeting anywhere -> all blank; null group is safe");
    }

    [Test]
    public void Moral_ExplicitOverrides_NullInherits_BothNullIsNone()
    {
        var g = new MapGroupRecord { Moral = MapMoral.Safe };
        Assert.That(MapGroupResolve.Moral(new MapRecord { Moral = MapMoral.Arena }, g), Is.EqualTo(MapMoral.Arena)); // map explicit wins
        Assert.That(MapGroupResolve.Moral(new MapRecord { Moral = null }, g), Is.EqualTo(MapMoral.Safe));            // null inherits group
        Assert.That(MapGroupResolve.Moral(new MapRecord { Moral = MapMoral.None }, g), Is.EqualTo(MapMoral.None));   // explicit None OVERRIDES Safe (why Moral is nullable)
        Assert.That(MapGroupResolve.Moral(new MapRecord { Moral = null }, new MapGroupRecord { Moral = null }), Is.EqualTo(MapMoral.None)); // both null → None
        Assert.That(MapGroupResolve.Moral(new MapRecord { Moral = null }, null), Is.EqualTo(MapMoral.None));         // null group safe
    }

    [Test]
    public void EnvBools_ExplicitOverrides_NullInherits_BothNullIsFalse()
    {
        var g = new MapGroupRecord { Indoors = true, AlwaysDark = true };
        Assert.That(MapGroupResolve.Indoors(new MapRecord { Indoors = null }, g), Is.True);   // null inherits group true
        Assert.That(MapGroupResolve.Indoors(new MapRecord { Indoors = false }, g), Is.False); // explicit false overrides (opt out)
        Assert.That(MapGroupResolve.AlwaysDark(new MapRecord { AlwaysDark = true }, new MapGroupRecord()), Is.True); // map true wins
        Assert.That(MapGroupResolve.Indoors(new MapRecord { Indoors = null }, new MapGroupRecord { Indoors = null }), Is.False); // both null → false
        Assert.That(MapGroupResolve.AlwaysDark(new MapRecord { AlwaysDark = null }, null), Is.False); // null group → false
    }

    [Test]
    public void BootDestination_TravelsAsASet()
    {
        var g = new MapGroupRecord { BootMap = 8, BootX = 1, BootY = 2 };
        var ownBoot = new MapRecord { BootMap = 3, BootX = 4, BootY = 5 };
        Assert.That(MapGroupResolve.BootMap(ownBoot, g), Is.EqualTo(3));   // map's own boot map...
        Assert.That(MapGroupResolve.BootX(ownBoot, g), Is.EqualTo(4));     // ...brings its own X/Y
        Assert.That(MapGroupResolve.BootY(ownBoot, g), Is.EqualTo(5));
        var noBoot = new MapRecord { BootMap = 0, BootX = 99, BootY = 99 };
        Assert.That(MapGroupResolve.BootMap(noBoot, g), Is.EqualTo(8));    // 0 boot map → whole set inherits
        Assert.That(MapGroupResolve.BootX(noBoot, g), Is.EqualTo(1));
        Assert.That(MapGroupResolve.BootY(noBoot, g), Is.EqualTo(2));
    }

    [Test]
    public void DisplayName_Chain_MapDisplay_Group_MapName_Empty()
    {
        var g = new MapGroupRecord { DisplayName = "Northern Reaches", Name = "north" };
        Assert.That(MapGroupResolve.DisplayName(new MapRecord { DisplayName = "Chapel", Name = "chapel1" }, g),
            Is.EqualTo("Chapel"));                                                        // map DisplayName wins
        Assert.That(MapGroupResolve.DisplayName(new MapRecord { DisplayName = "", Name = "chapel1" }, g),
            Is.EqualTo("Northern Reaches"));                                              // group DisplayName next
        var groupNoDisplay = new MapGroupRecord { DisplayName = "", Name = "north" };
        Assert.That(MapGroupResolve.DisplayName(new MapRecord { DisplayName = "", Name = "chapel1" }, groupNoDisplay),
            Is.EqualTo("chapel1"));                                                       // map Name next
        Assert.That(MapGroupResolve.DisplayName(new MapRecord { DisplayName = "", Name = "" }, null),
            Is.EqualTo(""));                                                              // caller adds "Map N"
    }
}
