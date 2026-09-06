namespace Mirage.Client.Core.Logic;

/// <summary>Layout + timing tunables for in-world chat bubbles. Single place to tweak look + feel.</summary>
public static class ChatBubbleStyle
{
    public const int MaxWidthPx = 180;
    public const int MaxLines = 3;
    public const int PadX = 6;
    public const int PadY = 3;
    public const int CornerRadius = 3;
    public const int ShadowOffset = 2;
    public const int GapAboveName = 4;
    // Reading-spd estimate: base + per-word, clamped.
    public const long BaseMs = 2000;
    public const long PerWordMs = 300;
    public const long MinMs = 2500;
    public const long MaxMs = 8000;
    // Drifter: rises BubbleFloatPx over BubbleFloatMs, alpha fades linearly to 0.
    public const long FloatMs = 600;
    public const float FloatPx = 24f;
}
