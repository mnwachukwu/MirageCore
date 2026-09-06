namespace Mirage.Editor.ViewModels;

/// <summary>Shared rules for duplicating a record, so the map editor and the record-list editors mark a
/// copy the same way.</summary>
public static class RecordCopy
{
    /// <summary>The marker appended to a copied record's name.
    /// <para>Deliberately NOT localized. It lands in authored content that ships, so switching the
    /// editor's language must not change what gets written into the world — every other string in this
    /// app is chrome, and this one is data.</para></summary>
    public const string Suffix = " (Copy)";
}
