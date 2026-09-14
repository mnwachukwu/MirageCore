using Compass.Compiler.Semantics;
using Compass.Runtime;

namespace Mirage.Scripting;

/// <summary>
/// What runs when a script calls something the engine registered.
///
/// <para><paramref name="receiver"/> is the value to the left of the dot, or null where the member was
/// reached through its type's name. <paramref name="arguments"/> are evaluated, in the order written,
/// in the CLR shapes <see cref="ScriptType"/> names.</para>
/// </summary>
public delegate object? ScriptCall(object? receiver, IReadOnlyList<object?> arguments);

/// <summary>
/// The types a script may name beyond the ones the language owns, and what each of their members does.
///
/// <para>🔴 <b>The catalog is the whole sandbox for anything the engine offers.</b> A script reaches
/// exactly what is declared here and nothing else — there is no ambient file access, no process, no
/// reflection, and no way to name a CLR type that was not registered. What the LANGUAGE offers is a
/// separate question with a separate answer: see <see cref="ScriptSandbox"/>.</para>
///
/// <para>🔴 <b>A catalog is built once and shared, never rebuilt per compilation.</b> Building a second
/// from the same declarations gives the same type two symbol identities, and a member yielding a
/// <c>Player</c> from one catalog will not fit a parameter named against the other — which the compiler
/// reports as a type error on code that is plainly correct. <see cref="Declare"/> is the only way in and
/// does the two-pass build internally, so the mistake cannot be made from outside this project.</para>
/// </summary>
public sealed class ScriptCatalog
{
    private ScriptCatalog(ExternalCatalog externals, IReadOnlyList<string> typeNames)
    {
        Externals = externals;
        TypeNames = typeNames;
    }

    /// <summary>A catalog offering nothing, which is what a script compiled without a host gets.</summary>
    public static ScriptCatalog Empty { get; } = new(ExternalCatalog.Empty, []);

    /// <summary>The names a script may write. Ordered as they were declared.</summary>
    public IReadOnlyList<string> TypeNames { get; }

    internal ExternalCatalog Externals { get; }

    /// <summary>
    /// What this catalog offers, as data.
    ///
    /// <para>🔴 <b>The reference a script author reads is generated from here.</b> A member cannot exist
    /// without appearing in it, and the sentence describing it sits beside the binding that performs it,
    /// so the two cannot drift. A hand-written page listing the same members would drift the first time
    /// somebody added one in a hurry, and nothing would report it.</para>
    /// </summary>
    public IReadOnlyList<ScriptTypeInfo> Types { get; private init; } = [];

    /// <summary>
    /// Declares a catalog.
    ///
    /// <para>The whole surface is described inside one call because a member's signature usually has to
    /// name a type the same catalog declares — <c>World.PlayerNamed</c> yields a <c>Player</c> — and the
    /// names have to be settled before any signature can mention one.</para>
    /// </summary>
    public static ScriptCatalog Declare(Action<ScriptCatalogBuilder> declare)
    {
        ArgumentNullException.ThrowIfNull(declare);

        var builder = new ScriptCatalogBuilder();
        declare(builder);

        IReadOnlyList<DeclaredType> declared = builder.Declared;
        if (declared.Count == 0) return Empty;

        // One catalog, described after its names are settled. This is the two-pass build, and it is not
        // an optimization: a second catalog built from the same description carries a second set of
        // symbols for the same names, and nothing then fits anything.
        ExternalCatalog externals = ExternalCatalog.Of(
            [.. declared.Select(t => t.Name)],
            catalog => [.. declared.Select(t => Describe(t, catalog))]);

        return new ScriptCatalog(externals, [.. declared.Select(t => t.Name)])
        {
            Types = [.. declared.Select(t => new ScriptTypeInfo(
                t.Name,
                t.Shared,
                [.. t.Members.Select(m => new ScriptMemberInfo(m.Name, m.Yields, m.Takes, m.IsValue, m.Note))]))],
        };
    }

    private static BuiltInModelInfo Describe(DeclaredType type, ExternalCatalog catalog) =>
        new(
            type.Name,
            Namespace: "Standard",
            MayBeExtended: false,
            Members: [.. type.Members.Select(m => Describe(type, m, catalog))],
            HasNoInstances: type.Shared);

    private static BuiltInMember Describe(DeclaredType type, DeclaredMember member, ExternalCatalog catalog) =>
        new(
            member.Name,
            Resolve(member.Yields, catalog, result: true),
            [.. member.Takes.Select(p => Resolve(p.Type, catalog, result: false))],
            IsValue: member.IsValue,
            // A type with no instances is reached only through its name; one with instances only through
            // a value. Nothing here wants both, which is a language convenience for Random and not a
            // shape an engine seam should be offering.
            Reach: type.Shared ? Reached.ThroughTheName : Reached.ThroughAValue,
            Binding: Bind(type, member));

    /// <summary>
    /// Wraps a binding so that what it hands back is the shape it said it would.
    ///
    /// <para>🔴 <b>This is the half of the boundary the compiler cannot check.</b> A call's arguments are
    /// type-checked before the binding ever sees them, but the value going the other way is whatever the
    /// engine's own code returned — so a member declared to yield an integer that returns an
    /// <see cref="int"/> rather than a <see cref="long"/>, or a <c>double</c> where the language means an
    /// exact decimal, is wrong in a way nothing would otherwise report. It surfaces later, somewhere
    /// else, as a script misbehaving.</para>
    /// </summary>
    private static ExternalBinding Bind(DeclaredType type, DeclaredMember member)
    {
        ScriptCall run = member.Run;
        string where = $"{type.Name}.{member.Name}";
        ScriptType yields = member.Yields;

        return (receiver, arguments) =>
        {
            object? result = run(receiver, arguments);

            if (!Fits(result, yields))
            {
                throw new InvalidOperationException(
                    $"'{where}' is declared to yield {yields} and handed back "
                    + $"{(result is null ? "nothing" : result.GetType().Name)}.");
            }

            return result;
        };
    }

    private static bool Fits(object? value, ScriptType type)
    {
        if (value is null) return type.IsOptional || type.Kind == ScriptType.Shape.Nothing;

        return type.Kind switch
        {
            ScriptType.Shape.Nothing => false,
            ScriptType.Shape.Integer => value is long,
            ScriptType.Shape.Real => value is decimal,
            ScriptType.Shape.Text => value is string,
            ScriptType.Shape.Truth => value is bool,
            ScriptType.Shape.Set => value is ICompassSet,
            // An opaque type is whatever CLR object the engine chose to represent it with, so all that
            // can be said is that it is not one of the shapes the language would read as its own.
            ScriptType.Shape.Named => value is not (long or decimal or string or bool or ICompassSet),
            _ => true,
        };
    }

    private static TypeSymbol? Resolve(ScriptType type, ExternalCatalog catalog, bool result)
    {
        if (type.Kind == ScriptType.Shape.Nothing)
        {
            if (!result) throw new ArgumentException("A parameter cannot have no type.");
            return null;
        }

        if (type.Kind == ScriptType.Shape.Anything)
        {
            if (result) throw new ArgumentException("A member yielding a value of any type is one no script could use.");
            return null;
        }

        TypeSymbol root = type.Kind switch
        {
            ScriptType.Shape.Integer => PrimitiveType.Integer,
            ScriptType.Shape.Real => PrimitiveType.Real,
            ScriptType.Shape.Text => PrimitiveType.String,
            ScriptType.Shape.Truth => PrimitiveType.Boolean,
            ScriptType.Shape.Set => new SetType(Resolve(type.Element!, catalog, result)!),
            _ => catalog.SymbolFor(type.Name!)
                 ?? throw new ArgumentException(
                     $"'{type.Name}' is named in a signature and is not one of this catalog's types."),
        };

        return type.IsOptional ? new OptionalType(root) : root;
    }

    internal sealed record DeclaredMember(
        string Name, ScriptType Yields, IReadOnlyList<ScriptParameter> Takes, bool IsValue,
        ScriptCall Run, string Note);

    internal sealed record DeclaredType(string Name, bool Shared, List<DeclaredMember> Members);
}

/// <summary>Declares the types a script may name. See <see cref="ScriptCatalog.Declare"/>.</summary>
public sealed class ScriptCatalogBuilder
{
    private readonly List<ScriptCatalog.DeclaredType> _types = [];

    internal IReadOnlyList<ScriptCatalog.DeclaredType> Declared => _types;

    /// <summary>
    /// A type a script holds values of and asks questions — <c>Player</c>, <c>Npc</c>. It cannot be
    /// declared, extended, or constructed by a script; values of it arrive from somewhere else and are
    /// opaque.
    /// </summary>
    public ScriptTypeBuilder Type(string name) => Add(name, shared: false);

    /// <summary>
    /// A type with no instances, whose members are reached through its name — <c>World</c>. This is how
    /// the engine offers something that is not about one particular thing.
    /// </summary>
    public ScriptTypeBuilder Shared(string name) => Add(name, shared: true);

    private ScriptTypeBuilder Add(string name, bool shared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_types.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"'{name}' is declared twice. A name belongs to one type.", nameof(name));
        }

        var type = new ScriptCatalog.DeclaredType(name, shared, []);
        _types.Add(type);

        return new ScriptTypeBuilder(type);
    }
}

/// <summary>Declares what one registered type offers.</summary>
public sealed class ScriptTypeBuilder
{
    private readonly ScriptCatalog.DeclaredType _type;

    internal ScriptTypeBuilder(ScriptCatalog.DeclaredType type) => _type = type;

    /// <summary>This type, for naming in another member's signature.</summary>
    public ScriptType AsType => ScriptType.Of(_type.Name);

    /// <summary>
    /// Something a script calls, with parentheses.
    ///
    /// <para>🔴 <b>A parameter is named, not just typed.</b> The name is written into the stub the
    /// checker reads, so it is what an author sees in the editor while they are typing the call —
    /// <c>Field(string key, string caption, ...)</c> rather than six positions they have to count.</para>
    /// </summary>
    /// <exception cref="ArgumentException">A parameter is named for one of Compass's own reserved
    /// words, which would not parse in the stub.</exception>
    public ScriptTypeBuilder Function(
        string name, ScriptType yields, ScriptParameter[] takes, ScriptCall run, string note = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(yields);
        ArgumentNullException.ThrowIfNull(takes);
        ArgumentNullException.ThrowIfNull(run);

        foreach (ScriptParameter parameter in takes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameter.Name);

            if (ScriptWords.IsReserved(parameter.Name))
            {
                throw new ArgumentException(
                    $"'{name}' names a parameter '{parameter.Name}', which Compass reserves. "
                    + "The stub written for the checker would not parse.", nameof(takes));
            }
        }

        _type.Members.Add(new ScriptCatalog.DeclaredMember(
            name, yields, [.. takes], IsValue: false, run, note ?? string.Empty));

        return this;
    }

    /// <summary>A call that yields nothing.</summary>
    public ScriptTypeBuilder Action(
        string name, ScriptParameter[] takes, ScriptCall run, string note = "") =>
        Function(name, ScriptType.Nothing, takes, run, note);

    /// <summary>
    /// Something a script reads, with no parentheses — <c>who.Name</c>. Writing the parentheses is
    /// reported, the same way it is for the language's own.
    /// </summary>
    public ScriptTypeBuilder Value(string name, ScriptType yields, ScriptCall read, string note = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(yields);
        ArgumentNullException.ThrowIfNull(read);

        if (yields.Kind == ScriptType.Shape.Nothing)
        {
            throw new ArgumentException($"'{name}' is a value, so it has to be a value of something.", nameof(yields));
        }

        _type.Members.Add(new ScriptCatalog.DeclaredMember(
            name, yields, [], IsValue: true, read, note ?? string.Empty));

        return this;
    }
}

/// <summary>
/// The CLR shapes a binding hands values over in.
///
/// <para>A Compass integer is a <see cref="long"/> and a Compass real is a <see cref="decimal"/>, which
/// are not the shapes engine code usually holds. These do the conversion in one place so that a binding
/// reads as what it means rather than as a cast.</para>
/// </summary>
public static class ScriptValue
{
    /// <summary>A whole number, whatever width the engine was holding it at.</summary>
    public static object Integer(long value) => value;

    /// <summary>An exact decimal. A <c>double</c> is not one, and rounding here is deliberate.</summary>
    public static object Real(decimal value) => value;

    /// <summary>An exact decimal from a binary float, which is where the exactness stops.</summary>
    public static object Real(double value) => (decimal)value;

    /// <summary>Text.</summary>
    public static object Text(string value) => value;

    /// <summary>Truth.</summary>
    public static object Truth(bool value) => value;

    /// <summary>An absent optional. Compass has no null, so this is how "there is not one" is said.</summary>
    public static object? Absent => null;

    /// <summary>A set, in the order given — a Compass set keeps its order and allows a value twice.</summary>
    public static object Set(IEnumerable<object?> values) => new CompassSet<object?>(values);

    /// <summary>A set of whole numbers.</summary>
    public static object Set(IEnumerable<long> values) => new CompassSet<object?>(values.Select(v => (object?)v));

    /// <summary>A set of text.</summary>
    public static object Set(IEnumerable<string> values) => new CompassSet<object?>(values.Select(v => (object?)v));

    /// <summary>An argument as a whole number.</summary>
    public static long AsInteger(this IReadOnlyList<object?> arguments, int index) => (long)arguments[index]!;

    /// <summary>An argument as an exact decimal.</summary>
    public static decimal AsReal(this IReadOnlyList<object?> arguments, int index) => (decimal)arguments[index]!;

    /// <summary>An argument as text.</summary>
    public static string AsText(this IReadOnlyList<object?> arguments, int index) => (string)arguments[index]!;

    /// <summary>An argument as truth.</summary>
    public static bool AsTruth(this IReadOnlyList<object?> arguments, int index) => (bool)arguments[index]!;

    /// <summary>An argument as one of the engine's own values, or null where the script passed none.</summary>
    public static T? As<T>(this IReadOnlyList<object?> arguments, int index) where T : class =>
        arguments[index] as T;
}


/// <summary>One type a script may name, as the reference renders it.</summary>
/// <param name="Name">What a script writes.</param>
/// <param name="Shared">True for a type with no instances, whose members are reached through its name.</param>
/// <param name="Members">What it offers, in the order it was declared.</param>
public sealed record ScriptTypeInfo(string Name, bool Shared, IReadOnlyList<ScriptMemberInfo> Members);

/// <summary>One parameter of a member: what it is called, and what it takes.</summary>
/// <param name="Name">What it is called. Reaches an author, so it is words rather than a position.</param>
/// <param name="Type">What it takes.</param>
public sealed record ScriptParameter(string Name, ScriptType Type)
{
    /// <summary>As it is written in a signature.</summary>
    public override string ToString() => $"{Type} {Name}";
}

/// <summary>One member of a registered type.</summary>
/// <param name="Name">What a script writes.</param>
/// <param name="Yields">What it hands back, or nothing.</param>
/// <param name="Takes">What it takes, in order.</param>
/// <param name="IsValue">True for one read without parentheses.</param>
/// <param name="Note">What it does, for the reference. Empty where nobody has said yet.</param>
public sealed record ScriptMemberInfo(
    string Name, ScriptType Yields, IReadOnlyList<ScriptParameter> Takes, bool IsValue, string Note)
{
    /// <summary>How a script writes this member: a value has no parentheses, a call names its own.</summary>
    public string Signature =>
        IsValue ? Name : $"{Name}({string.Join(", ", Takes)})";
}
