using Mirage.Shared.Extensibility;

namespace Mirage.Editor.Services;

/// <summary>
/// What record families the world being edited actually has.
///
/// <para><b>The editor is told, not compiled with the answer.</b> Its own build knows Core's families,
/// but the server it connects to may have loaded a game that declares more — and a family this list
/// does not name is one nobody can author. The server sends its schema on the login response, and this
/// is where that lands.</para>
///
/// <para><b>Offline it falls back to Core's.</b> A world folder on disk carries no schema, so editing
/// one without a server means editing the families the engine ships with. That is the honest answer
/// rather than an empty rail, and it matches what a world folder written by a stock server holds.</para>
///
/// <para>Static because the editor's other world-scoped facts are — <c>AppSettings.Current</c>,
/// <c>EditorStrings</c>, <c>ServerBookStore.Book</c> — and because one editor edits one world at a
/// time. <see cref="Changed"/> fires on every replacement, so the rail can follow.</para>
/// </summary>
public static class WorldFamilies
{
    private static RecordSchema _schema = CoreRegistry.CoreOnly.Schema;

    /// <summary>Raised after <see cref="Adopt"/> or <see cref="Reset"/> changes the list.</summary>
    public static event Action? Changed;

    /// <summary>The whole schema, including the choice sets a family's fields draw from.</summary>
    public static RecordSchema Schema => _schema;

    /// <summary>Every family this world has, in the order the editor should list them.</summary>
    public static IReadOnlyList<RecordFamily> All => _schema.Families;

    /// <summary>The family with this id, or null for one this world does not have.</summary>
    public static RecordFamily? Find(string id) => _schema.Family(id);

    /// <summary>True when this family is one the editor has a purpose-built screen for. Everything else
    /// is a family a module declared, which the editor can list but not yet author.</summary>
    public static bool HasCompiledEditor(string id) => CoreRecordFamilies.Find(id) is not null;

    /// <summary>Takes the schema a server reported. A null or empty one leaves Core's in place, because
    /// an empty rail is the worse guess: a server that said nothing has told the editor nothing, not that
    /// its world has no families.</summary>
    public static void Adopt(RecordSchema? schema)
    {
        if (schema is null || schema.Families.Count == 0) return;
        _schema = schema;
        Changed?.Invoke();
    }

    /// <summary>Back to the families this build ships with. Called on disconnect: the next thing edited
    /// may be a folder on disk, which carries no schema of its own.</summary>
    public static void Reset()
    {
        if (ReferenceEquals(_schema, CoreRegistry.CoreOnly.Schema)) return;
        _schema = CoreRegistry.CoreOnly.Schema;
        Changed?.Invoke();
    }
}
