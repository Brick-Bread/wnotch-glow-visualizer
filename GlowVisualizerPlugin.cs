using System.Diagnostics;
using Notch.Core.Plugins;

namespace GlowVisualizer;

/// <summary>
/// Turns the glow around the pill into a music visualiser. It reads the loudness of the PC's sound
/// through <see cref="IPluginAudio"/> about 30 times a second, turns it into a <see cref="GlowFrame"/>
/// with one of several animations and hands it to <see cref="IPluginGlow"/>. While nothing is playing
/// it steps aside, and the glow goes back to what the activities ask for.
/// </summary>
public sealed class GlowVisualizerPlugin : INotchPlugin
{
    private const string AutoName = "Auto";

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    // Silence for this long, with the light faded out, and the plugin lets go of the glow.
    private const double SilenceBeforeRelease = 1.0;

    private readonly Lock _gate = new();
    private readonly Signal _signal = new();
    private IReadOnlyList<IStyle> _styles = [];
    private IPluginHost? _host;
    private Timer? _timer;
    private Stopwatch _clock = new();
    private TimeSpan _last;
    private string _styleName = AutoName;
    private Palette _palette = Palette.Rainbow;
    private double _sensitivity = 1;
    private double _balance = 0.6;
    private double _cycleSeconds = 20;
    private int _autoIndex;
    private double _autoTime;
    private double _silentFor;
    private bool _lit;

    public void Start(IPluginHost host)
    {
        _host = host;
        _styles = Styles.CreateAll();

        // Written back so the options show up in settings.json with their defaults.
        host.Settings.Set("style", host.Settings.Get("style", AutoName));
        host.Settings.Set("palette", host.Settings.Get("palette", "Rainbow"));
        host.Settings.Set("sensitivity", host.Settings.Get("sensitivity", 100));
        host.Settings.Set("balance", host.Settings.Get("balance", 60));
        host.Settings.Set("cycleSeconds", host.Settings.Get("cycleSeconds", 20));
        LoadOptions();

        // Changing an option in Notch's settings applies at once, without restarting the plugin.
        host.Settings.Changed += OnSettingChanged;
        host.Shell.Changed += OnShellChanged;

        _clock = Stopwatch.StartNew();
        _last = _clock.Elapsed;
        _timer = new Timer(_ => Step(), null, Tick, Tick);
    }

    public void Stop()
    {
        // Notch takes the frame and the audio listener away; only the timer is the plugin's own.
        _timer?.Dispose();
    }

    private void OnSettingChanged(object? sender, string key)
    {
        try
        {
            LoadOptions();
        }
        catch (Exception e)
        {
            _host?.Log.Error("Could not read the options.", e);
        }
    }

    private void OnShellChanged(object? sender, EventArgs e)
    {
        // The accent colour may have changed, and the palette follows it when asked to.
        try
        {
            LoadOptions();
        }
        catch (Exception ex)
        {
            _host?.Log.Error("Could not read the options.", ex);
        }
    }

    private void LoadOptions()
    {
        IPluginHost host = _host!;
        string style = host.Settings.Get("style", AutoName) ?? AutoName;
        string palette = host.Settings.Get("palette", "Rainbow") ?? "Rainbow";
        double sensitivity = Math.Clamp(host.Settings.Get("sensitivity", 100), 25, 300) / 100.0;
        double balance = Math.Clamp(host.Settings.Get("balance", 60), 0, 100) / 100.0;
        double cycle = Math.Clamp(host.Settings.Get("cycleSeconds", 20), 5, 300);

        lock (_gate)
        {
            _styleName = style;
            _palette = Palette.Named(palette, host.Shell.Accent);
            _sensitivity = sensitivity;
            _balance = balance;
            _cycleSeconds = cycle;
        }
    }

    private void Step()
    {
        // This runs on a timer thread, where an escaping exception would end the whole app.
        try
        {
            if (!_gate.TryEnter())
            {
                return;
            }

            try
            {
                Frame();
            }
            finally
            {
                _gate.Exit();
            }
        }
        catch (Exception e)
        {
            _host?.Log.Error("Could not draw the glow.", e);
        }
    }

    private void Frame()
    {
        IPluginHost host = _host!;
        TimeSpan now = _clock.Elapsed;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.1);
        _last = now;

        AudioFrame audio = host.Audio.Latest;
        _signal.Update(audio, dt, _sensitivity, _balance);

        _silentFor = audio.Level < 0.02 ? _silentFor + dt : 0;
        if (_silentFor > SilenceBeforeRelease && _signal.Energy < 0.02)
        {
            if (_lit)
            {
                host.Glow.Clear();
                _lit = false;
            }

            return;
        }

        // Nothing is drawn while the notch is expanded; the signal above keeps running so the beat is not lost.
        if (host.Shell.IsExpanded)
        {
            return;
        }

        host.Glow.Set(Current(dt).Render(_signal, _palette));
        _lit = true;
    }

    /// <summary>The style to draw: the chosen one, or in Auto the next in turn every <see cref="_cycleSeconds"/>.</summary>
    private IStyle Current(double dt)
    {
        if (!string.Equals(_styleName, AutoName, StringComparison.OrdinalIgnoreCase))
        {
            IStyle? chosen = _styles.FirstOrDefault(s => string.Equals(s.Name, _styleName, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null)
            {
                return chosen;
            }
        }

        _autoTime += dt;
        if (_autoTime >= _cycleSeconds)
        {
            _autoTime = 0;
            _autoIndex = (_autoIndex + 1) % _styles.Count;
        }

        return _styles[_autoIndex % _styles.Count];
    }
}
