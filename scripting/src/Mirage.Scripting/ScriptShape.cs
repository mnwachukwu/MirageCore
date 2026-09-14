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
/// <para>Functions are named but not described. What one DOES is reached through
/// <see cref="LoadedScript.Call"/>; what is here is enough to decide whether to call it.</para>
/// </summary>
public sealed record ScriptModelInfo(
    string Name,
    IReadOnlyList<ScriptModelField> Fields,
    IReadOnlyList<ScriptModelFunction> Functions)
{
    /// <summary>The field with this name, or null. Ordinal, because a script's names are its own.</summary>
    public ScriptModelField? Field(string name)
        => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    /// <summary>The function of this name and arity, or null.</summary>
    public ScriptModelFunction? Function(string name, int arity)
        => Functions.FirstOrDefault(
            f => f.Arity == arity && string.Equals(f.Name, name, StringComparison.Ordinal));
}

/// <summary>One function a model declared, as something a host can decide whether to call.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Arity">How many arguments it takes, which is half of what a host matches on.</param>
/// <param name="IsShared">Whether a host can reach it. 🔴 An instance function needs an instance the
/// host has no way to name, so one is declared and never called — which is invisible from inside the
/// module, and worth telling an author about rather than skipping.</param>
public sealed record ScriptModelFunction(string Name, int Arity, bool IsShared);
