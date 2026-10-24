namespace IndependentKnob.Tests;

public class RouterTests
{
    static Settings Make(TargetMode mode, params AppEntry[] apps) => new() { Mode = mode, Apps = [.. apps] };

    static readonly AppEntry Spotify = new("Spotify", "Spotify", Modifier.None);
    static readonly AppEntry Chrome = new("chrome", "Chrome", Modifier.Ctrl);
    static readonly AppEntry Discord = new("Discord", "Discord", Modifier.Shift);

    static Func<string, bool> Playing(params string[] names) => n => names.Contains(n);

    static KnobAction Run(Settings s, KnobEvent e, Func<string, bool> has, string? fg = null, int selected = 0, bool timed = false) =>
        Router.Route(s, new RouterState(selected), e, has, fg, timed).Action;

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
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "Spotify"), Run(s, new(KnobKind.Press), has, timed: true));
        Assert.Equal(new KnobAction(ActionKind.CycleNext, "chrome"), Run(s, new(KnobKind.Press), has, timed: false));
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
        // The timed (double/long press) flag is ignored in modifier mode.
        Assert.Equal(ActionKind.CycleNext, Run(s, new(KnobKind.Press), has, timed: true).Kind);
    }

    [Fact]
    public void Cycle_long_press_mutes_short_press_cycles_release_does_nothing()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome);
        s.MuteTrigger = MuteTrigger.LongPress;
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "Spotify"), Run(s, new(KnobKind.Press), has, timed: true));
        Assert.Equal(new KnobAction(ActionKind.CycleNext, "chrome"), Run(s, new(KnobKind.Press), has, timed: false));
        Assert.Equal(ActionKind.PassThrough, Run(s, new(KnobKind.Release), has, timed: true).Kind);
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
    public void Activation_key_gates_cycle_and_focused()
    {
        var has = Playing("Spotify", "chrome");
        var cycle = Make(TargetMode.Cycle, Spotify, Chrome);
        cycle.ActivationModifier = Modifier.Ctrl;
        Assert.Equal(ActionKind.PassThrough, Run(cycle, new(KnobKind.Up), has).Kind);
        Assert.Equal(ActionKind.PassThrough, Run(cycle, new(KnobKind.Press, Modifier.Shift), has).Kind);
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "Spotify"), Run(cycle, new(KnobKind.Up, Modifier.Ctrl), has));
        Assert.Equal(ActionKind.CycleNext, Run(cycle, new(KnobKind.Press, Modifier.Ctrl), has).Kind);

        var focused = Make(TargetMode.Focused, Spotify, Chrome);
        focused.ActivationModifier = Modifier.Alt;
        Assert.Equal(ActionKind.PassThrough, Run(focused, new(KnobKind.Up), has, fg: "chrome").Kind);
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "chrome"), Run(focused, new(KnobKind.Up, Modifier.Alt), has, fg: "chrome"));
    }

    [Fact]
    public void Activation_key_plus_mute_key_mutes()
    {
        var s = Make(TargetMode.Cycle, Spotify, Chrome);
        s.ActivationModifier = Modifier.Ctrl;
        s.MuteTrigger = MuteTrigger.ModifierPress;
        s.MuteModifier = Modifier.Shift;
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.ToggleMute, "Spotify"), Run(s, new(KnobKind.Press, Modifier.Ctrl | Modifier.Shift), has));
        Assert.Equal(ActionKind.CycleNext, Run(s, new(KnobKind.Press, Modifier.Ctrl), has).Kind);
    }

    [Fact]
    public void Modifiers_mode_ignores_activation_key_and_matches_exact_keys()
    {
        var s = Make(TargetMode.Modifiers, Spotify, Chrome);
        s.ActivationModifier = Modifier.Alt;
        var has = Playing("Spotify", "chrome");
        Assert.Equal(new KnobAction(ActionKind.VolumeUp, "Spotify"), Run(s, new(KnobKind.Up), has));
        Assert.Equal(ActionKind.PassThrough, Run(s, new(KnobKind.Up, Modifier.Ctrl | Modifier.Shift), has).Kind);
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
