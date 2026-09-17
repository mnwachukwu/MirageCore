using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Named values carried by a record or an entity that the engine stores and never reads.
///
/// <para>One bag serves every per-record and per-entity game value there is: whatever a game hangs on
/// a character, an item instance, an NPC template, an NPC instance, or a map. A hook naming this type
/// in one subsystem means the same thing as a hook naming it in another.</para>
///
/// <para><b>Core does not know what any key means.</b> That is the whole constraint. It stores them,
/// persists them, compares them, and — for keys a game has declared in an
/// <see cref="AttributeSchema"/> — ships them. It never branches on one.</para>
///
/// <para><b>Keys are ordinal and case-sensitive.</b> <c>"hp"</c> and <c>"Hp"</c> are two entries. A
/// case-insensitive bag reads as friendlier and is worse: the key is also a wire token and a JSON
/// property name, and two spellings that agree in C# and disagree in a hand-edited world file is
/// exactly the class of bug that has no error message.</para>
///
/// <para><b>It compares by value.</b> Two bags with the same entries are equal regardless of insertion
/// order, because the editor decides whether a record is dirty by comparing what it holds against what
/// it last saved, and a bag that compared by reference would mark every loaded record unsaved.</para>
/// </summary>
[JsonConverter(typeof(AttributeBagConverter))]
public sealed class AttributeBag : IEquatable<AttributeBag>
{
    private readonly Dictionary<string, AttributeValue> _values;

    public AttributeBag() => _values = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

    public AttributeBag(IEnumerable<KeyValuePair<string, AttributeValue>> values)
        => _values = new Dictionary<string, AttributeValue>(values, StringComparer.Ordinal);

    /// <summary>How many entries. Zero is the ordinary state — most records carry no game data at
    /// all.</summary>
    public int Count => _values.Count;

    /// <summary>True when nothing has been set. Worth asking before writing one out: an empty bag is
    /// noise in an authored file.</summary>
    [JsonIgnore] public bool IsEmpty => _values.Count == 0;

    public IReadOnlyCollection<string> Keys => _values.Keys;

    /// <summary>The entries, in no guaranteed order. Order is deliberately not part of this type's
    /// contract — see the equality note on the class.</summary>
    public IEnumerable<KeyValuePair<string, AttributeValue>> Entries => _values;

    /// <summary>Reads a key, or the zero Integer when it is absent.
    ///
    /// <para>Absent and zero are deliberately the same answer. A game asking for a key it never set is
    /// asking about a concept this record does not have, and every caller would otherwise write the
    /// same null check. Use <see cref="Has"/> where the difference matters.</para></summary>
    public AttributeValue this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : default;
        set => _values[key] = value;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public bool TryGet(string key, out AttributeValue value) => _values.TryGetValue(key, out value);

    /// <summary>Sets a key and returns the bag, so a handful of them read as one statement.</summary>
    public AttributeBag Set(string key, AttributeValue value)
    {
        _values[key] = value;
        return this;
    }

    public bool Remove(string key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    /// <summary>An independent copy. Values are immutable, so copying the dictionary is the whole
    /// job — but the dictionary itself is not shared, which a caller about to edit one
    /// needs.</summary>
    public AttributeBag Clone() => new(_values);

    // ── Equality ──────────────────────────────────────────────────────────────

    public bool Equals(AttributeBag? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_values.Count != other._values.Count) return false;

        foreach (var (key, value) in _values)
        {
            if (!other._values.TryGetValue(key, out var theirs) || !value.Equals(theirs)) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as AttributeBag);

    /// <summary>Order-independent, to match <see cref="Equals(AttributeBag)"/>: the per-entry hashes are
    /// XORed rather than combined in sequence, so two bags built in different orders land in the same
    /// bucket.</summary>
    public override int GetHashCode()
    {
        int hash = _values.Count;
        foreach (var (key, value) in _values) hash ^= HashCode.Combine(key, value);
        return hash;
    }

    public static bool operator ==(AttributeBag? left, AttributeBag? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(AttributeBag? left, AttributeBag? right) => !(left == right);

    public override string ToString()
        => IsEmpty ? "{}" : "{ " + string.Join(", ", _values.Select(e => $"{e.Key}: {e.Value}")) + " }";

    [ExcludeFromCodeCoverage] // reached only by a serializer that has already decided the shape is an object
    internal Dictionary<string, AttributeValue> Raw => _values;
}
