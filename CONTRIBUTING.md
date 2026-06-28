# Contributing

Thanks for poking at Ember. It's a small, single-purpose **sleep app**, so the bar for any
change is simple: does it keep things calm, dim, and unobtrusive?

## Dev setup
- **.NET 10 SDK** + the MAUI Android workload: `dotnet workload install maui-android`
- **JDK 21** — newer JDKs fail the Android build with `error XA0030` ([#2](../../issues/2))
- A connected Android device (the headless emulator doesn't work here — [#3](../../issues/3))

Build, deploy to a device, and launch:

```bash
dotnet build AndroidApp1/AndroidApp1.csproj -t:Run -f net10.0-android \
  -p:JavaSdkDirectory=/path/to/jdk-21 -p:AdbTarget=-d
```

Debug builds use Fast Deployment, so deploy with `-t:Run` — **not** a bare `adb install`, which
crashes with *"No assemblies found…"*.

## Before you touch the UI
A few MAUI gesture/overlay traps bit us hard. Full notes are in [AGENTS.md](AGENTS.md); the short
list:
- Use **`IsVisible`** (not `InputTransparent`) to stop idle full-screen overlays eating taps.
- Don't put a `TapGestureRecognizer` on a container that holds `Button`s — it swallows their taps.
- "Tap anywhere to open" needs a real hit-testable **tap-catcher** view, not the root `Grid`.
- Don't pack 4+ default `Button`s into one row — the last one is silently dropped.

## Workflow
- Branch off `main`, open a PR.
- Verify on a real device and attach a screenshot. Note that **haptics**, the **sleep-timer
  fade**, and **backlight DIM** can't be confirmed by screenshot ([#1](../../issues/1)) — say how
  you checked them.
- Keep the aesthetic dim and low-contrast; no bright chrome, no extra reading.

## Open work
See the [issues](../../issues).
