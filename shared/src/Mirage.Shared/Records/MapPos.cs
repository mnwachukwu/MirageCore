namespace Mirage.Shared.Records;

/// <summary>A map-LOCAL tile coordinate (0..MaxMapX, 0..MaxMapY). Not a world coordinate — see
/// <see cref="Mirage.Shared.WorldCoordHelper"/> for the seamless-world mapping.</summary>
public readonly record struct MapPos(int X, int Y);
