using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace IndependentKnob;

// Shows which app the knob changed and its new volume, bottom-center of the primary screen.
// Showing it never takes focus, so turning the knob while typing doesn't interrupt anything.
// It stays up while the pointer is over it, and dragging the slider sets the app's volume.
public partial class OverlayWindow : Window
{
    static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(180);
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    // Runs while the card fades out; the window is hidden when it fires. Stopped if the overlay comes back first.
    readonly DispatcherTimer fadeOutTimer = new() { Interval = FadeTime };
    readonly DoubleTransition fade = new() { Property = OpacityProperty, Duration = FadeTime };
    readonly TransformOperationsTransition zoom = new() { Property = RenderTransformProperty, Duration = FadeTime };
    // Set by App; null in the previewer, where dragging just moves the slider.
    public IAudio? Audio { get; init; }
    string? app; // process name of the app shown
    bool updating; // ShowState is setting the slider, so ValueChanged isn't a drag
    bool upright; // current layout; starts as the XAML's horizontal one

    public OverlayWindow()
    {
        InitializeComponent();
        Card.Transitions = [fade, zoom];
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Animate(visible: false);
            fadeOutTimer.Start();
        };
        fadeOutTimer.Tick += (_, _) =>
        {
            fadeOutTimer.Stop();
            Hide();
        };
        PointerEntered += (_, _) =>
        {
            hideTimer.Stop();
            if (fadeOutTimer.IsEnabled) Appear(); // caught it while fading out: bring it back
        };
        PointerExited += (_, _) => HideLater();
        Bar.ValueChanged += (_, _) =>
        {
            if (updating || app == null) return;
            if (Audio?.TrySet(app, (float)(Bar.Value / 100)) is { } state) ShowLevel(state);
            HideLater(); // the pointer can leave the window mid-drag; keep it up a while after
        };
    }

    public void ShowState(string displayName, string processName, AudioState state, IImage? icon, OverlayPosition position, string? color)
    {
        Arrange(upright: position is OverlayPosition.Left or OverlayPosition.Right);
        SetColor(color);
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
            // WorkingArea is in physical pixels, Width/Height in DIPs. Centered along the chosen edge, 48 DIPs in from it,
            // or in the middle of the screen.
            var area = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)(Width * scale), h = (int)(Height * scale), gap = (int)(48 * scale);
            int centerX = area.X + (area.Width - w) / 2, centerY = area.Y + (area.Height - h) / 2;
            Position = position switch
            {
                OverlayPosition.Top => new PixelPoint(centerX, area.Y + gap),
                OverlayPosition.Left => new PixelPoint(area.X + gap, centerY),
                OverlayPosition.Right => new PixelPoint(area.Right - w - gap, centerY),
                OverlayPosition.Center => new PixelPoint(centerX, centerY),
                _ => new PixelPoint(centerX, area.Bottom - h - gap),
            };
        }

        Appear();
        HideLater(); // restart, so the overlay stays up while the knob keeps turning
    }

    void Appear()
    {
        fadeOutTimer.Stop();
        Show();
        Animate(visible: true); // from wherever the card is: hidden, or partway through fading out
    }

    // Appearing slows down as it arrives, leaving speeds up as it goes, like Windows' own flyouts.
    void Animate(bool visible)
    {
        fade.Easing = zoom.Easing = visible ? new CubicEaseOut() : new CubicEaseIn();
        Card.Opacity = visible ? 1 : 0;
        Card.RenderTransform = TransformOperations.Parse(visible ? "scale(1)" : "scale(0.96)");
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

    // The Fluent slider's own brushes for the filled line and the dot, in every state, so hover and drag keep the colour.
    static readonly string[] SliderBrushes =
    [
        "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
    ];

    // null (or an unreadable value) leaves the theme's Windows accent colour.
    void SetColor(string? color)
    {
        Bar.Resources.Clear();
        if (color == null || !Color.TryParse(color, out var parsed)) return;
        var brush = new SolidColorBrush(parsed);
        foreach (var key in SliderBrushes) Bar.Resources[key] = brush;
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
