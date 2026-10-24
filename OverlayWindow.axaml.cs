using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    bool upright; // current layout; starts as the XAML's horizontal one

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

    public void ShowState(string displayName, string processName, AudioState state, IImage? icon, OverlayPosition position)
    {
        Arrange(upright: position is OverlayPosition.Left or OverlayPosition.Right);
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
            // WorkingArea is in physical pixels, Width/Height in DIPs. Centered along the chosen edge, 48 DIPs in from it.
            var area = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)(Width * scale), h = (int)(Height * scale), gap = (int)(48 * scale);
            int centerX = area.X + (area.Width - w) / 2, centerY = area.Y + (area.Height - h) / 2;
            Position = position switch
            {
                OverlayPosition.Top => new PixelPoint(centerX, area.Y + gap),
                OverlayPosition.Left => new PixelPoint(area.X + gap, centerY),
                OverlayPosition.Right => new PixelPoint(area.Right - w - gap, centerY),
                _ => new PixelPoint(centerX, area.Bottom - h - gap),
            };
        }

        Show();
        HideLater(); // restart, so the overlay stays up while the knob keeps turning
    }

    // Upright (left/right edges): icon, name, vertical slider, percentage stacked. Otherwise the XAML's
    // row: icon beside name + percentage, slider underneath.
    void Arrange(bool upright)
    {
        if (this.upright == upright) return;
        this.upright = upright;
        Width = upright ? 132 : 380;
        Height = upright ? 320 : 92;
        Layout.ColumnDefinitions = new ColumnDefinitions(upright ? "*" : "Auto,*,Auto");
        Layout.RowDefinitions = new RowDefinitions(upright ? "Auto,Auto,*,Auto" : "Auto,Auto");
        Layout.VerticalAlignment = upright ? VerticalAlignment.Stretch : VerticalAlignment.Center;

        Place(AppIcon, upright ? (0, 0, 1, 1) : (0, 0, 2, 1));
        Place(AppName, upright ? (1, 0, 1, 1) : (0, 1, 1, 1));
        Place(Bar, upright ? (2, 0, 1, 1) : (1, 1, 1, 2));
        Place(Level, upright ? (3, 0, 1, 1) : (0, 2, 1, 1));
        AppIcon.Margin = upright ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 16, 0);
        AppName.Margin = upright ? new Thickness(0, 0, 0, 8) : default;
        Level.Margin = upright ? new Thickness(0, 8, 0, 0) : new Thickness(12, 0, 0, 0);
        AppName.HorizontalAlignment = Level.HorizontalAlignment = Bar.HorizontalAlignment =
            upright ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        Bar.Orientation = upright ? Orientation.Vertical : Orientation.Horizontal;
    }

    static void Place(Control control, (int Row, int Column, int RowSpan, int ColumnSpan) cell)
    {
        Grid.SetRow(control, cell.Row);
        Grid.SetColumn(control, cell.Column);
        Grid.SetRowSpan(control, cell.RowSpan);
        Grid.SetColumnSpan(control, cell.ColumnSpan);
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
