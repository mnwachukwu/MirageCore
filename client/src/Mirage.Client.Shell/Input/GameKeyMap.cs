using Microsoft.Xna.Framework.Input;
using Mirage.Shared.Extensibility;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Input;

/// <summary>
/// The keyboard key behind each name a game may bind.
///
/// <para><b>This is the other half of <see cref="GameKey.Offered"/>, and the pair is the whole point.</b>
/// The engine decides which keys a game may name; this decides what pressing one means. A name offered
/// with nothing here binds nothing, and a key here that the engine does not offer can never be reached —
/// and neither would report anything, because a shortcut that silently does not work looks exactly like
/// a player who has not pressed it yet. A test holds the two sets equal.</para>
/// </summary>
internal static class GameKeyMap
{
    private static readonly Dictionary<string, Keys> Bound = new(System.StringComparer.Ordinal)
    {
        ["B"] = Keys.B,
        ["E"] = Keys.E,
        ["J"] = Keys.J,
        ["K"] = Keys.K,
        ["N"] = Keys.N,
        ["P"] = Keys.P,
        ["Q"] = Keys.Q,
        ["R"] = Keys.R,
        ["T"] = Keys.T,
        ["U"] = Keys.U,
        ["Y"] = Keys.Y,
        ["Z"] = Keys.Z,
    };

    /// <summary>Every name this knows a key for. What the test compares against the engine's list.</summary>
    internal static IReadOnlyCollection<string> Names => Bound.Keys;

    /// <summary>The key <paramref name="name"/> stands for. False for a blank name, which is what almost
    /// everything a game declares carries.</summary>
    public static bool TryResolve(string? name, out Keys key)
    {
        key = default;
        return !string.IsNullOrEmpty(name) && Bound.TryGetValue(name, out key);
    }

    /// <summary>How a bound key is shown beside the thing it reaches — "(Q)". Blank for no key, so a
    /// caption can append this unconditionally.</summary>
    public static string Hint(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : $"  ({name})";
}
