using Mirage.Shared.Protocol;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// Every command the engine names is a command the engine can read.
///
/// <para><b>A name with nothing behind it is invisible without this.</b> It compiles, it is greppable,
/// and it reads to the next person as a command the protocol supports — so they send it, or write a
/// handler arm for it, and nothing arrives. Nothing else in the suite notices: the round-trip guard
/// walks packet TYPES and so never sees a constant that no type claims.</para>
/// </summary>
[TestFixture]
public class PacketNameCoverageTests
{
    private static IReadOnlyList<(string Name, string Value)> Declared() =>
        typeof(PacketNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .ToList();

    [Test]
    public void EveryDeclaredCommandHasARowInTheRegistry()
    {
        var orphans = Declared()
            .Where(c => !PacketSerializer.Registry.Knows(c.Value))
            .Select(c => $"{c.Name} (\"{c.Value}\")")
            .ToList();

        Assert.That(orphans, Is.Empty,
            "These names are declared but no packet can be read for them, so a line carrying one is "
            + "dropped before any router sees it. Either register a row or remove the name: "
            + string.Join(", ", orphans));
    }

    [Test]
    public void EveryRegisteredCommandHasAName()
    {
        var values = Declared().Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        var unnamed = PacketSerializer.Registry.Commands
            .Where(c => !values.Contains(c))
            .ToList();

        Assert.That(unnamed, Is.Empty,
            "These commands are registered as string literals with no constant naming them: "
            + string.Join(", ", unnamed));
    }

    /// <summary>Two names may share one wire value — a command used in both directions is one command.
    /// More than two would mean the pair-resolution rule has nothing to resolve on.</summary>
    [Test]
    public void NoWireValueIsClaimedByMoreThanTwoNames()
    {
        var crowded = Declared()
            .GroupBy(c => c.Value, StringComparer.Ordinal)
            .Where(g => g.Count() > 2)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(c => c.Name))}")
            .ToList();

        Assert.That(crowded, Is.Empty, string.Join("; ", crowded));
    }
}
