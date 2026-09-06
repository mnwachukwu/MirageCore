namespace Mirage.Shared;

/// <summary>Cross-project light-rendering math + constants, shared verbatim by the client's night render
/// (GameplayScreen/MirageGame) and the editor's faithful night preview so the two can't drift. Pure math —
/// no rendering-framework types. Colors are handled as packed <c>0xRRGGBB</c> / raw byte channels; the
/// renderers wrap them into their own Color/Vector types.</summary>
public static class LightModel
{
    // Navy tint the world multiplies toward at full night. Matches the client light-map ambient.
    public const byte NightAmbientR = 15;
    public const byte NightAmbientG = 20;
    public const byte NightAmbientB = 45;

    // Halo geometry: a light's outer reach = its Radius; the inner flame core is InnerRadiusFactor of that
    // (64/96 = 2/3). The dim outer tint = core color x OuterDimFactor. The inner size never shrinks below base.
    public const float InnerRadiusFactor = 2f / 3f;
    public const float OuterDimFactor = 1f / 3f;
    public const float MinInnerSizeFactor = 1.0f;

    // Flicker tuning (pronounced so Flame/Pulse read clearly vs steady None).
    public const float FlameAmp = 0.3f;         // flame flicker depth (gentle)
    public const float FlameMinFactor = 0.9f;   // flame never dims the core below this (no "core vanished" look)
    public const float FlickerSpeed = 3.0f;     // primary wander tempo (cells/sec) — a calm flicker: fast enough
                                                // not to read as a slow pulse, slow enough not to feel frantic/sharp
    public const float FlickerDetailMul = 2.7f; // second-octave speed multiple (finer jitter)
    public const float FlickerEnvSpeed = 0.5f;  // slow amplitude envelope (rad/s)
    public const float Tau = 6.2831855f; // 2*pi — per-source phase = Hash01(id)*Tau, kept BOUNDED (see Flame)
    public const float PulseAmp = 0.30f;        // magical "breathing" depth; higher = more pronounced
    public const float PulseSpeed = 3.2f;       // magical breathing tempo (rad/s)

    // Cheap integer hash → [0,1). Deterministic per (step, source) so the flicker is stable frame-to-frame.
    public static float Hash01(int n)
    {
        unchecked
        {
            n = (n << 13) ^ n;
            int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
            return m / 2147483647f;
        }
    }

    // Smooth value noise in [0,1): a random level at each integer cell, smoothstep-eased between.
    public static float ValueNoise(float x, int seed)
    {
        int i = (int)MathF.Floor(x);
        float f = x - i;
        float u = f * f * (3f - 2f * f);
        float a = Hash01(i + seed);
        float b = Hash01(i + 1 + seed);
        return a + (b - a) * u;
    }

    // Flame flicker: organic irregular jitter (two octaves of value noise + a slow envelope), floored so the
    // bright core never dims away and biased so the salient motion is upward "licks".
    public static float Flame(float t, int id)
    {
        // The phase MUST be bounded: it's added to the time argument, so a large id (a placed light's
        // Guid.GetHashCode() ~1e9, or an NPC light id ~1e6) used raw would swamp float precision and make the
        // per-frame time term vanish — freezing the flicker or quantizing it into a computerized jitter.
        // Hash01(id) gives a well-distributed phase in [0,1) regardless of id magnitude.
        float phase = Hash01(id) * Tau;
        // Seeds are deliberately allowed to wrap: a placed light's id is Guid.GetHashCode() (~1e9), so id*3/id*7
        // overflow int. unchecked keeps that deterministic wrap correct even if CheckForOverflowUnderflow is on.
        float speed = FlickerSpeed * (0.75f + 0.5f * Hash01(unchecked(id + 53)));
        float n1 = ValueNoise(t * speed + phase, unchecked(id * 3));
        float n2 = ValueNoise(t * speed * FlickerDetailMul + phase * 1.7f, unchecked(id * 7));
        float noise = (n1 * 0.82f + n2 * 0.18f) * 2f - 1f; // -1..1 (detail octave kept light to avoid a sharp jitter)
        float envelope = 0.6f + 0.4f * MathF.Sin(t * FlickerEnvSpeed + phase * 1.3f);
        return MathF.Max(FlameMinFactor, 1f + envelope * FlameAmp * noise);
    }

    // Smooth magical "breathing" — a single sine with per-source phase (FlickerStyle.Pulse).
    public static float Pulse(float t, int id) =>
        1f + PulseAmp * MathF.Sin(t * PulseSpeed + Hash01(id) * Tau); // bounded phase — see Flame

    public static float FlickerFor(FlickerStyle style, float t, int id) => style switch
    {
        FlickerStyle.Flame => Flame(t, id),
        FlickerStyle.Pulse => Pulse(t, id),
        _ => 1f, // None — steady
    };

    // Halo alpha falloffs over a normalized distance-from-center in [0,1] (>=1 is fully transparent).
    // Outer: smoothstep 1→0 (stable soft reach). Inner: Gaussian (sigma ~ radius/2.5) tight flame core.
    public static float Smoothstep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    public static float OuterFalloff(float dist01) => dist01 >= 1f ? 0f : Smoothstep(1f - dist01);
    public static float InnerFalloff(float dist01) => dist01 >= 1f ? 0f : MathF.Exp(-dist01 * dist01 * 6.25f);
}
