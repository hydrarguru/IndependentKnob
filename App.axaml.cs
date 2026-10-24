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
using IndependantKnob.Windows;

namespace IndependantKnob;

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
    // Cycle + DoublePress/LongPress: the press waits here while it's timed.
    // DoublePress: a second press before the timer fires mutes. LongPress: still held when it fires mutes.
    readonly DispatcherTimer pressTimer = new();
    KnobEvent? pendingPress;
    static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(300), HoldTime = TimeSpan.FromMilliseconds(500);
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

                var hook = new KeyboardHook();
                hook.Knob += OnKnob;
                IDisposable unhook = hook; // via the interface: the analyzer can't see the IsWindows guard inside the lambda
                desktop.Exit += (_, _) => unhook.Dispose();
                pressTimer.Tick += (_, _) =>
                {
                    pressTimer.Stop();
                    // DoublePress: no second press came, so switch app. LongPress: still held, so mute.
                    if (pendingPress != null) Apply(pendingPress, timedMute: settings.MuteTrigger == MuteTrigger.LongPress);
                };
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

    string DisplayName(string app) =>
        settings.Apps.FirstOrDefault(a => string.Equals(a.ProcessName, app, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? app;

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
        tray.ToolTipText = target == null ? "IndependantKnob" : $"IndependantKnob\nControlling: {target}";
    }

    // Shows the overlay where it's now set to appear: with the first listed app that's playing, else a sample.
    void PreviewOverlay()
    {
        if (overlay == null) return; // no overlay on non-Windows yet
        var app = settings.Apps.FirstOrDefault(a => audio!.HasSession(a.ProcessName));
        if (app != null && audio!.TryGet(app.ProcessName) is { } state)
            overlay.ShowState(app.DisplayName, app.ProcessName, state, icons?.TryGet(app.ProcessName), settings.OverlayPosition);
        else // no process name: dragging the sample's slider changes nothing
            overlay.ShowState("Volume overlay", "", new AudioState(0.5f, false), null, settings.OverlayPosition);
    }

    void Suggest()
    {
        if (prompt != null || settings.NextSuggestion(audio!.Sessions(), notNow) is not { } app) return;
        prompt = new PromptWindow(app.DisplayName);
        prompt.AddButton.Click += (_, _) => Answer(() => settings.Apps.Add(new AppEntry(app.ProcessName, app.DisplayName)));
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

    // Runs inside the keyboard hook: returns true to swallow the key.
    bool OnKnob(KnobEvent e)
    {
        if (e.Kind == KnobKind.Release)
        {
            // LongPress: let go before the hold time, so it was a short press: switch app.
            if (settings.MuteTrigger == MuteTrigger.LongPress && pressTimer.IsEnabled)
            {
                pressTimer.Stop();
                Apply(pendingPress!, timedMute: false);
            }
            return false; // key-ups are swallowed or not to match their key-down; the hook decides
        }

        if (settings.Mode == TargetMode.Cycle && e.Kind == KnobKind.Press && settings.MuteTrigger != MuteTrigger.ModifierPress
            && Router.Active(settings, e))
        {
            if (pressTimer.IsEnabled) // only DoublePress gets here: the hook folds a held button's repeats into one press
            {
                pressTimer.Stop();
                return Apply(e, timedMute: true);
            }
            // The hook must answer now, before we know what kind of press this is: swallow only if there's an app to act on.
            if (!settings.Apps.Any(a => audio!.HasSession(a.ProcessName))) return false;
            pendingPress = e;
            pressTimer.Interval = settings.MuteTrigger == MuteTrigger.LongPress ? HoldTime : DoublePressWindow;
            pressTimer.Start();
            return true;
        }
        return Apply(e, timedMute: false);
    }

    bool Apply(KnobEvent e, bool timedMute)
    {
        (var action, state) = Router.Route(settings, state, e, audio!.HasSession, foreground!.ProcessName(), timedMute);
        AudioState? result = action.Kind switch
        {
            ActionKind.VolumeUp => audio.TryAdjust(action.App!, Step),
            ActionKind.VolumeDown => audio.TryAdjust(action.App!, -Step),
            ActionKind.ToggleMute => audio.TryToggleMute(action.App!),
            ActionKind.CycleNext => audio.TryGet(action.App!),
            _ => null,
        };
        if (action.Kind == ActionKind.CycleNext) Dispatcher.UIThread.Post(UpdateTrayTip);
        if (result != null)
        {
            string name = DisplayName(action.App!);
            string app = action.App!;
            // Posted, so the hook callback returns before the icon is looked up and the window is shown.
            Dispatcher.UIThread.Post(() => overlay!.ShowState(name, app, result, icons?.TryGet(app), settings.OverlayPosition));
        }
        // A null result means the session vanished between routing and acting; let Windows handle the key.
        return action.Kind == ActionKind.CycleNext || result != null;
    }
}
