namespace Mirage.Updates;

/// <summary>Which app is asking for updates. Each ships its own installer and its own feed.</summary>
public enum UpdatableApp
{
    Client,
    Editor,
    Server,
}

/// <summary>
/// Where the apps look for updates, and under what name.
///
/// <para>Updates are published as GitHub releases on the game repository. Velopack reads a release's
/// assets and looks for one called <c>releases.{channel}.json</c> — with no pack id in the name. CI
/// attaches every file under <c>installers/</c> to a SINGLE release and release assets are a flat
/// namespace, so three apps sharing a channel would write that one filename three times and two of
/// them would lose. The channel therefore carries the app as well as the platform, and these strings
/// have to match the <c>&lt;_Channel&gt;</c> values the three <c>*.Publish.csproj</c> files pass to
/// <c>vpk pack</c>. <c>UpdateChannelTests</c> reads those files and fails if they drift.</para>
///
/// <para><b>macOS has no update feed.</b> Velopack can update a <c>.app</c> perfectly well — it ships an
/// <c>OsxVelopackLocator</c> for exactly that. We do not give it one: a macOS bundle cannot be
/// cross-built from the Windows/Linux release runner, so that leg hand-rolls a <c>.app.tar.gz</c> with
/// <c>tar</c> instead of packing with <c>vpk</c>. No <c>vpk pack</c> means no <c>.nupkg</c> and no
/// feed, so there is nothing for an update check to read. Mac users update by downloading the tarball
/// again. Fixing it means packing that leg on a real macOS runner, which is a separate job.</para>
/// </summary>
public static class UpdateFeed
{
    /// <summary>The GitHub repository whose releases carry the packages.</summary>
    public const string RepositoryUrl = "https://github.com/mnwachukwu/MirageSourceRemastered";

    /// <summary>The channel token for an app on the platform this process is running on, or null where
    /// no feed is published. Null is the normal answer on macOS and on any platform we do not ship.</summary>
    public static string? ChannelFor(UpdatableApp app)
    {
        string? platform = PlatformToken();
        return platform is null ? null : $"{AppToken(app)}-{platform}";
    }

    /// <summary>The app half of a channel. Lower-case and stable — it is baked into published filenames.</summary>
    public static string AppToken(UpdatableApp app) => app switch
    {
        UpdatableApp.Client => "client",
        UpdatableApp.Editor => "editor",
        UpdatableApp.Server => "server",
        _ => "client",
    };

    /// <summary>The platform half, or null where updates are not published.
    /// <para>macOS returns null deliberately — see the remarks on this class.</para></summary>
    public static string? PlatformToken()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsLinux()) return "linux";
        return null;
    }

    /// <summary>Whether an update check can find anything on this platform at all. False on macOS, where
    /// the app is shipped as a plain tarball rather than a Velopack package.</summary>
    public static bool IsSupportedOnThisPlatform => PlatformToken() is not null;

    /// <summary>Why updates are unavailable here, for a UI that would otherwise show nothing. Empty when
    /// they ARE available.</summary>
    public static string UnsupportedReason =>
        IsSupportedOnThisPlatform
            ? ""
            : "Automatic updates are not available on macOS: the macOS build ships as a plain archive "
              + "rather than an installer, so there is no update feed to check. Download the latest "
              + "release to update.";
}
