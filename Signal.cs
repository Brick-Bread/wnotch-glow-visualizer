using Notch.Core.Activities;
using Notch.Core.Plugins;

namespace GlowVisualizer;

/// <summary>
/// The music as the animations want it: the raw levels from Notch, smoothed into values that move
/// like light (quick to rise, slow to fall) and a beat detector. One instance lives for the plugin's run.
/// </summary>
internal sealed class Signal
{
    private const double BeatCooldown = 0.2;

    // How long a band's remembered peak takes to fade, so each pitch is judged against the track it is in.
    private const double PeakMemorySeconds = 8;

    // Peaks below this are not stretched up: a band with nothing in it should stay dark.
    private const double PeakFloor = 0.3;

    private readonly double[] _peaks = new double[PluginAudio.BandCount];
    private double _bassAverage;
    private double _sinceBeat = 1;

    /// <summary>Seconds since the previous frame.</summary>
    public double Dt { get; private set; } = 0.033;

    /// <summary>Seconds since the plugin started; drives anything that moves on its own.</summary>
    public double Time { get; private set; }

    /// <summary>Overall loudness, smoothed, 0..1.</summary>
    public double Energy { get; private set; }

    public double Bass { get; private set; }

    public double Mid { get; private set; }

    public double Treble { get; private set; }

    /// <summary>The bands of <see cref="AudioFrame.Bands"/>, smoothed, low pitch first.</summary>
    public float[] Bands { get; } = new float[PluginAudio.BandCount];

    /// <summary>
    /// Where the weight of the sound sits, 0 (all bass) to 1 (all treble), slowly smoothed: the track's EQ
    /// profile as one number. Bass-heavy music is low, bright music is high.
    /// </summary>
    public double Tone { get; private set; } = 0.35;

    /// <summary>The hue (0..1 round the colour wheel) that <see cref="Tone"/> maps to: red for bass-heavy, through green, to violet for bright.</summary>
    public double ToneHue => Tone * 0.78;

    /// <summary>True for the one frame in which a beat (a sudden rise in the bass) was heard.</summary>
    public bool Beat { get; private set; }

    /// <summary>1 at a beat, falling to 0 over about a third of a second.</summary>
    public double Kick { get; private set; }

    /// <summary>Takes in the newest frame. <paramref name="sensitivity"/> is 1 for the levels as they are.</summary>
    /// <param name="balance">0 shows each pitch at its true loudness; 1 judges each against its own recent peak, so the whole EQ profile shows, not just the bass.</param>
    public void Update(AudioFrame audio, double dt, double sensitivity, double balance = 0.6)
    {
        Dt = dt;
        Time += dt;

        Energy = Follow(Energy, Scale(audio.Level, sensitivity), 14, 3.5, dt);
        double bass = Scale(audio.Bass, sensitivity);
        Bass = Follow(Bass, bass, 30, 6, dt);
        Mid = Follow(Mid, Scale(audio.Mid, sensitivity), 22, 5, dt);
        Treble = Follow(Treble, Scale(audio.Treble, sensitivity), 30, 7, dt);
        double weight = 0, total = 0;
        for (int i = 0; i < Bands.Length; i++)
        {
            double raw = audio.Bands[i];
            weight += raw * i;
            total += raw;

            // Each band against its own recent peak: a quiet treble band then moves as much as a loud bass one.
            _peaks[i] = Math.Max(raw, _peaks[i] * Math.Exp(-dt / PeakMemorySeconds));
            double relative = raw / Math.Max(_peaks[i], PeakFloor);
            double shown = raw + ((Math.Min(1, relative) - raw) * balance);
            Bands[i] = (float)Follow(Bands[i], Scale(shown, sensitivity), 34, 6.5, dt);
        }

        // Only when something is playing; in a gap the tone stays where the music left it.
        if (total > 0.4)
        {
            double centroid = weight / total / (Bands.Length - 1);
            Tone = Follow(Tone, Math.Clamp((centroid - 0.15) / 0.55, 0, 1), 1.5, 1.5, dt);
        }

        // A beat is the bass jumping clear of what it has lately been, at most five times a second.
        _sinceBeat += dt;
        Beat = bass > 0.3 && bass > (_bassAverage * 1.28) + 0.04 && _sinceBeat > BeatCooldown;
        if (Beat)
        {
            _sinceBeat = 0;
        }

        _bassAverage += (bass - _bassAverage) * (1 - Math.Exp(-dt / 0.9));
        Kick = Beat ? 1 : Kick * Math.Exp(-dt * 6);
    }

    /// <summary>Moves <paramref name="current"/> towards <paramref name="target"/>, at the attack rate going up and the release rate going down (per second).</summary>
    public static double Follow(double current, double target, double attack, double release, double dt) =>
        current + ((target - current) * (1 - Math.Exp(-(target > current ? attack : release) * dt)));

    /// <summary>The band at a position from 0 (lowest pitch) to 1 (highest), blended between neighbours.</summary>
    public double BandAt(double position)
    {
        double x = Math.Clamp(position, 0, 1) * (Bands.Length - 1);
        int low = (int)Math.Floor(x);
        int high = Math.Min(Bands.Length - 1, low + 1);
        return Bands[low] + ((Bands[high] - Bands[low]) * (x - low));
    }

    private static double Scale(double value, double sensitivity) => Math.Clamp(value * sensitivity, 0, 1);
}

/// <summary>Where the animations get their colours: a rainbow that drifts, or one colour with a lighter edge.</summary>
internal sealed class Palette
{
    private readonly GlowColor? _single;

    private Palette(GlowColor? single) => _single = single;

    public static Palette Rainbow { get; } = new(null);

    public static Palette Of(GlowColor color) => new(color);

    /// <summary>The palette called <paramref name="name"/> in the options; <paramref name="accent"/> stands in for "Accent".</summary>
    public static Palette Named(string? name, GlowColor? accent)
    {
        if (string.Equals(name, "Accent", StringComparison.OrdinalIgnoreCase))
        {
            return Of(accent ?? GlowColor.Cyan);
        }

        if (string.Equals(name, "White", StringComparison.OrdinalIgnoreCase))
        {
            return Of(GlowColor.White);
        }

        return GlowColor.FromName(name) is { } color ? Of(color) : Rainbow;
    }

    /// <summary>The colour at <paramref name="position"/> (0..1, wrapping), moved on by <paramref name="drift"/> turns of the wheel.</summary>
    public GlowColor At(double position, double drift = 0)
    {
        if (_single is { } single)
        {
            // One colour, with a lighter shade towards the high end so a spectrum still has some shape.
            return single.Lerp(GlowColor.White, 0.4 * Math.Clamp(position, 0, 1));
        }

        double hue = (position * 0.75) + drift;
        return FromHsv(hue - Math.Floor(hue), 0.86, 1);
    }

    private static GlowColor FromHsv(double hue, double saturation, double value)
    {
        double h = hue * 6;
        int sector = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        double p = value * (1 - saturation);
        double q = value * (1 - (saturation * f));
        double t = value * (1 - (saturation * (1 - f)));
        (double r, double g, double b) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new GlowColor(ToByte(r), ToByte(g), ToByte(b));

        static byte ToByte(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
    }
}
