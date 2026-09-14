using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>How an <see cref="ActionCondition"/> reads the value it names.</summary>
public enum ConditionTest : byte
{
    /// <summary>The body carries the key at all, whatever it holds.</summary>
    Present = 0,

    /// <summary>The body does not carry it. What a verb that GRANTS something wants: offered until it
    /// has been used, and gone afterwards.</summary>
    Absent = 1,

    /// <summary>Carried, and at least <see cref="ActionCondition.Value"/>.</summary>
    AtLeast = 2,

    /// <summary>Carried, and no more than <see cref="ActionCondition.Value"/>.</summary>
    AtMost = 3,

    /// <summary>Carried, and exactly <see cref="ActionCondition.Value"/>.</summary>
    Exactly = 4,
}

/// <summary>
/// When a declared action is offered, as a question about an attribute the player already carries.
///
/// <para><b>A predicate rather than a rule.</b> Core has no idea what a game's verb means, so it cannot
/// be asked "may they do this" — but it can be asked "does this body's <c>harvest.satchel</c> read at
/// least one", because the attribute is already there and already synced. That is the whole of what
/// crosses: a key, a comparison, and a number.</para>
///
/// <para><b>It reads the ACTOR, never the target.</b> "Only while holding a satchel" is about the person
/// clicking. Whether the thing they clicked is a valid target is a question about a verb Core has no
/// name for, and belongs to the game's handler.</para>
///
/// <para>🔴 <b>Both sides ask the same question with the same code.</b> The client grays the entry out
/// and the server refuses the invoke, and they agree because <see cref="Holds"/> is the only
/// implementation. A predicate the client enforced alone would be a rule any modified client could
/// ignore; one the server enforced alone would be a menu item that fails when it is picked.</para>
/// </summary>
/// <param name="Key">The attribute to read, or blank for a verb that is always offered.</param>
/// <param name="Test">How to read it.</param>
/// <param name="Value">What to compare against, for the tests that compare.</param>
public readonly record struct ActionCondition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("test")] ConditionTest Test,
    [property: JsonPropertyName("n")] long Value)
{
    /// <summary>No condition: the verb is always offered. The default, so an action that says nothing
    /// about when it applies is offered always rather than never.</summary>
    public static ActionCondition Always => default;

    /// <summary>True for the condition that asks nothing.</summary>
    [JsonIgnore] public bool AsksNothing => string.IsNullOrEmpty(Key);

    /// <summary>Whether a body carrying <paramref name="bag"/> may be offered this.
    ///
    /// <para>A body with no attributes at all answers the same way one missing the key does, because
    /// those are the same thing: nothing said about a key is nothing carried.</para></summary>
    public bool Holds(AttributeBag? bag)
    {
        if (AsksNothing) return true;

        bool present = bag is not null && bag.TryGet(Key, out _);
        if (Test == ConditionTest.Present) return present;
        if (Test == ConditionTest.Absent) return !present;
        if (!present) return false;

        long carried = bag![Key].AsLong();
        return Test switch
        {
            ConditionTest.AtLeast => carried >= Value,
            ConditionTest.AtMost => carried <= Value,
            ConditionTest.Exactly => carried == Value,

            // An unknown test is a client and a server disagreeing about what a number means, which is
            // the one case where refusing is safer than allowing: a verb that will not appear is a bug
            // somebody reports, and one that appears when it should not is a rule silently gone.
            _ => false,
        };
    }

    /// <summary>Carrying the key at all.</summary>
    public static ActionCondition Carrying(string key) => new(key, ConditionTest.Present, 0);

    /// <summary>Not carrying it.</summary>
    public static ActionCondition NotCarrying(string key) => new(key, ConditionTest.Absent, 0);

    /// <summary>Carrying at least <paramref name="value"/> of it.</summary>
    public static ActionCondition AtLeast(string key, long value)
        => new(key, ConditionTest.AtLeast, value);
}
