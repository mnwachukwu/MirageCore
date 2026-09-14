using Mirage.Shared.Extensibility;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The game's own fields on the open map.
///
/// <para>The map screen is not one of the list editors, so the wiring every other record screen
/// inherits is written out here instead: which record the form is pointed at, whether its edits count
/// as the map being dirty, and saving it alongside the map's own save.</para>
///
/// <para>Everything it does is the same as elsewhere — the map's own packet carries only what the
/// engine acts on, and a game's fields travel on the generic record packet beside it.</para>
/// </summary>
public sealed partial class MapEditorViewModel
{
    private GameFieldsViewModel? _gameFields;

    /// <summary>The game's own fields on the open map. Empty of rows for a world whose game adds
    /// nothing to maps, which leaves the section off the screen.</summary>
    public GameFieldsViewModel GameFields => _gameFields ??= BuildGameFields();

    private GameFieldsViewModel BuildGameFields()
    {
        var fields = new GameFieldsViewModel(_data, _conn, CoreRecordFamilies.Maps);
        fields.Changed += NotifyMapDirtyState;
        return fields;
    }

    /// <summary>Point the form at the map that has just been selected.</summary>
    private void TrackGameFields(MapRowViewModel? row)
    {
        GameFields.Show(row?.Index ?? 0);
        NotifyMapDirtyState();
    }

    /// <summary>Write the open map's game fields, when they are the ones on screen and they changed.
    /// Called after the map's own save, so one press stores both halves of the record.</summary>
    private Task SaveGameFieldsForAsync(MapRowViewModel vm) =>
        GameFields is { IsDirty: true } fields && fields.Num == vm.Index
            ? fields.SaveAsync() : Task.CompletedTask;
}
