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
public sealed record CharacterAppearance
{
    /// <summary>What the picker shows. Blank is allowed and reads as an unnamed option, which is what a
    /// world that offers looks without naming them wants.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    /// <summary>Which row of <see cref="SpriteSheet"/> this appearance is drawn from.</summary>
    [JsonPropertyName("sprite")] public int Sprite { get; init; }

    /// <summary>Which sheet <see cref="Sprite"/> is a row of.</summary>
    [JsonPropertyName("spriteSheet")] public int SpriteSheet { get; init; }

    /// <summary>What a world that names no appearances offers: one, drawn from the first row of the
    /// first sheet. A world is playable before anybody has authored a roster.</summary>
    public static readonly CharacterAppearance Default = new();

    /// <summary>The list to offer when a world names none — a single default, so character creation is
    /// never a screen with nothing on it.</summary>
    public static readonly IReadOnlyList<CharacterAppearance> DefaultSet = [Default];
}
