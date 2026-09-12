using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>Receiving a map's stains.</summary>
public sealed partial class ClientPacketHandler
{
    /// <summary>Replaces one map's whole stain list.
    ///
    /// <para>A replace rather than a merge, because that is what the server sent: stains merge as they
    /// overlap, so one that stopped existing has no removal to send and simply is not in the new list.</para></summary>
    private void HandleDecalUpdate(DecalUpdatePacket p)
    {
        var decals = _state.DecalsForMap(p.MapNum);
        decals.Clear();

        foreach (var e in p.Decals)
        {
            float amount = DecalUpdatePacket.Dequantize(e.Amount);
            float peak = DecalUpdatePacket.Dequantize(e.Peak);
            decals.Add(new State.ClientState.Decal
            {
                X = e.X,
                Y = e.Y,
                Size = e.Size,
                Amount = amount,
                // Freshness is what the stain has left of its last deposit, so a fresh one opens at full
                // opacity and an old one arrives already faded rather than snapping dark on arrival.
                Freshness = peak > 0f ? Math.Clamp(amount / peak, 0f, 1f) : 0f,
                Layer = e.Layer,
            });
        }
    }
}
