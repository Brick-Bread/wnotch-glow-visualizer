# Glow visualizer

A plugin for [Brick-Bread/WNotch](https://github.com/Brick-Bread/WNotch) that turns the glow around the pill into a music visualiser. It listens to the loudness of whatever your PC is playing, by pitch, and draws it as light. Needs **WNotch 0.12 or newer** (plugin API 7).

## Animations

| Animation | Looks like |
|---|---|
| **Pulse** | The whole glow swells with the bass and kicks on every beat. |
| **Spectrum** | The spectrum along the pill: low notes on the left, high on the right. |
| **Mirror** | The spectrum from the middle out: bass in the centre, treble at both ends. |
| **Wave** | A ripple of light running along the pill, faster the louder it gets. |
| **Comet** | A bright head sweeping back and forth with a fading tail; beats make it flare. |
| **Fire** | Flames that flicker harder with the music, in their own red-to-yellow colours. |
| **Strobe** | A flash on every beat, in a new colour each time. |
| **Aurora** | Slow curtains of colour drifting along the pill. |
| **Auto** | Goes through all of them, changing every so often. |

## Follows the track's EQ

Two things respond to the balance of bass, mids and treble in what is playing:

- **The shape.** Each pitch band is judged against its own recent peak, so a quiet treble band moves as much as a loud bass one and you see the track's whole EQ profile, not just its bass. The **EQ balance** option sets how much (0 is each pitch at its true loudness).
- **The colour.** With the rainbow palette, the hue follows where the weight of the sound sits: bass-heavy music glows red and orange, bright music green through violet, and it shifts as the track's mood does.

## Options

Under the plugin in WNotch Settings: the animation, the colours (rainbow, your accent colour, or one colour), the EQ balance, the sensitivity (turn it up for quiet music, down if the glow sticks at full brightness) and, for Auto, how often it changes. Changes apply at once.

## Install

In WNotch Settings, under **Plugins**, enter `Brick-Bread/wnotch-glow-visualizer` and press **Install**, then save. Or find it in **Browse plugins**.

When nothing has played for a second the plugin steps aside and the normal glow comes back. Glow effects must be on in Settings, and nothing is drawn while the notch is expanded.

## Privacy

The plugin declares the `audio` permission. WNotch listens to the sound *output* (not a microphone) only while the plugin runs, and hands the plugin nothing but levels: overall loudness, bass, mid, treble and 16 pitch bands. The sound itself never reaches the plugin and nothing is recorded or sent anywhere.

## How it works

It is an example of the two pieces of plugin API 7 that make visualisers possible: `host.Audio` (loudness by pitch) and `host.Glow` (draw the light around the pill, optionally in coloured segments). See [the plugin guide](https://github.com/Brick-Bread/WNotch/blob/main/docs/plugins.md#the-glow-and-the-sound-visualisers).

- `Signal.cs` smooths the levels like light (fast to rise, slow to fall) and detects beats.
- `Styles.cs` has the animations, one small class each.
- `GlowVisualizerPlugin.cs` ties them together on a 30 Hz timer.

## Build

Requires the .NET 10 SDK and a WNotch build with API 7 or newer. The default reference is the installed `%LocalAppData%\Programs\Notch\Notch.Core.dll`.

```powershell
.\build.ps1
```

For another installation or a source build: `.\build.ps1 -NotchCorePath C:\path\to\Notch.Core.dll`. To try it without installing, quit Notch and run `Notch.exe --plugin=dist\brick-bread.glow-visualizer`.

## Releasing

Raise `version` in `plugin.json`, push a tag `v<version>`; the workflow builds the plugin and attaches the single zip to a release.

MIT licensed.
