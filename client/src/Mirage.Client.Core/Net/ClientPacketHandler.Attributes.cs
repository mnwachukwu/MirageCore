using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>
/// Receiving the loaded game's attribute keys, and the values that arrive against them.
///
/// <para><b>Nothing here filters.</b> The server sends only what this client may see, so a value that
/// arrives is a value to store. The client has no idea what any of these keys mean either — it holds
/// them so a game's own client code, or a display projection, can read them.</para>
/// </summary>
public sealed partial class ClientPacketHandler
{
    /// <summary>The numbering, which must arrive before any sync can be read. A sync naming an ordinal
    /// this does not know is dropped rather than guessed at.</summary>
    private void HandleAttributeSchema(AttributeSchemaPacket p)
    {
        _state.Attributes = new AttributeSchema(
            [.. p.Keys.Select(k => new AttributeDeclaration(k.Key, k.Ordinal, k.Visibility, Persist: false, k.LabelKey))]);
    }

    /// <summary>Which keys are drawn over a head. Arrives with the numbering rather than being inferred
    /// from it: an attribute is not a bar, and most of them are not.</summary>
    private void HandleOverheadBars(OverheadBarsPacket p)
    {
        _state.OverheadBars = new OverheadBarSet(
            [.. p.Bars.Select(b => new OverheadBar { ValueKey = b.ValueKey, MaxKey = b.MaxKey, Rgb = b.Rgb })]);
    }

    /// <summary>What each surface shows. Arrives with the numbering for the same reason the bars do:
    /// a field naming a key this client has no declaration for could never fill.</summary>
    private void HandleDisplayFields(DisplayFieldsPacket p)
    {
        _state.DisplayFields = new DisplayFieldSet(
            [.. p.Fields.Select(f => new DisplayField
            {
                Surface = f.Surface,
                ValueKey = f.ValueKey,
                MaxKey = f.MaxKey,
                LabelKey = f.LabelKey,
                Rgb = f.Rgb,
                Style = f.Style,
            })]);
    }

    /// <summary>What this game lets the player do. A caption and an id: the menus offer the first and
    /// send back the second, and what the verb DOES never reaches here.</summary>
    private void HandleGameActions(GameActionsPacket p)
    {
        _state.Actions = new GameActions(
            [.. p.Actions.Select(a => new GameAction
            {
                Id = a.Id,
                LabelKey = a.LabelKey,
                Surface = a.Surface,
                GroupKey = a.GroupKey,
                OpensPanel = a.OpensPanel,
                Key = a.Key,
            })]);
    }

    /// <summary>The screens this game paints. Names and numbers all the way down, which is why the
    /// declaration travels whole rather than being projected into a wire shape of its own.</summary>
    private void HandleGamePanels(GamePanelsPacket p) => _state.Panels = new GamePanels([.. p.Panels]);

    private void HandleAttributeSync(AttributeSyncPacket p)
    {
        var bag = _state.BagFor(p.Who);
        if (bag is null) return;   // a body this client is not tracking

        foreach (var entry in p.Set)
        {
            // An ordinal with no declaration means the server's schema moved under us, which cannot
            // happen mid-session — so this is a stray line, and dropping the entry is better than
            // inventing a key name for it.
            if (!_state.Attributes.TryGet(entry.Ordinal, out var declaration)) continue;
            bag[declaration.Key] = entry.Value;
        }

        _state.AttributeVersion++;
    }
}
