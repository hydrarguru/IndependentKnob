using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace IndependantKnob;

// Shows which app the knob changed and its new volume, bottom-center of the primary screen.
// Showing it never takes focus, so turning the knob while typing doesn't interrupt anything.
// It stays up while the pointer is over it, and dragging the slider sets the app's volume.
public partial class OverlayWindow : Window
{
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    // Set by App; null in the previewer, where dragging just moves the slider.
    public IAudio? Audio { get; init; }
    string? app; // process name of the app shown
    bool updating; // ShowState is setting the slider, so ValueChanged isn't a drag

    public OverlayWindow()
    {
        InitializeComponent();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
        PointerEntered += (_, _) => hideTimer.Stop();
        PointerExited += (_, _) => HideLater();
        Bar.ValueChanged += (_, _) =>
        {
            if (updating || app == null) return;
            if (Audio?.TrySet(app, (float)(Bar.Value / 100)) is { } state) ShowLevel(state);
            HideLater(); // the pointer can leave the window mid-drag; keep it up a while after
        };
    }

    public void ShowState(string displayName, string processName, AudioState state, IImage? icon)
    {
        app = processName;
        AppIcon.Source = icon;
        AppIcon.IsVisible = icon != null;
        AppName.Text = displayName;
        updating = true;
        Bar.Value = Math.Round(state.Volume * 100);
        updating = false;
        ShowLevel(state);

        if (Screens.Primary is { } screen)
        {
            // WorkingArea is in physical pixels, Width/Height in DIPs.
            var area = screen.WorkingArea;
            double scale = screen.Scaling;
            Position = new PixelPoint(
                area.X + (int)((area.Width - Width * scale) / 2),
                area.Bottom - (int)((Height + 48) * scale));
        }

        Show();
        HideLater(); // restart, so the overlay stays up while the knob keeps turning
    }

    void ShowLevel(AudioState state)
    {
        Bar.Opacity = state.Muted ? 0.4 : 1;
        Level.Text = state.Muted ? "Muted" : $"{Math.Round(state.Volume * 100)}%";
    }

    void HideLater()
    {
        hideTimer.Stop();
        if (!IsPointerOver) hideTimer.Start();
    }
}
