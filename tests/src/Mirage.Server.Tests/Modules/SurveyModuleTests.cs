using Mirage.Modules.Survey;
using Mirage.Server.Host;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// A game loaded through the real registry, declaring one of everything.
///
/// <para>🔴 This is the only test in the tree where a seam is exercised by a GAME rather than by a fake
/// built to exercise it. A stub that implements <c>ICoreModule</c> proves the registry accepts a module;
/// it cannot prove the seam is wide enough to say anything a game wants to say. The difference showed up
/// the first time this module was written: the shape of a choice set, the name an observer has to carry,
/// and the reference a module is allowed to take were all discovered here rather than designed.</para>
///
/// <para>It is also the specification the Compass host has to meet. Whatever replaces the C# below must
/// produce a registry that satisfies exactly these assertions.</para>
/// </summary>
[TestFixture]
public class SurveyModuleTests
{
    private static CoreRegistry Loaded() => CoreRegistry.Build(new SurveyModule());

    [Test]
    public void TheServerShipsWithItLoaded()
    {
        var names = CoreRegistry.Build(GameModules.Load("no-such-world")).ModuleNames;

        Assert.That(names, Is.EqualTo(new[] { "Core", "Survey", "Scripts" }),
            "the host loads this game, and the world's own scripts after it");
    }

    [Test]
    public void ItDeclaresTheValuesASurveyorCarries()
    {
        var attributes = Loaded().Attributes;

        Assert.Multiple(() =>
        {
            Assert.That(attributes.TryGet(Survey.Stamina, out var stamina), Is.True);
            Assert.That(stamina.Visibility, Is.EqualTo(AttributeVisibility.Viewport),
                "an onlooker reads the bar over your head, so the numbers behind it must reach them");
            Assert.That(attributes.TryGet(Survey.Specimens, out var specimens), Is.True);
            Assert.That(specimens.Visibility, Is.EqualTo(AttributeVisibility.Owner),
                "your own count is nobody else's business");
        });
    }

    /// <summary>The family Core has never heard of, with the form an author fills in.</summary>
    [Test]
    public void ItAddsAFamilyWithAnAuthoringForm()
    {
        var schema = Loaded().Schema;
        var species = schema.Families.SingleOrDefault(f => f.Id == Survey.Species);

        Assert.Multiple(() =>
        {
            Assert.That(species, Is.Not.Null, "the editor has no section to show");
            Assert.That(species!.EffectiveDirectory, Is.EqualTo("species"));
            Assert.That(species.Fields.Select(f => f.Key), Is.EqualTo(new[] { "name", "habitat", "notes" }));
            Assert.That(species.Fields.Single(f => f.Key == "habitat").ChoiceSetId, Is.EqualTo(Survey.Habitats));
            Assert.That(schema.ChoiceSets.Single(c => c.Id == Survey.Habitats).Members, Has.Count.EqualTo(3),
                "the habitat picker offers nothing, so an author types a free string or nothing at all");
        });
    }

    [Test]
    public void ItDeclaresWhatThePlayerSees()
    {
        var registry = Loaded();

        Assert.Multiple(() =>
        {
            Assert.That(registry.OverheadBars.Bars.Select(b => b.ValueKey), Is.EqualTo(new[] { Survey.Stamina }));
            Assert.That(registry.EquipSlots.Slots.Select(s => s.Key), Is.EqualTo(new[] { Survey.Satchel }));
            Assert.That(registry.DisplayFields.For(DisplaySurfaces.Hud).Select(f => f.Style),
                Is.EqualTo(new[]
                {
                    DisplayStyle.Heading, DisplayStyle.Text, DisplayStyle.Text, DisplayStyle.Meter,
                }));
        });
    }

    [Test]
    public void ItDeclaresItsRulesAndItsWorkOnTheTick()
    {
        var registry = Loaded();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Observers, Has.Count.EqualTo(1));
            Assert.That(registry.DeathPolicies, Has.Count.EqualTo(1));
            Assert.That(registry.LingerPolicies, Has.Count.EqualTo(1));
            Assert.That(registry.Tick.Work, Is.Not.Empty, "stamina never comes back");
        });
    }

    /// <summary>🔴 Every seam Core offers is exercised by this one module, on purpose. A seam no game uses
    /// is a seam nobody has checked, and the point of shipping a game in the box is that the list below
    /// cannot quietly stop being true.</summary>
    [Test]
    public void ItUsesEverySeamTheBuilderOffers()
    {
        var registry = Loaded();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Attributes.Declarations, Is.Not.Empty, "Attributes");
            Assert.That(registry.Schema.Families.Any(f => f.Id == Survey.Species), Is.True, "AddFamily");
            Assert.That(registry.Schema.ChoiceSets, Is.Not.Empty, "AddChoiceSet");
            Assert.That(registry.EquipSlots.Count, Is.GreaterThan(0), "AddEquipSlot");
            Assert.That(registry.DisplayFields.Count, Is.GreaterThan(0), "AddDisplayField");
            Assert.That(registry.OverheadBars.Count, Is.GreaterThan(0), "AddOverheadBar");
            Assert.That(registry.Tick.Work, Is.Not.Empty, "AddTickWork");
            Assert.That(registry.Observers, Is.Not.Empty, "AddObserver");
            Assert.That(registry.DeathPolicies, Is.Not.Empty, "AddDeathPolicy");
            Assert.That(registry.LingerPolicies, Is.Not.Empty, "AddLingerPolicy");
        });
    }

    /// <summary>Nothing dies on a survey, and the refusal carries a reason rather than failing
    /// silently — a player who cannot do a thing should be told which thing.</summary>
    [Test]
    public void ItRefusesEveryDeath()
    {
        var policy = Loaded().DeathPolicies.Single();

        var refusal = policy.MayDie(new Death(EntityHandle.ForPlayer(1), default, ""));

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Allowed, Is.False);
            Assert.That(refusal.HasReason, Is.True);
        });
    }

    [TestCase(0, "Unenrolled")]
    [TestCase(1, "Apprentice")]
    [TestCase(8, "Field Hand")]
    [TestCase(20, "Naturalist")]
    [TestCase(40, "Botanist")]
    [TestCase(4000, "Botanist")]
    public void ARankIsWhatACountHasEarned(int specimens, string expected)
        => Assert.That(Survey.RankFor(specimens), Is.EqualTo(expected));
}
