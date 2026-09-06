using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>A light source placed on a map at tile (<see cref="X"/>, <see cref="Y"/>). <see cref="Id"/> is a
/// stable per-light identity — assigned once on placement (fresh on paste) — used as the editor's handle and,
/// hashed to an int, as the runtime flicker seed so a light's phase never jumps when the list reorders.
/// <see cref="Layer"/> is the logical plane (Ground / Fringe) the light lives on: it contributes to its own
/// layer's light map, so a torch under a bridge lights the ground while a lamp on the deck lights the fringe
/// surface (two-layer world). Default <see cref="WorldLayer.Ground"/> — omitted from JSON so ground-only maps
/// stay byte-identical.</summary>
public readonly record struct PlacedLight(
    Guid Id, int X, int Y, LightSpec Light,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground);
