using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C: the loaded game asked for something to be shown, once.
///
/// <para>🔴 <b>One packet for the three effects a game may call</b>, because they are the same kind of
/// thing said three ways: something happened at a body, or between two bodies, and this says
/// what it looked like. Three commands would be three registrations, three events and three handlers for one
/// idea.</para>
///
/// <para><b>Addressed to BODIES rather than to tiles.</b> A tile is where somebody was when the packet
/// was built; the client centers on a footprint, follows a target that is still moving, and holds any
/// number owed to that target until a thrown thing lands. None of that is possible from coordinates.</para>
///
/// <para>⚠ <b>Two fields are read only for one effect each.</b> <see cref="Style"/> says what a
/// <see cref="GameEffect.Throw"/> looks like on its way, and <see cref="To"/> is where it is going;
/// neither means anything for the other two. <see cref="Intensity"/> is how big a
/// <see cref="GameEffect.Burst"/> is, and for a <see cref="GameEffect.Sweep"/> it is whether the sweep
/// connected — above zero flings sparks, so it reads as having hit something rather than passing
/// through air.</para>
/// </summary>
public sealed record GameEffectPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GameEffect;

    [JsonPropertyName("fx")] public GameEffect Effect { get; init; }

    /// <summary>Where it happens, or what it comes from.</summary>
    [JsonPropertyName("from")] public Body From { get; init; }

    /// <summary>What a <see cref="GameEffect.Throw"/> is aimed at. Unset otherwise.</summary>
    [JsonPropertyName("to")] public Body To { get; init; }

    /// <summary>What a <see cref="GameEffect.Throw"/> looks like on its way.</summary>
    [JsonPropertyName("style")] public ProjectileStyle Style { get; init; }

    /// <summary>Packed 0xRRGGBB.</summary>
    [JsonPropertyName("rgb")] public uint Rgb { get; init; }

    /// <summary>How big a burst is, 0 to 1 — and for a sweep, whether it connected.</summary>
    [JsonPropertyName("power")] public float Intensity { get; init; }

    /// <summary>
    /// One body, as the client's own roster knows it.
    ///
    /// <para>An NPC travels as the slot and map it is standing on RIGHT NOW rather than as the spawn
    /// identity a handle carries: the client has never seen a spawn identity and keys its roster by
    /// where things are.</para>
    /// </summary>
    public readonly record struct Body(
        [property: JsonPropertyName("npc")] bool IsNpc,
        [property: JsonPropertyName("idx")] int Index,
        [property: JsonPropertyName("nmap")] int NpcMap,
        [property: JsonPropertyName("map")] int MapNum,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("dir")] Direction Facing = Direction.Down)
    {
        /// <summary>Nobody, which a sweep and a burst carry for <c>To</c>.</summary>
        public static Body None => default;

        /// <summary>Whether this names anybody at all.</summary>
        [JsonIgnore] public bool IsSet => MapNum > 0;
    }
}
