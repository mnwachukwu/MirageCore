using Mirage.Shared;

namespace Mirage.Editor.Models;

/// <summary>
/// Which of the map canvas's fifteen art layers are drawn.
///
/// <para>View state and nothing else. It never reaches a <c>MapRecord</c>, a save or a packet: hiding a
/// layer is a way of looking at a map, not a fact about one.</para>
///
/// <para>A value rather than a mutable object, so a change produces a new instance and Avalonia's
/// <c>AffectsRender</c> sees it. What is stored is the set of HIDDEN layers, which makes the default
/// value — every bit clear — mean everything visible. Storing the visible set instead would make a
/// forgotten initializer blank the whole canvas.</para>
/// </summary>
/// <param name="Hidden">Bit per layer, set when that layer is hidden.</param>
public readonly record struct LayerVisibility(int Hidden)
{
    /// <summary>Layers per stack, and so the bit stride between stacks.</summary>
    private static readonly int Stride = Math.Max(
        Constants.MaxGroundLayers, Math.Max(Constants.MaxFringeLayers, Constants.MaxCanopyLayers));

    private static readonly int StackCount = Enum.GetValues<LayerType>().Length;
    private static readonly int FullMask = (1 << (StackCount * Stride)) - 1;

    /// <summary>Everything drawn. Also what the default value means.</summary>
    public static LayerVisibility All => default;

    /// <summary>Nothing drawn.</summary>
    public static LayerVisibility Nothing => new(FullMask);

    private static int Bit(LayerType type, int index) => 1 << ((int)type * Stride + index);

    /// <summary>Every bit of one stack, so a stack can be read or set as a unit.</summary>
    private static int StackBits(LayerType type) => ((1 << Stride) - 1) << ((int)type * Stride);

    /// <summary>Whether one layer is drawn. <paramref name="index"/> is 0-based within its stack.</summary>
    public bool IsVisible(LayerType type, int index) => (Hidden & Bit(type, index)) == 0;

    /// <summary>The stack's five bits as a visible-set, for a renderer walking a layer span.</summary>
    public int VisibleBits(LayerType type) => (~Hidden & StackBits(type)) >> ((int)type * Stride);

    public LayerVisibility With(LayerType type, int index, bool visible) =>
        new(visible ? Hidden & ~Bit(type, index) : Hidden | Bit(type, index));

    public LayerVisibility WithStack(LayerType type, bool visible) =>
        new(visible ? Hidden & ~StackBits(type) : Hidden | StackBits(type));

    public static LayerVisibility ForAll(bool visible) => visible ? All : Nothing;

    /// <summary>A whole stack's state for a three-state parent box: true all shown, false all hidden,
    /// null a mix.</summary>
    public bool? StackState(LayerType type)
    {
        int hidden = Hidden & StackBits(type);
        if (hidden == 0) return true;
        return hidden == StackBits(type) ? false : null;
    }

    public bool AnyHidden => Hidden != 0;
    public bool AllVisible => Hidden == 0;
}
