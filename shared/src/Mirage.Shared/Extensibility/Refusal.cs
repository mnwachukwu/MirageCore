namespace Mirage.Shared.Extensibility;

/// <summary>
/// The answer to "may this happen", carrying the reason when the answer is no.
///
/// <para><b>It is fail-closed, and the zero value denies.</b> <c>default(Refusal)</c>
/// denies. A gate that forgot to assign an answer, a collection of gates that was never populated, and
/// a struct that was never written all refuse — so unfinished wiring fails by doing nothing rather
/// than by happening to everyone. A <c>bool</c> defaulting to
/// <c>true</c> is the same mechanism with the opposite failure.</para>
///
/// <para><b>The reason is a localization key, not a sentence.</b> The side that knows why is rarely the
/// side that speaks the player's language, and a message assembled here would arrive in the language of
/// whichever process built it.</para>
/// </summary>
public readonly record struct Refusal
{
    private Refusal(bool allowed, string? reasonKey, int color)
    {
        Allowed = allowed;
        ReasonKey = reasonKey;
        Color = color;
    }

    /// <summary>Whether it may happen. False for <c>default</c>.</summary>
    public bool Allowed { get; }

    /// <summary>Why not, as a localization key. Null when allowed, and null for a refusal that has no
    /// explanation worth showing — a gate may decline silently.</summary>
    public string? ReasonKey { get; }

    /// <summary>What color to say it in. Meaningless when <see cref="Allowed"/>.</summary>
    public int Color { get; }

    /// <summary>Yes.</summary>
    public static Refusal Allow => new(true, null, 0);

    /// <summary>No, for a reason worth telling the player.</summary>
    public static Refusal Deny(string reasonKey, int color = GameColor.BrightRed)
        => new(false, reasonKey, color);

    /// <summary>No, with nothing to say about it. What <c>default</c> means.</summary>
    public static Refusal DenySilently => default;

    /// <summary>True when this refuses and has something to say — the condition for sending a
    /// message.</summary>
    public bool HasReason => !Allowed && !string.IsNullOrEmpty(ReasonKey);

    /// <summary>The first refusal among these, or <see cref="Allow"/> when every one of them allows.
    ///
    /// <para>Short-circuits, so a gate that is expensive to evaluate can be ordered after a cheap one.
    /// An empty set allows: nothing objected. That is the one place this mechanism is deliberately
    /// fail-OPEN, so a game that registers no gates behaves as though the gate did not
    /// exist.</para></summary>
    public static Refusal FirstRefusal(IEnumerable<Func<Refusal>> gates)
    {
        foreach (var gate in gates)
        {
            var answer = gate();
            if (!answer.Allowed) return answer;
        }

        return Allow;
    }

    public override string ToString() => Allowed ? "allow" : $"deny({ReasonKey ?? "silent"})";
}
