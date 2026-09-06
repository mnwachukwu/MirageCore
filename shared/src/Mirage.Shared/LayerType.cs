namespace Mirage.Shared;

/// <summary>
/// The three tile-art visual stacks. Each is a stack of numbered layers (1..Max{Ground,Fringe,Canopy}Layers)
/// selected separately, and each layer can carry the per-layer Anim flag. Ground draws below entities, Fringe
/// between the ground- and fringe-entity passes (the bridge surface), Canopy OVER everything (treetops /
/// roofs / foliage above both logical layers).
///
/// <para>Distinct from <see cref="WorldLayer"/>, which is the LOGICAL plane a gameplay attribute and an
/// entity live on. A tile has three art stacks and two logical layers, and they are not the same three
/// and two: Canopy is paint with no gameplay meaning at all.</para>
/// </summary>
public enum LayerType { Ground, Fringe, Canopy }
