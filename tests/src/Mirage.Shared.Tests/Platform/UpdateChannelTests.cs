using Mirage.Updates;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// The update channel is written down twice: MSBuild passes it to <c>vpk pack</c>, and the app passes
/// it to Velopack when it looks for updates. Nothing connects the two but agreement.
///
/// <para>Drift here is silent in the worst way. A published feed named <c>releases.client-win.json</c>
/// and an app asking for <c>releases.win.json</c> both work perfectly — the app simply never finds an
/// update, forever, and looks exactly like an app that is up to date. There is no error to notice.</para>
///
/// <para>The channel also has to keep carrying the APP, not just the platform: Velopack omits the pack
/// id from the feed filename, and CI flattens every app's installers into one GitHub release, so three
/// apps sharing a channel would publish one filename three times.</para>
/// </summary>
[TestFixture]
public class UpdateChannelTests
{
    /// <summary>The repository root, baked in by the csproj at build time. Deliberately NOT a walk up
    /// from <c>AppContext.BaseDirectory</c>: that finds nothing when the suite is built to a redirected
    /// output path, and these checks would skip rather than fail — a guard that can silently not run.</summary>
    private static string RepoRoot()
    {
        string root = typeof(UpdateChannelTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    // The publish project for each app, and the app it packages.
    private static readonly (string Path, UpdatableApp App)[] PublishProjects =
    [
        (Path.Combine("publish", "Mirage.Client.Publish.csproj"), UpdatableApp.Client),
        (Path.Combine("publish", "Mirage.Editor.Publish.csproj"), UpdatableApp.Editor),
        (Path.Combine("publish", "Mirage.Server.Publish.csproj"), UpdatableApp.Server),
    ];

    private static List<string> ChannelsIn(string csprojPath) =>
        Regex.Matches(File.ReadAllText(csprojPath), @"<_Channel>([^<]+)</_Channel>")
             .Select(m => m.Groups[1].Value)
             .ToList();

    [Test]
    public void EveryPackagedChannel_IsOneTheAppWouldAskFor()
    {
        string root = RepoRoot();

        var problems = new List<string>();
        int checkedCount = 0;

        foreach (var (relative, app) in PublishProjects)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) { problems.Add($"missing {relative}"); continue; }

            foreach (string channel in ChannelsIn(path))
            {
                checkedCount++;
                // Every packaged channel is "<app>-<platform>". The app half must match what
                // UpdateFeed would send for that app; the platform half is one of the three legs.
                string expectedPrefix = UpdateFeed.AppToken(app) + "-";
                if (!channel.StartsWith(expectedPrefix, StringComparison.Ordinal))
                    problems.Add($"{relative}: channel '{channel}' does not start with '{expectedPrefix}'");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
            Assert.That(checkedCount, Is.EqualTo(9), "three apps times three platform legs");
        });
    }

    /// <summary>The collision this whole naming scheme exists to prevent: two apps must never publish a
    /// feed under the same name into the one flat release.</summary>
    [Test]
    public void NoTwoApps_ShareAChannel()
    {
        string root = RepoRoot();

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var collisions = new List<string>();

        foreach (var (relative, _) in PublishProjects)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;
            foreach (string channel in ChannelsIn(path))
            {
                if (seen.TryGetValue(channel, out string? owner))
                    collisions.Add($"'{channel}' used by both {owner} and {relative}");
                else seen[channel] = relative;
            }
        }

        Assert.That(collisions, Is.Empty, string.Join(Environment.NewLine, collisions));
    }

    /// <summary>The feed has to actually ship. Deleting it after packing is what the packaging used to
    /// do, and an app checking a release with no <c>releases.{channel}.json</c> in it never updates.</summary>
    [Test]
    public void ThePackagingDoesNotDeleteTheUpdateFeed()
    {
        string root = RepoRoot();

        var offenders = new List<string>();
        foreach (var (relative, _) in PublishProjects)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;
            string src = File.ReadAllText(path);
            if (src.Contains(".nupkg", StringComparison.OrdinalIgnoreCase)
                || src.Contains("releases.*.json", StringComparison.OrdinalIgnoreCase))
                offenders.Add(relative);
        }

        Assert.That(offenders, Is.Empty,
            "These still name the Velopack feed files, which historically meant deleting them: "
            + string.Join(", ", offenders));
    }

    // ── What the running app asks for ─────────────────────────────────────────

    [Test]
    public void EachApp_HasItsOwnChannelToken()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdateFeed.AppToken(UpdatableApp.Client), Is.EqualTo("client"));
            Assert.That(UpdateFeed.AppToken(UpdatableApp.Editor), Is.EqualTo("editor"));
            Assert.That(UpdateFeed.AppToken(UpdatableApp.Server), Is.EqualTo("server"));
        });
    }

    /// <summary>macOS publishes no feed, so the channel is null rather than a name that would find
    /// nothing — and the reason is available to say out loud instead of reporting "up to date".</summary>
    [Test]
    public void WhereNoFeedIsPublished_ThereIsNoChannelAndAStatedReason()
    {
        if (UpdateFeed.IsSupportedOnThisPlatform)
        {
            Assert.Multiple(() =>
            {
                Assert.That(UpdateFeed.ChannelFor(UpdatableApp.Client), Is.Not.Null);
                Assert.That(UpdateFeed.UnsupportedReason, Is.Empty);
            });
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(UpdateFeed.ChannelFor(UpdatableApp.Client), Is.Null);
            Assert.That(UpdateFeed.UnsupportedReason, Is.Not.Empty);
        });
    }
}
