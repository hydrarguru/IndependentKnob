namespace IndependantKnob.Tests;

public class RouterTests
{
    static Settings Make(TargetMode mode, params AppEntry[] apps) => new() { Mode = mode, Apps = [.. apps] };

    static readonly AppEntry Spotify = new("Spotify", "Spotify", Modifier.None);
    static readonly AppEntry Chrome = new("chrome", "Chrome", Modifier.Ctrl);
    static readonly AppEntry Discord = new("Discord", "Discord", Modifier.Shift);

    static Func<string, bool> Playing(params string[] names) => n => names.Contains(n);

    static KnobAction Run(Settings s, KnobEvent e, Func<string, bool> has, string? fg = null, int selected = 0, bool second = false) =>
        Router.Route(s, new RouterState(selected), e, has, fg, second).Action;

    [Fact]
    public void Cycle_turn_adjusts_selected_app()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome);
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "chrome"), Run(s, new(KnobKind.Up), Playing("Spotify", "chrome"), selected: 1));
        Assert.Equal(new KnobAction(ActionKind.VolumeDown, "Spotify"), Run(s, new(KnobKind.Down), Playing("Spotify", "chrome")));
    }

    [Fact]
    public void No_session_or_no_apps_passes_through()
    {
        Assert.Equal(ActionKind.PassThrough, Run(Make(TargetMode.Cycle, Spotify), new(KnobKind.Up), Playing()).Kind);
        Assert.Equal(ActionKind.PassThrough, Run(Make(TargetMode.Cycle), new(KnobKind.Up), Playing("Spotify")).Kind);
        Assert.Equal(ActionKind.PassThrough, Run(Make(TargetMode.Cycle), new(KnobKind.Press), Playing("Spotify")).Kind);
    }

    [Fact]
    public void Cycle_press_skips_apps_without_session_and_wraps()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome, Discord);
        var (action, state) = Router.Route(s, new RouterState(0), new(KnobKind.Press), Playing("Spotify", "Discord"), null);
        Assert.Equal(new KnobAction(ActionKind.CycleNext, "Discord"), action);
        Assert.Equal(2, state.Selected);

        (action, state) = Router.Route(s, state, new(KnobKind.Press), Playing("Spotify", "Discord"), null);
        Assert.Equal(new KnobAction(ActionKind.CycleNext, "Spotify"), action);
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void Cycle_double_press_mutes_single_press_cycles()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome);
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "Spotify"), Run(s, new(KnobKind.Press), has, second: true));
        Assert.Equal(new KnobAction(ActionKind.CycleNext, "chrome"), Run(s, new(KnobKind.Press), has, second: false));
    }

    [Fact]
    public void Cycle_modifier_press_mutes_plain_press_cycles()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome);
        s.MuteTrigger = MuteTrigger.ModifierPress;
        s.MuteModifier = Modifier.Alt;
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "Spotify"), Run(s, new(KnobKind.Press, Modifier.Alt), has));
        Assert.Equal(ActionKind.CycleNext, Run(s, new(KnobKind.Press), has).Kind);
        // Double-press flag is ignored in modifier mode.
        Assert.Equal(ActionKind.CycleNext, Run(s, new(KnobKind.Press), has, second: true).Kind);
    }

    [Fact]
    public void Focused_uses_listed_foreground_app_else_default()
    {
        var s = Make(TargetMode.Focused, Spotify, Chrome);
        s.DefaultApp = "Spotify";
        var has = Playing("Spotify", "chrome");
        // Foreground match is case-insensitive and resolves to the listed name.
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "chrome"), Run(s, new(KnobKind.Up), has, fg: "CHROME"));
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "Spotify"), Run(s, new(KnobKind.Up), has, fg: "notepad"));
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "chrome"), Run(s, new(KnobKind.Press), has, fg: "chrome"));
        s.DefaultApp = null;
        Assert.Equal(ActionKind.PassThrough, Run(s, new(KnobKind.Up), has, fg: "notepad").Kind);
    }

    [Fact]
    public void Modifiers_picks_app_by_held_modifier()
    {
        var s = Make(TargetMode.Modifiers, Spotify, Chrome);
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "Spotify"), Run(s, new(KnobKind.Up), has));
        Assert.Equal(new KnobAction(ActionKind.VolumeDown, "chrome"), Run(s, new(KnobKind.Down, Modifier.Ctrl), has));
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "chrome"), Run(s, new(KnobKind.Press, Modifier.Ctrl), has));
        Assert.Equal(ActionKind.PassThrough, Run(s, new(KnobKind.Up, Modifier.Alt), has).Kind);
    }
}
