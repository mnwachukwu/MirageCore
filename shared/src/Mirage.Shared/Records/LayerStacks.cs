using System.Runtime.CompilerServices;

namespace Mirage.Shared.Records;

// One visual layer stack, stored INLINE. A tile's art is part of the tile itself rather than an array
// object hanging off it, so a map's whole art lives in the tile array and costs one allocation however
// many tiles there are.
//
// Three types rather than one because the three stacks are independent: how deep the ground stack is says
// nothing about how deep the canopy is, and each is sized by its own constant so changing one cannot
// silently resize the others.

/// <summary>The ground layer stack — <see cref="Constants.MaxGroundLayers"/> packed
/// <see cref="LayerCell"/> values, drawn below all entities.</summary>
[InlineArray(Constants.MaxGroundLayers)]
public struct GroundStack
{
    private int _cell;
}

/// <summary>The fringe layer stack — <see cref="Constants.MaxFringeLayers"/> packed
/// <see cref="LayerCell"/> values, drawn between the ground and fringe entity passes.</summary>
[InlineArray(Constants.MaxFringeLayers)]
public struct FringeStack
{
    private int _cell;
}

/// <summary>The canopy layer stack — <see cref="Constants.MaxCanopyLayers"/> packed
/// <see cref="LayerCell"/> values, drawn on top of everything.</summary>
[InlineArray(Constants.MaxCanopyLayers)]
public struct CanopyStack
{
    private int _cell;
}
