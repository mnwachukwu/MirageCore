using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>The visual kind of a particle — selects its texture, blend, and per-frame behavior in the
/// shell's draw + this file's <see cref="ParticleSystem.Move"/>.</summary>
public enum ParticleKind : byte
{
    RainStreak,   // falling blue streak (morphs to Splash at end of life)
    Splash,       // brief impact ring where a raindrop landed
    SnowFlake,    // slow fluttering flake that settles + fades
    WindStreak,   // fast horizontal speed-line
    Debris,       // wind-blown leaf/dust
    Spark,        // melee hit spark
    Sparkle,      // restore-spell glitter
    SpellBall,    // drain-spell bullet (homes on its target)
    Cube,         // give-item box (homes on its target)
    ImpactBurst,  // spell/impact burst on arrival
    Swoosh,       // melee crescent blade-arc over the target tile
    Orbit,        // restore/give-item landing swirl: motes circle the sprite briefly, then fade
    BloodSplatter,// blood droplet sprayed on a damaging hit (gravity, dark red); client-side flair over the pool
}

/// <summary>One pooled particle, world-anchored (world pixels) so night-dimming, camera parallax, and
/// seam-cross re-anchoring all work uniformly. Mutable struct, updated in place.</summary>
public struct Particle
{
    public float X, Y;      // world pixels
    public float Vx, Vy;    // px/sec
    public float Tx, Ty;    // homing target (world pixels) for projectile kinds
    public float Age, Life; // seconds
    public float Size;      // px — dot diameter, or streak length
    public uint Rgb;        // packed 0xRRGGBB core color
    public float Seed;      // 0..1 per-particle variation (sway phase, jitter)
    public ParticleKind Kind;
    // Two-layer world: the logical layer a SPELL/COMBAT particle lives on, so it occludes with the bridge (a
    // ground-layer burst draws under the deck, a fringe-layer one on top).  Weather kinds ignore it (drawn global).
    public WorldLayer Layer;
}

/// <summary>Pooled, allocation-free particle subsystem: a fixed pre-allocated array with the live particles
/// packed at the front <c>[0..Count)</c>. <see cref="Update"/> ages/moves in place and swap-removes the dead;
/// spawning appends at the tail (silently dropped when full — the pool is deliberately bounded to honor the
/// lightweight-game mandate). Simulation only; the shell draws <see cref="Active"/> and owns the textures.
/// Mirrors the floating-text pattern but with a true pool since weather can spawn hundreds/sec.</summary>
public sealed class ParticleSystem
{
    public const int Capacity = 4096;
    private readonly Particle[] _pool = new Particle[Capacity];
    private int _count;
    private readonly Random _rng = new();

    public int Count => _count;
    /// <summary>The live particles, packed at the front — the shell iterates this to draw.</summary>
    public ReadOnlySpan<Particle> Active => _pool.AsSpan(0, _count);

    public void ClearAll() => _count = 0;

    /// <summary>Re-anchor every particle by the seamless-map seam-cross pixel offset so world-anchored FX
    /// stay pinned to their world spot instead of jumping when the observable area shifts.
    ///
    /// <para><b>The homing target moves with the particle, and must.</b> These coordinates are relative to
    /// the 3x3 grid's own origin, which a seam cross re-anchors onto a different centre map — so BOTH ends
    /// of a projectile's flight are expressed in a space that just slid by a whole map, 16 tiles across or
    /// 12 down. Carrying the position without the target leaves a bolt correctly placed and aimed 512 pixels
    /// from its victim, which reads in play as a spell flying off in a random direction.</para></summary>
    public void ShiftAll(float dx, float dy)
    {
        var span = _pool.AsSpan(0, _count);
        for (int i = 0; i < span.Length; i++)
        {
            span[i].X += dx;
            span[i].Y += dy;
            span[i].Tx += dx;
            span[i].Ty += dy;
        }
    }

    /// <summary>Reserve a pooled slot. Returns false (no spawn) when the bounded pool is full.</summary>
    public bool TrySpawn(in Particle p)
    {
        if (_count >= Capacity) return false;
        _pool[_count++] = p;
        return true;
    }

    // Randomness for emitters (app code — System.Random is fine here).
    public float Rand01() => (float)_rng.NextDouble();
    public float RandRange(float lo, float hi) => lo + (hi - lo) * (float)_rng.NextDouble();

    public void Update(float dtSec)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ref var p = ref _pool[i];
            p.Age += dtSec;
            Move(ref p, dtSec);
            // A homing projectile that reached its target resolves its arrival FX: a drain bolt bursts, a give-item
            // cube blooms a swirl on the receiver, and each restore-glitter mote morphs into that same swirl (living
            // on as an Orbit mote rather than dying — the rain→splash pattern, so no new motes are spawned).
            if (IsHoming(p.Kind) && p.X == p.Tx && p.Y == p.Ty)
            {
                switch (p.Kind)
                {
                    case ParticleKind.SpellBall:
                        EmitImpact(p.X, p.Y, p.Rgb, p.Layer);
                        break;  // drain: radial burst
                    case ParticleKind.Cube:
                        SpawnOrbit(p.X, p.Y, p.Rgb, p.Layer);
                        break;  // give-item: swirl in its light color
                    case ParticleKind.Sparkle:
                        MorphToOrbit(ref p);
                        continue;  // restore: glitter becomes the swirl
                }
                p.Age = p.Life; // SpellBall + Cube expire now; Sparkle already continued as an Orbit mote
            }
            if (p.Age >= p.Life)
            {
                // A raindrop doesn't vanish — it becomes the splash where it landed.
                if (p.Kind == ParticleKind.RainStreak)
                {
                    MorphToSplash(ref p);
                    continue;
                }
                _pool[i] = _pool[--_count]; // swap-remove the dead
            }
        }
    }

    private void Move(ref Particle p, float dtSec)
    {
        switch (p.Kind)
        {
            case ParticleKind.SnowFlake:
                // Once settling (last SnowSettleSec of life) the flake has landed: FREEZE in place — no fall,
                // no sway — and just fade (AlphaOf). It stays world-anchored, so it scrolls with the ground.
                if (p.Age >= p.Life - SnowSettleSec) break;
                float sway = MathF.Sin((p.Age + p.Seed * SnowSwayPhaseSpread) * SnowSwayFreq) * SnowSwayAmp;
                p.X += (p.Vx + sway) * dtSec;
                p.Y += StabilizedFall(p.Vy) * dtSec;
                break;
            case ParticleKind.RainStreak:
                p.X += p.Vx * dtSec;
                p.Y += StabilizedFall(p.Vy) * dtSec;
                break;
            case ParticleKind.Spark:
            case ParticleKind.ImpactBurst:
            case ParticleKind.BloodSplatter:
                p.Vy += ParticleGravity * dtSec;
                p.X += p.Vx * dtSec;
                p.Y += p.Vy * dtSec;
                break;
            case ParticleKind.Debris:
                // Wind-blown: mostly horizontal drift with a gentle vertical bob (a tumbling leaf, not a fall).
                float bob = MathF.Sin((p.Age + p.Seed * DebrisBobPhaseSpread) * DebrisBobFreq) * DebrisBobAmp;
                p.X += p.Vx * dtSec;
                p.Y += (p.Vy + bob) * dtSec;
                break;
            case ParticleKind.Splash:
            case ParticleKind.Swoosh:
                break; // static (Vx/Vy hold facing for the swoosh's draw rotation), just ages out
            case ParticleKind.SpellBall:
            case ParticleKind.Sparkle:
            case ParticleKind.Cube:
                // Home toward the target at constant speed; clamp on arrival (Update fires the impact).
                float hx = p.Tx - p.X, hy = p.Ty - p.Y;
                float hd = MathF.Sqrt(hx * hx + hy * hy);
                float hstep = ProjectileSpeed * dtSec;
                if (hd <= hstep || hd < 0.001f)
                {
                    p.X = p.Tx;
                    p.Y = p.Ty;
                }
                else
                {
                    p.X += hx / hd * hstep;
                    p.Y += hy / hd * hstep;
                }
                break;
            case ParticleKind.Orbit:
                // Circle the landing point (Tx,Ty): radius blooms 0→max (sqrt = quick out, then holds) while the
                // mote sweeps around at its angular velocity. World-anchored on the arrival spot, so it reads as a
                // swirl on the sprite. Vx = max radius, Vy = angular velocity (signed), Seed = start phase.
                float orbR = p.Vx * MathF.Sqrt(p.Age / MathF.Max(p.Life, 0.0001f));
                float orbA = p.Seed * MathF.Tau + p.Vy * p.Age;
                p.X = p.Tx + MathF.Cos(orbA) * orbR;
                p.Y = p.Ty + MathF.Sin(orbA) * orbR;
                break;
            default: // WindStreak — straight-line horizontal drift
                p.X += p.Vx * dtSec;
                p.Y += p.Vy * dtSec;
                break;
        }
    }

    private static void MorphToSplash(ref Particle p)
    {
        p.Kind = ParticleKind.Splash;
        p.Age = 0f;
        p.Life = SplashLifeSec;
        p.Vx = 0f;
        p.Vy = 0f;
        p.Size = SplashSize;
    }

    /// <summary>Draw alpha (0..1) for a particle given its kind/age — the fade curve lives with the motion so
    /// the shell just multiplies it into the tint.</summary>
    public static float AlphaOf(in Particle p) => p.Kind switch
    {
        ParticleKind.RainStreak => RainAlpha,
        ParticleKind.SnowFlake => p.Age >= p.Life - SnowSettleSec
            ? MathF.Max(0f, (p.Life - p.Age) / SnowSettleSec)
            : 1f,
        _ => MathF.Max(0f, 1f - p.Age / p.Life),
    };

    // ── Weather emission ────────────────────────────────────────────────────
    // Spawn over the world-space viewport + a margin so particles enter from just off each edge. Rain and
    // snow fall straight DOWN in world space — the camera-follow parallax supplies the "lean opposite your
    // motion" the design calls for (left→leans right, up/down→stays vertical), so no per-particle velocity
    // hack. Weather states are mutually exclusive, so there's no rain-during-wind angle to blend.
    private float _fallAccum;      // rain/snow spawn carry
    private float _windAccum;      // wind-streak spawn carry
    private float _debrisAccum;    // wind-debris spawn carry
    private float _stabCamVelY; // camera Y velocity (px/s) — gentle vertical stabilization for falling weather

    /// <summary>Emit the active weather over the world-space viewport + a margin (follows the camera), so the
    /// particles fall through the world and scroll with the map. Drawn into the world RT, so they night-dim.
    /// <paramref name="camVelY"/> feeds a gentle vertical stabilization so a fast camera can't outrun/suspend
    /// the fall while the full horizontal parallax still tilts the droplets.</summary>
    public void EmitWeather(WeatherType weather, Camera camera, float camVelY, float dtSec)
    {
        _stabCamVelY = camVelY;
        switch (weather)
        {
            case WeatherType.Rain:
                EmitRain(camera, dtSec);
                break;
            case WeatherType.Snow:
                EmitSnow(camera, dtSec);
                break;
            case WeatherType.HeavyWind:
                EmitWind(camera, dtSec);
                break;
                // Clear = nothing; HeatWave = a post-composite shader, not particles.
        }
    }

    private void EmitRain(Camera camera, float dtSec) => EmitFalling(camera, dtSec, RainPerSec,
        RainSpeedMin, RainSpeedMax, RainLenMin, RainLenMax, RainRgb, ParticleKind.RainStreak, extraLife: 0f);

    private void EmitSnow(Camera camera, float dtSec) => EmitFalling(camera, dtSec, SnowPerSec,
        SnowSpeedMin, SnowSpeedMax, SnowSizeMin, SnowSizeMax, SnowRgb, ParticleKind.SnowFlake, extraLife: SnowSettleSec);

    /// <summary>Shared rain/snow spawn. Emits across [CameraX ± MarginX], entering MarginTop above the top and
    /// falling to a random depth that reaches MarginBottom below the bottom — so panning sideways/down scrolls in
    /// already-populated weather. The spawn rate scales up with the widened width so on-screen density is
    /// unchanged; the taller fall range fills below-viewport columns for free (longer Life, same per-column
    /// density). <paramref name="extraLife"/> is the snow settle/fade tail (0 for rain, which morphs to Splash).</summary>
    private void EmitFalling(Camera camera, float dtSec, float baseRate,
                             float speedMin, float speedMax, float sizeMin, float sizeMax,
                             uint rgb, ParticleKind kind, float extraLife)
    {
        float width = Camera.ViewW + WeatherMarginX * 2f;
        _fallAccum += baseRate * (width / FallCalibWidth) * dtSec;
        int n = (int)_fallAccum;
        _fallAccum -= n;
        float left = camera.CameraX - WeatherMarginX;
        float top = camera.CameraY - WeatherMarginTop;
        for (int i = 0; i < n; i++)
        {
            float speed = RandRange(speedMin, speedMax);
            float fall = RandRange(WeatherMarginTop, Camera.ViewH + WeatherMarginBottom); // land at a random depth, some below-screen
            TrySpawn(new Particle
            {
                X = left + Rand01() * width, Y = top,
                Vx = 0f, Vy = speed,
                Life = fall / speed + extraLife,
                Size = RandRange(sizeMin, sizeMax),
                Rgb = rgb, Seed = Rand01(), Kind = kind,
            });
        }
    }

    private void EmitWind(Camera camera, float dtSec)
    {
        float top = camera.CameraY - WeatherMarginTop;
        float height = Camera.ViewH + WeatherMarginTop + WeatherMarginBottom;
        float crossW = Camera.ViewW + WeatherMarginX * 2f;
        float heightScale = height / WindCalibHeight; // keep areal density constant as the vertical band grows

        _windAccum += WindStreakPerSec * heightScale * dtSec;
        int ns = (int)_windAccum;
        _windAccum -= ns;
        for (int i = 0; i < ns; i++)
        {
            float speed = RandRange(WindSpeedMin, WindSpeedMax);
            TrySpawn(new Particle
            {
                X = camera.CameraX - WeatherMarginX, Y = top + Rand01() * height,
                Vx = speed, Vy = 0f,
                Life = crossW / speed,
                Size = RandRange(WindLenMin, WindLenMax),
                Rgb = WindRgb, Seed = Rand01(), Kind = ParticleKind.WindStreak,
            });
        }

        _debrisAccum += DebrisPerSec * heightScale * dtSec;
        int nd = (int)_debrisAccum;
        _debrisAccum -= nd;
        for (int i = 0; i < nd; i++)
        {
            float speed = RandRange(DebrisSpeedMin, DebrisSpeedMax);
            TrySpawn(new Particle
            {
                X = camera.CameraX - WeatherMarginX, Y = top + Rand01() * height,
                Vx = speed, Vy = RandRange(-DebrisVyJitter, DebrisVyJitter),
                Life = crossW / speed,
                Size = RandRange(DebrisSizeMin, DebrisSizeMax),
                Rgb = DebrisRgb, Seed = Rand01(), Kind = ParticleKind.Debris,
            });
        }
    }

    // ── Combat emission ─────────────────────────────────────────────────────
    /// <summary>A melee swing: an oriented crescent blade-arc over the target tile in the attacker's facing
    /// direction (dirX,dirY = unit tile step). When <paramref name="sparks"/> is true (the swing connected)
    /// it also flings a crescent of sparks; a whiff (no target struck) shows the blade-arc alone. Augments
    /// the sprite's attack frame.</summary>
    public void EmitMeleeSwing(float x, float y, int dirX, int dirY, bool sparks, WorldLayer layer = WorldLayer.Ground)
    {
        // The blade-arc itself: an oriented crescent that sweeps + fades over the target tile. Vx/Vy hold the
        // facing unit vector (no motion) so the draw can rotate the crescent to point where the attacker faces.
        TrySpawn(new Particle
        {
            X = x, Y = y, Vx = dirX, Vy = dirY,
            Life = SwooshLifeSec, Size = SwooshSize, Rgb = SwooshRgb, Seed = Rand01(), Kind = ParticleKind.Swoosh, Layer = layer,
        });

        // Sparks only on contact — a whiff shows the blade-arc with nothing struck.
        if (!sparks) return;

        float baseAng = MathF.Atan2(dirY, dirX);
        for (int i = 0; i < MeleeSparkCount; i++)
        {
            float t = i / (float)(MeleeSparkCount - 1) - 0.5f; // -0.5..0.5 across the crescent
            float ang = baseAng + t * MeleeArcRad;
            float sp = RandRange(MeleeSparkSpeedMin, MeleeSparkSpeedMax);
            TrySpawn(new Particle
            {
                X = x + MathF.Cos(ang) * MeleeArcRadius,
                Y = y + MathF.Sin(ang) * MeleeArcRadius,
                Vx = MathF.Cos(ang) * sp, Vy = MathF.Sin(ang) * sp,
                Life = RandRange(MeleeSparkLifeMin, MeleeSparkLifeMax),
                Size = RandRange(MeleeSparkSizeMin, MeleeSparkSizeMax),
                Rgb = MeleeSparkRgb, Seed = Rand01(), Kind = ParticleKind.Spark, Layer = layer,
            });
        }
    }

    private const int MeleeSparkCount = 9;
    private const float MeleeArcRad = 2.0f;      // ~115deg crescent spread
    private const float MeleeArcRadius = 14f;    // px from target-tile center
    private const float MeleeSparkSpeedMin = 60f, MeleeSparkSpeedMax = 170f;
    private const float MeleeSparkLifeMin = 0.18f, MeleeSparkLifeMax = 0.32f;
    private const float MeleeSparkSizeMin = 3f, MeleeSparkSizeMax = 5f;
    private const uint MeleeSparkRgb = 0xFFF0C0; // warm white spark
    private const float SwooshLifeSec = 0.16f;   // quick blade flash
    private const float SwooshSize = 46f;        // ~1.4 tiles so the arc sweeps around the target
    private const uint SwooshRgb = 0xE0E8FF;     // pale steel

    /// <summary>Spawn the FX for a spell cast: a typed projectile homing from the caster (sx,sy) to the
    /// target (tx,ty). Drain spells throw a colored bullet; restore spells a glitter cluster; give-item a
    /// cube. If start ~= end (self-cast or unresolved target) the projectile arrives instantly in place.</summary>
    public void EmitSpell(SpellType type, float sx, float sy, float tx, float ty, WorldLayer layer = WorldLayer.Ground)
    {
        uint rgb = SpellColor(type);
        switch (type)
        {
            case SpellType.AddHp:
            case SpellType.AddMp:
            case SpellType.AddSp:
                // A loose glitter cluster, not a ball: jitter BOTH ends so each mote flies its own path and lands
                // scattered around the target (staggered sizes add to the twinkle) instead of converging to a point.
                for (int i = 0; i < SparkleCount; i++)
                {
                    SpawnProjectile(ParticleKind.Sparkle,
                        sx + RandRange(-SparkleSpread, SparkleSpread), sy + RandRange(-SparkleSpread, SparkleSpread),
                        tx + RandRange(-SparkleSpread, SparkleSpread), ty + RandRange(-SparkleSpread, SparkleSpread),
                        rgb, RandRange(SparkleSizeMin, SparkleSizeMax), layer);
                }

                break;
            case SpellType.GiveItem:
                SpawnProjectile(ParticleKind.Cube, sx, sy, tx, ty, CubeLightRgb, CubeSize, layer);
                break;
            default: // SubHp / SubMp / SubSp — a single bullet
                SpawnProjectile(ParticleKind.SpellBall, sx, sy, tx, ty, rgb, BallSize, layer);
                break;
        }
    }

    private void SpawnProjectile(ParticleKind kind, float sx, float sy, float tx, float ty, uint rgb, float size, WorldLayer layer)
        => TrySpawn(new Particle
        {
            X = sx, Y = sy, Tx = tx, Ty = ty,
            Life = ProjectileMaxLifeSec, Size = size, Rgb = rgb, Seed = Rand01(), Kind = kind, Layer = layer,
        });

    // The impact inherits the arriving projectile's layer so the burst occludes with the bridge like its bolt did.
    private void EmitImpact(float x, float y, uint rgb, WorldLayer layer)
    {
        for (int i = 0; i < ImpactCount; i++)
        {
            float ang = RandRange(0f, MathF.Tau);
            float sp = RandRange(ImpactSpeedMin, ImpactSpeedMax);
            TrySpawn(new Particle
            {
                X = x, Y = y,
                Vx = MathF.Cos(ang) * sp, Vy = MathF.Sin(ang) * sp,
                Life = RandRange(ImpactLifeMin, ImpactLifeMax),
                Size = RandRange(ImpactSizeMin, ImpactSizeMax),
                Rgb = rgb, Seed = Rand01(), Kind = ParticleKind.ImpactBurst, Layer = layer,
            });
        }
    }

    /// <summary>Spray a burst of blood droplets from (x,y) on a damaging hit — layered over the melee sparks /
    /// spell impact.  Count, speed, and size scale with <paramref name="intensity"/> (= damage / target max HP),
    /// so bigger hits spray more.  Droplets kick up + out, then arc down under gravity (the Spark/ImpactBurst arm
    /// of <see cref="Move"/>) and fade.  Purely client-side flair; the persistent pool is server-authoritative.</summary>
    public void EmitBloodSplatter(float x, float y, float intensity, WorldLayer layer = WorldLayer.Ground)
    {
        intensity = Math.Clamp(intensity, 0f, 1f);
        int count = (int)MathF.Round(BloodDropMin + (BloodDropMax - BloodDropMin) * intensity);
        for (int i = 0; i < count; i++)
        {
            float ang = RandRange(0f, MathF.Tau);
            float sp = RandRange(BloodDropSpeedMin, BloodDropSpeedMax) * (0.6f + 0.4f * intensity);
            TrySpawn(new Particle
            {
                X = x, Y = y,
                Vx = MathF.Cos(ang) * sp,
                Vy = MathF.Sin(ang) * sp - BloodDropUpBias,   // slight upward kick so droplets arc before falling
                Life = RandRange(BloodDropLifeMin, BloodDropLifeMax),
                Size = RandRange(BloodDropSizeMin, BloodDropSizeMax) * (0.7f + 0.6f * intensity),
                Rgb = BloodDropRgb, Seed = Rand01(), Kind = ParticleKind.BloodSplatter, Layer = layer,
            });
        }
    }

    private const int BloodDropMin = 3;         // droplets at intensity 0 (a little splatter, guaranteed)
    private const int BloodDropMax = 12;        // droplets at intensity 1
    private const float BloodDropSpeedMin = 40f, BloodDropSpeedMax = 150f;
    private const float BloodDropUpBias = 60f;  // initial upward kick (screen Y+ is down) so the spray fountains, then gravity pulls it down
    private const float BloodDropLifeMin = 0.30f, BloodDropLifeMax = 0.55f;
    private const float BloodDropSizeMin = 2f, BloodDropSizeMax = 5f;
    private const uint BloodDropRgb = 0x8E1010; // droplet red (a touch brighter than the pool tint so it reads mid-air)

    /// <summary>Turn an arrived restore-glitter mote into one that circles its landing point briefly, then fades —
    /// reuses the flying mote (no new spawn) so the swirl has exactly the cluster that flew in. Keeps its Size + Rgb
    /// (already the spell color).</summary>
    private void MorphToOrbit(ref Particle p)
    {
        p.Kind = ParticleKind.Orbit;
        p.Tx = p.X;
        p.Ty = p.Y;  // orbit center = where the mote landed
        p.Vx = RandRange(OrbitRadiusMin, OrbitRadiusMax);
        p.Vy = RandRange(OrbitAngSpeedMin, OrbitAngSpeedMax) * (Rand01() < 0.5f ? -1f : 1f);
        p.Age = 0f;
        p.Life = RandRange(OrbitLifeMin, OrbitLifeMax);
        p.Seed = Rand01();
    }

    /// <summary>Bloom a swirl of light-colored motes circling (cx,cy). Used for the give-item arrival, whose single
    /// cube has no glitter cluster to reuse — so the swirl is spawned fresh in the cube's light color.</summary>
    private void SpawnOrbit(float cx, float cy, uint rgb, WorldLayer layer)
    {
        for (int i = 0; i < OrbitCount; i++)
        {
            TrySpawn(new Particle
            {
                X = cx, Y = cy, Tx = cx, Ty = cy,
                Vx = RandRange(OrbitRadiusMin, OrbitRadiusMax),
                Vy = RandRange(OrbitAngSpeedMin, OrbitAngSpeedMax) * (Rand01() < 0.5f ? -1f : 1f),
                Age = 0f, Life = RandRange(OrbitLifeMin, OrbitLifeMax),
                Size = RandRange(OrbitSizeMin, OrbitSizeMax),
                Rgb = rgb, Seed = Rand01(), Kind = ParticleKind.Orbit, Layer = layer,
            });
        }
    }

    private static bool IsHoming(ParticleKind k) =>
        k is ParticleKind.SpellBall or ParticleKind.Sparkle or ParticleKind.Cube;

    /// <summary>Weather particles (rain/snow/wind/debris) are GLOBAL — drawn above everything, ignoring the layer.
    /// Every other (spell/combat) kind carries a <see cref="Particle.Layer"/> and draws in that layer's world pass
    /// so it occludes with the bridge (a ground burst under the deck, a fringe one on top).</summary>
    public static bool IsWeatherKind(ParticleKind k) =>
        k is ParticleKind.RainStreak or ParticleKind.Splash or ParticleKind.SnowFlake or ParticleKind.WindStreak or ParticleKind.Debris;

    /// <summary>Which particle kinds emit a transient light + glow core (magical FX read at night).</summary>
    public static bool EmitsLight(ParticleKind k) =>
        k is ParticleKind.SpellBall or ParticleKind.Sparkle or ParticleKind.Cube or ParticleKind.ImpactBurst or ParticleKind.Orbit;

    // Falling weather (rain/snow) never lets its ON-SCREEN fall drop below this floor, so a fast camera can't
    // outrun it panning down or make it hang. Full natural world parallax applies whenever the plain on-screen
    // fall already clears the floor (which is always, for anything but a fast downward pan) — this replaces the
    // old fixed-fraction follow that over-corrected slow snow (made it suspend/reverse when moving up).
    private const float MinScreenFall = 40f; // px/s minimum apparent downward speed for rain/snow

    /// <summary>World fall velocity adjusted so the ON-SCREEN fall (worldFall - cameraVelY) never drops below
    /// MinScreenFall. Returns the natural fall untouched unless the camera is outrunning it downward — so world
    /// parallax is fully preserved except in the one case that would otherwise suspend/reverse the weather.</summary>
    private float StabilizedFall(float vy) =>
        vy - _stabCamVelY < MinScreenFall ? _stabCamVelY + MinScreenFall : vy;

    /// <summary>The particle's on-screen velocity: world velocity minus camera velocity, with the same fall
    /// floor as <see cref="StabilizedFall"/> applied to rain streaks. The shell angles rain/wind streaks along
    /// this so the droplet visibly tilts toward its true screen direction as you move (horizontal parallax).</summary>
    public static (float vx, float vy) OnScreenVelocity(in Particle p, float camVelX, float camVelY)
    {
        float vy = p.Vy - camVelY;
        if (p.Kind == ParticleKind.RainStreak) vy = MathF.Max(vy, MinScreenFall);
        return (p.Vx - camVelX, vy);
    }

    /// <summary>FX color for a spell type: action-color for HP (damage red / heal green), identity for MP
    /// (blue) and SP (gold/amber), white for give-item. Green means heal only — see the SP recolor.</summary>
    private static uint SpellColor(SpellType type) => type switch
    {
        SpellType.SubHp => 0xE03828, // red
        SpellType.AddHp => 0x40E050, // green (heal)
        SpellType.SubMp => 0x3060E0, // deep blue
        SpellType.AddMp => 0x40D0F0, // cyan
        SpellType.SubSp => 0xE0A030, // amber
        SpellType.AddSp => 0xF0D040, // gold
        _ => 0xFFFFFF,               // GiveItem / fallback: white
    };

    /// <summary>Milliseconds a projectile takes to cross <paramref name="distancePx"/> at the homing speed,
    /// capped at the projectile lifetime — the client times deferred hit FX (number/death) to the bolt's arrival.</summary>
    public static float ProjectileFlightMs(float distancePx) =>
        MathF.Min(distancePx / ProjectileSpeed, ProjectileMaxLifeSec) * 1000f;

    // ── Spell FX tuning ─────────────────────────────────────────────────────
    private const float ProjectileSpeed = 520f;      // px/s homing speed
    private const float ProjectileMaxLifeSec = 0.9f; // flight cap if the target slips out of reach
    private const float BallSize = 10f;
    private const float CubeSize = 12f;
    private const uint CubeLightRgb = 0xFFFFFF;       // the cube's LIGHT is white; its body is drawn gray by the shell
    private const int SparkleCount = 8;              // motes per restore cast — enough to read as a glitter cluster
    private const float SparkleSpread = 11f;         // start/target jitter radius (px) so motes scatter, not converge
    private const float SparkleSizeMin = 3f, SparkleSizeMax = 8f; // wide range => mixed tiny/bright motes twinkle
    private const int ImpactCount = 7;
    private const float ImpactSpeedMin = 70f, ImpactSpeedMax = 180f;
    private const float ImpactLifeMin = 0.18f, ImpactLifeMax = 0.34f;
    private const float ImpactSizeMin = 3f, ImpactSizeMax = 6f;
    // Restore/give-item "landing" swirl — motes circle the sprite for a beat then fade (the gain-side impact).
    private const int OrbitCount = 10;                                // motes spawned for the give-item swirl (restore reuses its SparkleCount motes)
    private const float OrbitLifeMin = 0.45f, OrbitLifeMax = 0.7f;    // "momentarily"
    private const float OrbitRadiusMin = 12f, OrbitRadiusMax = 20f;   // circle around the ~32px sprite body
    private const float OrbitAngSpeedMin = 6f, OrbitAngSpeedMax = 10f;// rad/s — roughly half-to-full turn over the life
    private const float OrbitSizeMin = 3f, OrbitSizeMax = 6f;         // sparkle-sized glints

    // ── Weather tuning ──────────────────────────────────────────────────────
    // Weather spawns across the viewport widened by these margins and lives (via its fall/cross Life) well past
    // the edges, so panning scrolls in already-populated weather instead of a hard edge that visibly fills in.
    // The DRAW still culls at the viewport (ParticleCullMargin), so the off-screen reservoir costs only Update.
    // Spawn RATES scale with the widened band (below) so on-screen density is identical to the un-widened tuning.
    private const float WeatherMarginX = 512f;       // horizontal reservoir beyond each side (~one viewport)
    private const float WeatherMarginTop = 64f;      // falling weather enters this far above the top edge
    private const float WeatherMarginBottom = 384f;  // ...and keeps falling/settling this far below the bottom edge
    private const float FallCalibWidth = Camera.ViewW + 96f;  // width the fall rates were tuned at (ViewW + 2*48)
    private const float WindCalibHeight = Camera.ViewH + 96f; // height the wind rates were tuned at (ViewH + 2*48)
    private const float RainPerSec = 140f;
    private const float RainSpeedMin = 720f, RainSpeedMax = 1000f;
    private const float RainLenMin = 14f, RainLenMax = 24f;
    private const uint RainRgb = 0x4F7DC2;           // medium blue (reads on bright tiles; outline covers dark)
    private const float SnowPerSec = 55f;
    private const float SnowSpeedMin = 45f, SnowSpeedMax = 95f;
    private const float SnowSizeMin = 5f, SnowSizeMax = 8f;
    private const uint SnowRgb = 0xFFFFFF;           // pure white
    private const float WindStreakPerSec = 45f;
    private const float WindSpeedMin = 500f, WindSpeedMax = 950f;
    private const float WindLenMin = 22f, WindLenMax = 40f;
    private const uint WindRgb = 0xFFFFFF;           // pure white speed-lines (brightest; fades over each streak's life)
    private const float DebrisPerSec = 13f;
    private const float DebrisSpeedMin = 220f, DebrisSpeedMax = 380f;
    private const float DebrisSizeMin = 5f, DebrisSizeMax = 9f;
    private const float DebrisVyJitter = 20f;        // spawn vertical velocity spread (px/s)
    private const uint DebrisRgb = 0x453820;         // deeper brown — a touch darker for more presence, still wispy
    private const float DebrisBobFreq = 3.0f;        // rad/s vertical bob
    private const float DebrisBobAmp = 40f;          // px/s bob velocity
    private const float DebrisBobPhaseSpread = 6f;   // desync debris by seed

    // ── Motion / fade tuning ────────────────────────────────────────────────
    private const float ParticleGravity = 520f;      // px/s^2 for sparks/debris/bursts
    private const float RainAlpha = 0.8f;            // semi-transparent rain streaks
    private const float SnowSwayFreq = 2.2f;         // rad/s horizontal flutter
    private const float SnowSwayAmp = 14f;           // px/s peak sway velocity
    private const float SnowSwayPhaseSpread = 10f;   // desync flakes by their seed
    private const float SnowSettleSec = 2.6f;        // settle-and-fade window at end of life (snow sticks + lingers)
    private const float SplashLifeSec = 0.28f;       // splash ring lifetime
    private const float SplashSize = 6f;             // splash ring diameter (px)
}
