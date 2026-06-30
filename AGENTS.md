# AGENTS.md

Guidance for AI agents working in this repo.

## What this is
**Ember** — a .NET MAUI (Android) breathing pacer for sleep, ported from `ember.html`. The
animation is drawn with SkiaSharp; the UI is a tap-to-open control panel over a full-screen
ember. It's a **sleep app**, so the guiding aesthetic is dim, low-contrast, and minimal.

## Build & run (read this first)
- **JDK 21 is required.** Newer JDKs fail the Android build with `error XA0030`. Either copy
  `Directory.Build.local.props.example` to `Directory.Build.local.props` (git-ignored) and set
  `JavaSdkDirectory` there once, or pass `-p:JavaSdkDirectory=<jdk-21>` per build (on the
  original dev machine that was `/usr/lib/jvm/java-21-amazon-corretto`), or configure it in the
  IDE's Android settings.
- Needs the **MAUI Android workload**: `dotnet workload install maui-android`.
- Deploy and launch on a connected device:
  ```bash
  dotnet build Ember/Ember.csproj -t:Run -f net10.0-android \
    -p:JavaSdkDirectory=<jdk-21> -p:AdbTarget=-d
  ```
- **Debug = Fast Deployment.** Assemblies are pushed separately, so a bare `adb install` crashes
  with *"No assemblies found…"*. Always deploy via `-t:Run`. For a self-contained APK, build
  Release or add `-p:EmbedAssembliesIntoApk=true`.

## Environment notes
- **The headless Android emulator does not work in the original dev environment** — the guest
  kernel boots, but the host SwiftShader renderer segfaults during composition (reproducible
  across every `-gpu` mode, with and without the build sandbox). Use a **physical device** or a
  **windowed emulator backed by a real GPU**.
- `adb exec-out screencap -p` captures the framebuffer, **not** the physical backlight — so the
  real-backlight DIM behaviour can't be confirmed from a screenshot.

## Architecture
- `EmberRenderer.cs` — pure drawing/logic port of the JS canvas: `Rhythms`, `BreathState`,
  `Smooth`, the motes, and the linear/radial-gradient ember. `Draw(canvas, w, h, time, dt)` runs
  each frame.
- `MainPage.xaml(.cs)` — overlays (canvas, tap-catcher, hint, phase word, scrim, panel, info
  card, fade overlay) and all interaction: open/dismiss, rhythm/sleep selection, toggles,
  haptics, the sleep timer, persistence via `Preferences`, and Android backlight/fullscreen
  behind `#if ANDROID`.
- `MauiProgram.cs` / `App.xaml` — bootstrap, `UseSkiaSharp()`, font registration, palette.
- `Platforms/Android/` — `MainActivity`, `MainApplication`, `AndroidManifest.xml`.

## MAUI gesture/overlay gotchas (these cost real debugging time)
1. `InputTransparent="True"` on full-screen overlays **still swallows taps**. Toggle **`IsVisible`**
   instead so idle overlays don't hit-test.
2. A `TapGestureRecognizer` on a container (Border/Grid) **kills taps to child Buttons** — don't
   put one on the panel; a `Border` absorbs background taps anyway.
3. The root `Grid`'s tap recognizer won't fire over input-transparent children — use the
   dedicated near-transparent **tap-catcher** `BoxView` (`Color="#01000000"`) for "tap anywhere".
4. Four default `Button`s in one `HorizontalStackLayout` overflow the width and the last one is
   silently dropped — give overflow items their own row.
5. The `ⓘ` (U+24D8) glyph isn't in the bundled font; use a text label.

## Conventions
- Keep everything **dim, low-contrast, translucent**. No bright modal chrome, no extra reading,
  and keep the central ember unobstructed.
- Verify changes on a real device and screenshot. Remember that **haptics** (feel), the
  **sleep-timer fade** (needs minutes), and **backlight DIM** (framebuffer) can't be confirmed by
  screenshot alone.

## Recreating the README screenshots/GIFs (`docs/*.gif`)
The three `docs/*.gif` files are real on-device captures, not mockups, and they're shot with the
app's own **immersive fullscreen mode on** (status bar + nav bar both hidden via `SetFullscreen`)
— that's why there's no system UI in them and no blurring is needed. To redo one after a UI
change:

1. Deploy the current build to a connected device (see Build & run).
2. Turn fullscreen on: tap the screen to open the panel, then tap **"enter fullscreen"**. This
   reflows the panel (it gains the height back that the status/nav bar insets used to take), so
   get tap coordinates *after* this, not before.
3. Get exact tap coordinates instead of eyeballing them from a screenshot:
   `adb shell uiautomator dump /sdcard/ui.xml && adb pull /sdcard/ui.xml` dumps every view's
   `bounds="[x1,y1][x2,y2]"` (button text is in there too, so it's easy to find e.g. "what the
   rhythms mean"). The device used for the originals is 720×1600 (`adb shell wm size`); rescale
   if capturing on a different device. `adb` may not be on `PATH` — on the original dev machine
   it's `~/Android/Sdk/platform-tools/adb`.
4. Dismiss the panel (tap the scrim) to get back to the idle ember before capturing hero.
5. Capture with `adb shell screenrecord --bit-rate 12000000 --time-limit <secs> /sdcard/x.mp4`,
   then `adb pull`. Useful durations:
   - **hero** (idle ember, no UI): capture exactly one breath cycle so the GIF loops seamlessly —
     `Rhythm.Total` seconds (slow exhale = 14s, 4·7·8 = 19s, box = 16s). Check which rhythm is
     currently selected first: `adb shell run-as com.companyname.Ember sh -c 'cat
     /data/data/com.companyname.Ember/shared_prefs/*.xml'` (look at `rhythmIndex`). Any
     exactly-one-period window loops cleanly because the breath curve is a pure function of
     `time mod Total` — but the background motes drift independently of the phase clock, so
     they'll show a small jump at the seam regardless of trim point.
   - **panel**: the menu auto-dismisses 4.5s after it last sees interaction (`_hideTimer` in
     `MainPage.xaml.cs`), so keep the clip at ~4.5s or under or it'll catch the close-fade.
   - **info**: no auto-dismiss, so duration is flexible. Tap to open the panel, then tap the info
     button ~0.3–0.4s later (enough for layout) to catch the panel→info transition in one take.
6. Convert with `ffmpeg` — just trim to the window from step 5 and drop the frame rate to
   something GIF-reasonable, no blur filter needed since fullscreen mode already removed the
   status bar:
   ```bash
   ffmpeg -i in.mp4 -ss <start> -t <dur> -vf "fps=20" trimmed.mp4
   ffmpeg -i trimmed.mp4 -vf "palettegen=stats_mode=diff" palette.png
   ffmpeg -i trimmed.mp4 -i palette.png -lavfi "paletteuse=dither=sierra2_4a" -loop 0 out.gif
   ```
7. Sanity-check a couple of extracted frames before committing (first vs. last frame for hero,
   to confirm the loop seam lines up), then drop the result into `docs/` under the filename the
   README already references. Multi-MB GIFs are an accepted tradeoff here — don't downscale
   resolution/duration to chase a smaller file unless asked.
8. Afterwards, open the panel and tap **"exit fullscreen"** again so the device/app aren't left
   in an unusual state for whatever comes next.
