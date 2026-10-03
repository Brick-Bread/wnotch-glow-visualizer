using Notch.Core.Activities;
using Notch.Core.Plugins;

namespace GlowVisualizer;

/// <summary>One way of turning the music into light. A style may keep state between frames.</summary>
internal interface IStyle
{
    /// <summary>The name used in the options.</summary>
    string Name { get; }

    GlowFrame Render(Signal signal, Palette palette);
}

internal static class Styles
{
    /// <summary>A fresh set of every style, in the order Auto goes through them.</summary>
    public static IReadOnlyList<IStyle> CreateAll() =>
    [
        new Pulse(), new Spectrum(), new Mirror(), new Wave(), new Comet(), new Fire(), new Strobe(), new Aurora(),
    ];

    /// <summary>The whole glow swells with the bass and kicks on every beat.</summary>
    private sealed class Pulse : IStyle
    {
        public string Name => "Pulse";

        public GlowFrame Render(Signal s, Palette palette)
        {
            double level = Math.Clamp((s.Bass * 0.6) + (s.Energy * 0.3) + (s.Kick * 0.4), 0, 1);
            return new GlowFrame
            {
                Color = palette.At(0.5, s.Time * 0.04),
                Intensity = 0.15 + (0.85 * level),
                Reach = 0.8 + (0.9 * level),
            };
        }
    }

    /// <summary>The spectrum along the pill: low notes on the left, high on the right.</summary>
    private sealed class Spectrum : IStyle
    {
        private const int Count = 14;

        public string Name => "Spectrum";

        public GlowFrame Render(Signal s, Palette palette)
        {
            var segments = new GlowSegment[Count];
            for (int i = 0; i < Count; i++)
            {
                double x = i / (double)(Count - 1);
                segments[i] = new GlowSegment(palette.At(x, s.Time * 0.02), 0.05 + (0.95 * s.BandAt(x)));
            }

            return new GlowFrame { Segments = segments, Reach = 1 + (0.4 * s.Energy) };
        }
    }

    /// <summary>The spectrum from the middle out: bass in the centre, treble at both ends.</summary>
    private sealed class Mirror : IStyle
    {
        private const int Count = 15;

        public string Name => "Mirror";

        public GlowFrame Render(Signal s, Palette palette)
        {
            var segments = new GlowSegment[Count];
            double middle = (Count - 1) / 2.0;
            for (int i = 0; i < Count; i++)
            {
                double distance = Math.Abs(i - middle) / middle;
                segments[i] = new GlowSegment(palette.At(distance, s.Time * 0.02), 0.05 + (0.95 * s.BandAt(distance * 0.9)));
            }

            return new GlowFrame { Segments = segments, Reach = 1 + (0.4 * s.Energy) };
        }
    }

    /// <summary>A ripple of light that runs along the pill, faster the louder it gets.</summary>
    private sealed class Wave : IStyle
    {
        private const int Count = 20;

        private double _phase;

        public string Name => "Wave";

        public GlowFrame Render(Signal s, Palette palette)
        {
            _phase += s.Dt * (0.4 + (2.4 * s.Energy) + (1.2 * s.Kick));
            double level = Math.Clamp((s.Energy * 0.8) + (s.Kick * 0.3), 0, 1);
            var segments = new GlowSegment[Count];
            for (int i = 0; i < Count; i++)
            {
                double x = i / (double)(Count - 1);
                double ripple = 0.5 + (0.5 * Math.Sin(2 * Math.PI * ((x * 1.4) - _phase)));
                segments[i] = new GlowSegment(palette.At(x * 0.6, _phase * 0.08), 0.08 + (0.92 * level * ripple));
            }

            return new GlowFrame { Segments = segments, Reach = 1 + (0.5 * level) };
        }
    }

    /// <summary>A bright head that sweeps back and forth leaving a fading tail; beats make it flare.</summary>
    private sealed class Comet : IStyle
    {
        private const int Count = 28;

        private readonly double[] _trail = new double[Count];
        private double _position = 0.1;
        private double _direction = 1;

        public string Name => "Comet";

        public GlowFrame Render(Signal s, Palette palette)
        {
            _position += _direction * s.Dt * (0.25 + (1.8 * s.Energy) + (0.6 * s.Kick));
            if (_position is > 1 or < 0)
            {
                _position = Math.Clamp(_position, 0, 1);
                _direction = -_direction;
            }

            double fade = Math.Exp(-s.Dt * 3.2);
            var segments = new GlowSegment[Count];
            int head = (int)Math.Round(_position * (Count - 1));
            for (int i = 0; i < Count; i++)
            {
                _trail[i] *= fade;
                double reach = Math.Abs(i - head) <= 1 ? 1 : 0;
                _trail[i] = Math.Max(_trail[i], reach * Math.Clamp(0.45 + (0.4 * s.Energy) + (0.3 * s.Kick), 0, 1));
                double x = i / (double)(Count - 1);
                segments[i] = new GlowSegment(palette.At(x, s.Time * 0.03), 0.04 + (0.96 * _trail[i]));
            }

            return new GlowFrame { Segments = segments, Reach = 1 + (0.4 * s.Kick) };
        }
    }

    /// <summary>Flames that flicker harder with the music. Uses its own colours, red to yellow.</summary>
    private sealed class Fire : IStyle
    {
        private const int Count = 16;

        private readonly Random _random = new(7);
        private readonly double[] _heat = new double[Count];
        private readonly double[] _target = new double[Count];

        public string Name => "Fire";

        public GlowFrame Render(Signal s, Palette palette)
        {
            var segments = new GlowSegment[Count];
            for (int i = 0; i < Count; i++)
            {
                // Each flame picks a new height now and then, about a dozen times a second.
                if (_random.NextDouble() < s.Dt * 12)
                {
                    _target[i] = Math.Clamp((s.Energy * (0.35 + (0.9 * _random.NextDouble()))) + (s.Kick * 0.35), 0, 1);
                }

                _heat[i] = Signal.Follow(_heat[i], _target[i], 28, 7, s.Dt);
                GlowColor color = GlowColor.Red.Lerp(GlowColor.Yellow, Math.Pow(_heat[i], 1.6));
                segments[i] = new GlowSegment(color, 0.08 + (0.92 * _heat[i]));
            }

            return new GlowFrame { Segments = segments, Reach = 1 + (0.6 * s.Energy) };
        }
    }

    /// <summary>A flash on every beat, in a new colour each time.</summary>
    private sealed class Strobe : IStyle
    {
        private double _hue;
        private double _flash;

        public string Name => "Strobe";

        public GlowFrame Render(Signal s, Palette palette)
        {
            if (s.Beat)
            {
                _flash = 1;
                _hue += 0.137;
            }
            else
            {
                _flash *= Math.Exp(-s.Dt * 8);
            }

            return new GlowFrame
            {
                Color = palette.At(_hue - Math.Floor(_hue)),
                Intensity = 0.08 + (0.92 * Math.Pow(_flash, 1.2)),
                Reach = 0.8 + (1.2 * _flash),
            };
        }
    }

    /// <summary>Slow curtains of colour that drift along the pill, brighter with the music.</summary>
    private sealed class Aurora : IStyle
    {
        private const int Count = 14;

        public string Name => "Aurora";

        public GlowFrame Render(Signal s, Palette palette)
        {
            double level = 0.25 + (0.75 * Math.Clamp(s.Energy + (s.Mid * 0.3), 0, 1));
            var segments = new GlowSegment[Count];
            for (int i = 0; i < Count; i++)
            {
                double shimmer = 0.55 + (0.45 * Math.Sin((s.Time * (0.5 + (i * 0.06))) + (i * 1.3)));
                double hue = 0.5 + (0.5 * Math.Sin((s.Time * 0.18) + (i * 0.4)));
                segments[i] = new GlowSegment(palette.At(hue, s.Time * 0.01), level * shimmer);
            }

            return new GlowFrame { Segments = segments, Reach = 1.2 };
        }
    }
}
