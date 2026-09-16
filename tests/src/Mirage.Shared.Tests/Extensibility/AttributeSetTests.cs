using System.Text.Json;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// A bag entry holding several of one thing.
///
/// <para>🔴 <b>Typed, and one kind only.</b> The factories take a sequence of one primitive each, so a
/// mixed set cannot be built — the invariant is the signature rather than a check somebody could skip.
/// What it is for is the one-to-many fact that would otherwise need a side table: which classes may
/// wield a sword, which may learn a spell.</para>
/// </summary>
[TestFixture]
public sealed class AttributeSetTests
{
    [Test]
    public void ASetKnowsItsKindAndItsMembers()
    {
        var set = AttributeValue.From(new long[] { 1, 10 });

        Assert.Multiple(() =>
        {
            Assert.That(set.Kind, Is.EqualTo(AttributeKind.Set));
            Assert.That(set.Of, Is.EqualTo(AttributeKind.Integer));
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.AsLongs(), Is.EqualTo(new long[] { 1, 10 }));
        });
    }

    [Test]
    public void MembershipIsTheQuestionItExistsToAnswer()
    {
        var set = AttributeValue.From(new long[] { 1, 10 });

        Assert.Multiple(() =>
        {
            Assert.That(set.Has(10), Is.True);
            Assert.That(set.Has(4), Is.False);
        });
    }

    /// <summary>⚠ Asking a value that is not a set answers no rather than throwing. A rule reading a
    /// field an author never wrote should see "not in it", not a crash.</summary>
    [Test]
    public void AnythingThatIsNotASetHoldsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AttributeValue.From(7L).Has(7), Is.False);
            Assert.That(AttributeValue.From(7L).Count, Is.Zero);
            Assert.That(AttributeValue.From("ember").Members, Is.Empty);
        });
    }

    [Test]
    public void AWordSetComparesAsWords()
    {
        var set = AttributeValue.From(new[] { "ember", "frost" });

        Assert.Multiple(() =>
        {
            Assert.That(set.Of, Is.EqualTo(AttributeKind.Text));
            Assert.That(set.Has("frost"), Is.True);
            Assert.That(set.Has("Frost"), Is.False, "compared exactly");
        });
    }

    /// <summary>🔴 <b>Two sets built from the same numbers are equal.</b> A record struct compares its
    /// fields, and the members live in an array — which compares by reference unless somebody says
    /// otherwise. The editor decides whether a record is dirty by exactly this test, so without it every
    /// record carrying a set would look changed the moment it was reloaded.</summary>
    [Test]
    public void TwoSetsOfTheSameThingAreEqual()
    {
        var one = AttributeValue.From(new long[] { 1, 10 });
        var two = AttributeValue.From(new long[] { 1, 10 });
        var other = AttributeValue.From(new long[] { 10, 1 });

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(two));
            Assert.That(one.GetHashCode(), Is.EqualTo(two.GetHashCode()));
            Assert.That(one, Is.Not.EqualTo(other), "order is part of what was written");
        });
    }

    [Test]
    public void ASetOfNumbersIsNotASetOfWords()
    {
        Assert.That(AttributeValue.From(new long[] { 1 }),
                    Is.Not.EqualTo(AttributeValue.From(new[] { "1" })));
    }

    // ── On disk and on the wire ───────────────────────────────────────────────

    [Test]
    public void ASetIsWrittenAsAnArrayAndReadBack()
    {
        var bag = new AttributeBag();
        bag.Set("mayWield", AttributeValue.From(new long[] { 1, 10 }));
        bag.Set("name", AttributeValue.From("Rusted Greataxe"));

        string json = JsonSerializer.Serialize(bag);
        var back = JsonSerializer.Deserialize<AttributeBag>(json);

        Assert.That(json, Does.Contain("[1,10]").Or.Contain("[1, 10]"));
        Assert.That(back, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(back!.TryGet("mayWield", out AttributeValue held), Is.True);
            Assert.That(held.Has(10), Is.True);
            Assert.That(held.Of, Is.EqualTo(AttributeKind.Integer));
        });
    }

    /// <summary>⚠ A hand-edited file is exactly where a stray word among numbers comes from. The first
    /// member settles the kind and the rest are read through it, because a set may hold only one kind
    /// and refusing the file would lose every other field in it.</summary>
    [Test]
    public void AMixedArrayIsReadAsTheKindOfItsFirstMember()
    {
        var back = JsonSerializer.Deserialize<AttributeBag>("""{ "mayWield": [1, "3", 5] }""");

        Assert.That(back, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(back!["mayWield"].Of, Is.EqualTo(AttributeKind.Integer));
            Assert.That(back["mayWield"].AsLongs(), Is.EqualTo(new long[] { 1, 3, 5 }));
        });
    }

    [Test]
    public void AnEmptyArrayIsAnEmptySet()
    {
        var back = JsonSerializer.Deserialize<AttributeBag>("""{ "mayWield": [] }""");

        Assert.That(back, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(back!["mayWield"].Kind, Is.EqualTo(AttributeKind.Set));
            Assert.That(back["mayWield"].Count, Is.Zero);
        });
    }

    [Test]
    public void AWordSetRoundTrips()
    {
        var bag = new AttributeBag();
        bag.Set("tags", AttributeValue.From(new[] { "ember", "frost" }));

        var back = JsonSerializer.Deserialize<AttributeBag>(JsonSerializer.Serialize(bag));

        Assert.That(back, Is.Not.Null);
        Assert.That(back!["tags"].AsTexts(), Is.EqualTo(new[] { "ember", "frost" }));
    }
}
