using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Clears expired PK flags. Polls rather than scheduling a per-player timer: the flag only has
/// to lapse promptly enough to be believable, and one sweep a minute over the roster is far cheaper than
/// a timer per flagged player.</summary>
public sealed class PkExpirySystem : GameSystem
{
    private readonly PlayerManager _pm;
    private readonly GameWorld _world;
    private long _lastCheckUtc;

    public PkExpirySystem(PlayerManager pm, IPacketDispatcher dispatcher, GameWorld world,
                          IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _pm = pm;
        _world = world;
    }

    /// <summary>Called every game tick, but does real work at most once a minute. Each newly-unflagged
    /// player is re-broadcast to their observers (so the name color clears) and announced server-wide.</summary>
    public void Tick()
    {
        // Rate gate: the sweep is a full roster scan, so it runs at most once a minute.
        long nowUtc = NowUtc;
        if (nowUtc - _lastCheckUtc < 60) return;
        _lastCheckUtc = nowUtc;

        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            var p = sp.Char;
            if (p.PkExpiryUtc == 0 || p.PkExpiryUtc > nowUtc) continue;

            p.PkExpiryUtc = 0;
            SendToMap(_world, p.Map, PacketBuilder.PlayerData(i, p, p.Map, sp.PkGraceUntilUtc, sp.AggressorUntilUtcNow));
            _dispatcher.SendLocalizedChatToAll(
                ServerStrings.PkExpirySystem_CrimesFaded,
                new ChatMetadata(GameColor.BrightGreen, ChatChannel.System),
                ("PlayerName", p.TrimmedName));
        }
    }
}
