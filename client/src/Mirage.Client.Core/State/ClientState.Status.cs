using Mirage.Client.Core.Logic;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>The status readouts the loading screen and HUD chrome show: the alert that ended a connect
/// attempt, the place in a full server's queue, the online count, and the frame rate.</summary>
public sealed partial class ClientState
{
    public int GameFps { get; set; }
    public int PlayersOnline { get; set; }
    public string LoadingMessage { get; set; } = "";

    /// <summary>Whether the frame-rate readout sits on screen. Toggled by <c>/fps</c>, and not persisted:
    /// it is something you put up while looking at a problem, not a setting.</summary>
    public bool ShowFps { get; set; }

    /// <summary>How the last ten seconds of frames were distributed. The readout shows the median and the
    /// worst rather than an average, because an average is the one summary a stutter can hide in.</summary>
    public FrameMetrics Frames { get; } = new();

    /// <summary>Place in the line at a full server, or 0 when we are not waiting for one. Set from the
    /// server's push and read by the loading screen, which writes the sentence itself — the numbers cross
    /// the wire, the words do not, so a player waits in the language their menus are in.</summary>
    public int QueuePosition { get; set; }

    /// <summary>How many are waiting in total, so a position can be shown as "3rd of 40".</summary>
    public int QueueTotal { get; set; }

    /// <summary>
    /// Non-empty when the server sent an alert and immediately disconnected
    /// (e.g. bad password, name taken).  Cleared by LoadingScreen on enter/exit.
    /// </summary>
    public string Alert { get; set; } = "";
}
