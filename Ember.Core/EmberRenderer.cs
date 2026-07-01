using SkiaSharp;

namespace Ember;

/// <summary>A breathing rhythm: a named sequence of (label, seconds) steps.</summary>
public sealed class Rhythm
{
    public required string Name { get; init; }
    public required (string Label, double Seconds)[] Steps { get; init; }

    public double Total
    {
        get
        {
            double s = 0;
            foreach (var step in Steps) s += step.Seconds;
            return s;
        }
    }

    /// <summary>Compact cadence, e.g. "4·2·6·2".</summary>
    public string Cadence =>
        string.Join("·", Array.ConvertAll(Steps, s => s.Seconds.ToString("0.###")));
}

/// <summary>
/// Ports ember.html's canvas drawing to SkiaSharp: a warm gradient background, a
/// breathing ember/candle core, a soft halo, and drifting spark "motes".
/// </summary>
public sealed class EmberRenderer
{
    public static readonly Rhythm[] Rhythms =
    {
        new() { Name = "slow exhale", Steps = new[] { ("breathe in", 4.0), ("hold", 2.0), ("let go", 6.0), ("rest", 2.0) } },
        new() { Name = "4 · 7 · 8",   Steps = new[] { ("breathe in", 4.0), ("hold", 7.0), ("let go", 8.0) } },
        new() { Name = "box",         Steps = new[] { ("breathe in", 4.0), ("hold", 4.0), ("let go", 4.0), ("rest", 4.0) } },
    };

    public Rhythm Rhythm { get; set; } = Rhythms[0];
    public double Dim { get; set; } = 0.85;

    /// <summary>The current phase label, surfaced for the phase-word UI.</summary>
    public string PhaseName { get; private set; } = "";

    private struct Mote
    {
        public double X, Y, R, Vy, Vx, Tw, Tws;
    }

    private const int NMotes = 40;
    private readonly Mote[] _motes = new Mote[NMotes];
    private readonly Random _rng = new();

    public EmberRenderer()
    {
        for (int i = 0; i < NMotes; i++)
        {
            _motes[i] = new Mote
            {
                X = _rng.NextDouble(),
                Y = _rng.NextDouble(),
                R = 0.6 + _rng.NextDouble() * 1.4,
                Vy = -(0.004 + _rng.NextDouble() * 0.01), // gentle rise, like sparks
                Vx = (_rng.NextDouble() - 0.5) * 0.004,
                Tw = _rng.NextDouble() * Math.PI * 2,
                Tws = 0.3 + _rng.NextDouble() * 0.6,
            };
        }
    }

    internal static double Smooth(double t)
    {
        t = Math.Min(Math.Max(t, 0), 1);
        return t * t * (3 - 2 * t);
    }

    // phase value: 0 = exhaled, 1 = inhaled
    internal (double P, string Name) BreathState(double time)
    {
        double total = Rhythm.Total;
        double t = time % total;
        double level = 0; // current fill level entering this step
        foreach (var (name, dur) in Rhythm.Steps)
        {
            if (t < dur)
            {
                double p;
                if (name == "breathe in") p = Smooth(t / dur);
                else if (name == "let go") p = 1 - Smooth(t / dur);
                else p = level; // hold / rest keep the level
                return (p, name);
            }
            if (name == "breathe in") level = 1;
            else if (name == "let go") level = 0;
            t -= dur;
        }
        return (0, "rest");
    }

    private static byte B(double v) => (byte)Math.Clamp(v, 0, 255);
    private static byte A(double a) => (byte)Math.Clamp(a * 255, 0, 255);

    /// <param name="time">seconds since start</param>
    /// <param name="dt">seconds since the previous frame</param>
    public void Draw(SKCanvas canvas, int width, int height, double time, double dt)
    {
        double W = width, H = height;
        double dim = Dim;
        var (p, name) = BreathState(time);
        PhaseName = name;

        // background: warm near-black vertical gradient
        canvas.Clear(SKColors.Black);
        using (var bg = new SKPaint())
        {
            var top = new SKColor(B(10 * dim), B(5 * dim), B(3 * dim));
            var bottom = new SKColor(B(24 * dim), B(11 * dim), B(5 * dim));
            bg.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, (float)H),
                new[] { top, bottom }, null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, (float)W, (float)H, bg);
        }

        float cx = (float)(W / 2), cy = (float)(H * 0.46);
        double minDim = Math.Min(W, H);
        float R = (float)(minDim * (0.085 + 0.13 * p));

        // wide halo
        using (var halo = new SKPaint())
        {
            double hAlpha = (0.10 + 0.14 * p) * dim;
            halo.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), R * 3.2f,
                new[] { new SKColor(232, 140, 60, A(hAlpha)), new SKColor(232, 140, 60, 0) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, (float)W, (float)H, halo);
        }

        // core: ember -> candlelight as it fills
        using (var core = new SKPaint { IsAntialias = true })
        {
            double g1 = 181 - 50 * (1 - p); // candle to deep ember
            double b1 = 102 - 60 * (1 - p);
            var c0 = new SKColor(B(242 * dim), B(Math.Max(g1, 60) * dim), B(Math.Max(b1, 20) * dim), A(0.85 * dim));
            var c1 = new SKColor(B(196 * dim), B(88 * dim), B(28 * dim), A(0.5 * dim));
            var c2 = new SKColor(196, 88, 28, 0);
            core.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), R,
                new[] { c0, c1, c2 }, new[] { 0f, 0.7f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx, cy, R, core);
        }

        // motes
        using (var motePaint = new SKPaint { IsAntialias = true })
        {
            for (int i = 0; i < _motes.Length; i++)
            {
                ref Mote m = ref _motes[i];
                m.Y += m.Vy * dt; // per-second rate (JS used vy/60 at 60fps)
                m.X += m.Vx * dt;
                if (m.Y < -0.02) { m.Y = 1.02; m.X = _rng.NextDouble(); }
                if (m.X < -0.02) m.X = 1.02;
                if (m.X > 1.02) m.X = -0.02;
                double tw = 0.25 + 0.2 * Math.Sin(time * m.Tws + m.Tw);
                motePaint.Color = new SKColor(B(230 * dim), B(150 * dim), B(70 * dim), A(tw * dim * 0.55));
                canvas.DrawCircle((float)(m.X * W), (float)(m.Y * H), (float)m.R, motePaint);
            }
        }
    }
}
