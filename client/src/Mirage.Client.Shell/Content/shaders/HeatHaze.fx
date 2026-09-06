// Heat-haze post effect for the world composite: a time-animated horizontal ripple (rising hot air) plus a
// subtle warm tint. Applied via SpriteBatch when Weather == HeatWave. PIXEL-SHADER ONLY — SpriteBatch supplies
// the vertex stage (its own MatrixTransform); a custom vertex shader here would get an unset transform and
// collapse the geometry to black. Built by MonoGame.Content.Builder.Task through Content.mgcb (DesktopGL/Reach).
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float Time;      // seconds, for the ripple animation
float Intensity; // 0..1 heat strength
float ScrollY;   // camera Y in screen-heights — anchors the wave to the world so it doesn't speed up when moving

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    // Two summed sines by WORLD vertical position (uv.y + ScrollY) + time = a shimmering horizontal wobble that
    // reads as hot air. Anchoring to world-Y keeps the shimmer attached to the ground, so moving up/down doesn't
    // change its apparent speed. Lower frequencies => longer, calmer waves.
    float y = uv.y + ScrollY;
    // Frequencies are exact multiples of 2*PI so the wave stays continuous across a Y-axis seam cross, where
    // ScrollY steps by exactly 1.0 (one screen height) — otherwise the phase jumps and the shimmer flickers.
    // 12.56637 = 2*(2*PI), 31.41593 = 5*(2*PI).
    float wave = sin(y * 12.56637 + Time * 3.1) * 0.0050 * Intensity
               + sin(y * 31.41593 - Time * 5.3) * 0.0022 * Intensity;
    float4 c = tex2D(SpriteTextureSampler, float2(uv.x + wave, uv.y)) * input.Color;
    // Subtle warm color-grade, scaled by intensity.
    float3 warm = c.rgb * float3(1.08, 1.02, 0.92) + float3(0.03, 0.015, 0.0);
    c.rgb = lerp(c.rgb, warm, Intensity);
    return c;
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
