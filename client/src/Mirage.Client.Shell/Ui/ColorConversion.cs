using System;

namespace Mirage.Client.Shell.Ui;

/// <summary>HSV ↔ RGB conversion for the color picker. Hue is degrees [0,360), saturation/value are
/// [0,1], and RGB channels are 0-255. Pure and static so the picker's box/strip math is unit-testable.</summary>
public static class ColorConversion
{
    public static (int R, int G, int B) HsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f; // normalize into [0,360)
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);

        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r1, g1, b1;
        switch ((int)(h / 60f) % 6)
        {
            case 0:
                (r1, g1, b1) = (c, x, 0f);
                break;
            case 1:
                (r1, g1, b1) = (x, c, 0f);
                break;
            case 2:
                (r1, g1, b1) = (0f, c, x);
                break;
            case 3:
                (r1, g1, b1) = (0f, x, c);
                break;
            case 4:
                (r1, g1, b1) = (x, 0f, c);
                break;
            default:
                (r1, g1, b1) = (c, 0f, x);
                break;
        }
        return (ToByte(r1 + m), ToByte(g1 + m), ToByte(b1 + m));
    }

    public static (float H, float S, float V) RgbToHsv(int r, int g, int b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float d = max - min;

        float h;
        if (d <= float.Epsilon) h = 0f;
        else if (max == rf) h = 60f * (((gf - bf) / d) % 6f);
        else if (max == gf) h = 60f * ((bf - rf) / d + 2f);
        else h = 60f * ((rf - gf) / d + 4f);
        if (h < 0f) h += 360f;

        float s = max <= float.Epsilon ? 0f : d / max;
        return (h, s, max);
    }

    private static int ToByte(float f) => Math.Clamp((int)MathF.Round(f * 255f), 0, 255);
}
