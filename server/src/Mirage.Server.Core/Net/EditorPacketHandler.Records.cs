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
/// <para><b>A Core family is refused on this path.</b> Its typed packets normalize what they are sent —
/// zeroing the fields a record's type does not use, capping authored lists — and a bag written straight
/// into the array would skip all of it. The two paths are not interchangeable, and the refusal says so
/// rather than quietly writing a half-valid record.</para>
/// </summary>
public sealed partial class EditorPacketHandler
{
    private void HandleEditorRequestRecord(int editorIndex, EditorRequestRecordPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Mapper)) return;
        if (ModuleFamily(p.Family) is null) return;
        if (_world.ModuleRecords.Get(p.Family, p.Num) is null) return;

        _dispatcher.SendToEditor(editorIndex, BuildUpdateRecord(p.Family, p.Num));
    }

    private void HandleEditorRequestAllRecords(int editorIndex, EditorRequestAllRecordsPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Mapper)) return;
        if (ModuleFamily(p.Family) is null) return;

        int limit = _world.ModuleRecords.Limit(p.Family);
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
        if (_world.ModuleRecords.Get(p.Family, p.Num) is null) return;
        if (LockedByAnother(editorIndex, p.Family, p.Num)) return;

        // The author's own copy, so a later edit in the editor cannot reach into what the world now holds.
        var stored = p.Fields.Clone();
        _world.ModuleRecords.Set(p.Family, p.Num, stored);

        _bg.Run(_persistence.SaveModuleRecordAsync(family, p.Num, stored),
                nameof(IPersistenceService.SaveModuleRecordAsync));

        // Every editor sees the save, the same as a typed one. Game clients are not told: what a record of
        // a game's family means to a player is the module's to push, through its own packets.
        _dispatcher.SendToAllEditors(BuildUpdateRecord(p.Family, p.Num));
        _logger.LogInformation("Editor saved {Family} #{Num}.", p.Family, p.Num);
    }

    /// <summary>The family by that id when a module declared it, or null for one Core owns and one
    /// nobody declared. Both answer the same way on purpose: an id this server cannot author generically
    /// is refused, and which of the two reasons applies is the editor's business, not the wire's.</summary>
    private RecordFamily? ModuleFamily(string? familyId)
    {
        if (familyId is null || CoreRecordFamilies.Find(familyId) is not null) return null;
        return _world.ModuleRecords.Has(familyId) ? _registry.Schema.Family(familyId) : null;
    }

    private UpdateRecordPacket BuildUpdateRecord(string familyId, int num) =>
        new() { Family = familyId, Num = num, Fields = RecordAt(familyId, num) };

    /// <summary>A copy of what the slot holds. Cloned because the game thread may re-author the record
    /// while this one is being serialized.</summary>
    private AttributeBag RecordAt(string familyId, int num) =>
        _world.ModuleRecords.Get(familyId, num)?.Clone() ?? new AttributeBag();
}
