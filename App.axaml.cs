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
    OverlayWindow? overlay;
    MainWindow? window;
    // Cycle + DoublePress: the first press waits here; a second press inside the window mutes instead.
    readonly DispatcherTimer pressTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    KnobEvent? pendingPress;
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
                overlay = new OverlayWindow();

                var hook = new KeyboardHook();
                hook.Knob += OnKnob;
                IDisposable unhook = hook; // via the interface: the analyzer can't see the IsWindows guard inside the lambda
                desktop.Exit += (_, _) => unhook.Dispose();
                pressTimer.Tick += (_, _) =>
                {
                    pressTimer.Stop();
                    if (pendingPress != null) Apply(pendingPress, secondPress: false);
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
            window = new MainWindow(settings, audio);
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        window.Activate();
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
        prompt?.Close();
    }

    void OnExit(object? sender, EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    // Runs inside the keyboard hook: returns true to swallow the key.
    bool OnKnob(KnobEvent e)
    {
        if (settings.Mode == TargetMode.Cycle && e.Kind == KnobKind.Press && settings.MuteTrigger == MuteTrigger.DoublePress)
        {
            if (pressTimer.IsEnabled)
            {
                pressTimer.Stop();
                return Apply(e, secondPress: true);
            }
            // The hook must answer now, before we know if a second press is coming: swallow only if there's an app to act on.
            if (!settings.Apps.Any(a => audio!.HasSession(a.ProcessName))) return false;
            pendingPress = e;
            pressTimer.Start();
            return true;
        }
        return Apply(e, secondPress: false);
    }

    bool Apply(KnobEvent e, bool secondPress)
    {
        (var action, state) = Router.Route(settings, state, e, audio!.HasSession, foreground!.ProcessName(), secondPress);
        AudioState? result = action.Kind switch
        {
            ActionKind.VolumeUp => audio.TryAdjust(action.App!, Step),
            ActionKind.VolumeDown => audio.TryAdjust(action.App!, -Step),
            ActionKind.ToggleMute => audio.TryToggleMute(action.App!),
            ActionKind.CycleNext => audio.TryGet(action.App!),
            _ => null,
        };
        if (result != null)
        {
            string name = settings.Apps.FirstOrDefault(a => string.Equals(a.ProcessName, action.App, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? action.App!;
            // Posted, so the hook callback returns before the window is shown.
            Dispatcher.UIThread.Post(() => overlay!.ShowState(name, result));
        }
        // A null result means the session vanished between routing and acting; let Windows handle the key.
        return action.Kind == ActionKind.CycleNext || result != null;
    }
}
