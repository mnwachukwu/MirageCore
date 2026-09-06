using Mirage.Client.Shell.Ui;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.IO;

namespace Mirage.Client.Shell.Tests.Rendering;

/// <summary>The map-name fallback chain shown in the HUD sidebar: authored DisplayName, else the map's MapGroup
/// DisplayName (resolved client-side against the cached group), else the internal Name, else a generic "Map N".
/// Whitespace-only values count as blank at each step.</summary>
[TestFixture]
public class MapDisplayNameTests
{
    [Test]
    public void DisplayNameSet_UsesDisplayName()
    {
        var map = new MapRecord { DisplayName = "Kordavan Keep", Name = "keep_internal_01" };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group: null), Is.EqualTo("Kordavan Keep"));
    }

    [Test]
    public void DisplayNameSet_IsTrimmed()
    {
        var map = new MapRecord { DisplayName = "  Kordavan Keep  ", Name = "" };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group: null), Is.EqualTo("Kordavan Keep"));
    }

    // A blank map DisplayName falls through to the map's GROUP DisplayName — resolved from the cached group the
    // client holds, so a live group rename would change this without any map re-send.
    [Test]
    public void DisplayNameBlank_FallsBackToGroupDisplayName()
    {
        var map = new MapRecord { DisplayName = "   ", MapGroup = 3, Name = "Deep Forest" };
        var group = new MapGroupRecord { Index = 3, DisplayName = "Northern Reaches" };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group), Is.EqualTo("Northern Reaches"));
    }

    [Test]
    public void DisplayNameAndGroupBlank_FallsBackToName()
    {
        var map = new MapRecord { DisplayName = "   ", MapGroup = 3, Name = "Deep Forest" };
        var group = new MapGroupRecord { Index = 3, DisplayName = "" };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group), Is.EqualTo("Deep Forest"));
    }

    // No group at all (group-less map) behaves like a blank group display name.
    [Test]
    public void NoGroup_FallsBackToName()
    {
        var map = new MapRecord { DisplayName = "", Name = "Deep Forest" };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group: null), Is.EqualTo("Deep Forest"));
    }

    [Test]
    public void AllBlank_FallsBackToMapNumber()
    {
        var map = new MapRecord { DisplayName = "", Name = "  " };
        Assert.That(UiHelper.ResolveMapDisplayName(map, 7, group: null), Is.EqualTo("Map 7"));
    }
}
