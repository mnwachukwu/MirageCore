using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One appearance a player may choose when making a character.
///
/// <para><b>Authored, not discovered.</b> A world offers the looks its author decided to offer — which
/// is never simply every row of art the engine happens to have loaded. A game whose cast is four
/// villagers offers four; one that wants a single fixed look offers one; one that assigns appearance
/// some other way offers none and lets its own rules decide.</para>
///
/// <para>What an appearance MEANS is the game's business. A name here reads to a player as whatever the
/// author intends — a species, a build, a uniform, a gender — and the engine attaches nothing to it
/// beyond the art it selects.</para>
/// </summary>
/// <summary>
/// One answer that has to have been given for a look to be offered.
///
/// <para>🔴 <b>Core has no idea what the answer MEANS.</b> It holds a creation question's key and the
/// record numbers this look goes with, and compares them. Whether that reads as "plate armor is for
/// warriors", "a tail is for the beastfolk", or "this uniform belongs to the western company" is the
/// world author's business, and a world that wants every look available to everybody writes none of
/// these.</para>
/// </summary>
public sealed record AppearanceGate
{
    /// <summary>The creation question's key — the same one its answer is written under.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>The record numbers this look is offered for. Empty offers it for none, which is a look
    /// nobody can pick — said plainly rather than silently meaning "all", because a list that means its
    /// opposite when left blank is one an author gets wrong once and never notices.</summary>
    [JsonPropertyName("is")] public IReadOnlyList<int> Is { get; init; } = [];
}

public sealed record CharacterAppearance
{
    /// <summary>What the picker shows. Blank is allowed and reads as an unnamed option, which is what a
    /// world that offers looks without naming them wants.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    /// <summary>Which row of <see cref="SpriteSheet"/> this appearance is drawn from.</summary>
    [JsonPropertyName("sprite")] public int Sprite { get; init; }

    /// <summary>Which sheet <see cref="Sprite"/> is a row of.</summary>
    [JsonPropertyName("spriteSheet")] public int SpriteSheet { get; init; }

    /// <summary>Which answers in the creation flow this look is offered for. Empty is offered to
    /// everybody, which is what most looks in most worlds want.
    ///
    /// <para>Every gate has to hold, and a gate holds when the answer under its key is one of its
    /// numbers. Two gates on one look is "for these classes AND from this homeland".</para></summary>
    [JsonPropertyName("needs")] public IReadOnlyList<AppearanceGate> Needs { get; init; } = [];

    /// <summary>Whether this look is offered to somebody who answered the creation questions this way,
    /// keyed by each question's own key. A question left unanswered fails any gate that names it.
    ///
    /// <para>⚠ Asked on BOTH sides, from the one implementation. The screen filters the list with
    /// it and the server checks the pick against it, so a client that offers a look it should not cannot
    /// make the server accept one.</para></summary>
    public bool OfferedWhen(IReadOnlyDictionary<string, int> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        foreach (AppearanceGate gate in Needs)
        {
            if (!answers.TryGetValue(gate.Key, out int picked)) return false;
            if (!gate.Is.Contains(picked)) return false;
        }

        return true;
    }

    /// <summary>What a world that names no appearances offers: one, drawn from the first row of the
    /// first sheet. A world is playable before anybody has authored a roster.</summary>
    public static readonly CharacterAppearance Default = new();

    /// <summary>The list to offer when a world names none — a single default, so character creation is
    /// never a screen with nothing on it.</summary>
    public static readonly IReadOnlyList<CharacterAppearance> DefaultSet = [Default];
}
