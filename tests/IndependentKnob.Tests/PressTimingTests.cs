namespace IndependentKnob.Tests;

public class PressTimingTests
{
    // Records what PressTiming asks App to do; route swallows (true) like a real app action would.
    sealed class Harness
    {
        public readonly List<(KnobEvent Press, bool TimedMute)> Routed = [];
        public TimeSpan? Timer; // running timer's interval, null when stopped
        public bool Playing = true;
        public readonly PressTiming Presses;

        public Harness(Settings settings) => Presses = new PressTiming(settings,
            (e, timed) => { Routed.Add((e, timed)); return true; }, () => Playing,
            interval => Timer = interval, () => Timer = null);
    }

    static Settings Cycle(MuteTrigger trigger, Modifier hold = Modifier.None) =>
        new() { Mode = TargetMode.Cycle, MuteTrigger = trigger, ActivationModifier = hold, Apps = [new("Spotify", "Spotify")] };

    static readonly KnobEvent Press = new(KnobKind.Press), Release = new(KnobKind.Release);

    [Fact]
    public void Double_press_mutes_single_press_switches_after_the_window()
    {
        var h = new Harness(Cycle(MuteTrigger.DoublePress));
        Assert.True(h.Presses.Knob(Press));
        Assert.Equal(PressTiming.DoublePressWindow, h.Timer);
        Assert.Empty(h.Routed); // waiting to see if a second press comes

        Assert.True(h.Presses.Knob(Press));
        Assert.Equal([(Press, true)], h.Routed);
        Assert.Null(h.Timer);

        h.Presses.Knob(Press);
        h.Presses.Expired();
        Assert.Equal((Press, false), h.Routed[^1]);
    }

    [Fact]
    public void Double_press_still_mutes_when_the_hold_key_is_let_go_before_the_second_press()
    {
        var h = new Harness(Cycle(MuteTrigger.DoublePress, hold: Modifier.Ctrl));
        var ctrlPress = new KnobEvent(KnobKind.Press, Modifier.Ctrl);
        Assert.True(h.Presses.Knob(ctrlPress));
        Assert.True(h.Presses.Knob(Press)); // second press without Ctrl: part of the double-press, not Windows mute
        Assert.Equal([(ctrlPress, true)], h.Routed);
        h.Presses.Expired();
        Assert.Single(h.Routed); // and no app switch afterwards
    }

    [Fact]
    public void Press_and_hold_mutes_when_held_and_switches_when_let_go_early()
    {
        var h = new Harness(Cycle(MuteTrigger.LongPress));
        Assert.True(h.Presses.Knob(Press));
        Assert.Equal(PressTiming.HoldTime, h.Timer);
        Assert.False(h.Presses.Knob(Release));
        Assert.Equal([(Press, false)], h.Routed);

        h.Presses.Knob(Press);
        h.Presses.Expired();
        h.Presses.Knob(Release); // let go after it already muted: nothing more
        Assert.Equal((Press, true), h.Routed[^1]);
        Assert.Equal(2, h.Routed.Count);
    }

    [Fact]
    public void Not_timed_when_nothing_plays_without_the_hold_key_or_for_modifier_press()
    {
        var h = new Harness(Cycle(MuteTrigger.DoublePress)) { Playing = false };
        h.Presses.Knob(Press);
        Assert.Null(h.Timer);
        Assert.Equal([(Press, false)], h.Routed); // routed straight away; the Router passes it to Windows

        h = new Harness(Cycle(MuteTrigger.DoublePress, hold: Modifier.Ctrl));
        h.Presses.Knob(Press);
        Assert.Null(h.Timer);

        h = new Harness(Cycle(MuteTrigger.ModifierPress));
        h.Presses.Knob(Press);
        Assert.Null(h.Timer);
        Assert.Single(h.Routed);
    }

    [Fact]
    public void Turns_and_other_modes_route_straight_away()
    {
        var h = new Harness(Cycle(MuteTrigger.DoublePress));
        h.Presses.Knob(new KnobEvent(KnobKind.Up));
        var focused = new Harness(new Settings { Mode = TargetMode.Focused, MuteTrigger = MuteTrigger.DoublePress });
        focused.Presses.Knob(Press);
        Assert.Null(h.Timer);
        Assert.Null(focused.Timer);
        Assert.Single(h.Routed);
        Assert.Single(focused.Routed);
    }
}
