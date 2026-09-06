namespace Mirage.Shared;

/// <summary>
/// Shared rule that picks the overhead-name <see cref="GameColor"/> for a player.  Used by the
/// client world renderer for the overhead name and by the party overlay so both reads of "this
/// player's color" stay aligned.
/// </summary>
public static class PlayerNameColor
{
    public static int For(bool showAsPk, AdminLevel access) =>
        showAsPk ? GameColor.BrightRed : access switch
        {
            AdminLevel.Monitor => GameColor.Orange,
            AdminLevel.Mapper => GameColor.Turquoise,
            AdminLevel.Developer => GameColor.RoyalBlue,
            AdminLevel.Creator => GameColor.Amethyst,
            _ => GameColor.Tan,
        };
}
