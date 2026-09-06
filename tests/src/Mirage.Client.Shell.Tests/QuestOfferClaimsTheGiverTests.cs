using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Client.Shell.Tests;

/// <summary>
/// Opening a quest offer claims the giver on the server first.
///
/// <para>Accept and turn-in name only the quest. The giver comes from the NPC whose menu the SERVER has
/// recorded, which is what stops one NPC's menu accepting another's quest — so an offer panel opened
/// without that claim is refused, and refused in a way the player experiences as a button that does
/// nothing.</para>
///
/// <para>🔴 The offer panel opens from two places. One is the server's own reply to an interact, where the
/// claim has already happened. The other is the NPC's right-click menu, which opens it locally — and local
/// is exactly where the claim is easy to leave out.</para>
///
/// <para>Read from source: the menu item is a closure built inside a screen that needs a graphics device,
/// and what is being pinned is which calls it makes.</para>
/// </summary>
[TestFixture]
public class QuestOfferClaimsTheGiverTests
{
    private static string SourceFile()
    {
        string root = typeof(QuestOfferClaimsTheGiverTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string path = Path.Combine(root, "client", "src", "Mirage.Client.Shell", "Screens",
                                   "GameplayScreen.ContextMenus.cs");
        Assert.That(File.Exists(path), Is.True, path);
        return path;
    }

    /// <summary>The file with comments stripped — a commented-out call still reads as a call.</summary>
    private static string Code()
    {
        string raw = File.ReadAllText(SourceFile());
        return string.Join("\n", raw.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
    }

    /// <summary>The one place the offer panel opens locally. Everything else reaches it through the server's
    /// reply, where the claim has already been made.</summary>
    [Test]
    public void TheMenuItemThatOpensAnOffer_ClaimsTheNpcFirst()
    {
        string code = Code();

        int item = code.IndexOf("foreach (var (questNum, action) in _ctx.State.ActionableQuestsAt(num))", StringComparison.Ordinal);
        Assert.That(item, Is.GreaterThan(-1), "the per-quest menu items moved — this guard is looking at nothing");

        int end = code.IndexOf("return items.Count > 0", item, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(item));
        string block = code[item..end];

        Assert.That(block, Does.Contain("OpenQuestDialog"), "the menu item no longer opens an offer");
        Assert.That(block, Does.Contain("NpcInteractChoice.QuestOffer"),
            "the offer opens without telling the server which NPC it belongs to, so accepting is refused");

        int claim = block.IndexOf("SendNpcInteract", StringComparison.Ordinal);
        int open = block.IndexOf("OpenQuestDialog", StringComparison.Ordinal);
        Assert.That(claim, Is.LessThan(open), "the offer opens before the NPC is claimed");
    }

    /// <summary>The claim carries the NPC the menu was built for. Sending the wrong pair would claim a
    /// different NPC and the accept would be refused for naming the wrong giver.</summary>
    [Test]
    public void TheClaim_NamesTheNpcTheMenuBelongsTo()
    {
        string code = Code();
        var call = Regex.Match(code, @"SendNpcInteract\(([^)]*NpcInteractChoice\.QuestOffer)\)");

        Assert.That(call.Success, Is.True, "no QuestOffer interact is sent at all");
        Assert.That(call.Groups[1].Value, Does.Match(@"^\s*map\s*,\s*slot\s*,"),
            "the claim sends something other than the menu's own (map, slot): " + call.Groups[1].Value);
    }
}
