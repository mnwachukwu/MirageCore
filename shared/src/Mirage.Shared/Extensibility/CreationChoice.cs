using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One thing a game asks before a character exists.
///
/// <para>🔴 <b>The one question a game could not otherwise ask.</b> Everything else a game wants to
/// know it asks of a body that is already in the world — a panel, a verb, a conversation. A class, a
/// bloodline, a starting town, a difficulty: those decide what the character IS, so being asked after
/// they exist is being asked too late, and being asked by walking up to somebody is a different game
/// than the one that was wanted.</para>
///
/// <para><b>A LIST of options, and the answer is which one.</b> Not free text, not a number — the
/// screen is the first thing a new player sees and the only thing it should be able to do wrong is
/// pick the option they did not mean. Options come from a record family the game authored, so what is
/// offered comes from a world author rather than from a script hard-coding it.</para>
///
/// <para><b>The answer lands on the character's own attributes, under <see cref="Key"/>, before the
/// game is told they joined.</b> So a game reads it the ordinary way and needs no handler of its own:
/// by the time <c>OnPlayerJoined</c> runs, the choice is already on the body.</para>
/// </summary>
public sealed record CreationChoice
{
    /// <summary>The attribute key the answer is written under, as the number of the record chosen.
    ///
    /// <para>Persisted with the character, so it cannot be renamed freely — a character whose choice
    /// lives under a key nothing declares has made a choice nothing can read.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>Localization key for the caption above the list.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Which record family the options come from.</summary>
    [JsonPropertyName("familyId")] public string FamilyId { get; init; } = string.Empty;

    /// <summary>What to show, in slot order, filled by the SERVER when it greets a client.
    ///
    /// <para>⚠ Declared empty and never authored: a game says which records to offer and the server
    /// says what they are called, because only the server holds them. A client cannot be asked to
    /// resolve a family it has never seen.</para></summary>
    [JsonPropertyName("options")] public IReadOnlyList<CreationOption> Options { get; init; } = [];
}

/// <summary>One thing on a creation list: which record it is, and what to call it.</summary>
/// <param name="Num">The record's own 1-based slot, which the answer carries.</param>
/// <param name="Name">What the player reads.</param>
/// <param name="Description">A line under it, or blank. What the record itself says about being chosen.</param>
public readonly record struct CreationOption(
    [property: JsonPropertyName("num")] int Num,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

/// <summary>
/// Everything a game asks at character creation, in the order it asks.
///
/// <para>Empty until a game says otherwise, and then the creation screen asks for a name and an
/// appearance exactly as it always has. That is a supported world, not a half-configured one.</para>
/// </summary>
public sealed class CreationChoiceSet(IReadOnlyList<CreationChoice> choices)
{
    /// <summary>A world that asks nothing beyond a name and a face.</summary>
    public static readonly CreationChoiceSet Empty = new([]);

    public IReadOnlyList<CreationChoice> Choices { get; } = choices;

    public int Count => Choices.Count;
}
