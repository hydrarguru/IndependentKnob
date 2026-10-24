using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace IndependantKnob;

// Shows which app the knob changed and its new volume, bottom-center of the primary screen.
// Never takes focus, so turning the knob while typing doesn't interrupt anything.
public partial class OverlayWindow : Window
{
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public OverlayWindow()
    {
        InitializeComponent();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    public void ShowState(string app, AudioState state)
    {
        AppName.Text = app;
        Bar.Value = Math.Round(state.Volume * 100);
        Bar.Opacity = state.Muted ? 0.4 : 1;
        Level.Text = state.Muted ? "Muted" : $"{Bar.Value}%";

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
        hideTimer.Stop(); // restart, so the overlay stays up while the knob keeps turning
        hideTimer.Start();
    }
}
