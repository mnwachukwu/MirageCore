using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;
/// <summary>The ambient pushes that belong to no single entity: weather, time of day, the open/shut
/// state of map doors, and the online count.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    private void HandleWeather(WeatherPacket p) => _state.Weather = p.Weather;

    private void HandleTimeOfDay(TimeOfDayPacket p)
    {
        _state.TimePhase = p.Phase;
        _state.TimeProgress = p.Progress;
        _state.TimePhaseReceivedMs = Environment.TickCount64;
    }

    private void HandleMapKey(MapKeyPacket p)
    {
        if (p.X < 0 || p.Y < 0) return;
        var doors = _state.TempTilesForMap(p.MapNum);
        if (doors is null) return;
        doors.Set(p.X, p.Y, (int)p.Layer, p.Open);   // per-layer: a fringe-deck door is tracked apart from the ground one
    }

    private void HandlePlayersOnline(PlayersOnlinePacket p)
    {
        _state.PlayersOnline = p.Count;
        PlayersOnlineChanged?.Invoke(p.Count);
    }
}
