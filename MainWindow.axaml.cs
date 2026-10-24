using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace IndependentKnob;

// Edits the Settings object App routes with, so changes apply to the knob immediately; each edit is saved.
public partial class MainWindow : Window
{
    // Order of every modifier dropdown ("None"-less ones skip the first entry).
    static readonly Modifier[] Keys = [Modifier.None, Modifier.Ctrl, Modifier.Shift, Modifier.Alt];

    // Overlay slider colours; a null value follows the Windows accent colour.
    static readonly (string Name, string? Hex)[] Colors =
    [
        ("Windows accent", null), ("Blue", "#3B82F6"), ("Teal", "#14B8A6"), ("Green", "#22C55E"), ("Purple", "#A855F7"),
        ("Pink", "#EC4899"), ("Red", "#EF4444"), ("Orange", "#F97316"), ("Yellow", "#EAB308"), ("White", "#F5F5F5"),
    ];

    readonly Settings settings;
    readonly IAudio? audio;
    readonly IAppIcons? icons;
    // Set by App: briefly shows the overlay at the current position.
    public Action? PreviewOverlay { get; init; }
    // Set by App: called after every edit made in this window.
    public Action? SettingsChanged { get; init; }
    List<(string ProcessName, string DisplayName)> available = [];
    // Set while Refresh writes the controls, so their change events don't count as user edits.
    bool refreshing;

    // For the XAML previewer and runtime loader only; the app passes its live settings.
    public MainWindow() : this(new Settings(), null, null) { }

    public MainWindow(Settings settings, IAudio? audio, IAppIcons? icons)
    {
        this.settings = settings;
        this.audio = audio;
        this.icons = icons;
        InitializeComponent();
        AddBox.ItemTemplate = new FuncDataTemplate<(string ProcessName, string DisplayName)>((s, _) => AppLabel(s.ProcessName, s.DisplayName));

        ModeBox.SelectionChanged += (_, _) => Edit(() => settings.Mode = (TargetMode)ModeBox.SelectedIndex);
        MuteTriggerBox.SelectionChanged += (_, _) => Edit(() => settings.MuteTrigger = (MuteTrigger)MuteTriggerBox.SelectedIndex);
        MuteModifierBox.SelectionChanged += (_, _) => Edit(() => settings.MuteModifier = Keys[MuteModifierBox.SelectedIndex + 1]);
        ActivationBox.SelectionChanged += (_, _) => Edit(() => settings.ActivationModifier = Keys[ActivationBox.SelectedIndex]);
        OverlayPositionBox.SelectionChanged += (_, _) => Edit(() =>
        {
            settings.OverlayPosition = (OverlayPosition)OverlayPositionBox.SelectedIndex;
            PreviewOverlay?.Invoke(); // show the user where it will appear
        });
        OverlayColorBox.ItemTemplate = new FuncDataTemplate<(string Name, string? Hex)>((c, _) => Swatch(c.Name, c.Hex));
        OverlayColorBox.ItemsSource = Colors;
        OverlayColorBox.SelectionChanged += (_, _) => Edit(() =>
        {
            settings.OverlayColor = Colors[OverlayColorBox.SelectedIndex].Hex;
            PreviewOverlay?.Invoke(); // show the new colour
        });
        DefaultAppBox.SelectionChanged += (_, _) => Edit(() =>
            settings.DefaultApp = DefaultAppBox.SelectedIndex > 0 ? settings.Apps[DefaultAppBox.SelectedIndex - 1].ProcessName : null);

        // Fresh list every time it opens, so newly started apps show up.
        AddBox.DropDownOpened += (_, _) =>
        {
            available = (audio?.Sessions() ?? []).Where(s => !settings.Apps.Any(a => Same(a.ProcessName, s.ProcessName))).ToList();
            AddBox.ItemsSource = available;
        };
        AddBox.SelectionChanged += (_, _) => AddButton.IsEnabled = AddBox.SelectedIndex >= 0;
        AddButton.Click += (_, _) =>
        {
            var (process, display) = available[AddBox.SelectedIndex];
            AddBox.SelectedIndex = -1;
            Edit(() =>
            {
                settings.Apps.Add(new AppEntry(process, display));
                settings.IgnoredApps.RemoveAll(i => Same(i, process));
            });
        };

        Refresh();
    }

    void Edit(Action change)
    {
        if (refreshing) return;
        change();
        Changed(settings.TrySave(Settings.DefaultPath));
        SettingsChanged?.Invoke();
    }

    // Also called by App when the "add this app?" popup changes the settings.
    internal void Changed(string? saveError)
    {
        // On a save error the change still applies to the knob; only saving it failed.
        Status.Text = $"Couldn't save settings: {saveError}";
        Status.IsVisible = saveError != null;
        Refresh();
    }

    void Refresh()
    {
        refreshing = true;
        ModeBox.SelectedIndex = (int)settings.Mode;
        MuteTriggerBox.SelectedIndex = (int)settings.MuteTrigger;
        MuteModifierBox.SelectedIndex = Array.IndexOf(Keys, settings.MuteModifier) - 1;
        MuteModifierBox.IsVisible = settings.MuteTrigger == MuteTrigger.ModifierPress;
        ActivationBox.SelectedIndex = Array.IndexOf(Keys, settings.ActivationModifier);
        OverlayPositionBox.SelectedIndex = (int)settings.OverlayPosition;
        // A hand-edited colour that isn't a preset still shows on the overlay; the box then shows the accent entry.
        OverlayColorBox.SelectedIndex = Math.Max(0, Array.FindIndex(Colors, c => string.Equals(c.Hex, settings.OverlayColor, StringComparison.OrdinalIgnoreCase)));
        ActivationPanel.IsVisible = settings.Mode != TargetMode.Modifiers;
        // Router strips the hold key before checking the mute key, so the same key can never mute.
        KeyClash.IsVisible = settings.MuteTrigger == MuteTrigger.ModifierPress && settings.ActivationModifier == settings.MuteModifier;
        CyclePanel.IsVisible = settings.Mode == TargetMode.Cycle;
        FocusedPanel.IsVisible = settings.Mode == TargetMode.Focused;

        DefaultAppBox.ItemsSource = settings.Apps.Select(a => a.DisplayName).Prepend("Nothing (use Windows volume)").ToList();
        DefaultAppBox.SelectedIndex = 1 + settings.Apps.FindIndex(a => Same(a.ProcessName, settings.DefaultApp)); // not found -> 0

        AppList.Children.Clear();
        foreach (var app in settings.Apps) AppList.Children.Add(Row(app));
        EmptyHint.IsVisible = settings.Apps.Count == 0;

        IgnoredList.Children.Clear();
        foreach (var app in settings.IgnoredApps) IgnoredList.Children.Add(IgnoredRow(app));
        IgnoredPanel.IsVisible = settings.IgnoredApps.Count > 0;
        refreshing = false;
    }

    Control Row(AppEntry app)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        row.Children.Add(AppLabel(app.ProcessName, app.DisplayName));

        if (settings.Mode == TargetMode.Modifiers)
        {
            var modifier = new ComboBox { ItemsSource = new[] { "No modifier", "Ctrl", "Shift", "Alt" }, SelectedIndex = Array.IndexOf(Keys, app.Modifier), Width = 130, Margin = new(8, 0, 0, 0) }; // fixed width keeps the buttons in line
            modifier.SelectionChanged += (_, _) => Edit(() => settings.Apps[settings.Apps.IndexOf(app)] = app with { Modifier = Keys[modifier.SelectedIndex] });
            Grid.SetColumn(modifier, 1);
            row.Children.Add(modifier);
        }

        var remove = IconButton("TrashIcon", $"Remove {app.DisplayName}");
        remove.Click += (_, _) => Edit(() =>
        {
            settings.Apps.Remove(app);
            if (Same(app.ProcessName, settings.DefaultApp)) settings.DefaultApp = null;
            // Otherwise the popup would offer it again within seconds.
            settings.IgnoredApps.Add(app.ProcessName);
        });
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        return row;
    }

    Control IgnoredRow(string process)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(AppLabel(process, process));
        var askAgain = IconButton("UndoIcon", $"Suggest {process} again");
        askAgain.Click += (_, _) => Edit(() => settings.IgnoredApps.Remove(process));
        Grid.SetColumn(askAgain, 1);
        row.Children.Add(askAgain);
        return row;
    }

    // Logo (if the app has one we can read) and name. The logo column keeps its width either way, so names line up.
    Control AppLabel(string process, string display)
    {
        var label = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*"), VerticalAlignment = VerticalAlignment.Center };
        if (icons?.TryGet(process) is { } icon) label.Children.Add(new Image { Source = icon, Width = 20, Height = 20 });
        var name = new TextBlock { Text = Label(process, display), Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        label.Children.Add(name);
        return label;
    }

    // Colour dot and name for the overlay colour dropdown.
    Control Swatch(string name, string? hex)
    {
        IBrush fill = hex != null
            ? SolidColorBrush.Parse(hex)
            : new SolidColorBrush(this.FindResource("SystemAccentColor") is Color accent ? accent : Avalonia.Media.Colors.Gray);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new Ellipse { Width = 14, Height = 14, Fill = fill, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    // Icon-only button; the text goes in the tooltip and the screen-reader name.
    Button IconButton(string icon, string text)
    {
        var button = new Button { Content = new PathIcon { Data = (Geometry)this.FindResource(icon)! }, Margin = new(8, 0, 0, 0), Classes = { "icon" } };
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
        return button;
    }

    static string Label(string process, string display) => Same(process, display) ? display : $"{display} ({process})";

    static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
