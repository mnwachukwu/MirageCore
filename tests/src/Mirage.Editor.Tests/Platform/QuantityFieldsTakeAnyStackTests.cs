using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// The give-item and give-to-vault boxes accept any amount a stack can actually hold.
///
/// <para>🔴 Avalonia's NumericUpDown caps at 100 unless told otherwise, so every one of them carries an
/// explicit Maximum — and a round number picked for looks becomes a wall. Gold has no ceiling of its own
/// beyond the <c>int</c> its stack lives in, so anything short of that refuses amounts the game can hold,
/// with no message saying why: the box simply stops taking digits.</para>
///
/// <para>Read from the .axaml. A Maximum is a property of the markup, and constructing the view says
/// nothing about what was written there.</para>
/// </summary>
[TestFixture]
public class QuantityFieldsTakeAnyStackTests
{
    private static string Markup()
    {
        string dir = typeof(QuantityFieldsTakeAnyStackTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        string path = Path.Combine(dir, "Views", "AccountEditorView.axaml");
        Assert.That(File.Exists(path), Is.True, path);
        // Comments removed: a commented-out control still carries a Maximum.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", "", RegexOptions.Singleline);
    }

    /// <summary>Every NumericUpDown bound to a give quantity, with the Maximum it declares.</summary>
    private static (string Binding, long Max)[] QuantityBoxes()
    {
        var found = Regex.Matches(Markup(),
            @"<NumericUpDown\b[^>]*?Value=""\{Binding (?<bind>\w*Quantity)\}""[^>]*?Maximum=""(?<max>\d+)""[^>]*?>",
            RegexOptions.Singleline);
        return [.. found.Select(m => (m.Groups["bind"].Value, long.Parse(m.Groups["max"].Value)))];
    }

    [Test]
    public void BothQuantityBoxes_ReachTheTopOfAStack()
    {
        var boxes = QuantityBoxes();

        Assert.That(boxes.Select(b => b.Binding), Is.EquivalentTo(new[] { "BankQuantity", "GiveQuantity" }),
            "the quantity boxes moved, or lost their Maximum — this guard is looking at nothing");

        foreach (var (binding, max) in boxes)
        {
            Assert.That(max, Is.EqualTo(int.MaxValue),
                $"{binding} stops short of what a stack holds, so large amounts cannot be typed at all");
        }
    }
}
