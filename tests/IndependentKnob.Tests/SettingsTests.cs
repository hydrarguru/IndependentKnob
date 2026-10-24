namespace IndependentKnob.Tests;

public class SettingsTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("knob-test-").FullName;
    string Path => System.IO.Path.Combine(dir, "sub", "settings.json");

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = Settings.Load(Path);
        Assert.Equal(TargetMode.Cycle, s.Mode);
        Assert.Empty(s.Apps);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var s = new Settings
        {
            Mode = TargetMode.Modifiers,
            MuteTrigger = MuteTrigger.ModifierPress,
            MuteModifier = Modifier.Shift,
            Apps = [new("chrome", "Chrome", Modifier.Ctrl)],
            DefaultApp = "chrome",
            IgnoredApps = ["explorer"],
            ActivationModifier = Modifier.Alt,
            OverlayPosition = OverlayPosition.Right,
            OverlayColor = "#EC4899",
        };
        s.Save(Path);

        Assert.Contains("\"Modifiers\"", File.ReadAllText(Path)); // enums stored as names
        var loaded = Settings.Load(Path);
        Assert.Equal(TargetMode.Modifiers, loaded.Mode);
        Assert.Equal(MuteTrigger.ModifierPress, loaded.MuteTrigger);
        Assert.Equal(Modifier.Shift, loaded.MuteModifier);
        Assert.Equal(s.Apps, loaded.Apps);
        Assert.Equal("chrome", loaded.DefaultApp);
        Assert.Equal(["explorer"], loaded.IgnoredApps);
        Assert.Equal(Modifier.Alt, loaded.ActivationModifier);
        Assert.Equal(OverlayPosition.Right, loaded.OverlayPosition);
        Assert.Equal("#EC4899", loaded.OverlayColor);
    }

    [Fact]
    public void Suggests_first_app_not_listed_ignored_or_put_off()
    {
        var s = new Settings { Apps = [new("Spotify", "Spotify")], IgnoredApps = ["explorer"] };
        var notNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam" };
        (string, string)[] sessions = [("spotify", "Spotify"), ("Explorer", "Explorer"), ("STEAM", "Steam"), ("Discord", "Discord")];

        Assert.Equal(("Discord", "Discord"), s.NextSuggestion(sessions, notNow));
        Assert.Null(s.NextSuggestion(sessions[..3], notNow));
    }

    [Fact]
    public void Old_settings_folder_is_moved_to_new_name_but_never_over_an_existing_one()
    {
        string old = System.IO.Path.Combine(dir, "IndependantKnob"), current = System.IO.Path.Combine(dir, "IndependentKnob");
        Directory.CreateDirectory(old);
        File.WriteAllText(System.IO.Path.Combine(old, "settings.json"), "old");

        Settings.MoveFromOldName(dir);
        Assert.False(Directory.Exists(old));
        Assert.Equal("old", File.ReadAllText(System.IO.Path.Combine(current, "settings.json")));

        Directory.CreateDirectory(old);
        File.WriteAllText(System.IO.Path.Combine(old, "settings.json"), "older");
        Settings.MoveFromOldName(dir);
        Assert.Equal("old", File.ReadAllText(System.IO.Path.Combine(current, "settings.json")));
        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public void Hand_edited_nulls_are_cleaned_up_on_load()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, """
            { "Apps": [null, { "ProcessName": null }, { "ProcessName": "Spotify" }], "IgnoredApps": null }
            """);

        var s = Settings.Load(Path);
        Assert.Equal([new AppEntry("Spotify", "Spotify")], s.Apps); // missing display name falls back to the process name
        Assert.Empty(s.IgnoredApps);

        File.WriteAllText(Path, """{ "Apps": null }""");
        Assert.Empty(Settings.Load(Path).Apps);
    }

    [Fact]
    public void Unreadable_file_gives_defaults_instead_of_crashing()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, "{}");
        using (File.Open(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // locked by "another program"
            Assert.Empty(Settings.Load(Path).Apps);
    }

    [Fact]
    public void Adding_an_app_twice_lists_it_once_and_un_ignores_it()
    {
        var s = new Settings { IgnoredApps = ["discord"] };
        s.AddApp("Discord", "Discord");
        s.AddApp("discord", "Discord");
        Assert.Single(s.Apps);
        Assert.Empty(s.IgnoredApps);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_is_backed_up()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, "{ not json");

        Assert.Equal(TargetMode.Cycle, Settings.Load(Path).Mode);
        Assert.Equal("{ not json", File.ReadAllText(Path + ".bak"));
    }
}
