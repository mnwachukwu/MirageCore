using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Localization;
using NUnit.Framework;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// The log lines the server writes while starting up, rendered with the arguments it actually passes.
///
/// <para>🔴 <c>StringLoader</c> THROWS on a placeholder with no value, and these run before the host is
/// up — so a template naming a field the call site stopped supplying is not a wrong log line, it is a
/// server that will not boot. Nothing else covers it: every suite can be green while the thing refuses to
/// start, because no test starts it.</para>
///
/// <para>The argument lists below are deliberately a second copy of the call sites in
/// <c>MirageServerService.LoadWorldDataAsync</c>. Two copies that must agree is the whole point — when
/// they disagree, this fails instead of the server.</para>
/// </summary>
[TestFixture]
public class StartupLogTemplateTests
{
    [OneTimeSetUp]
    public void LoadStrings() => ServerStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));

    private static readonly (string Key, object? Value)[] WorldCounts =
    [
        ("Items", 1), ("Npcs", 1), ("Shops", 1),
        ("Quests", 1), ("Conversations", 1), ("Maps", 1),
    ];

    [Test]
    public void TheLoadedSummary_RendersWithWhatTheServerPasses()
    {
        Assert.DoesNotThrow(() =>
            LocalizedLog.Info(NullLogger.Instance, ServerStrings.Server_LoadedSummary, WorldCounts));
    }

    [Test]
    public void ThePaddedSummary_RendersWithWhatTheServerPasses()
    {
        Assert.DoesNotThrow(() =>
            LocalizedLog.Info(NullLogger.Instance, ServerStrings.Server_PaddedSummary, WorldCounts));
    }

    /// <summary>Every language, not just the one this machine runs in. A translator's copy of a template
    /// carries its own placeholders, so a field removed from the English one can survive in three others
    /// and take the server down for whoever runs it in Spanish.</summary>
    [Test]
    public void EveryLanguage_RendersBothSummaries()
    {
        string langDir = Path.Combine(AppContext.BaseDirectory, "lang");

        Assert.Multiple(() =>
        {
            foreach (string file in Directory.GetFiles(langDir, "*.json"))
            {
                string locale = Path.GetFileNameWithoutExtension(file);
                foreach (string key in new[] { ServerStrings.Server_LoadedSummary, ServerStrings.Server_PaddedSummary })
                {
                    Assert.DoesNotThrow(
                        () => ServerStrings.ForLocale(locale, key, WorldCounts),
                        $"{locale}.json: {key} names a placeholder the server does not supply");
                }
            }
        });
    }
}
