using System.Text.Json;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// Which looks a world offers, and to whom.
///
/// <para>🔴 <b>Core does not know what a gate MEANS.</b> It holds a creation question's key and the
/// record numbers a look goes with. Whether that reads as a class, a bloodline or a uniform is the
/// world author's business, so every test here is about the comparison and none is about classes.</para>
/// </summary>
[TestFixture]
public sealed class AppearanceGateTests
{
    private static readonly Dictionary<string, int> AnsweredTwo =
        new(StringComparer.Ordinal) { ["class"] = 2 };

    /// <summary>The common case, and the one a world gets without thinking about it: a look with no
    /// gate is offered whatever anybody answered.</summary>
    [Test]
    public void AnUngatedLookIsOfferedToEverybody()
    {
        var look = new CharacterAppearance { Name = "Wanderer" };

        Assert.Multiple(() =>
        {
            Assert.That(look.OfferedWhen(AnsweredTwo), Is.True);
            Assert.That(look.OfferedWhen(new Dictionary<string, int>()), Is.True,
                        "and to somebody who has answered nothing at all");
        });
    }

    [Test]
    public void AGatedLookIsOfferedOnlyForTheNumbersItNames()
    {
        var look = new CharacterAppearance
        {
            Name = "Plate",
            Needs = [new AppearanceGate { Key = "class", Is = [1, 2] }],
        };

        Assert.Multiple(() =>
        {
            Assert.That(look.OfferedWhen(AnsweredTwo), Is.True);
            Assert.That(look.OfferedWhen(new Dictionary<string, int>(StringComparer.Ordinal) { ["class"] = 3 }),
                        Is.False);
        });
    }

    /// <summary>⚠ Unanswered is not the same as answered zero. A gate naming a question nobody answered
    /// fails, so a client that simply left a question out cannot reach a look behind it.</summary>
    [Test]
    public void AGateOnAQuestionNobodyAnsweredFails()
    {
        var look = new CharacterAppearance
        {
            Needs = [new AppearanceGate { Key = "class", Is = [1] }],
        };

        Assert.That(look.OfferedWhen(new Dictionary<string, int>(StringComparer.Ordinal) { ["homeland"] = 1 }),
                    Is.False);
    }

    /// <summary>Two gates are AND, as "for these classes, from this homeland" needs.</summary>
    [Test]
    public void EveryGateHasToHold()
    {
        var look = new CharacterAppearance
        {
            Needs =
            [
                new AppearanceGate { Key = "class", Is = [2] },
                new AppearanceGate { Key = "homeland", Is = [5] },
            ],
        };

        var both = new Dictionary<string, int>(StringComparer.Ordinal) { ["class"] = 2, ["homeland"] = 5 };
        var one = new Dictionary<string, int>(StringComparer.Ordinal) { ["class"] = 2, ["homeland"] = 4 };

        Assert.Multiple(() =>
        {
            Assert.That(look.OfferedWhen(both), Is.True);
            Assert.That(look.OfferedWhen(one), Is.False);
        });
    }

    /// <summary>A gate naming no numbers offers the look to nobody, and says so plainly. Reading a
    /// blank list as "all" would make an author's empty array mean its opposite.</summary>
    [Test]
    public void AGateNamingNothingOffersItToNobody()
    {
        var look = new CharacterAppearance { Needs = [new AppearanceGate { Key = "class", Is = [] }] };

        Assert.That(look.OfferedWhen(AnsweredTwo), Is.False);
    }

    /// <summary>⚠ Both halves of the seam, or the feature is half-built: a manifest that carries gates
    /// has to read them back, and the screen and the server both ask the record rather than the file.
    /// </summary>
    [Test]
    public void AManifestKeepsTheGatesItWasGiven()
    {
        const string json = """
            {
              "name": "Somewhere",
              "appearances": [
                { "name": "Plain", "sprite": 0 },
                { "name": "Plate", "sprite": 3, "needs": [ { "key": "class", "is": [1, 2] } ] }
              ]
            }
            """;

        var manifest = JsonSerializer.Deserialize<WorldManifest>(json);

        Assert.That(manifest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(manifest!.Appearances, Has.Count.EqualTo(2));
            Assert.That(manifest.Appearances[0].Needs, Is.Empty);
            Assert.That(manifest.Appearances[1].Needs, Has.Count.EqualTo(1));
            Assert.That(manifest.Appearances[1].Needs[0].Key, Is.EqualTo("class"));
            Assert.That(manifest.Appearances[1].Needs[0].Is, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(manifest.Appearances[1].OfferedWhen(AnsweredTwo), Is.True);
        });
    }
}
