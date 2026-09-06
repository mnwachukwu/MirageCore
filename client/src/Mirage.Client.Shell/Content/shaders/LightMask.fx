// Multiplies a light halo by its reach mask.
//
// The mask is a SIGNED DISTANCE FIELD: each texel holds how far it is from the shadow's edge rather than
// whether it is lit, with 0.5 sitting exactly on the edge. That is what makes the edge sharp. A mask of
// 0s and 1s sampled with linear filtering ramps from lit to dark across the gap between two texel CENTRES
// — four world pixels, centred on the boundary, so half of it lands on the art itself and every silhouette
// wears a hairline of light. Interpolating a DISTANCE is different: the blend of two distances is still
// very nearly the distance, so thresholding it here puts the edge on the boundary to a fraction of a texel.
// The same trick reads crisp glyphs out of a small font atlas.
//
// The halo itself is unchanged: the gradient is still the baked sprite's.
//
// MaskScale/MaskOffset map the halo quad's own 0..1 coordinates onto the mask's rectangle, which is
// anchored to the tile the occlusion was traced from rather than to the halo. That is what lets a halo
// slide sub-tile with a walking emitter while its shadows stay nailed to the world.
//
// PIXEL-SHADER ONLY — SpriteBatch supplies the vertex stage. Built through Content.mgcb (DesktopGL/Reach).
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float2 MaskScale;
float2 MaskOffset;
// The tile a moving emitter is entering, and how far into the step it is. Reach is answered per tile, so
// one mask can only change in a jump at a border; blending the two makes it continuous. Zero for anything
// standing still, which leaves the second sample doing nothing.
float2 IntoScale;
float2 IntoOffset;
float MaskBlend;

// Where the edge sits in the encoded byte: 128 of 255. LightOcclusion.Encode packs the distance around it.
#define MaskEdge (128.0 / 255.0)
// The width the edge softens over, in the same units. The byte spans two texels each way, so one step of
// the encoding is a sixteenth of a texel; this is about a quarter of a texel — one world pixel, enough to
// keep the edge from crawling as a light moves, narrow enough to still read as an edge.
//
// 🔴 The ramp runs from the edge INTO THE LIGHT, never across it. Centre it on the boundary and half of it
// falls on the art's own first pixel, which is a hairline of light tracing every silhouette — worst where a
// lit tile sits directly above a shaded one, where it reads as a seam between them. Running it one-sided
// costs a single world pixel of ground in front of an occluder and leaves the art itself fully dark.
//
// A field is what makes that possible: with a mask of bits the smallest shift is a whole texel, four pixels,
// which is the trade this replaces. Here the threshold moves by whatever fraction of a texel we ask for.
#define MaskSoftness (0.0623)

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

Texture2D MaskTexture;
sampler2D MaskTextureSampler = sampler_state
{
    Texture = <MaskTexture>;
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D IntoTexture;
sampler2D IntoTextureSampler = sampler_state
{
    Texture = <IntoTexture>;
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
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
    float4 c = tex2D(SpriteTextureSampler, uv) * input.Color;
    // .r because the masks are Alpha8, which DesktopGL maps to GL_LUMINANCE — sampled as (L,L,L,1). A
    // DirectX build gets A8_UNORM instead, sampled as (0,0,0,a), and would need .a here or every light in
    // the game reads as fully occluded. See GameplayScreen.ReachTexture.
    float from = tex2D(MaskTextureSampler, uv * MaskScale + MaskOffset).r;
    float into = tex2D(IntoTextureSampler, uv * IntoScale + IntoOffset).r;
    // Blend the two DISTANCES, then threshold once: a light mid-step slides its shadow's edge across the
    // world instead of cross-fading between two pictures of it.
    float edge = lerp(from, into, MaskBlend) - MaskEdge;
    // The light map accumulates additively and ignores source alpha for color, so the mask scales the RGB.
    c.rgb *= smoothstep(0.0, MaskSoftness, edge);
    return c;
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
