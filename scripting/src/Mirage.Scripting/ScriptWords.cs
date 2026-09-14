namespace Mirage.Scripting;

/// <summary>
/// What Compass reserves, for the engine's own use in naming things a script will read.
///
/// <para>🔴 <b>A parameter named for a reserved word does not parse.</b> The catalog's parameter names
/// are written into the stub a checker reads, so a member taking a <c>string to</c> produces a file
/// that fails to compile — and it fails in the generated file, which is the one place an author
/// cannot fix it. Refused where the member is registered instead, which is where the name is.</para>
///
/// <para>The list is the one <c>cm vocabulary</c> prints. It is short, closed, and changes about
/// once a release, so it is written here rather than shelled out for while a world is loading.</para>
/// </summary>
public static class ScriptWords
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "base", "begin", "bitwise", "boolean", "break", "case", "catch",
        "character", "constant", "continue", "default", "delegate", "each", "else", "end",
        "enumeration", "extends", "false", "finally", "float", "for", "fraction", "function", "if",
        "import", "in", "integer", "internal", "is", "let", "loop", "model", "namespace", "new",
        "not", "or", "override", "protected", "public", "real", "sealed", "shared", "shiftleft",
        "shiftright", "stepby", "string", "structure", "switch", "then", "this", "throw", "to",
        "true", "try", "until", "using", "virtual", "while", "xor", "yield",
    };

    /// <summary>Whether Compass reserves this word, so nothing may be named for it.</summary>
    public static bool IsReserved(string word) => Reserved.Contains(word);
}
