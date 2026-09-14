namespace Mirage.Scripting;

/// <summary>
/// A type at the boundary between the engine and a script.
///
/// <para>🔴 <b>Nothing outside this project names a Compass type.</b> A game module's host surface is
/// declared in this vocabulary and translated here, so the language the engine scripts in is one
/// project's dependency rather than the whole engine's. That is not tidiness: a host surface written
/// against the compiler's own symbols would have to be rewritten by everybody the day the language
/// changes shape, and there is no reason for a seam declaring "this yields a number" to care.</para>
///
/// <para>The list is what the boundary actually carries — whole numbers, exact decimals, text, truth,
/// optionals of those, sets of those, and the opaque types the engine registers. There is no map, no
/// record, and no engine type crossing by its fields; a script holds a <c>Player</c> and asks it
/// questions, which is the shape that survives the engine changing what a player is.</para>
/// </summary>
public sealed record ScriptType
{
    /// <summary>What kind of thing this is, before a name or an element type is applied.</summary>
    internal enum Shape
    {
        /// <summary>A call that yields nothing. Not writable as a parameter.</summary>
        Nothing,

        /// <summary>A parameter that takes a value of any type. Not writable as a result.</summary>
        Anything,

        Integer,
        Real,
        Text,
        Truth,

        /// <summary>One of the types the catalog declares, by name.</summary>
        Named,

        /// <summary>A set of <see cref="Element"/>.</summary>
        Set,
    }

    private ScriptType(Shape kind, string? name = null, ScriptType? element = null, bool optional = false)
    {
        Kind = kind;
        Name = name;
        Element = element;
        IsOptional = optional;
    }

    internal Shape Kind { get; }
    internal string? Name { get; }
    internal ScriptType? Element { get; }
    internal bool IsOptional { get; }

    /// <summary>A whole number. A binding hands one over as a <see cref="long"/>.</summary>
    public static ScriptType Integer { get; } = new(Shape.Integer);

    /// <summary>
    /// An exact decimal, which is what Compass means by a real — a tenth is a tenth. A binding hands one
    /// over as a <see cref="decimal"/>, and a <c>double</c> is not one.
    /// </summary>
    public static ScriptType Real { get; } = new(Shape.Real);

    /// <summary>Text. A binding hands one over as a <see cref="string"/>.</summary>
    public static ScriptType Text { get; } = new(Shape.Text);

    /// <summary>True or false. A binding hands one over as a <see cref="bool"/>.</summary>
    public static ScriptType Truth { get; } = new(Shape.Truth);

    /// <summary>A call that yields nothing. Only a result, never a parameter.</summary>
    public static ScriptType Nothing { get; } = new(Shape.Nothing);

    /// <summary>
    /// A parameter that accepts a value of any type, which is how the language's own <c>Console.Write</c>
    /// takes anything. Only a parameter, never a result: a member whose result has no type is one no
    /// script could do anything with.
    /// </summary>
    public static ScriptType Anything { get; } = new(Shape.Anything);

    /// <summary>One of the types the same catalog declares, named the way a script writes it.</summary>
    public static ScriptType Of(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        return new ScriptType(Shape.Named, typeName);
    }

    /// <summary>A set of this type.</summary>
    public static ScriptType SetOf(ScriptType element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Kind is Shape.Nothing or Shape.Anything)
        {
            throw new ArgumentException($"There is no set of {element}.", nameof(element));
        }

        return new ScriptType(Shape.Set, element: element);
    }

    /// <summary>
    /// This type, or absent. Compass has no null, so "there might not be one" is a type rather than a
    /// value a script has to remember to check — which is why a member that can fail to find something
    /// says so here rather than by handing back a zero.
    /// </summary>
    public ScriptType OrNothing()
    {
        if (Kind is Shape.Nothing or Shape.Anything)
        {
            throw new InvalidOperationException($"{this} cannot be optional.");
        }

        return IsOptional ? this : new ScriptType(Kind, Name, Element, optional: true);
    }

    /// <summary>A parameter of this type, under that name.</summary>
    public ScriptParameter Named(string name) => new(name, this);

    /// <summary>True for the result of a call that hands nothing back.</summary>
    public bool IsNothing => Kind == Shape.Nothing;

    /// <summary>
    /// How a script would write this type — <b>exactly</b>, because the stub a checker reads is built
    /// out of these strings.
    ///
    /// <para>⚠ A set is <c>Player[]</c>. "set of Player" reads like a description and is not Compass;
    /// written into the stub it takes the whole file down, and every model after it with it.</para>
    /// </summary>
    public override string ToString()
    {
        string root = Kind switch
        {
            Shape.Integer => "integer",
            Shape.Real => "real",
            Shape.Text => "string",
            Shape.Truth => "boolean",
            Shape.Named => Name!,
            Shape.Set => $"{Element}[]",
            Shape.Anything => "any type",
            _ => "nothing",
        };

        return IsOptional ? root + "?" : root;
    }
}
