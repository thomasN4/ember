using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using SkiaSharp.Views.Maui;

namespace Ember;

public partial class MainPage : ContentPage
{
    private readonly EmberRenderer _renderer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Button> _rhythmButtons = new();
    private readonly List<Button> _sleepButtons = new();

    private IDispatcherTimer? _frameTimer;
    private IDispatcherTimer? _hideTimer;
    private IDispatcherTimer? _sleepTimer;

    private double _lastFrameSeconds;
    private string _lastPhaseName = "";

    private bool _initializing = true;
    private bool _menuOpen;
    private bool _infoOpen;
    private bool _fadedOut;

    // persisted settings
    private double _dim = 0.85;
    private bool _showWords = true;
    private bool _haptics;
    private int _sleepMinutes;     // 0 = off (∞)
    private bool _isFullscreen;

    private static readonly int[] SleepOptions = { 0, 10, 20, 30 };

    private static Color Candle => (Color)Application.Current!.Resources["Candle"];
    private static Color AmberDim => (Color)Application.Current!.Resources["AmberDim"];

    public MainPage()
    {
        InitializeComponent();
        LoadPrefs();
        BuildRhythmButtons();
        BuildSleepButtons();
        ApplyInitialState();
        _initializing = false;
    }

    // ---------- settings ----------
    private void LoadPrefs()
    {
        _dim = Preferences.Get("dim", 0.85);
        _showWords = Preferences.Get("showWords", true);
        _haptics = Preferences.Get("haptics", false);
        _sleepMinutes = Preferences.Get("sleepMinutes", 0);
    }

    private void ApplyInitialState()
    {
        Dimmer.Value = _dim;            // OnDimChanged is guarded by _initializing
        _renderer.Dim = _dim;
        WordsButton.Text = _showWords ? "hide words" : "show words";
        HapticsButton.Text = _haptics ? "haptics on" : "haptics off";
    }

    // ---------- lifecycle ----------
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // keep the screen awake during a session (replaces the web wakeLock)
        try { DeviceDisplay.Current.KeepScreenOn = true; } catch { /* best effort */ }

        // ~60fps render loop
        _frameTimer = Dispatcher.CreateTimer();
        _frameTimer.Interval = TimeSpan.FromMilliseconds(16);
        _frameTimer.Tick += (_, _) => Canvas.InvalidateSurface();
        _frameTimer.Start();

        // idle backstop that dismisses the panel
        _hideTimer = Dispatcher.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(4500);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => DismissMenu();

        // sleep timer (one-shot)
        _sleepTimer = Dispatcher.CreateTimer();
        _sleepTimer.IsRepeating = false;
        _sleepTimer.Tick += (_, _) => BeginSleepFade();

        SetBacklight(MapBrightness(_dim));
        RestartSleepTimer();

        _ = ShowHintAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _frameTimer?.Stop();
        _hideTimer?.Stop();
        _sleepTimer?.Stop();
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch { /* best effort */ }
        SetBacklight(-1f); // restore system brightness on the way out
    }

    // ---------- chips ----------
    private static Button MakeChip(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Padding = new Thickness(12, 7),
        CornerRadius = 16,
        BorderWidth = 1,
        BackgroundColor = Colors.Transparent,
    };

    private static void StyleChip(Button b, bool on)
    {
        b.TextColor = on ? Candle : AmberDim;
        b.BorderColor = on ? Candle : AmberDim;
        b.BackgroundColor = on ? Color.FromRgba(212, 149, 88, 40) : Colors.Transparent;
    }

    private void BuildRhythmButtons()
    {
        int sel = Math.Clamp(Preferences.Get("rhythmIndex", 0), 0, EmberRenderer.Rhythms.Length - 1);
        for (int i = 0; i < EmberRenderer.Rhythms.Length; i++)
        {
            var r = EmberRenderer.Rhythms[i];
            var btn = MakeChip($"{r.Name}  {r.Cadence}");
            int idx = i;
            btn.Clicked += (_, _) => { SelectRhythm(idx); KeepOpen(); };
            _rhythmButtons.Add(btn);
            RhythmRow.Add(btn);
        }
        SelectRhythm(sel);
    }

    private void SelectRhythm(int index)
    {
        _renderer.Rhythm = EmberRenderer.Rhythms[index];
        for (int i = 0; i < _rhythmButtons.Count; i++)
            StyleChip(_rhythmButtons[i], i == index);
        Preferences.Set("rhythmIndex", index);
    }

    private void BuildSleepButtons()
    {
        foreach (var m in SleepOptions)
        {
            var btn = MakeChip(m == 0 ? "∞" : $"{m}m");
            btn.Padding = new Thickness(10, 6);
            btn.FontSize = 11;
            int minutes = m;
            btn.Clicked += (_, _) => { SelectSleep(minutes); KeepOpen(); };
            _sleepButtons.Add(btn);
            SleepRow.Add(btn);
        }
        SelectSleep(_sleepMinutes, persist: false, restart: false);
    }

    private void SelectSleep(int minutes, bool persist = true, bool restart = true)
    {
        _sleepMinutes = minutes;
        for (int i = 0; i < _sleepButtons.Count; i++)
            StyleChip(_sleepButtons[i], SleepOptions[i] == minutes);
        if (persist) Preferences.Set("sleepMinutes", minutes);
        if (restart) RestartSleepTimer();
    }

    // ---------- render ----------
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = _lastFrameSeconds == 0 ? 0 : now - _lastFrameSeconds;
        _lastFrameSeconds = now;

        var info = e.Info;
        _renderer.Draw(e.Surface.Canvas, info.Width, info.Height, now, dt);

        if (_renderer.PhaseName != _lastPhaseName)
        {
            _lastPhaseName = _renderer.PhaseName;
            OnPhaseChanged(_renderer.PhaseName);
        }
    }

    private void OnPhaseChanged(string name)
    {
        if (_showWords && !_menuOpen && !_infoOpen)
            AnimatePhase(name);

        if (_haptics && (name == "breathe in" || name == "let go"))
            Buzz(name == "breathe in" ? 60 : 40);   // a touch longer on the inhale
    }

    private async void AnimatePhase(string text)
    {
        await PhaseLabel.FadeToAsync(0, 400);
        PhaseLabel.Text = text;
        await PhaseLabel.FadeToAsync(0.85, 400);
    }

    private static void Buzz(int ms)
    {
        try { if (Vibration.Default.IsSupported) Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(ms)); }
        catch { /* best effort */ }
    }

    // ---------- menu open / dismiss ----------
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_fadedOut) { WakeSession(); return; }
        OpenMenu();
    }

    private void OpenMenu()
    {
        if (_infoOpen) return;
        RestartSleepTimer();   // any interaction means the user is awake
        if (!_menuOpen)
        {
            _menuOpen = true;
            Scrim.IsVisible = true;
            Panel.IsVisible = true;
            _ = Scrim.FadeToAsync(1, 250);
            _ = Panel.FadeToAsync(1, 250);
            _ = PhaseLabel.FadeToAsync(0, 250);
        }
        ResetIdle();
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => DismissMenu();

    private void KeepOpen()
    {
        RestartSleepTimer();
        ResetIdle();
    }

    private void ResetIdle()
    {
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private async void DismissMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false;
        _hideTimer?.Stop();
        if (_showWords && !_infoOpen) _ = PhaseLabel.FadeToAsync(0.85, 350);
        await Task.WhenAll(Scrim.FadeToAsync(0, 350), Panel.FadeToAsync(0, 350));
        if (!_menuOpen)   // collapse only if not reopened mid-fade
        {
            Scrim.IsVisible = false;
            Panel.IsVisible = false;
        }
    }

    // ---------- info card ----------
    private void OnInfoClicked(object? sender, EventArgs e)
    {
        DismissMenu();
        _infoOpen = true;
        InfoOverlay.IsVisible = true;
        _ = InfoOverlay.FadeToAsync(1, 250);
        _ = PhaseLabel.FadeToAsync(0, 200);
    }

    private async void OnInfoTapped(object? sender, TappedEventArgs e)
    {
        if (!_infoOpen) return;
        _infoOpen = false;
        RestartSleepTimer();
        if (_showWords) _ = PhaseLabel.FadeToAsync(0.85, 300);
        await InfoOverlay.FadeToAsync(0, 300);
        if (!_infoOpen) InfoOverlay.IsVisible = false;
    }

    // ---------- toggles ----------
    private void OnDimChanged(object? sender, ValueChangedEventArgs e)
    {
        _dim = e.NewValue;
        _renderer.Dim = _dim;
        SetBacklight(MapBrightness(_dim));
        Preferences.Set("dim", _dim);
        if (!_initializing) KeepOpen();
    }

    private void OnToggleWords(object? sender, EventArgs e)
    {
        _showWords = !_showWords;
        WordsButton.Text = _showWords ? "hide words" : "show words";
        Preferences.Set("showWords", _showWords);
        KeepOpen();
    }

    private void OnToggleHaptics(object? sender, EventArgs e)
    {
        _haptics = !_haptics;
        HapticsButton.Text = _haptics ? "haptics on" : "haptics off";
        Preferences.Set("haptics", _haptics);
        if (_haptics) Buzz(50);   // confirmation pulse
        KeepOpen();
    }

    private void OnToggleFullscreen(object? sender, EventArgs e)
    {
        _isFullscreen = !_isFullscreen;
        SetFullscreen(_isFullscreen);
        FullscreenButton.Text = _isFullscreen ? "exit fullscreen" : "enter fullscreen";
        KeepOpen();
    }

    // ---------- sleep timer ----------
    private void RestartSleepTimer()
    {
        if (_sleepTimer is null) return;
        _sleepTimer.Stop();
        if (_sleepMinutes > 0)
        {
            _sleepTimer.Interval = TimeSpan.FromMinutes(_sleepMinutes);
            _sleepTimer.Start();
        }
    }

    private async void BeginSleepFade()
    {
        _fadedOut = true;
        DismissMenu();
        SetBacklight(0f);
        FadeOverlay.IsVisible = true;
        await FadeOverlay.FadeToAsync(1, 45000);   // gentle ~45s fade to black
        _frameTimer?.Stop();
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch { /* best effort */ }
    }

    private void OnFadeTapped(object? sender, TappedEventArgs e) => WakeSession();

    private async void WakeSession()
    {
        if (!_fadedOut) return;
        _fadedOut = false;
        try { DeviceDisplay.Current.KeepScreenOn = true; } catch { /* best effort */ }
        SetBacklight(MapBrightness(_dim));
        _frameTimer?.Start();
        RestartSleepTimer();
        await FadeOverlay.FadeToAsync(0, 600);
        if (!_fadedOut) FadeOverlay.IsVisible = false;
    }

    // ---------- platform: backlight + fullscreen ----------
    private static float MapBrightness(double dim)
    {
        // slider 0.25..1.0  ->  window backlight 0.02..0.85
        double t = Math.Clamp((dim - 0.25) / 0.75, 0, 1);
        return (float)(0.02 + t * (0.85 - 0.02));
    }

    private static void SetBacklight(float level)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity?.Window is not { } window) return;
        activity.RunOnUiThread(() =>
        {
            var attrs = window.Attributes;
            if (attrs is null) return;
            attrs.ScreenBrightness = level;   // -1 = system default
            window.Attributes = attrs;
        });
#endif
    }

    private static void SetFullscreen(bool on)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity?.Window is not { } window) return;

        var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is null) return;
        AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, !on);
        var systemBars = AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars();
        if (on)
        {
            controller.Hide(systemBars);
            controller.SystemBarsBehavior =
                AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
        else
        {
            controller.Show(systemBars);
        }
#endif
    }

    private async Task ShowHintAsync()
    {
        await Task.Delay(800);
        await HintLabel.FadeToAsync(0.7, 1000);
        await Task.Delay(5000);
        await HintLabel.FadeToAsync(0, 2000);
    }
}
