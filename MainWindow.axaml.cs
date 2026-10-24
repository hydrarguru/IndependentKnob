using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IndependantKnob;

// Edits the Settings object App routes with, so changes apply to the knob immediately; each edit is saved.
public partial class MainWindow : Window
{
    readonly Settings settings;
    readonly IAudio? audio;
    List<(string ProcessName, string DisplayName)> available = [];
    // Set while Refresh writes the controls, so their change events don't count as user edits.
    bool refreshing;

    // For the XAML previewer and runtime loader only; the app passes its live settings.
    public MainWindow() : this(new Settings(), null) { }

    public MainWindow(Settings settings, IAudio? audio)
    {
        this.settings = settings;
        this.audio = audio;
        InitializeComponent();

        ModeBox.SelectionChanged += (_, _) => Edit(() => settings.Mode = (TargetMode)ModeBox.SelectedIndex);
        MuteTriggerBox.SelectionChanged += (_, _) => Edit(() => settings.MuteTrigger = (MuteTrigger)MuteTriggerBox.SelectedIndex);
        MuteModifierBox.SelectionChanged += (_, _) => Edit(() => settings.MuteModifier = (Modifier)(MuteModifierBox.SelectedIndex + 1));
        DefaultAppBox.SelectionChanged += (_, _) => Edit(() =>
            settings.DefaultApp = DefaultAppBox.SelectedIndex > 0 ? settings.Apps[DefaultAppBox.SelectedIndex - 1].ProcessName : null);

        // Fresh list every time it opens, so newly started apps show up.
        AddBox.DropDownOpened += (_, _) =>
        {
            available = (audio?.Sessions() ?? []).Where(s => !settings.Apps.Any(a => Same(a.ProcessName, s.ProcessName))).ToList();
            AddBox.ItemsSource = available.Select(s => Label(s.ProcessName, s.DisplayName)).ToList();
        };
        AddBox.SelectionChanged += (_, _) => AddButton.IsEnabled = AddBox.SelectedIndex >= 0;
        AddButton.Click += (_, _) =>
        {
            var (process, display) = available[AddBox.SelectedIndex];
            AddBox.SelectedIndex = -1;
            Edit(() => settings.Apps.Add(new AppEntry(process, display)));
        };

        Refresh();
    }

    void Edit(Action change)
    {
        if (refreshing) return;
        change();
        try
        {
            settings.Save(Settings.DefaultPath);
            Status.IsVisible = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The change still applies to the knob; only saving it failed.
            Status.Text = $"Couldn't save settings: {e.Message}";
            Status.IsVisible = true;
        }
        Refresh();
    }

    void Refresh()
    {
        refreshing = true;
        ModeBox.SelectedIndex = (int)settings.Mode;
        MuteTriggerBox.SelectedIndex = (int)settings.MuteTrigger;
        MuteModifierBox.SelectedIndex = (int)settings.MuteModifier - 1;
        MuteModifierBox.IsVisible = settings.MuteTrigger == MuteTrigger.ModifierPress;
        CyclePanel.IsVisible = settings.Mode == TargetMode.Cycle;
        FocusedPanel.IsVisible = settings.Mode == TargetMode.Focused;

        DefaultAppBox.ItemsSource = settings.Apps.Select(a => a.DisplayName).Prepend("Nothing (use Windows volume)").ToList();
        DefaultAppBox.SelectedIndex = 1 + settings.Apps.FindIndex(a => Same(a.ProcessName, settings.DefaultApp)); // not found -> 0

        AppList.Children.Clear();
        foreach (var app in settings.Apps) AppList.Children.Add(Row(app));
        EmptyHint.IsVisible = settings.Apps.Count == 0;
        refreshing = false;
    }

    Control Row(AppEntry app)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        row.Children.Add(new TextBlock
        {
            Text = Label(app.ProcessName, app.DisplayName),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (settings.Mode == TargetMode.Modifiers)
        {
            // Item order matches the Modifier enum.
            var modifier = new ComboBox { ItemsSource = new[] { "No modifier", "Ctrl", "Shift", "Alt" }, SelectedIndex = (int)app.Modifier, Margin = new(8, 0, 0, 0) };
            modifier.SelectionChanged += (_, _) => Edit(() => settings.Apps[settings.Apps.IndexOf(app)] = app with { Modifier = (Modifier)modifier.SelectedIndex });
            Grid.SetColumn(modifier, 1);
            row.Children.Add(modifier);
        }

        var remove = new Button { Content = "Remove", Margin = new(8, 0, 0, 0) };
        remove.Click += (_, _) => Edit(() =>
        {
            settings.Apps.Remove(app);
            if (Same(app.ProcessName, settings.DefaultApp)) settings.DefaultApp = null;
        });
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        return row;
    }

    static string Label(string process, string display) => Same(process, display) ? display : $"{display} ({process})";

    static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
