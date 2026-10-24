using Avalonia;
using Avalonia.Controls;

namespace IndependantKnob;

// "Discord is using audio. Control it with the knob?" Bottom-right, above the tray; never takes focus.
// App wires AddButton / NotNowButton / NeverButton.
public partial class PromptWindow : Window
{
    // For the XAML previewer and runtime loader only.
    public PromptWindow() : this("Discord") { }

    public PromptWindow(string app)
    {
        InitializeComponent();
        Message.Text = $"{app} is using audio. Control it with the knob?";

        if (Screens.Primary is { } screen)
        {
            // WorkingArea is in physical pixels, Width/Height in DIPs.
            var area = screen.WorkingArea;
            double scale = screen.Scaling;
            Position = new PixelPoint(area.Right - (int)((Width + 16) * scale), area.Bottom - (int)((Height + 16) * scale));
        }
    }
}
