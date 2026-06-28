# AGENTS.md

Guidance for AI agents working in this repo.

## What this is
**Ember** — a .NET MAUI (Android) breathing pacer for sleep, ported from `ember.html`. The
animation is drawn with SkiaSharp; the UI is a tap-to-open control panel over a full-screen
ember. It's a **sleep app**, so the guiding aesthetic is dim, low-contrast, and minimal.

## Build & run (read this first)
- **JDK 21 is required.** Newer JDKs fail the Android build with `error XA0030`. Pass
  `-p:JavaSdkDirectory=<jdk-21>` (on the original dev machine that was
  `/usr/lib/jvm/java-21-amazon-corretto`), or configure it in the IDE's Android settings.
- Needs the **MAUI Android workload**: `dotnet workload install maui-android`.
- Deploy and launch on a connected device:
  ```bash
  dotnet build AndroidApp1/AndroidApp1.csproj -t:Run -f net10.0-android \
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
