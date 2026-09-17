using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Persistence;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.Net;

/// <summary>
/// Authoring the families a module declared — the one handler that serves all of them.
///
/// <para><b>Core's own families each have a typed handler; these cannot.</b> The server holds a module's
/// records as bags, so the request/save pair takes a family id and a slot number and moves keys it does
/// not read. Adding a family to a game adds nothing here.</para>
///
/// <para><b>A Core family's OWN properties are refused on this path.</b> Its typed packets normalize what
/// they are sent — zeroing the fields a record's type does not use, capping authored lists — and a bag
/// written straight into the array would skip all of it.</para>
///
/// <para>🔴 <b>Its extension bag is not, and the whole distinction turns on that.</b> When a module adds fields
/// to <c>Items</c> or <c>NPCs</c>, those land in the record's attribute bag — which nothing normalizes,
/// because Core has never heard of a single key in it. So this path serves them exactly as it serves a
/// module's own family, and the typed path goes on owning the properties Core acts on. Two halves of one
/// record, each authored by the path that knows how.</para>
/// </summary>
public sealed partial class EditorPacketHandler
{
    private void HandleEditorRequestRecord(int editorIndex, EditorRequestRecordPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Mapper)) return;
        if (ModuleFamily(p.Family) is null) return;
        if (BagAt(p.Family, p.Num) is null) return;

        _dispatcher.SendToEditor(editorIndex, BuildUpdateRecord(p.Family, p.Num));
    }

    private void HandleEditorRequestAllRecords(int editorIndex, EditorRequestAllRecordsPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Mapper)) return;
        if (ModuleFamily(p.Family) is null) return;

        int limit = LimitOf(p.Family);
        _dispatcher.SendToEditor(editorIndex, new EditorAllRecordsPacket
        {
            Family = p.Family,
            Records = [.. Enumerable.Range(1, limit).Select(num =>
                new EditorAllRecordsPacket.Entry(num, RecordAt(p.Family, num)))],
        });
    }

    private void HandleEditorSaveRecord(int editorIndex, EditorSaveRecordPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Developer)) return;
        if (ModuleFamily(p.Family) is not { } family) return;
        if (BagAt(p.Family, p.Num) is null) return;
        if (LockedByAnother(editorIndex, p.Family, p.Num)) return;

        // The author's own copy, so a later edit in the editor cannot reach into what the world now holds.
        StoreRecord(family, p.Num, p.Fields.Clone());

        // Every editor sees the save, the same as a typed one. Game clients are not told: what a record of
        // a game's family means to a player is the module's to push, through its own packets.
        _dispatcher.SendToAllEditors(BuildUpdateRecord(p.Family, p.Num));
        _logger.LogInformation("Editor saved {Family} #{Num}.", p.Family, p.Num);
    }

    /// <summary>The family by that id when this path may author it — one a module declared, or one Core
    /// owns that a module added fields to. Null for anything else.
    ///
    /// <para>An id this server cannot author generically is refused, and which reason applies is the
    /// editor's business rather than the wire's.</para></summary>
    private RecordFamily? ModuleFamily(string? familyId)
    {
        if (familyId is null) return null;

        if (CoreRecordFamilies.Find(familyId) is not null)
        {
            // Only once a module has actually put something there. A Core family nobody extended has an
            // empty bag on every record, and an editor asking for one would get a form with no rows.
            var extended = _registry.Schema.Family(familyId);
            return extended is { Fields.Count: > 0 } ? extended : null;
        }

        return _world.ModuleRecords.Has(familyId) ? _registry.Schema.Family(familyId) : null;
    }

    /// <summary>The bag a slot's fields live in, or null for a slot that is not there.
    ///
    /// <para>For one of Core's families that is the record's EXTENSION bag — the keys a game added, and
    /// never the properties Core acts on, which have a typed packet of their own.</para></summary>
    private AttributeBag? BagAt(string familyId, int num) => familyId switch
    {
        CoreRecordFamilies.Items =>
            SlotValidation.IsValidItemNum(num, _world.Limits.Items) ? _world.Items[num].Attributes : null,
        CoreRecordFamilies.Npcs =>
            SlotValidation.IsValidNpcNum(num, _world.Limits.Npcs) ? _world.Npcs[num].Attributes : null,
        CoreRecordFamilies.Maps =>
            num >= 1 && num <= _world.Limits.Maps && num < _world.Maps.Length ? _world.Maps[num].Attributes : null,
        CoreRecordFamilies.MapGroups =>
            num >= 1 && _world.MapGroups.TryGetValue(num, out var group) ? group.Attributes : null,
        _ => _world.ModuleRecords.Get(familyId, num),
    };

    /// <summary>How many slots this family holds.</summary>
    private int LimitOf(string familyId) => familyId switch
    {
        CoreRecordFamilies.Items => _world.Limits.Items,
        CoreRecordFamilies.Npcs => _world.Limits.Npcs,
        CoreRecordFamilies.Maps => _world.Limits.Maps,
        CoreRecordFamilies.MapGroups => _world.MapGroups.Count,
        _ => _world.ModuleRecords.Limit(familyId),
    };

    /// <summary>Write a slot's fields and get them onto disk, through whichever save owns that record.
    ///
    /// <para>⚠ A Core record is saved WHOLE, because its extension bag is one property of a file that
    /// also holds everything Core acts on. Writing only the bag would mean a second file per item, which
    /// is a world that can be half-copied.</para></summary>
    private void StoreRecord(RecordFamily family, int num, AttributeBag fields)
    {
        switch (family.Id)
        {
            case CoreRecordFamilies.Items:
                _world.Items[num].Attributes = fields;
                _bg.Run(_persistence.SaveItemAsync(num, _world.Items[num]),
                        nameof(IPersistenceService.SaveItemAsync));
                break;

            case CoreRecordFamilies.Npcs:
                _world.Npcs[num].Attributes = fields;
                _bg.Run(_persistence.SaveNpcAsync(num, _world.Npcs[num]),
                        nameof(IPersistenceService.SaveNpcAsync));
                break;

            case CoreRecordFamilies.Maps:
                _world.Maps[num].Attributes = fields;
                _bg.Run(_persistence.SaveMapAsync(num, _world.Maps[num]),
                        nameof(IPersistenceService.SaveMapAsync));
                break;

            case CoreRecordFamilies.MapGroups when _world.MapGroups.TryGetValue(num, out var group):
                group.Attributes = fields;
                _bg.Run(_persistence.SaveMapGroupAsync(num, group),
                        nameof(IPersistenceService.SaveMapGroupAsync));
                break;

            default:
                _world.ModuleRecords.Set(family.Id, num, fields);
                _bg.Run(_persistence.SaveModuleRecordAsync(family, num, fields),
                        nameof(IPersistenceService.SaveModuleRecordAsync));
                break;
        }
    }

    private UpdateRecordPacket BuildUpdateRecord(string familyId, int num) =>
        new() { Family = familyId, Num = num, Fields = RecordAt(familyId, num) };

    /// <summary>A copy of what the slot holds. Cloned because the game thread may re-author the record
    /// while this one is being serialized.</summary>
    private AttributeBag RecordAt(string familyId, int num) =>
        BagAt(familyId, num)?.Clone() ?? new AttributeBag();
}
