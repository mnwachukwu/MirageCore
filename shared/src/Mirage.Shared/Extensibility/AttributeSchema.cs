using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>Who is allowed to see an attribute as it changes.</summary>
public enum AttributeVisibility : byte
{
    /// <summary>Nobody. The value lives on the server, is persisted, and never reaches a client.
    ///
    /// <para>The default, and the fail-closed one: a key a game forgot to think about does not leak.
    /// A seed, a cooldown ledger, or a hidden disposition is this.</para></summary>
    None = 0,

    /// <summary>The entity's own player, and nobody else. A currency balance, a quest flag.</summary>
    Owner = 1,

    /// <summary>Everyone who can see the entity. A value drawn above a head or on a bar an onlooker
    /// reads.</summary>
    Viewport = 2,
}

/// <summary>
/// One declared attribute key: what it is called, who sees it, whether it is written to disk, and the
/// small number that stands for it on the wire.
/// </summary>
/// <param name="Key">The name the game uses in code and in authored files.</param>
/// <param name="Ordinal">What the key is called on the wire. Assigned by the
/// <see cref="AttributeSchema"/> that holds this declaration, and meaningful only against that schema
/// — a client learns the numbering from the server rather than agreeing on it in advance.</param>
/// <param name="Visibility">Who is told when it changes.</param>
/// <param name="Persist">Whether it survives a restart.</param>
/// <param name="LabelKey">Localization key for a human-facing label, or null for a key that is never
/// displayed.</param>
public readonly record struct AttributeDeclaration(
    string Key,
    int Ordinal,
    AttributeVisibility Visibility,
    bool Persist,
    string? LabelKey);

/// <summary>
/// Every attribute key a game has declared, and the wire numbering for them.
///
/// <para><b>A key that is not declared here still works.</b> It is stored, compared, and persisted like
/// any other; it simply never reaches a client. Declaring a key is how a game asks for it to be
/// synced, which means a game with no live values pays nothing: there is no dirty set to scan and no
/// packet to build.</para>
///
/// <para><b>The ordinal is assigned here, not agreed in advance.</b> Keys are strings in code and in
/// authored files, where a name is worth its bytes, and small numbers on the wire, where a name is
/// repeated once per entity per tick. Nothing outside this type needs to know the number: the schema
/// travels from server to client and to the editor, so both ends learn the numbering from whoever
/// owns the world rather than from a shared constant that could drift.</para>
///
/// <para>Built once at startup and not changed after, so every reader can hold one without
/// locking.</para>
/// </summary>
public sealed class AttributeSchema
{
    /// <summary>The schema of a game that declared nothing — every key server-only. What Core runs on
    /// when no game layer is loaded.</summary>
    public static readonly AttributeSchema Empty = new(Array.Empty<AttributeDeclaration>());

    private readonly Dictionary<string, AttributeDeclaration> _byKey;
    private readonly Dictionary<int, AttributeDeclaration> _byOrdinal;

    [JsonConstructor]
    public AttributeSchema(IReadOnlyList<AttributeDeclaration> declarations)
    {
        Declarations = declarations;
        _byKey = declarations.ToDictionary(d => d.Key, StringComparer.Ordinal);
        _byOrdinal = declarations.ToDictionary(d => d.Ordinal);
    }

    /// <summary>Every declaration, in ordinal order. This is the form that travels on the wire.</summary>
    public IReadOnlyList<AttributeDeclaration> Declarations { get; }

    public bool TryGet(string key, out AttributeDeclaration declaration)
        => _byKey.TryGetValue(key, out declaration);

    public bool TryGet(int ordinal, out AttributeDeclaration declaration)
        => _byOrdinal.TryGetValue(ordinal, out declaration);

    /// <summary>Whether a key is shown to <paramref name="viewer"/>. An undeclared key is visible to
    /// nobody, which is the same answer as a key declared <see cref="AttributeVisibility.None"/>.</summary>
    /// <param name="key">The attribute being asked about.</param>
    /// <param name="asker">How close the person asking stands to the entity:
    /// <see cref="AttributeVisibility.Owner"/> when the entity is theirs,
    /// <see cref="AttributeVisibility.Viewport"/> when they can merely see it. A closer asker sees
    /// everything a more distant one does, so an owner reads both their own values and the public
    /// ones, while an onlooker reads only the public ones.</param>
    public bool IsVisibleTo(string key, AttributeVisibility asker)
        => asker != AttributeVisibility.None
           && _byKey.TryGetValue(key, out var d)
           && d.Visibility != AttributeVisibility.None
           && d.Visibility >= asker;

    /// <summary>Whether a key is written to disk. An undeclared key IS persisted — the bag is the
    /// record, and dropping values a game never declared would lose authored content.</summary>
    public bool IsPersisted(string key)
        => !_byKey.TryGetValue(key, out var d) || d.Persist;

    /// <summary>Collects declarations and hands out ordinals in the order they arrive.</summary>
    public sealed class Builder
    {
        private readonly List<AttributeDeclaration> _declarations = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        /// <summary>Declares a key.</summary>
        /// <exception cref="ArgumentException">The key is blank, or was already declared. A second
        /// declaration is a mistake worth stopping for: whichever visibility loses would be
        /// unobservable, and the two call sites disagreeing about a key is the bug.</exception>
        public Builder Declare(string key, AttributeVisibility visibility = AttributeVisibility.None,
                               bool persist = true, string? labelKey = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("An attribute key cannot be blank.", nameof(key));
            if (!_seen.Add(key)) throw new ArgumentException($"Attribute key '{key}' is declared twice.", nameof(key));

            _declarations.Add(new AttributeDeclaration(key, _declarations.Count, visibility, persist, labelKey));
            return this;
        }

        public AttributeSchema Build() => new(_declarations.ToArray());
    }
}
