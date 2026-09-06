using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Config;

/// <summary>
/// The global client settings stored in <c>appsettings.json</c> — everything that is not per-character.
/// The property initializers below ARE the shipped defaults: <c>MirageGame.ReadConfig</c> starts from a
/// fresh instance and overwrites only the keys the file actually contains, so a missing file, a missing
/// key and a hand-deleted key all land on the same value from one declaration.
///
/// <para>This replaced a fourteen-element tuple. Nine of its members were <c>int</c> or <c>bool</c>, all
/// assigned positionally at the single call site, where transposing (say) the window X and Y — or the
/// music volume and the menu track — would have compiled cleanly and been wrong.</para>
///
/// <para>Deliberately mutable: <c>ReadConfig</c> fills it in place as it walks the JSON, which is what
/// lets a value that fails to parse leave the remaining keys alone rather than discarding the whole file.
/// Nothing writes to it after startup.</para>
/// </summary>
public sealed record ClientSettings
{
    public string ServerHost { get; set; } = "localhost";
    public int ServerPort { get; set; } = 4000;
    public bool MaintainAspectRatio { get; set; } = true;
    public bool PlayMusic { get; set; } = true;
    public int MusicVolume { get; set; } = 100;
    public int MainMenuMusic { get; set; } = 1;
    public bool UseGamepad { get; set; }
    public string Language { get; set; } = "en";

    /// <summary>Whether the login screen prefills the account name, and the name it prefills. NAMES ONLY —
    /// no password is stored, in any form. Off by default: a name on disk is a small disclosure, so it
    /// happens because the player ticked a box, never because they did nothing.</summary>
    public bool RememberLogin { get; set; }
    public string RememberedLogin { get; set; } = "";

    /// <summary>The Options window's saved position/size, or null when the player has never moved it —
    /// the panel then keeps the centered rectangle it was declared with.</summary>
    public Rectangle? OptionsPanelBounds { get; set; }

    /// <summary><see cref="int.MinValue"/> means "never saved": the window takes whatever position the OS
    /// assigns it, which is then captured on the first frame.</summary>
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; } = UiHelper.RefW;
    public int WindowHeight { get; set; } = UiHelper.RefH;
    public bool WindowMaximized { get; set; }
}
