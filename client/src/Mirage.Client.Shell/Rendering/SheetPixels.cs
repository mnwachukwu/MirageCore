using Microsoft.Xna.Framework;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Turning a decoded sheet's pixels into what the renderer expects.
///
/// <para>Everything draws through a premultiplied <c>BlendState.AlphaBlend</c>, so a sheet has to arrive
/// premultiplied whichever way its transparency was authored. The two paths meet here rather than in the
/// loader so the arithmetic can be exercised without a graphics device.</para>
/// </summary>
public static class SheetPixels
{
    /// <summary>
    /// Makes every pixel matching the top-left one fully transparent.
    /// </summary>
    /// <remarks>
    /// The color-key convention BMP art is authored against. Matching is on color alone — a BMP has no
    /// alpha to consider — and the result is already premultiplied, because a keyed pixel becomes
    /// <c>(0,0,0,0)</c> and every other pixel stays opaque.
    /// </remarks>
    public static void ApplyColorKey(Color[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length == 0) return;

        byte kr = pixels[0].R, kg = pixels[0].G, kb = pixels[0].B;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].R == kr && pixels[i].G == kg && pixels[i].B == kb)
                pixels[i] = Color.Transparent;
        }
    }

    /// <summary>
    /// Scales each pixel's color by its own alpha.
    /// </summary>
    /// <remarks>
    /// What a PNG needs before it can be drawn. Straight alpha composited through a premultiplied blend
    /// makes a half-transparent pixel arrive at full strength, and a fully transparent one still add its
    /// color — so art exported over a colored background shows a halo of that background around every
    /// edge. Opaque pixels are left exactly as they are.
    /// </remarks>
    public static void Premultiply(Color[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p.A == byte.MaxValue) continue;
            pixels[i] = new Color(p.R * p.A / 255, p.G * p.A / 255, p.B * p.A / 255, (int)p.A);
        }
    }
}
