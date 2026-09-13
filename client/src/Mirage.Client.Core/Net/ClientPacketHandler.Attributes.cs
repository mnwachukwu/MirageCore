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
