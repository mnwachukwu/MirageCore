using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// An empty MOTD means take it down, and taking it down says nothing to anybody.
///
/// <para>The set announcement quotes the new text to every player. A clear has no text to quote, so
/// broadcasting one puts "Message of the Day changed to:" with nothing after it in front of everyone —
/// which says nothing and reads as broken. Players simply stop being shown one.</para>
///
/// <para>🔴 There are TWO ways to set it: the console's <c>/motd</c> and the in-game admin packet. They
/// are the same action arrived at from two sides, so a rule applied to one has to be applied to the
/// other — the in-game path used to broadcast unconditionally.</para>
///
/// <para>⚠️ Read from SOURCE with comments stripped. Both handlers sit behind twenty-odd constructor
/// dependencies, and standing one up would test the container rather than the rule.</para>
/// </summary>
[TestFixture]
public class ClearingTheMotdIsSilentTests
{
    static string RepoRoot()
    {
        string root = typeof(ClearingTheMotdIsSilentTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>One handler's body, comments removed.</summary>
    static string Body(string relativePath, string signature)
    {
        string raw = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string code = string.Join("\n", raw.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));

        int start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(-1), $"{signature} not found — has it been renamed?");

        // To the next method at the same indentation, or the end.
        int next = code.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? code[start..] : code[start..next];
    }

    static readonly (string Path, string Signature, string What)[] Setters =
    [
        ("server/src/Mirage.Server.Host/Services/ConsoleCommands.World.cs", "private void CmdMotd(string args)", "the console's /motd"),
        ("server/src/Mirage.Server.Core/Net/PacketHandler.Admin.cs", "private void HandleSetMotd(int index, SetMotdPacket p)", "the in-game /motd"),
    ];

    [Test]
    public void NeitherSetter_BroadcastsAClear()
    {
        foreach (var (path, signature, what) in Setters)
        {
            string body = Body(path, signature);

            Assert.That(body, Does.Match(@"bool clearing = string\.IsNullOrWhiteSpace"),
                $"{what} does not recognise an empty message as a clear");

            int guard = body.IndexOf("if (!clearing)", StringComparison.Ordinal);
            int broadcast = body.IndexOf("SendLocalizedChatToAll(ServerStrings.AdminCommand_MotdChanged", StringComparison.Ordinal);

            Assert.That(broadcast, Is.GreaterThan(-1), $"{what} no longer announces a set at all");
            Assert.That(guard, Is.GreaterThan(-1), $"{what} announces unconditionally, so a clear reaches every player");
            Assert.That(guard, Is.LessThan(broadcast), $"{what} announces before it checks whether it is clearing");
        }
    }

    /// <summary>An empty message is stored, not refused. Refusing it would leave no way to take one down.</summary>
    [Test]
    public void AnEmptyMessageIsAccepted_NotTurnedAway()
    {
        string body = Body(Setters[0].Path, Setters[0].Signature);

        Assert.That(body, Does.Not.Match(@"IsNullOrWhiteSpace\(args\)\s*\)\s*\{[^}]*return;"),
            "the console's /motd still bails out on an empty argument, so a message cannot be cleared");
        Assert.That(body, Does.Contain("SaveMotdAsync(motd)"),
            "the cleared value is not persisted, so the old message returns on restart");
    }
}
