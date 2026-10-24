namespace IndependantKnob.Tests;

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
        };
        s.Save(Path);

        Assert.Contains("\"Modifiers\"", File.ReadAllText(Path)); // enums stored as names
        var loaded = Settings.Load(Path);
        Assert.Equal(TargetMode.Modifiers, loaded.Mode);
        Assert.Equal(MuteTrigger.ModifierPress, loaded.MuteTrigger);
        Assert.Equal(Modifier.Shift, loaded.MuteModifier);
        Assert.Equal(s.Apps, loaded.Apps);
        Assert.Equal("chrome", loaded.DefaultApp);
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
