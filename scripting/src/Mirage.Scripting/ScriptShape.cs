namespace Mirage.Scripting;

/// <summary>What kind of value a model's field holds, in terms the engine can act on.</summary>
public enum ScriptFieldShape : byte
{
    /// <summary>Something the host has no equivalent for — a set, an optional, a function. Described
    /// rather than dropped, so a caller can say which field it could not use.</summary>
    Unsupported = 0,

    Text = 1,
    Whole = 2,
    Fraction = 3,
    Truth = 4,

    /// <summary>One of a fixed set. <see cref="ScriptModelField.Choices"/> holds the members.</summary>
    Choice = 5,

    /// <summary>Another model. <see cref="ScriptModelField.TypeName"/> names it.</summary>
    Reference = 6,
}

/// <summary>One field of a model a script declared: what it is called, and what it holds.</summary>
/// <param name="Name">The field's own name, exactly as the script wrote it.</param>
/// <param name="Shape">What it holds.</param>
/// <param name="TypeName">What the script called the type, for a message about a field nobody can use.</param>
/// <param name="Choices">For <see cref="ScriptFieldShape.Choice"/>, the members in declaration order.</param>
public sealed record ScriptModelField(
    string Name,
    ScriptFieldShape Shape,
    string TypeName,
    IReadOnlyList<string> Choices);

/// <summary>
/// The shape of one model a script declared, as something outside this project can read.
///
/// <para><b>A description, not a symbol.</b> Compass's own symbol types stay inside this project, the
/// same way <see cref="ScriptType"/> keeps its type vocabulary in: an engine that named a
/// <c>ModelSymbol</c> would be an engine that breaks when the language reorganizes its compiler.</para>
///
/// <para>Only fields are described. A model's functions are reachable through
/// <see cref="LoadedScript.Offers"/> and <see cref="LoadedScript.Call"/>, which is a different
/// question.</para>
/// </summary>
public sealed record ScriptModelInfo(string Name, IReadOnlyList<ScriptModelField> Fields)
{
    /// <summary>The field with this name, or null. Ordinal, because a script's names are its own.</summary>
    public ScriptModelField? Field(string name)
        => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}
