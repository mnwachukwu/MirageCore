namespace Mirage.Shared;

/// <summary>
/// What a player's name is colored by RANK. Read by the world renderer, the chat log and the party
/// overlay, so all three say the same thing about the same person.
///
/// <para>⚠ Access only. An operator's rank is a permission Core owns, and it reads the same in every
/// world — a game cannot repaint it, and so cannot disguise one. Anything a GAME decides about a name,
/// including what a marked player looks like, is declared instead and arrives as a free color.</para>
/// </summary>
public static class PlayerNameColor
{
    public static int For(AdminLevel access) =>
        access switch
        {
            AdminLevel.Monitor => GameColor.Orange,
            AdminLevel.Mapper => GameColor.Turquoise,
            AdminLevel.Developer => GameColor.RoyalBlue,
            AdminLevel.Creator => GameColor.Amethyst,
            _ => GameColor.Tan,
        };
}
