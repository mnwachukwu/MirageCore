using Velopack;
using Velopack.Sources;

namespace Mirage.Updates;

/// <summary>
/// Looking on GitHub for a newer build of one of the apps.
///
/// <para>Nothing here ever throws at the caller. An update check runs against a network the app does
/// not control, on a schedule nobody asked for, and a release page being unreachable is not a reason
/// for a game or an editor to fall over. Every path returns null instead.</para>
///
/// <para>It also returns null, without contacting anything, in three ordinary situations: on macOS,
/// where no feed is published (see <see cref="UpdateFeed"/>); when the app is running portable or
/// straight from a build output, where there is no installation to replace; and where the release
/// carries nothing newer.</para>
/// </summary>
public static class AppUpdates
{
    /// <summary>An update manager pointed at this app's channel, or null when this build cannot be
    /// updated in place — macOS, a portable copy, or a run from the build output.</summary>
    private static UpdateManager? ManagerFor(UpdatableApp app)
    {
        string? channel = UpdateFeed.ChannelFor(app);
        if (channel is null) return null;

        var manager = new UpdateManager(
            new GithubSource(UpdateFeed.RepositoryUrl, accessToken: null, prerelease: false),
            new UpdateOptions { ExplicitChannel = channel });

        // IsInstalled is false for a portable copy and for a run from bin/, both of which have no
        // Velopack installation behind them to update.
        return manager.IsInstalled ? manager : null;
    }

    /// <summary>Ask what is available without downloading anything. Returns the newer version, or null
    /// when this build is current or cannot be updated in place.
    /// <para>This is the whole of what the SERVER does. Applying an update restarts the process, which
    /// disconnects every player on it, so that stays an operator's deliberate act.</para></summary>
    public static async Task<string?> CheckAsync(UpdatableApp app)
    {
        try
        {
            var manager = ManagerFor(app);
            if (manager is null) return null;
            var info = await manager.CheckForUpdatesAsync();
            return info?.TargetFullRelease.Version.ToString();
        }
        catch
        {
            // Offline, rate-limited, no release yet: all ordinary, none worth a message.
            return null;
        }
    }

    /// <summary>Download a newer build and stage it to install when this process exits. Returns the
    /// version staged, or null when there was nothing to do.
    /// <para>Staged rather than applied: replacing files under a running app means restarting it, and
    /// deciding to restart is not something a background check should do to somebody mid-session. The
    /// next launch is already the new version.</para></summary>
    public static async Task<string?> StageForNextLaunchAsync(UpdatableApp app)
    {
        try
        {
            var manager = ManagerFor(app);
            if (manager is null) return null;
            var info = await manager.CheckForUpdatesAsync();
            if (info is null) return null;

            await manager.DownloadUpdatesAsync(info);
            manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: false);
            return info.TargetFullRelease.Version.ToString();
        }
        catch
        {
            return null;
        }
    }
}
