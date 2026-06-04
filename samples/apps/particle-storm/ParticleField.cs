using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;
using WinColors = Microsoft.UI.Colors;

namespace ParticleStorm;

public enum Palette
{
    Galaxy,
    Ember,
    Glacier,
    Garden,
}

public static class Palettes
{
    public static Color[] For(Palette palette) => palette switch
    {
        Palette.Ember => Ember,
        Palette.Glacier => Glacier,
        Palette.Garden => Garden,
        _ => Galaxy,
    };

    public static readonly Color[] Galaxy = Build((25, 8, 65), (74, 38, 148), (0, 180, 216), (255, 255, 255));
    public static readonly Color[] Ember = Build((45, 12, 4), (184, 46, 18), (255, 159, 28), (255, 244, 214));
    public static readonly Color[] Glacier = Build((4, 23, 38), (17, 96, 140), (122, 220, 232), (240, 252, 255));
    public static readonly Color[] Garden = Build((9, 47, 28), (32, 120, 70), (145, 210, 80), (247, 252, 195));

    static Color[] Build(params (byte R, byte G, byte B)[] stops)
    {
        var colors = new Color[256];
        int segments = stops.Length - 1;
        for (int i = 0; i < colors.Length; i++)
        {
            double t = i / 255.0;
            double scaled = t * segments;
            int segment = Math.Min((int)scaled, segments - 1);
            double local = scaled - segment;
            var a = stops[segment];
            var b = stops[segment + 1];
            colors[i] = Color.FromArgb(
                255,
                Lerp(a.R, b.R, local),
                Lerp(a.G, b.G, local),
                Lerp(a.B, b.B, local));
        }

        return colors;
    }

    static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
}

public sealed class ParticleField : IDisposable
{
    public const int Capacity = 100_000;
    const float InitialWidth = 1_000f;
    const float InitialHeight = 700f;
    readonly Particle[] _particles;
    readonly Random _random = new(53);
    // Burst requests are produced on the UI thread (button click) and consumed on the
    // Win2D game thread at the start of Step. ConcurrentQueue gives us a lock-free
    // hand-off — without it, ResetParticle mutations from the UI thread would race
    // the Step inner loop's reads on the same _particles slots, producing torn
    // Particle structs (17 bytes, not atomic).
    readonly System.Collections.Concurrent.ConcurrentQueue<(float X, float Y, int N)> _pendingBursts = new();
    CanvasDevice? _spriteDevice;
    CanvasRenderTarget? _sprite;
    int _burstCursor;
    float _autoBurstTimer;
    float _idleAdvance;
    const float AutoBurstIntervalSeconds = 3.5f;
    const int _autoBurstReseedCount = 600;

    public ParticleField(int initialCount = 5_000)
    {
        _particles = new Particle[Capacity];
        ActiveCount = Math.Clamp(initialCount, 1, Capacity);
        for (int i = 0; i < ActiveCount; i++)
            ResetParticle(i, InitialWidth * 0.5f, InitialHeight * 0.5f, spread: 360f);
    }

    public int ActiveCount { get; private set; }

    public struct Particle
    {
        public float x;
        public float y;
        public float vx;
        public float vy;
        public byte hue;
    }

    public void Step(
        TimeSpan elapsed,
        int count,
        float gravity,
        float drag,
        float cursorX,
        float cursorY,
        bool cursorActive,
        float canvasWidth,
        float canvasHeight)
    {
        // Drain any UI-thread-queued burst requests before iterating the particle
        // array, so all mutations happen single-threaded on the game thread.
        while (_pendingBursts.TryDequeue(out var b))
            ApplyBurst(b.X, b.Y, b.N);

        int targetCount = Math.Clamp(count, 1, Capacity);
        while (ActiveCount < targetCount)
            ResetParticle(ActiveCount++, cursorX, cursorY, spread: 240f);
        ActiveCount = targetCount;

        float dt = Math.Clamp((float)elapsed.TotalSeconds, 0f, 1f / 20f);
        float damping = MathF.Exp(-Math.Max(0f, drag) * dt * 60f);

        if (cursorActive)
        {
            StepChase(dt, damping, gravity * dt, cursorX, cursorY);
            _idleAdvance = 0f;
        }
        else
        {
            // Accumulate idle time for the gentle bob phase. Reset when chase resumes.
            _idleAdvance += dt;
            StepIdle(dt, damping, canvasWidth, canvasHeight);
        }

        // Periodic auto-burst keeps the scene lively even when idle.
        _autoBurstTimer += dt;
        if (_autoBurstTimer >= AutoBurstIntervalSeconds && cursorActive)
        {
            _autoBurstTimer = 0f;
            float ax = cursorX + ((float)_random.NextDouble() - 0.5f) * 800f;
            float ay = cursorY + ((float)_random.NextDouble() - 0.5f) * 500f;
            ReseedFastest(_autoBurstReseedCount, ax, ay);
        }
    }

    void StepChase(float dt, float damping, float attract, float cursorX, float cursorY)
    {
        // SIMD is intentionally not used here: the mandated Particle[] AoS layout is
        // optimal for sprite-batch rendering, while vectorizing Step would require a
        // per-frame gather/scatter or a parallel SoA buffer that costs more complexity
        // than this readable scalar loop for the sample's 100k-particle ceiling.
        const float MinSpeedSq = 12f * 12f; // anti-cluster floor — re-energize stalled particles
        for (int i = 0; i < ActiveCount; i++)
        {
            ref var p = ref _particles[i];
            float dx = cursorX - p.x;
            float dy = cursorY - p.y;
            float distSq = (dx * dx) + (dy * dy) + 80f;
            float invDist = 1.0f / MathF.Sqrt(distSq);
            float force = attract * invDist;

            p.vx = ((p.vx + (dx * force)) * damping) + Drift(i, p.hue, dt);
            p.vy = (p.vy + (dy * force)) * damping;

            // Anti-cluster: when a particle stalls too close to the attractor,
            // give it a tangential kick so the field doesn't collapse to a blob.
            float speedSq = (p.vx * p.vx) + (p.vy * p.vy);
            if (speedSq < MinSpeedSq)
            {
                float tangentX = -dy * invDist;
                float tangentY = dx * invDist;
                float kick = 80f + ((i & 31) * 4f);
                p.vx += tangentX * kick;
                p.vy += tangentY * kick;
            }

            p.x += p.vx * dt;
            p.y += p.vy * dt;
        }
    }

    void StepIdle(float dt, float damping, float canvasWidth, float canvasHeight)
    {
        // Cursor is outside the canvas — particles drift to a deterministic uniform
        // grid layout and bob gently in place. Per-particle home is computed from
        // index so the field re-converges deterministically every time we go idle.
        float w = canvasWidth > 0 ? canvasWidth : 800f;
        float h = canvasHeight > 0 ? canvasHeight : 600f;
        float aspect = w / h;
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ActiveCount * aspect)));
        int rows = Math.Max(1, (int)Math.Ceiling((float)ActiveCount / cols));
        float cellW = w / cols;
        float cellH = h / rows;

        // Spring constant tuned so a particle ~100 px from home reaches it in ~1s.
        const float SpringK = 6.0f;
        const float IdleDamping = 0.91f;     // stronger settle than chase damping
        const float BobAmplitude = 4.5f;     // px
        const float BobFreq = 0.9f;          // Hz

        float idleDamp = MathF.Pow(IdleDamping, dt * 60f);
        float bobPhaseGlobal = _idleAdvance * BobFreq * MathF.Tau;

        for (int i = 0; i < ActiveCount; i++)
        {
            ref var p = ref _particles[i];

            int row = i / cols;
            int col = i % cols;
            float homeX = (col + 0.5f) * cellW;
            float homeY = (row + 0.5f) * cellH;

            // Per-particle bob phase from index + hue → independent micro-motion.
            float phaseSeed = (i * 0.137f) + (p.hue * 0.041f);
            float bobX = MathF.Sin(bobPhaseGlobal + phaseSeed) * BobAmplitude;
            float bobY = MathF.Cos(bobPhaseGlobal * 0.85f + phaseSeed) * BobAmplitude;

            float dx = (homeX + bobX) - p.x;
            float dy = (homeY + bobY) - p.y;

            p.vx = (p.vx + dx * SpringK * dt) * idleDamp;
            p.vy = (p.vy + dy * SpringK * dt) * idleDamp;

            p.x += p.vx * dt;
            p.y += p.vy * dt;
        }
    }

    public void Render(CanvasDrawingSession session, Color[] palette)
    {
        if (_sprite is null || !ReferenceEquals(_spriteDevice, session.Device))
            CreateSprite(session.Device);

        using var batch = session.CreateSpriteBatch();
        // Speed-modulated color: faster particles glow brighter in the palette's
        // upper register. The palette's first slot is reserved for the dark base
        // hue (assigned at spawn), so the hue field acts as a low-frequency tint
        // and speed picks the actual sample index from the palette ramp.
        const float MaxSpeed = 600f;
        const int PaletteLen = 256;
        for (int i = 0; i < ActiveCount; i++)
        {
            var p = _particles[i];
            float speed = MathF.Sqrt((p.vx * p.vx) + (p.vy * p.vy));
            float t = Math.Clamp(speed / MaxSpeed, 0f, 1f);
            int baseIdx = p.hue >> 2;                      // 0..63 — palette band per particle
            int idx = baseIdx + (int)(t * (PaletteLen - 1 - baseIdx));
            batch.Draw(_sprite!, new Rect(p.x, p.y, 4, 4), ToVector4(palette[idx]));
        }
    }

    /// <summary>
    /// UI-thread safe: enqueues a burst request that the game thread applies on
    /// the next <see cref="Step"/>. The <paramref name="palette"/> parameter is
    /// unused — hue is set from <see cref="Random"/> in ResetParticle so each
    /// burst gets a fresh-looking spread.
    /// </summary>
    public void Burst(float x, float y, int n, Color[] palette)
    {
        _ = palette;
        if (n <= 0) return;
        _pendingBursts.Enqueue((x, y, Math.Min(n, Capacity)));
    }

    void ApplyBurst(float x, float y, int n)
    {
        int count = Math.Clamp(n, 0, Capacity);
        for (int i = 0; i < count; i++)
        {
            int index = ActiveCount < Capacity ? ActiveCount++ : _burstCursor++ % Capacity;
            ResetParticle(index, x, y, spread: 12f, fast: true);
        }
    }

    public void Dispose()
    {
        _sprite?.Dispose();
        _sprite = null;
        _spriteDevice = null;
    }

    void CreateSprite(CanvasDevice device)
    {
        if (_sprite is not null && ReferenceEquals(_spriteDevice, device))
            return;

        _sprite?.Dispose();
        _spriteDevice = device;
        _sprite = new CanvasRenderTarget(device, 4, 4, 96);
        using var ds = _sprite.CreateDrawingSession();
        ds.Clear(WinColors.Transparent);
        ds.FillCircle(2, 2, 1.7f, WinColors.White);
    }

    void ResetParticle(int index, float originX, float originY, float spread, bool fast = false)
    {
        double angle = _random.NextDouble() * Math.Tau;
        float radius = (float)(_random.NextDouble() * spread);
        float speed = fast ? 220f + (float)_random.NextDouble() * 360f : 10f + (float)_random.NextDouble() * 80f;
        _particles[index] = new Particle
        {
            x = originX + MathF.Cos((float)angle) * radius,
            y = originY + MathF.Sin((float)angle) * radius,
            vx = MathF.Cos((float)angle) * speed,
            vy = MathF.Sin((float)angle) * speed,
            hue = (byte)_random.Next(0, 256),
        };
    }

    /// <summary>Re-seed N particles starting from index 0 — runs the auto-burst without changing ActiveCount.</summary>
    void ReseedFastest(int n, float originX, float originY)
    {
        int count = Math.Clamp(n, 0, ActiveCount);
        for (int i = 0; i < count; i++)
        {
            int index = _burstCursor++ % ActiveCount;
            ResetParticle(index, originX, originY, spread: 12f, fast: true);
        }
    }

    static Vector4 ToVector4(Color color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    static float Drift(int index, byte hue, float dt)
    {
        float wave = MathF.Sin((index * 0.0137f) + (hue * 0.071f));
        return wave * 6f * dt;
    }
}
