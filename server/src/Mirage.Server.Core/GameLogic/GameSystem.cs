using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Net;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Base for a game system that talks to players: it owns the packet dispatcher and the
/// localized-chat vocabulary every system needs.
///
/// <para><b>The default channel is per system.</b> Most speak on <see cref="ChatChannel.System"/>;
/// combat-facing ones default to <see cref="ChatChannel.Combat"/> so their damage and heal lines land
/// in the combat tab. It is a constructor argument rather than an override, so the choice is visible at
/// the point of construction.</para>
///
/// <para>Guild-scoped broadcasts stay on the guild systems that own them: a different audience, not
/// this per-player vocabulary.</para>
/// </summary>
public abstract class GameSystem
{
    protected readonly IPacketDispatcher _dispatcher;

    // The channel SendMsg/ViewportMsg use when a call site does not name one.
    private readonly ChatChannel _defaultChannel;

    /// <summary>Wall-clock time. Injected so time-dependent rules — PK expiry, grace windows, mail
    /// maturity, listing lifetime, tax due dates — can be asserted rather than sampled. Defaults to
    /// the machine clock, so a system constructed without one behaves exactly as before the seam
    /// existed. NOT the tick clock: see <see cref="IClock"/>.</summary>
    protected readonly IClock Clock;

    /// <summary>The source of chance. Injected so rolled outcomes — block/dodge, crits, loot,
    /// stat drain, NPC cast/wander/kite choices, spawn placement, mail transit — can be pinned in a
    /// test. Defaults to <c>Random.Shared</c>, matching the direct calls it replaced.</summary>
    protected readonly IRandomSource Rng;

    /// <summary>The operator's server-only rules. Injected on the same terms as <see cref="Clock"/> and
    /// <see cref="Rng"/> — optional, defaulting to the stock rules, so a system constructed without one
    /// behaves exactly as it did before the seam existed, and a test can pin a switch the same way it
    /// pins a roll. Immutable, so passing the one instance to every system is safe.</summary>
    protected readonly ServerConfig Config;

    protected GameSystem(IPacketDispatcher dispatcher, ChatChannel defaultChannel = ChatChannel.System,
                         IClock? clock = null, IRandomSource? rng = null, ServerConfig? config = null)
    {
        _dispatcher = dispatcher;
        _defaultChannel = defaultChannel;
        Clock = clock ?? SystemClock.Instance;
        Rng = rng ?? SharedRandom.Instance;
        Config = config ?? ServerConfig.Default;
    }

    /// <summary>Now as a Unix second — the shorthand the deadline arithmetic throughout the systems
    /// uses. Reads the injected <see cref="Clock"/>, so pinning the clock pins every deadline.</summary>
    protected long NowUtc => Clock.UtcNowUnix;

    // ── Per-player localized chat ─────────────────────────────────────────────

    /// <summary>Localized line to one player on this system's default channel.</summary>
    protected void SendMsg(int index, string key, int color,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, _defaultChannel), args);

    /// <summary>Localized line to one player on an explicitly named channel.</summary>
    protected void SendMsg(int index, string key, int color, ChatChannel channel,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, channel), args);

    /// <summary>A refusal or warning to one player — red, on the Notice channel.</summary>
    protected void Notify(int index, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatTo(index, key,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice), args);

    /// <summary>A positive confirmation to one player — green, on the Notice channel.</summary>
    protected void NotifyOk(int index, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatTo(index, key,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice), args);

    // ── Viewport (earshot) localized chat ─────────────────────────────────────

    /// <summary>Localized line to everyone in the speaker's viewport (earshot), on this system's
    /// default channel. Narrower than the observer set — see <see cref="IPacketDispatcher"/>.</summary>
    protected void ViewportMsg(int speakerIndex, string key, int color,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToViewport(speakerIndex, key, new ChatMetadata(color, _defaultChannel), args);

    /// <summary>Localized line to the speaker's viewport on an explicitly named channel.</summary>
    protected void ViewportMsg(int speakerIndex, string key, int color, ChatChannel channel,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToViewport(speakerIndex, key, new ChatMetadata(color, channel), args);

    // ── Map-observer sends ────────────────────────────────────────────────────

    /// <summary>Sends a packet to everyone observing <paramref name="mapNum"/>.
    /// <para>Replaces the <c>_dispatcher.SendToObservers(_world.MapObservers[mapNum], packet)</c>
    /// idiom that appeared 151 times across sixteen files, each site reaching through
    /// <see cref="World.GameWorld"/> into a raw <c>HashSet<int>[]</c> just to name an
    /// audience.</para></summary>
    protected void SendToMap(World.GameWorld world, int mapNum, IPacket packet) =>
        _dispatcher.SendToObservers(world.MapObservers[mapNum], packet);

    /// <summary>Map-observer send that skips one player — typically the actor who caused the event
    /// and has already been told about it locally.</summary>
    protected void SendToMapBut(World.GameWorld world, int mapNum, int exclude, IPacket packet) =>
        _dispatcher.SendToObserversBut(world.MapObservers[mapNum], exclude, packet);

    /// <summary>Per-recipient localized chat to everyone observing <paramref name="mapNum"/> — the
    /// chat counterpart of <see cref="SendToMap"/>. A yell, or any notice whose audience is the whole
    /// observable region rather than earshot (for earshot, use <c>ViewportMsg</c>).</summary>
    protected void ChatToMap(World.GameWorld world, int mapNum, string key, int color, ChatChannel channel,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToObservers(world.MapObservers[mapNum], key,
            new ChatMetadata(color, channel), args);

    /// <summary>Localized chat to a map's observers, skipping one player — used when the excluded
    /// player gets a different wording of the same event (a death notice reads differently to the
    /// victim than to onlookers).</summary>
    protected void ChatToMapBut(World.GameWorld world, int mapNum, int exclude, string key, int color,
        ChatChannel channel, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToObserversBut(world.MapObservers[mapNum], exclude, key,
            new ChatMetadata(color, channel), args);

    /// <summary>Metadata overload — for SPEAKER-ATTRIBUTED chat (a yell), where the metadata carries
    /// the speaker triplet and login that the ignore-list filter keys on, and so cannot be rebuilt
    /// from a color and a channel. Also the right overload when one prebuilt metadata is reused
    /// across several sends of the same event.</summary>
    protected void ChatToMap(World.GameWorld world, int mapNum, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToObservers(world.MapObservers[mapNum], key, meta, args);

    /// <inheritdoc cref="ChatToMap(World.GameWorld, int, string, ChatMetadata, ValueTuple{string, object}[])"/>
    protected void ChatToMapBut(World.GameWorld world, int mapNum, int exclude, string key, ChatMetadata meta,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToObserversBut(world.MapObservers[mapNum], exclude, key, meta, args);
}
