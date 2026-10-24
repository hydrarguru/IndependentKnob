using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IndependentKnob.Windows;

namespace IndependentKnob;

public partial class App : Application
{
    const float Step = 0.02f; // same as the Windows volume keys

    Settings settings = new();
    RouterState state = new();
    IAudio? audio;
    IForeground? foreground;
    IAppIcons? icons;
    OverlayWindow? overlay;
    MainWindow? window;
    // Times presses for Cycle mode's double-press / press-and-hold; PressTiming decides what they mean.
    readonly DispatcherTimer pressTimer = new();
    PressTiming? presses;
    // Looks for audio apps that aren't listed yet and asks about them, one popup at a time.
    readonly DispatcherTimer scanTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    PromptWindow? prompt;
    readonly HashSet<string> notNow = new(StringComparer.OrdinalIgnoreCase); // until restart

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Lives in the tray: no window at startup, and closing the window doesn't quit. Only tray Exit does.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Settings.MoveFromOldName(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            settings = Settings.Load(Settings.DefaultPath);
            UpdateTrayTip();

            // A second copy of the app signals this instead of starting (see Program.Main).
            if (Program.OpenRequest is { } openRequest)
                new Thread(() =>
                {
                    while (openRequest.WaitOne()) Dispatcher.UIThread.Post(() => OnOpen(null, EventArgs.Empty));
                }) { IsBackground = true }.Start();

            // Other platforms get the UI only until they have their own IAudio/IKnobInput.
            if (OperatingSystem.IsWindows())
            {
                audio = new WindowsAudio();
                foreground = new WindowsForeground();
                icons = new WindowsIcons();
                overlay = new OverlayWindow { Audio = audio };

                presses = new PressTiming(settings, Apply, AnyListedPlaying,
                    startTimer: interval => { pressTimer.Interval = interval; pressTimer.Start(); }, stopTimer: pressTimer.Stop);
                pressTimer.Tick += (_, _) => presses.Expired();
                IKnobInput knob = new KeyboardHook();
                knob.Knob += presses.Knob;
                desktop.Exit += (_, _) => knob.Dispose();
                scanTimer.Tick += (_, _) => Suggest();
                scanTimer.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Closing the window just drops it; Open makes a fresh one, so there's no hide/cancel-close dance.
    void OnOpen(object? sender, EventArgs e)
    {
        if (window == null)
        {
            window = new MainWindow(settings, audio, icons) { PreviewOverlay = PreviewOverlay, SettingsChanged = UpdateTrayTip };
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        window.Activate();
    }

    string DisplayName(string app) => settings.Apps.FirstOrDefault(a => Settings.Same(a.ProcessName, app))?.DisplayName ?? app;

    // Process names with audio right now: one scan of the sound streams, checked as often as needed.
    // The keyboard hook must answer fast (Windows silently drops a slow hook), so it never scans once per app.
    HashSet<string> Playing() => audio!.Sessions().Select(s => s.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    bool AnyListedPlaying()
    {
        var playing = Playing();
        return settings.Apps.Exists(a => playing.Contains(a.ProcessName));
    }

    // Second line says what the knob controls: Cycle mode's selected app, or what the other modes follow.
    void UpdateTrayTip()
    {
        if (TrayIcon.GetIcons(this) is not [var tray, ..]) return;
        string? target = settings.Mode switch
        {
            TargetMode.Cycle => Router.Selected(settings, state) is { } app ? DisplayName(app) : null,
            TargetMode.Focused => "the app you're using",
            _ => "the app for the key you hold",
        };
        tray.ToolTipText = target == null ? "IndependentKnob" : $"IndependentKnob\nControlling: {target}";
    }

    // Shows the overlay as it's now set up (position, colour): with the first listed app that's playing, else a sample.
    void PreviewOverlay()
    {
        if (overlay == null) return; // no overlay on non-Windows yet
        var playing = Playing();
        var app = settings.Apps.FirstOrDefault(a => playing.Contains(a.ProcessName));
        if (app != null && audio!.TryGet(app.ProcessName) is { } state)
            overlay.ShowState(app.DisplayName, app.ProcessName, state, icons?.TryGet(app.ProcessName), settings.OverlayPosition, settings.OverlayColor);
        else // no process name: dragging the sample's slider changes nothing
            overlay.ShowState("Volume overlay", "", new AudioState(0.5f, false), null, settings.OverlayPosition, settings.OverlayColor);
    }

    void Suggest()
    {
        if (prompt != null || settings.NextSuggestion(audio!.Sessions(), notNow) is not { } app) return;
        prompt = new PromptWindow(app.DisplayName);
        prompt.AddButton.Click += (_, _) => Answer(() => settings.AddApp(app.ProcessName, app.DisplayName));
        prompt.NotNowButton.Click += (_, _) => Answer(() => notNow.Add(app.ProcessName));
        prompt.NeverButton.Click += (_, _) => Answer(() => settings.IgnoredApps.Add(app.ProcessName));
        prompt.Closed += (_, _) => prompt = null;
        prompt.Show();
    }

    void Answer(Action change)
    {
        change();
        string? error = settings.TrySave(Settings.DefaultPath);
        if (error != null) Debug.WriteLine($"Couldn't save settings: {error}");
        window?.Changed(error);
        UpdateTrayTip();
        prompt?.Close();
    }

    void OnExit(object? sender, EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    // Acts on a knob event (via PressTiming, mostly inside the keyboard hook): returns true to swallow the key.
    bool Apply(KnobEvent e, bool timedMute)
    {
        (var action, state) = Router.Route(settings, state, e, Playing().Contains, foreground!.ProcessName(), timedMute);
        AudioState? result = action.Kind switch
        {
            ActionKind.VolumeUp => audio!.TryAdjust(action.App!, Step),
            ActionKind.VolumeDown => audio!.TryAdjust(action.App!, -Step),
            ActionKind.ToggleMute => audio!.TryToggleMute(action.App!),
            ActionKind.CycleNext => audio!.TryGet(action.App!),
            _ => null,
        };
        if (action.Kind == ActionKind.CycleNext) Dispatcher.UIThread.Post(UpdateTrayTip);
        if (result != null)
        {
            string name = DisplayName(action.App!);
            string app = action.App!;
            // Posted, so the hook callback returns before the icon is looked up and the window is shown.
            Dispatcher.UIThread.Post(() => overlay!.ShowState(name, app, result, icons?.TryGet(app), settings.OverlayPosition, settings.OverlayColor));
        }
        // A null result means the session vanished between routing and acting; let Windows handle the key.
        return action.Kind == ActionKind.CycleNext || result != null;
    }
}
