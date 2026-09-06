namespace Mirage.Shared.Records;

/// <summary>Authorable light attributes, shared by NPC emitters (<see cref="NpcRecord.Light"/>) and
/// map-placed light sources (<see cref="PlacedLight.Light"/>). <see cref="Rgb"/> is the packed 0xRRGGBB core
/// color; <see cref="Radius"/> is the outer reach in TILES (converted to pixels at render); <see cref="Flicker"/>
/// picks the core animation; <see cref="Intensity"/> (0..1) scales halo brightness. <see cref="Torch"/> is the
/// classic hard-coded torch, used as the default for new lights and newly-lit NPCs.</summary>
public readonly record struct LightSpec(uint Rgb, float Radius, FlickerStyle Flicker, float Intensity)
{
    /// <summary>The classic warm player/NPC torch: 0xB49669, 3-tile reach, flame flicker, full intensity.</summary>
    public static readonly LightSpec Torch = new(0xB49669u, 3f, FlickerStyle.Flame, 1f);
}
