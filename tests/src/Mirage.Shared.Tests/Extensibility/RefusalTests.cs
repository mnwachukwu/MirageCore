using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The one property that matters is which way it fails. A gate that was never assigned, a struct that
/// was never written, and a field left at its default all have to refuse — so unfinished wiring shows
/// up as a thing that does not happen, which somebody reports, rather than a thing that happens to
/// everyone, which nobody notices until it is exploited.
/// </summary>
[TestFixture]
public class RefusalTests
{
    [Test]
    public void TheZeroValueRefuses()
    {
        Refusal unassigned = default;

        Assert.Multiple(() =>
        {
            Assert.That(unassigned.Allowed, Is.False);
            Assert.That(unassigned.HasReason, Is.False);
            Assert.That(unassigned, Is.EqualTo(Refusal.DenySilently));
        });
    }

    [Test]
    public void AllowIsTheOnlyWayToSayYes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Refusal.Allow.Allowed, Is.True);
            Assert.That(Refusal.Allow.ReasonKey, Is.Null);
            Assert.That(Refusal.Allow.HasReason, Is.False);
        });
    }

    [Test]
    public void ADenialCarriesItsReasonAndColor()
    {
        var denial = Refusal.Deny("door.locked", GameColor.BrightRed);

        Assert.Multiple(() =>
        {
            Assert.That(denial.Allowed, Is.False);
            Assert.That(denial.ReasonKey, Is.EqualTo("door.locked"));
            Assert.That(denial.Color, Is.EqualTo(GameColor.BrightRed));
            Assert.That(denial.HasReason, Is.True);
        });
    }

    /// <summary>Nothing objected, so nothing is refused. This is the one place the mechanism is
    /// deliberately fail-open, and it is what makes a game registering no gates behave as though the
    /// gate were not there.</summary>
    [Test]
    public void NoGatesAtAllAllows()
    {
        Assert.That(Refusal.FirstRefusal(Array.Empty<Func<Refusal>>()).Allowed, Is.True);
    }

    [Test]
    public void EveryGateAllowingAllows()
    {
        Func<Refusal>[] gates = [() => Refusal.Allow, () => Refusal.Allow];

        Assert.That(Refusal.FirstRefusal(gates).Allowed, Is.True);
    }

    [Test]
    public void TheFirstRefusalIsTheAnswerAndTheRestAreNotAsked()
    {
        bool laterGateRan = false;
        Func<Refusal>[] gates =
        [
            () => Refusal.Allow,
            () => Refusal.Deny("too.far"),
            () => { laterGateRan = true; return Refusal.Deny("also.no"); },
        ];

        var answer = Refusal.FirstRefusal(gates);

        Assert.Multiple(() =>
        {
            Assert.That(answer.ReasonKey, Is.EqualTo("too.far"));
            Assert.That(laterGateRan, Is.False, "evaluation short-circuits");
        });
    }

    [Test]
    public void ASilentDenialStillStopsTheAction()
    {
        Func<Refusal>[] gates = [() => Refusal.DenySilently];

        var answer = Refusal.FirstRefusal(gates);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Allowed, Is.False);
            Assert.That(answer.HasReason, Is.False, "nothing to say, but still no");
        });
    }
}

/// <summary>An NPC is named by where it spawns rather than where it stands, which is what makes one
/// handle good for a native NPC and a visitor from two maps away alike.</summary>
[TestFixture]
public class EntityHandleTests
{
    [Test]
    public void TheZeroValueNamesNobody()
    {
        EntityHandle nobody = default;

        Assert.Multiple(() =>
        {
            Assert.That(nobody.IsSet, Is.False);
            Assert.That(nobody, Is.EqualTo(EntityHandle.None));
            Assert.That(nobody.IsPlayer, Is.False);
            Assert.That(nobody.IsNpc, Is.False);
        });
    }

    [Test]
    public void APlayerIsNamedByIndex()
    {
        var handle = EntityHandle.ForPlayer(7);

        Assert.Multiple(() =>
        {
            Assert.That(handle.IsPlayer, Is.True);
            Assert.That(handle.PlayerIndex, Is.EqualTo(7));
            Assert.That(handle.SpawnMap, Is.Zero, "a player has no spawn slot to read");
            Assert.That(handle.SpawnSlot, Is.Zero);
        });
    }

    [Test]
    public void AnNpcIsNamedByWhereItSpawns()
    {
        var handle = EntityHandle.ForNpc(spawnMap: 12, spawnSlot: 3);

        Assert.Multiple(() =>
        {
            Assert.That(handle.IsNpc, Is.True);
            Assert.That(handle.SpawnMap, Is.EqualTo(12));
            Assert.That(handle.SpawnSlot, Is.EqualTo(3));
            Assert.That(handle.PlayerIndex, Is.Zero);
        });
    }

    [Test]
    public void TheSameNpcIsTheSameHandleWhereverItIsStanding()
    {
        Assert.That(EntityHandle.ForNpc(12, 3), Is.EqualTo(EntityHandle.ForNpc(12, 3)));
    }

    [Test]
    public void APlayerAndAnNpcWithTheSameNumbersAreDifferentBodies()
    {
        Assert.That(EntityHandle.ForPlayer(12), Is.Not.EqualTo(EntityHandle.ForNpc(12, 0)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AnImpossibleIndexNamesNobodyRatherThanSomebodyWrong(int index)
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntityHandle.ForPlayer(index), Is.EqualTo(EntityHandle.None));
            Assert.That(EntityHandle.ForNpc(index, 1), Is.EqualTo(EntityHandle.None));
            Assert.That(EntityHandle.ForNpc(1, index), Is.EqualTo(EntityHandle.None));
        });
    }
}
