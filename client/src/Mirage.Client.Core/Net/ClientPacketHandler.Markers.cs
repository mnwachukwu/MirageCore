using Mirage.Shared.Protocol.Packets;

namespace Mirage.Client.Core.Net;

/// <summary>Receiving what a game has marked on a map.</summary>
public sealed partial class ClientPacketHandler
{
    /// <summary>Replaces one map's whole mark list.
    ///
    /// <para>A replace rather than a merge, because that is what the server sent. A mark can move, change
    /// color, change its meter, or stop being this player's business, and saying what is true now covers
    /// every one of those without a removal wire.</para>
    ///
    /// <para>⚠ An empty list is a real message: it says this map holds nothing for this player. Ignoring
    /// one leaves a mark on the screen that the server has taken away.</para></summary>
    private void HandleMarkerUpdate(MarkerUpdatePacket p)
    {
        var marks = _state.MarkersForMap(p.MapNum);
        marks.Clear();

        foreach (var e in p.Markers)
        {
            marks.Add(new State.ClientState.Marker
            {
                X = e.X,
                Y = e.Y,
                Rgb = e.Rgb,
                Label = e.Label,
                Radius = e.Radius,
                Value = e.Value,
                Ceiling = e.Ceiling,
                Layer = e.Layer,
            });
        }
    }
}
