using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Client-side prediction helpers on <see cref="PlayerRecord"/>. Kept as extension methods
/// (not instance methods on the shared record) so the prediction concept stays out of the
/// server's view of the record.
/// </summary>
public static class PlayerRecordPredictExtensions
{
    /// <summary>Start the client-side animation for a confirmed-in-bounds step BEFORE the
    /// server's echo arrives: place the player on the destination tile, kick the
    /// interpolation offset back to the origin, and stamp facing + movement type. Same
    /// 6-field mutation for every step-prediction site, collapsed so callers don't drift.</summary>
    public static void PredictMove(this PlayerRecord me, Direction dir, int nx, int ny, MovementType movement, WorldLayer newLayer)
    {
        me.XOffset = -(nx - me.X) * Constants.PicX;
        me.YOffset = -(ny - me.Y) * Constants.PicY;
        me.X = nx;
        me.Y = ny;
        me.Dir = dir;
        me.Moving = movement;
        me.PrevLayer = me.Layer;   // remember the pre-step layer for the cross-layer slide-occlusion fix
        me.Layer = newLayer;   // two-layer world: commit the predicted layer (sticky / ramp-gated)
    }
}
