using Mirage.Editor.Services;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// What families the editor believes the world has.
///
/// <para>The editor is compiled knowing Core's families and connects to servers that may have more. Every
/// case here is about which of those two answers is right at a given moment, because a family this list
/// omits is one nobody can author.</para>
/// </summary>
[TestFixture]
public class WorldFamiliesTests
{
    [TearDown]
    public void TearDown() => WorldFamilies.Reset();

    private static RecordSchema SchemaWith(params string[] extraIds) => new()
    {
        Families =
        [
            .. CoreRegistry.CoreOnly.Schema.Families,
            .. extraIds.Select(id => new RecordFamily { Id = id, Directory = id.ToLowerInvariant() }),
        ],
    };

    [Test]
    public void BeforeConnecting_ItIsTheFamiliesThisBuildShipsWith()
    {
        Assert.That(WorldFamilies.All.Select(f => f.Id),
                    Is.EqualTo(CoreRecordFamilies.World.Select(f => f.Id)));
    }

    [Test]
    public void AdoptingAServersSchema_AddsTheFamiliesItDeclared()
    {
        WorldFamilies.Adopt(SchemaWith("Species"));

        Assert.Multiple(() =>
        {
            Assert.That(WorldFamilies.Find("Species"), Is.Not.Null);
            Assert.That(WorldFamilies.Find(CoreRecordFamilies.Items), Is.Not.Null, "Core's are still there");
        });
    }

    [Test]
    public void Resetting_GoesBackToThisBuildsFamilies()
    {
        WorldFamilies.Adopt(SchemaWith("Species"));
        WorldFamilies.Reset();

        Assert.That(WorldFamilies.Find("Species"), Is.Null);
    }

    // An older server sends nothing here. Believing it would empty the rail, which is a worse guess than
    // assuming the families this build knows — those are what a stock server's world folder holds.
    [Test]
    public void AServerThatReportsNoSchema_LeavesTheFamiliesAlone()
    {
        WorldFamilies.Adopt(RecordSchema.Empty);
        WorldFamilies.Adopt(null);

        Assert.That(WorldFamilies.All, Is.Not.Empty);
        Assert.That(WorldFamilies.Find(CoreRecordFamilies.Maps), Is.Not.Null);
    }

    [Test]
    public void OnlyACompiledFamily_HasAScreenBehindIt()
    {
        WorldFamilies.Adopt(SchemaWith("Species"));

        Assert.Multiple(() =>
        {
            Assert.That(WorldFamilies.HasCompiledEditor(CoreRecordFamilies.Items), Is.True);
            Assert.That(WorldFamilies.HasCompiledEditor("Species"), Is.False);
        });
    }

    [Test]
    public void ChangedFires_OnAdoptAndOnReset()
    {
        int fired = 0;
        void Count() => fired++;

        WorldFamilies.Changed += Count;
        try
        {
            WorldFamilies.Adopt(SchemaWith("Species"));
            WorldFamilies.Reset();
            WorldFamilies.Reset();   // already stock — nothing changed, so nothing to announce
        }
        finally
        {
            WorldFamilies.Changed -= Count;
        }

        Assert.That(fired, Is.EqualTo(2));
    }
}
